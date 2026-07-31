using System.Security.Claims;
using System.Text;
using Cours.Data;
using Cours.DTOs;
using Cours.Helpers;
using Cours.Model;
using Cours.Service;
using GPOE26.Ai;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Pgvector.Npgsql;

var builder = WebApplication.CreateBuilder(args);

// ─── Aspire ───────────────────────────────────────────────────────────────────
builder.AddServiceDefaults();

// ─── Base de données + pgvector ───────────────────────────────────────────────
//
// On n'utilise pas builder.AddNpgsqlDbContext ici : cette méthode construit sa propre
// NpgsqlDataSource, sur laquelle on ne peut pas greffer le plugin pgvector. Or le plugin
// est indispensable côté Npgsql pour sérialiser/désérialiser le type `vector` sur le fil,
// en plus du mapping EF.
//
// On enregistre donc la source de données et le DbContext à la main, puis on récupère
// tout l'apport Aspire (health checks, traces, métriques, retry) via EnrichNpgsqlDbContext.
builder.Services.AddSingleton<NpgsqlDataSource>(_ =>
{
    var connectionString = builder.Configuration.GetConnectionString("coursdb")
        ?? throw new InvalidOperationException("La chaîne de connexion 'coursdb' est absente.");

    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
    dataSourceBuilder.UseVector();
    return dataSourceBuilder.Build();
});

builder.Services.AddDbContext<CoursContext>((sp, options) =>
    options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>(), npgsql => npgsql.UseVector()));

// Réintègre health checks, traces, métriques et retry Aspire sur ce DbContext.
// ⚠️ C'est la seule couture Aspire ↔ pgvector du projet, et la seule ligne non
// vérifiable sans exécuter : si l'enrichissement refuse un DbContext enregistré sur
// une NpgsqlDataSource, supprimez cette ligne — on perd la télémétrie, rien d'autre.
builder.EnrichNpgsqlDbContext<CoursContext>();

// ─── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddScoped<PdfExtractorService>();

// Passerelle OpenRouter + agents de traitement de contenu (transcription, structuration).
builder.Services.AddGpoeAi(builder.Configuration);

builder.Services.AddScoped<CourseFormattingService>();
builder.Services.AddScoped<CourseSearchService>();
builder.Services.AddSingleton<CourseFormattingQueue>();
builder.Services.AddHostedService<CourseFormattingWorker>();

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Cours API",
        Description = "Gestion des cours — texte structuré, PDF, photos et recherche sémantique",
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

// Les fichiers statiques doivent être servis AVANT l'authentification : les documents
// de cours (wwwroot/uploads/cours) sont récupérés par le proxy anonyme du frontend,
// qui ne porte pas de JWT. Placé après MapControllers, ce middleware n'était de toute
// façon jamais atteint pour ces requêtes.
app.UseStaticFiles();

