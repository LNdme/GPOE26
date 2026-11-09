using Microsoft.Extensions.Http.Resilience;
using System.Security.Claims;
using System.Text;
using Cours.Data;
using Cours.DTOs;
using Cours.Helpers;
using Cours.Model;
using Cours.Service;
using GPOE26.ServiceDefaults.Llm;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// ─── Aspire ───────────────────────────────────────────────────────────────────
builder.AddServiceDefaults();

// ─── Base de données ──────────────────────────────────────────────────────────
var coursConnStr = builder.Configuration.GetConnectionString("CoursDB")
    ?? throw new InvalidOperationException("No connection string for CoursDB");

builder.Services.AddDbContext<CoursContext>(options =>
    options.UseNpgsql(coursConnStr));

// ─── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddScoped<PdfExtractorService>();
builder.Services.AddScoped<PdfStructuringService>();
builder.Services.AddSingleton<PdfStructuringQueue>();
builder.Services.AddHostedService<PdfStructuringWorker>();
builder.AddLlmClient();

// Service User : classes de l'utilisateur, pour le partage des cours (JWT de l'appelant transmis)
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<UserDirectoryClient>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:User"] ?? "http://localhost:5197"))
    .AddStandardResilienceHandler();
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
    await db.Database.ExecuteSqlRawAsync(
        @"CREATE TABLE IF NOT EXISTS ""Courses"" (
            ""Id"" uuid NOT NULL,
            ""Title"" character varying(200) NOT NULL,
            ""Subject"" character varying(100) NOT NULL,
            ""Description"" text,
            ""ContentType"" text NOT NULL DEFAULT 'Text',
            ""PdfPath"" text,
            ""ExtractedText"" text,
            ""OwnerId"" uuid NOT NULL,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            ""UpdatedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_Courses"" PRIMARY KEY (""Id"")
        )");
    await db.Database.ExecuteSqlRawAsync(
        @"CREATE INDEX IF NOT EXISTS ""IX_Courses_OwnerId"" ON ""Courses"" (""OwnerId"")");
    await db.Database.ExecuteSqlRawAsync(
        @"CREATE INDEX IF NOT EXISTS ""IX_Courses_OwnerId_Subject"" ON ""Courses"" (""OwnerId"", ""Subject"")");
    await db.Database.ExecuteSqlRawAsync(
        @"CREATE TABLE IF NOT EXISTS ""CourseSections"" (
            ""Id"" uuid NOT NULL,
            ""CourseId"" uuid NOT NULL,
            ""Type"" text NOT NULL DEFAULT 'Paragraph',
            ""Content"" text NOT NULL,
            ""Order"" integer NOT NULL DEFAULT 0,
            ""Level"" integer NOT NULL DEFAULT 0,
            CONSTRAINT ""PK_CourseSections"" PRIMARY KEY (""Id""),
            CONSTRAINT ""FK_CourseSections_Courses_CourseId"" FOREIGN KEY (""CourseId"") REFERENCES ""Courses"" (""Id"") ON DELETE CASCADE
        )");
    await db.Database.ExecuteSqlRawAsync(
        @"CREATE INDEX IF NOT EXISTS ""IX_CourseSections_CourseId_Order"" ON ""CourseSections"" (""CourseId"", ""Order"")");
    // Partage des cours avec les classes (phase 1 bis)
    await db.Database.ExecuteSqlRawAsync(
        @"ALTER TABLE ""Courses"" ADD COLUMN IF NOT EXISTS ""Status"" text NOT NULL DEFAULT 'Draft'");
    await db.Database.ExecuteSqlRawAsync(
        @"ALTER TABLE ""Courses"" ADD COLUMN IF NOT EXISTS ""ClassroomIds"" uuid[] NOT NULL DEFAULT ARRAY[]::uuid[]");
    await db.Database.ExecuteSqlRawAsync(
        @"ALTER TABLE ""Courses"" ADD COLUMN IF NOT EXISTS ""PublishedAt"" timestamp with time zone");
    await db.Database.ExecuteSqlRawAsync(
        @"CREATE INDEX IF NOT EXISTS ""IX_Courses_ClassroomIds"" ON ""Courses"" USING gin (""ClassroomIds"")");
    await db.Database.ExecuteSqlRawAsync(
        @"ALTER TABLE ""Courses"" ADD COLUMN IF NOT EXISTS ""StructuringStatus"" text");
    await CoursSeeder.SeedAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Cours API V1"));
}

