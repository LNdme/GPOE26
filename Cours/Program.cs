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
// "coursdb" = déclaré dans AppHost.cs : postgres.AddDatabase("coursdb")
//builder.AddNpgsqlDbContext<CoursContext>("coursdb");


var lol = builder.Configuration.GetConnectionString("CoursDB");
builder.Services.AddDbContext<CoursContext>(op => op.UseNpgsql(lol));






// ─── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddScoped<PdfExtractorService>();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Cours API",
        Description = "Gestion des cours — texte et PDF",
        Version = "v1"
    });
    // Indiquer à Swagger que les endpoints nécessitent un Bearer token
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Entrez : Bearer {votre_token}",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer"
    });

});

// ─── JWT — même config que le service User ────────────────────────────────────
// IMPORTANT : la clé, l'issuer et l'audience doivent être IDENTIQUES
// au service User pour que les tokens générés là-bas soient acceptés ici.
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


// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi


var app = builder.Build();

app.MapDefaultEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CoursContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Cours API V1"));
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();











// Nécessaire pour servir les PDFs uploadés via /uploads/cours/...
app.UseStaticFiles();

// ─── Groupe /cours ────────────────────────────────────────────────────────────
var cours = app.MapGroup("/cours")
    .WithTags("Cours")
    .RequireAuthorization(); // Tous les endpoints nécessitent un JWT valide

// ── GET /cours ────────────────────────────────────────────────────────────────
// Liste des cours de l'utilisateur connecté, avec filtre optionnel par matière.
// On renvoie CourseSummaryDto (sans ExtractedText) pour alléger la réponse.
cours.MapGet("", async (
    ClaimsPrincipal principal,
    CoursContext db,
    string? subject) =>   // ?subject=Mathématiques dans l'URL
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
// Détail complet d'un cours (avec ExtractedText).
// Vérifie que le cours appartient bien à l'utilisateur connecté.
cours.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal principal, CoursContext db) =>
{
    var ownerId = ClaimsHelper.GetUserId(principal);
    if (ownerId is null) return Results.Unauthorized();

    var course = await db.Courses.FirstOrDefaultAsync(
        c => c.Id == id && c.OwnerId == ownerId);

    return course is null
        ? Results.NotFound(new { message = "Cours introuvable." })
        : Results.Ok(new CourseDto(course));
})
.WithSummary("Détail d'un cours")
.Produces<CourseDto>()
.Produces(404);

// ── POST /cours ───────────────────────────────────────────────────────────────
// Créer un cours en texte.
// Pour un cours PDF, créer d'abord avec POST /cours puis uploader via POST /cours/{id}/upload.
cours.MapPost("", async (
    CreateCourseRequest req,
    ClaimsPrincipal principal,
    CoursContext db) =>
{
    var ownerId = ClaimsHelper.GetUserId(principal);
    if (ownerId is null) return Results.Unauthorized();

    var course = new Course
    {
        Title = req.Title,
        Subject = req.Subject,
        Description = req.Description,
        OwnerId = ownerId.Value,
        ContentType = req.TextContent is not null ? ContentType.Text : ContentType.Pdf,
        TextContent = req.TextContent,
        // Si c'est du texte, ExtractedText = TextContent directement
        // → le LLM recevra ce champ sans logique conditionnelle
        ExtractedText = req.TextContent,
    };

    db.Courses.Add(course);
    await db.SaveChangesAsync();

    return Results.Created($"/cours/{course.Id}", new CourseDto(course));
})
.WithSummary("Créer un cours (texte)")
.Produces<CourseDto>(201)
.Produces(400);

// ── POST /cours/{id}/upload ───────────────────────────────────────────────────
// Uploader un PDF sur un cours existant.
// Séparé du POST /cours pour éviter de mélanger JSON et multipart/form-data.
//
// Le client envoie : multipart/form-data avec un champ "file" contenant le PDF.
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

    // Vérifier que le cours existe et appartient à l'utilisateur
    var course = await db.Courses.FirstOrDefaultAsync(
        c => c.Id == id && c.OwnerId == ownerId);

    if (course is null)
        return Results.NotFound(new { message = "Cours introuvable." });

    // Vérifier que c'est bien un PDF
    if (!file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { message = "Seuls les fichiers PDF sont acceptés." });

    // Limite de taille : 10 Mo
    if (file.Length > 10 * 1024 * 1024)
        return Results.BadRequest(new { message = "Le fichier ne doit pas dépasser 10 Mo." });

    // Supprimer l'ancien PDF si existant
    if (course.PdfPath is not null)
    {
        var oldPath = Path.Combine(env.WebRootPath, course.PdfPath);
        if (File.Exists(oldPath)) File.Delete(oldPath);
    }

    // Sauvegarder le nouveau PDF
    var uploadFolder = Path.Combine(env.WebRootPath, "uploads", "cours");
    var relativePath = await extractor.SavePdfAsync(file, uploadFolder);

    // Extraire le texte du PDF
    using var stream = file.OpenReadStream();
    var extractedText = extractor.ExtractText(stream);

    // Mettre à jour le cours
    course.ContentType = ContentType.Pdf;
    course.PdfPath = relativePath;
    course.ExtractedText = extractedText;
    course.TextContent = null;
    course.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();

    return Results.Ok(new CourseDto(course));
})
.WithSummary("Uploader un PDF sur un cours existant")
.Produces<CourseDto>()
.Produces(400)
.Produces(404)
.DisableAntiforgery(); // Nécessaire pour multipart/form-data en Minimal API

// ── PUT /cours/{id} ───────────────────────────────────────────────────────────
// Remplacer le contenu texte d'un cours (titre, matière, contenu).
// Pour mettre à jour un PDF, utiliser POST /cours/{id}/upload.
cours.MapPut("/{id:guid}", async (
    Guid id,
    UpdateCourseRequest req,
    ClaimsPrincipal principal,
    CoursContext db) =>
{
    var ownerId = ClaimsHelper.GetUserId(principal);
    if (ownerId is null) return Results.Unauthorized();

    var course = await db.Courses.FirstOrDefaultAsync(
        c => c.Id == id && c.OwnerId == ownerId);

    if (course is null)
        return Results.NotFound(new { message = "Cours introuvable." });

    course.Title = req.Title;
    course.Subject = req.Subject;
    course.Description = req.Description;
    course.TextContent = req.TextContent;
    course.ExtractedText = req.TextContent; // On met à jour aussi le texte extrait
    course.ContentType = req.TextContent is not null ? ContentType.Text : course.ContentType;
    course.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(new CourseDto(course));
})
.WithSummary("Remplacer un cours (texte)")
.Produces<CourseDto>()
.Produces(404);


// -------------------------Patch--------------------------
cours.MapPatch("/{id:guid}", async (
    Guid id,
    PatchCourseRequest req,
    ClaimsPrincipal principal,
    CoursContext db) =>
{
    var ownerId = ClaimsHelper.GetUserId(principal);
    if (ownerId is null) return Results.Unauthorized();

    var course = await db.Courses.FirstOrDefaultAsync(
        c => c.Id == id && c.OwnerId == ownerId);

    if (course is null)
        return Results.NotFound(new { message = "Cours introuvable." });

    // On ne met à jour QUE les champs fournis (non null)
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

    // Supprimer le fichier PDF du disque si existant
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