app.UseAuthentication();   // ← AJOUT : sans ceci, le JWT n'est jamais lu → 401
app.UseAuthorization();
app.MapControllers();

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
        .Include(c => c.Assets.OrderBy(a => a.Order))
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
        ContentType = Cours.Model.ContentType.Text,
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
//
// Accepte un PDF, ou plusieurs photos du cours. C'est le point d'entrée qui rend
// possible « je photographie mon cours et je l'étudie ».
cours.MapPost("/{id:guid}/upload", async (
    Guid id,
    IFormFileCollection files,
    ClaimsPrincipal principal,
    CoursContext db,
    PdfExtractorService extractor,
    CourseFormattingQueue queue,
    IWebHostEnvironment env) =>
{
    var ownerId = ClaimsHelper.GetUserId(principal);
    if (ownerId is null) return Results.Unauthorized();

    var course = await db.Courses
        .Include(c => c.Sections)
        .Include(c => c.Assets)
        .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId);

    if (course is null)
        return Results.NotFound(new { message = "Cours introuvable." });

    if (files.Count == 0)
        return Results.BadRequest(new { message = "Aucun fichier n'a été fourni." });

    const long maxFileSize = 10 * 1024 * 1024;
    var pdfs = new List<IFormFile>();
    var photos = new List<IFormFile>();

    foreach (var file in files)
    {
        if (file.Length == 0)
            return Results.BadRequest(new { message = $"Le fichier « {file.FileName} » est vide." });

        if (file.Length > maxFileSize)
            return Results.BadRequest(new { message = $"« {file.FileName} » dépasse 10 Mo." });

        if (file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            pdfs.Add(file);
        else if (file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                 && AgentImage.MediaTypeForExtension(Path.GetExtension(file.FileName)) is not null)
            photos.Add(file);
        else
            return Results.BadRequest(new
            {
                message = $"« {file.FileName} » n'est ni un PDF ni une image JPEG/PNG/WebP."
            });
    }

    if (pdfs.Count > 0 && photos.Count > 0)
        return Results.BadRequest(new
        {
            message = "Déposez soit un PDF, soit des photos — pas les deux à la fois."
        });

    if (pdfs.Count > 1)
        return Results.BadRequest(new { message = "Un seul PDF par cours." });

    var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
    var uploadFolder = Path.Combine(webRoot, "uploads", "cours");

    // Purger les documents précédents : un nouveau dépôt remplace l'ancien.
    foreach (var previous in course.Assets)
    {
        var previousPath = Path.Combine(webRoot, previous.Path);
        if (File.Exists(previousPath)) File.Delete(previousPath);
    }
    if (course.PdfPath is not null)
    {
        var legacyPath = Path.Combine(webRoot, course.PdfPath);
        if (File.Exists(legacyPath)) File.Delete(legacyPath);
    }
    db.CourseAssets.RemoveRange(course.Assets);
    course.Assets.Clear();

    var incoming = pdfs.Count > 0 ? pdfs : photos;
    var kind = pdfs.Count > 0 ? AssetKind.Pdf : AssetKind.Photo;
    var order = 0;

    foreach (var file in incoming)
    {
        var relativePath = await extractor.SaveAssetAsync(file, uploadFolder);

        course.Assets.Add(new CourseAsset
        {
            CourseId = course.Id,
            Kind = kind,
            Path = relativePath,
            OriginalFileName = Path.GetFileName(file.FileName),
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            Order = order++,
        });
    }

    course.ContentType = kind == AssetKind.Pdf ? Cours.Model.ContentType.Pdf : Cours.Model.ContentType.Images;
    course.PdfPath = kind == AssetKind.Pdf ? course.Assets[0].Path : null;
    course.Sections.Clear();

    // La mise en forme dure des dizaines de secondes : on ne la fait pas dans cette
    // requête. L'élève reçoit tout de suite un cours en statut Pending, et le canvas
    // affiche l'avancement.
    course.FormatStatus = FormatStatus.Pending;
    course.FormatError = null;
    course.FormattedMarkdown = null;
    course.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();
    await queue.EnqueueAsync(course.Id);

    return Results.Ok(new CourseDto(course));
})
.WithSummary("Uploader un PDF ou des photos sur un cours existant")
.Produces<CourseDto>()
.Produces(400)
.Produces(404)
.DisableAntiforgery();

// ── POST /cours/{id}/format ───────────────────────────────────────────────────
//
// Relance la mise en forme et la réindexation. C'est la cible du bouton
// « Régénérer la mise en forme » du canvas de lecture.
cours.MapPost("/{id:guid}/format", async (
    Guid id,
    ClaimsPrincipal principal,
    CoursContext db,
    CourseFormattingQueue queue) =>
{
    var ownerId = ClaimsHelper.GetUserId(principal);
    if (ownerId is null) return Results.Unauthorized();

    var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId);
    if (course is null)
        return Results.NotFound(new { message = "Cours introuvable." });

    if (course.FormatStatus == FormatStatus.Pending)
        return Results.Ok(new { status = FormatStatus.Pending.ToString(), message = "Mise en forme déjà en cours." });

    course.FormatStatus = FormatStatus.Pending;
    course.FormatError = null;
    await db.SaveChangesAsync();

    await queue.EnqueueAsync(course.Id);

    return Results.Accepted($"/cours/{course.Id}", new
    {
        status = FormatStatus.Pending.ToString(),
        message = "Mise en forme lancée."
    });
})
.WithSummary("Relancer la mise en forme et l'indexation d'un cours")
.Produces(202)
.Produces(404);

