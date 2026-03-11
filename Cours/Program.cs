using System.Security.Claims;
using System.Text;
using Cours.Data;
using Cours.DTOs;
using Cours.Helpers;
using Cours.Model;
using Cours.Service;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// ─── Aspire ───────────────────────────────────────────────────────────────────
builder.AddServiceDefaults();

// ─── Base de données ──────────────────────────────────────────────────────────
builder.AddNpgsqlDbContext<CoursContext>("coursdb");

// ─── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddScoped<PdfExtractorService>();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Cours API",
        Description = "Gestion des cours — texte structuré et PDF",
        Version = "v1"
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Entrez : Bearer {votre_token}",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer"
    });
});

// ─── JWT ──────────────────────────────────────────────────────────────────────
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? "votre_cle_secrete_tres_longue_et_aleatoire_ici_changez_moi_en_production_cle_256_bits";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();

var app = builder.Build();

app.MapDefaultEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CoursContext>();
    await db.Database.MigrateAsync();
    
    // ⚠️ AJOUT : injection des cours factices pour tests
    await CoursSeeder.SeedAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Cours API V1"));
}

app.UseHttpsRedirection();
app.UseAuthentication();   // ← AJOUT : sans ceci, le JWT n'est jamais lu → 401
app.UseAuthorization();
app.MapControllers();
app.UseStaticFiles();

// ─── Groupe /cours ────────────────────────────────────────────────────────────
var cours = app.MapGroup("/cours")
    .WithTags("Cours")
    .RequireAuthorization();

// ── GET /cours ────────────────────────────────────────────────────────────────
cours.MapGet("", async (
    ClaimsPrincipal principal,
    CoursContext db,
    string? subject) =>
{
    var ownerId = ClaimsHelper.GetUserId(principal);
    if (ownerId is null) return Results.Unauthorized();

    var query = db.Courses.Where(c => c.OwnerId == ownerId);

    if (!string.IsNullOrWhiteSpace(subject))
        query = query.Where(c => c.Subject == subject);

    var results = await query
        .OrderByDescending(c => c.UpdatedAt)
        .Select(c => new CourseSummaryDto(c))
        .ToListAsync();

    return Results.Ok(results);
})
.WithSummary("Liste de mes cours")
.Produces<List<CourseSummaryDto>>();

// ── GET /cours/{id} ───────────────────────────────────────────────────────────
cours.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal principal, CoursContext db) =>
{
    var ownerId = ClaimsHelper.GetUserId(principal);
    if (ownerId is null) return Results.Unauthorized();

    var course = await db.Courses
        .Include(c => c.Sections.OrderBy(s => s.Order))
        .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId);

    return course is null
        ? Results.NotFound(new { message = "Cours introuvable." })
        : Results.Ok(new CourseDto(course));
})
.WithSummary("Détail d'un cours")
.Produces<CourseDto>()
.Produces(404);

// ── POST /cours ───────────────────────────────────────────────────────────────
cours.MapPost("", async (
    CreateCourseRequest req,
    ClaimsPrincipal principal,
    CoursContext db) =>
{
    var ownerId = ClaimsHelper.GetUserId(principal);
    if (ownerId is null) return Results.Unauthorized();

    var course = new Course
    {
        Title = req.Title.Length > 200 ? req.Title[..200] : req.Title,
        Subject = req.Subject.Length > 100 ? req.Subject[..100] : req.Subject,
        Description = req.Description,
        OwnerId = ownerId.Value,
        ContentType = ContentType.Text,
    };

    // Ajouter les sections
    if (req.Sections is { Count: > 0 })
    {
        course.Sections = req.Sections.Select(s => new CourseSection
        {
            CourseId = course.Id,
            Type = s.Type,
            Content = s.Content,
            Order = s.Order,
            Level = s.Level
        }).ToList();
    }

    // Calculer ExtractedText à partir des sections
    course.RebuildExtractedText();

    db.Courses.Add(course);
    await db.SaveChangesAsync();

    return Results.Created($"/cours/{course.Id}", new CourseDto(course));
})
.WithSummary("Créer un cours (structuré)")
.Produces<CourseDto>(201)
.Produces(400);