// En développement, les services s'appellent en HTTP avec un JWT : une redirection vers HTTPS
// ferait retirer l'en-tête Authorization par HttpClient, et chaque appel finirait en 401.
if (!app.Environment.IsDevelopment())
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

// ── GET /cours/shared ─────────────────────────────────────────────────────────
// Cours publiés pour les classes de l'élève (gratuits dans le modèle freemium).
cours.MapGet("/shared", async (CoursContext db, UserDirectoryClient users, string? subject) =>
{
    var classIds = (await users.MyClassroomIdsAsync()).ToList();
    if (classIds.Count == 0) return Results.Ok(new List<CourseSummaryDto>());

    var query = db.Courses.Where(c => c.Status == CourseStatus.Published && c.ClassroomIds.Any(id => classIds.Contains(id)));
    if (!string.IsNullOrWhiteSpace(subject))
        query = query.Where(c => c.Subject == subject);

    var results = await query
        .OrderByDescending(c => c.PublishedAt)
        .Select(c => new CourseSummaryDto(c))
        .ToListAsync();
    return Results.Ok(results);
})
.WithSummary("Cours partagés avec mes classes")
.Produces<List<CourseSummaryDto>>();

// ── GET /cours/{id} ───────────────────────────────────────────────────────────
// L'auteur, ou un membre d'une classe avec laquelle le cours est publié.
cours.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal principal, CoursContext db, UserDirectoryClient users) =>
{
    var userId = ClaimsHelper.GetUserId(principal);
    if (userId is null) return Results.Unauthorized();

    var course = await db.Courses
        .Include(c => c.Sections.OrderBy(s => s.Order))
        .FirstOrDefaultAsync(c => c.Id == id);

    var allowed = course is not null
        && (course.OwnerId == userId
            || (course.Status == CourseStatus.Published
                && course.ClassroomIds.Count > 0
                && (await users.MyClassroomIdsAsync()).Overlaps(course.ClassroomIds)));

    return !allowed
        ? Results.NotFound(new { message = "Cours introuvable." })
        : Results.Ok(new CourseDto(course!));
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
    PdfStructuringService structuring,
    PdfStructuringQueue queue,
    LlmClient llm,
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

    var root = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");

    // Supprimer l'ancien PDF si existant
    if (course.PdfPath is not null)
    {
        var oldPath = Path.Combine(root, course.PdfPath);
        if (File.Exists(oldPath)) File.Delete(oldPath);
    }

    using var memory = new MemoryStream();
    await file.CopyToAsync(memory);
    course.PdfPath = await extractor.SavePdfAsync(file, Path.Combine(root, "uploads", "cours"));

    // Version construite d'après la mise en page, affichée tout de suite ; l'IA l'améliore ensuite
    try
    {
        structuring.Import(db, course, memory.ToArray(), root, llm.IsConfigured);
    }
    catch (Exception ex) when (ex is not OutOfMemoryException)
    {
        return Results.BadRequest(new { message = "Ce PDF n'a pas pu être lu. Vérifiez qu'il n'est ni protégé ni endommagé." });
    }

    await db.SaveChangesAsync();
    if (llm.IsConfigured) queue.Enqueue(course.Id);

    return Results.Ok(new CourseDto(course));
})
.WithSummary("Uploader un PDF sur un cours existant")
.Produces<CourseDto>()
.Produces(400)
.Produces(404)
.DisableAntiforgery();