// ── POST /cours/{id}/search ───────────────────────────────────────────────────
//
// Recherche sémantique — le seul point d'entrée du RAG. Le service Chat l'appelle
// pour ancrer les réponses du tuteur ; il ne manipule jamais d'embeddings lui-même.
cours.MapPost("/{id:guid}/search", async (
    Guid id,
    SearchCourseRequest req,
    ClaimsPrincipal principal,
    CoursContext db,
    CourseSearchService search) =>
{
    var ownerId = ClaimsHelper.GetUserId(principal);
    if (ownerId is null) return Results.Unauthorized();

    var exists = await db.Courses.AnyAsync(c => c.Id == id && c.OwnerId == ownerId);
    if (!exists) return Results.NotFound(new { message = "Cours introuvable." });

    if (string.IsNullOrWhiteSpace(req.Query))
        return Results.BadRequest(new { message = "La requête de recherche est vide." });

    var hits = await search.SearchAsync(id, req.Query, req.K);

    return Results.Ok(hits
        .Select(h => new SearchHitDto(h.ChunkId, h.HeadingPath, h.Content, h.Order, h.Score))
        .ToList());
})
.WithSummary("Recherche sémantique dans un cours")
.Produces<List<SearchHitDto>>()
.Produces(400)
.Produces(404);

// ══════════════════════════════════════════════════════════════════════════════
//  Parcours d'apprentissage : Lire → Comprendre → Consolider
// ══════════════════════════════════════════════════════════════════════════════

// ── GET /cours/{id}/parcours ──────────────────────────────────────────────────
cours.MapGet("/{id:guid}/parcours", async (Guid id, ClaimsPrincipal principal, CoursContext db) =>
{
    var ownerId = ClaimsHelper.GetUserId(principal);
    if (ownerId is null) return Results.Unauthorized();

    var course = await db.Courses
        .Include(c => c.Steps.OrderBy(s => s.Order))
        .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId);

    return course is null
        ? Results.NotFound(new { message = "Cours introuvable." })
        : Results.Ok(BuildJourney(course));
})
.WithSummary("Le parcours d'un cours et l'étape en cours")
.Produces<JourneyDto>()
.Produces(404);

// ── POST /cours/{id}/parcours/{stepId}/lu ─────────────────────────────────────
//
// Déclenché par le bouton « J'ai lu », lui-même allumé au franchissement de 90 %
// du défilement. Une étape de lecture n'a pas de score : elle se valide en étant faite.
cours.MapPost("/{id:guid}/parcours/{stepId:guid}/lu", async (
    Guid id,
    Guid stepId,
    ClaimsPrincipal principal,
    CoursContext db) =>
{
    var ownerId = ClaimsHelper.GetUserId(principal);
    if (ownerId is null) return Results.Unauthorized();

    var course = await db.Courses
        .Include(c => c.Steps.OrderBy(s => s.Order))
        .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId);

    if (course is null) return Results.NotFound(new { message = "Cours introuvable." });

    var step = course.Steps.FirstOrDefault(s => s.Id == stepId);
    if (step is null) return Results.NotFound(new { message = "Étape introuvable." });

    if (step.Kind != StepKind.Lecture)
        return Results.BadRequest(new { message = "Cette étape n'est pas une étape de lecture." });

    if (step.Status == StepStatus.Locked)
        return Results.BadRequest(new { message = "Cette étape n'est pas encore accessible." });

    step.Status = StepStatus.Passed;
    step.CompletedAt = DateTime.UtcNow;
    step.Attempts++;

    CourseFormattingService.UnlockNext(course.Steps);
    course.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(BuildJourney(course));
})
.WithSummary("Marquer une étape de lecture comme faite")
.Produces<JourneyDto>()
.Produces(400)
.Produces(404);