// ── POST /cours/{id}/upload ───────────────────────────────────────────────────
cours.MapPost("/{id:guid}/upload", async (
    Guid id,
    IFormFile file,
    ClaimsPrincipal principal,
    CoursContext db,
    PdfExtractorService extractor,
    IWebHostEnvironment env) =>
{
    var ownerId = ClaimsHelper.GetUserId(principal);
    if (ownerId is null) return Results.Unauthorized();

    var course = await db.Courses
        .Include(c => c.Sections)
        .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId);

    if (course is null)
        return Results.NotFound(new { message = "Cours introuvable." });

    if (!file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { message = "Seuls les fichiers PDF sont acceptés." });

    if (file.Length > 10 * 1024 * 1024)
        return Results.BadRequest(new { message = "Le fichier ne doit pas dépasser 10 Mo." });

    // Supprimer l'ancien PDF si existant
    if (course.PdfPath is not null)
    {
        var oldPath = Path.Combine(env.WebRootPath, course.PdfPath);
        if (File.Exists(oldPath)) File.Delete(oldPath);
    }

    // Sauvegarder le nouveau PDF (fallback si WebRootPath est null)
    var root = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
    var uploadFolder = Path.Combine(root, "uploads", "cours");
    var relativePath = await extractor.SavePdfAsync(file, uploadFolder);

    // Extraire le texte du PDF
    using var stream = file.OpenReadStream();
    var extractedText = extractor.ExtractText(stream);

    // Mettre à jour le cours — supprimer les sections texte
    course.ContentType = ContentType.Pdf;
    course.PdfPath = relativePath;
    course.ExtractedText = extractedText;
    course.Sections.Clear();
    course.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();

    return Results.Ok(new CourseDto(course));
})
.WithSummary("Uploader un PDF sur un cours existant")
.Produces<CourseDto>()
.Produces(400)
.Produces(404)
.DisableAntiforgery();

// ── PUT /cours/{id} ───────────────────────────────────────────────────────────
cours.MapPut("/{id:guid}", async (
    Guid id,
    UpdateCourseRequest req,
    ClaimsPrincipal principal,
    CoursContext db) =>
{
    var ownerId = ClaimsHelper.GetUserId(principal);
    if (ownerId is null) return Results.Unauthorized();

    var course = await db.Courses
        .Include(c => c.Sections)
        .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId);

    if (course is null)
        return Results.NotFound(new { message = "Cours introuvable." });

    course.Title = req.Title.Length > 200 ? req.Title[..200] : req.Title;
    course.Subject = req.Subject.Length > 100 ? req.Subject[..100] : req.Subject;
    course.Description = req.Description;

    // Remplacer les sections — supprimer les anciennes via le DbContext pour éviter
    // le DbUpdateConcurrencyException, puis ajouter les nouvelles
    if (req.Sections is not null)
    {
        db.CourseSections.RemoveRange(course.Sections.ToList());
        course.Sections = new List<CourseSection>();

        foreach (var s in req.Sections)
        {
            course.Sections.Add(new CourseSection
            {
                CourseId = course.Id,
                Type = s.Type,
                Content = s.Content,
                Order = s.Order,
                Level = s.Level
            });
        }

        course.ContentType = ContentType.Text;
        course.RebuildExtractedText();
    }

    course.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(new CourseDto(course));
})
.WithSummary("Remplacer un cours (structuré)")
.Produces<CourseDto>()
.Produces(404);

// ── PATCH /cours/{id} ─────────────────────────────────────────────────────────
cours.MapPatch("/{id:guid}", async (
    Guid id,
    PatchCourseRequest req,
    ClaimsPrincipal principal,
    CoursContext db) =>
{
    var ownerId = ClaimsHelper.GetUserId(principal);
    if (ownerId is null) return Results.Unauthorized();

    var course = await db.Courses
        .Include(c => c.Sections)
        .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId);

    if (course is null)
        return Results.NotFound(new { message = "Cours introuvable." });

    if (req.Title is not null) course.Title = req.Title;
    if (req.Subject is not null) course.Subject = req.Subject;
    if (req.Description is not null) course.Description = req.Description;
    course.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(new CourseDto(course));
})
.WithSummary("Modifier partiellement un cours (métadonnées)")
.Produces<CourseDto>()
.Produces(404);

// ── DELETE /cours/{id} ────────────────────────────────────────────────────────
cours.MapDelete("/{id:guid}", async (
    Guid id,
    ClaimsPrincipal principal,
    CoursContext db,
    IWebHostEnvironment env) =>
{
    var ownerId = ClaimsHelper.GetUserId(principal);
    if (ownerId is null) return Results.Unauthorized();

    var course = await db.Courses.FirstOrDefaultAsync(
        c => c.Id == id && c.OwnerId == ownerId);

    if (course is null)
        return Results.NotFound(new { message = "Cours introuvable." });

    if (course.PdfPath is not null)
    {
        var fullPath = Path.Combine(env.WebRootPath, course.PdfPath);
        if (File.Exists(fullPath)) File.Delete(fullPath);
    }

    db.Courses.Remove(course);
    await db.SaveChangesAsync();

    return Results.NoContent();
})
.WithSummary("Supprimer un cours")
.Produces(204)
.Produces(404);

app.Run();