// ── POST /cours/{id}/restructure ──────────────────────────────────────────────
// Relit un PDF déjà importé avec la mise en forme actuelle. Le texte retouché à la main est remplacé.
cours.MapPost("/{id:guid}/restructure", async (
    Guid id,
    ClaimsPrincipal principal,
    CoursContext db,
    PdfStructuringService structuring,
    PdfStructuringQueue queue,
    LlmClient llm,
    IWebHostEnvironment env) =>
{
    var ownerId = ClaimsHelper.GetUserId(principal);
    if (ownerId is null) return Results.Unauthorized();

    var course = await db.Courses.Include(c => c.Sections)
        .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId);
    if (course is null) return Results.NotFound(new { message = "Cours introuvable." });
    if (course.PdfPath is null) return Results.BadRequest(new { message = "Ce cours ne provient pas d'un PDF." });

    var root = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
    var path = Path.Combine(root, course.PdfPath);
    if (!File.Exists(path)) return Results.NotFound(new { message = "Le fichier PDF d'origine est introuvable." });

    structuring.Import(db, course, await File.ReadAllBytesAsync(path), root, llm.IsConfigured);
    await db.SaveChangesAsync();
    if (llm.IsConfigured) queue.Enqueue(course.Id);
    return Results.Ok(new CourseDto(course));
})
.WithSummary("Relire un PDF et reconstruire le cours")
.Produces<CourseDto>()
.Produces(400)
.Produces(404);

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
        

        foreach (var s in req.Sections)
        {
            db.CourseSections.Add(new CourseSection
            {
                CourseId = course.Id,
                Type = s.Type,
                Content = s.Content,
                Order = s.Order,
                Level = s.Level
            });
        }

        // Un PDF retouché à la main reste un PDF : l'IA ne repassera pas dessus
        if (course.ContentType == ContentType.Pdf)
            course.StructuringStatus = PdfStructuringService.StatusDone;
        else
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

// ── PUT /cours/{id}/sharing ───────────────────────────────────────────────────
// Publie le cours pour des classes de l'auteur, ou le repasse en brouillon.
cours.MapPut("/{id:guid}/sharing", async (Guid id, ShareCourseRequest req, ClaimsPrincipal principal,
    CoursContext db, UserDirectoryClient users) =>
{
    var ownerId = ClaimsHelper.GetUserId(principal);
    if (ownerId is null) return Results.Unauthorized();

    var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId);
    if (course is null) return Results.NotFound(new { message = "Cours introuvable." });

    var classroomIds = req.ClassroomIds?.Distinct().ToList() ?? new();
    if (req.Publish)
    {
        if (classroomIds.Count == 0)
            return Results.BadRequest(new { message = "Choisissez au moins une classe." });
        var owned = await users.MyClassroomIdsAsync();
        if (classroomIds.Any(c => !owned.Contains(c)))
            return Results.BadRequest(new { message = "Vous ne pouvez partager un cours qu'avec vos propres classes." });
    }

    course.ClassroomIds = classroomIds;
    course.Status = req.Publish ? CourseStatus.Published : CourseStatus.Draft;
    course.PublishedAt = req.Publish ? course.PublishedAt ?? DateTime.UtcNow : null;
    course.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(new CourseSummaryDto(course));
})
.WithSummary("Partager un cours avec ses classes")
.Produces<CourseSummaryDto>()
.Produces(400);

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
        var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        var fullPath = Path.Combine(webRoot, course.PdfPath);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        var figures = PdfStructuringService.FiguresFolder(webRoot, course.Id);
        if (Directory.Exists(figures)) Directory.Delete(figures, recursive: true);
    }

    db.Courses.Remove(course);
    await db.SaveChangesAsync();

    return Results.NoContent();
})
.WithSummary("Supprimer un cours")
.Produces(204)
.Produces(404);

// ── GET /uploads/cours/{id}/{fichier} ─────────────────────────────────────────
// Images extraites d'un PDF, lues par le navigateur (sans jeton) : seuls ces fichiers sont servis, jamais le PDF.
app.MapGet("/uploads/cours/{courseId:guid}/{file}", (Guid courseId, string file, IWebHostEnvironment env) =>
{
    if (!System.Text.RegularExpressions.Regex.IsMatch(file, @"^fig-\d+\.(png|jpg)$")) return Results.NotFound();
    var root = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
    var path = Path.Combine(PdfStructuringService.FiguresFolder(root, courseId), file);
    return File.Exists(path) ? Results.File(path, file.EndsWith(".png") ? "image/png" : "image/jpeg") : Results.NotFound();
})
.AllowAnonymous()
.WithSummary("Image extraite d'un PDF");

app.Run();