// ── POST /cours/{id}/parcours/{stepId}/resultat ───────────────────────────────
//
// Enregistre le résultat d'une étape évaluée et applique le seuil. C'est ici que se
// décide le déverrouillage de la suite du parcours.
cours.MapPost("/{id:guid}/parcours/{stepId:guid}/resultat", async (
    Guid id,
    Guid stepId,
    StepResultRequest req,
    ClaimsPrincipal principal,
    CoursContext db) =>
{
    var ownerId = ClaimsHelper.GetUserId(principal);
    if (ownerId is null) return Results.Unauthorized();

    if (req.Total <= 0 || req.Score < 0 || req.Score > req.Total)
        return Results.BadRequest(new { message = "Score invalide." });

    var course = await db.Courses
        .Include(c => c.Steps.OrderBy(s => s.Order))
        .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId);

    if (course is null) return Results.NotFound(new { message = "Cours introuvable." });

    var step = course.Steps.FirstOrDefault(s => s.Id == stepId);
    if (step is null) return Results.NotFound(new { message = "Étape introuvable." });

    if (!step.IsEvaluated)
        return Results.BadRequest(new { message = "Cette étape ne se valide pas par un score." });

    if (step.Status == StepStatus.Locked)
        return Results.BadRequest(new { message = "Cette étape n'est pas encore accessible." });

    step.Score = req.Score;
    step.Total = req.Total;
    step.Attempts++;

    // Les sections ratées servent à renvoyer l'élève au bon endroit du cours, et
    // porteront plus tard la granularité du suivi de maîtrise sur l'année.
    step.WeakHeadings = req.WeakHeadings is { Count: > 0 }
        ? string.Join(" | ", req.WeakHeadings.Distinct())
        : null;

    var passed = (double)req.Score / req.Total >= CourseStep.PassThreshold;

    if (passed)
    {
        step.Status = StepStatus.Passed;
        step.CompletedAt = DateTime.UtcNow;
        CourseFormattingService.UnlockNext(course.Steps);
    }
    else
    {
        // L'étape reste accessible : l'élève doit pouvoir refaire son test.
        step.Status = StepStatus.Failed;
        step.CompletedAt = null;
    }

    course.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(BuildJourney(course));
})
.WithSummary("Enregistrer le résultat d'une étape évaluée")
.Produces<JourneyDto>()
.Produces(400)
.Produces(404);

// ── PUT /cours/{id} ───────────────────────────────────────────────────────────
cours.MapPut("/{id:guid}", async (
    Guid id,
    UpdateCourseRequest req,
    ClaimsPrincipal principal,
    CoursContext db,
    CourseFormattingQueue queue) =>
{
    var ownerId = ClaimsHelper.GetUserId(principal);
    if (ownerId is null) return Results.Unauthorized();

    var course = await db.Courses
        .Include(c => c.Sections)
        .Include(c => c.Assets)
        .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId);

    if (course is null)
        return Results.NotFound(new { message = "Cours introuvable." });

    course.Title = req.Title.Length > 200 ? req.Title[..200] : req.Title;
    course.Subject = req.Subject.Length > 100 ? req.Subject[..100] : req.Subject;
    course.Description = req.Description;

    var contentChanged = false;

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

        course.ContentType = Cours.Model.ContentType.Text;
        course.RebuildExtractedText();

        course.FormatStatus = FormatStatus.Pending;
        course.FormatError = null;
        contentChanged = true;
    }

    course.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();

    // Le contenu a changé : l'index vectoriel est périmé, il faut le reconstruire —
    // sinon le tuteur citerait des passages qui n'existent plus.
    if (contentChanged) await queue.EnqueueAsync(course.Id);

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
        .Include(c => c.Assets)
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

    var course = await db.Courses
        .Include(c => c.Assets)
        .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId);

    if (course is null)
        return Results.NotFound(new { message = "Cours introuvable." });

    var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");

    foreach (var asset in course.Assets)
    {
        var assetPath = Path.Combine(webRoot, asset.Path);
        if (File.Exists(assetPath)) File.Delete(assetPath);
    }

    if (course.PdfPath is not null)
    {
        var fullPath = Path.Combine(webRoot, course.PdfPath);
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

// ── Helpers ───────────────────────────────────────────────────────────────────

/// <summary>
/// Projette le parcours d'un cours, en désignant l'étape sur laquelle ouvrir la page :
/// la première qui n'est pas encore validée.
/// </summary>
static JourneyDto BuildJourney(Course course)
{
    var steps = course.Steps.OrderBy(s => s.Order).ToList();

    var active = steps.FirstOrDefault(s => s.Status is StepStatus.Available or StepStatus.InProgress or StepStatus.Failed)
                 ?? steps.LastOrDefault();

    return new JourneyDto(
        course.Id,
        course.JourneyMode,
        steps.Select(s => new StepDto(s)).ToList(),
        active?.Id,
        steps.Count(s => s.Status == StepStatus.Passed),
        CourseStep.PassThreshold * 100);
}
