using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Security.Claims;
using System.Text;
using User.Data;
using User.DTOs;
using User.Model;
using User.Service;

var builder = WebApplication.CreateBuilder(args);



// ─── Aspire ───────────────────────────────────────────────────────────────────
builder.AddServiceDefaults();

// ─── Base de données ──────────────────────────────────────────────────────────
// ⚠️ BUG CORRIGÉ : on n'utilise plus GetConnectionString + AddDbContext manuellement.
//
// Avant (incorrect avec Aspire) :
//   var lol = builder.Configuration.GetConnectionString("UserDB");
//   builder.Services.AddDbContext<UserContext>(op => op.UseNpgsql(lol));
//
// Après (correct) :
builder.AddNpgsqlDbContext<UserContext>("userdb");
//
// "userdb" = le nom déclaré dans AppHost.cs : postgres.AddDatabase("userdb")
// Aspire gère automatiquement : retry, health check, OpenTelemetry, connection string.
// La connection string dans appsettings.json n'est plus nécessaire.
//builder.AddNpgsqlDbContext<UserContext>("userdb");



/*
var lol = builder.Configuration.GetConnectionString("UserDB");
builder.Services.AddDbContext<UserContext>(op => op.UseNpgsql(lol));
*/




// Add services to the container.

//builder.Services.AddControllers();
builder.Services.ConfigureHttpJsonOptions(options => {
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "User API", Description = "API de gestion des comptes", Version = "v1" });
});



builder.Services.AddScoped<Jwtservice>();

// Nécessaire pour le PatchController uniquement
builder.Services.AddControllers().AddNewtonsoftJson();






// ─── JWT ──────────────────────────────────────────────────────────────────────
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("La configuration Jwt:Key est manquante. Définissez-la dans appsettings ou les variables d'environnement.");

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




var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.


// ⚠️ AJOUT : migration automatique au démarrage
// Crée les tables si elles n'existent pas encore.
// En prod, préférer une migration explicite dans le pipeline CI/CD.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<UserContext>();
    await db.Database.MigrateAsync();
    // ⚠️ AJOUT : injection des données de test
    await UserSeeder.SeedAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // ⚠️ BUG CORRIGÉ : AddSwaggerGen était enregistré mais UseSwagger/UseSwaggerUI
    // étaient absents du pipeline → /swagger était inaccessible
    app.UseSwagger();
    app.UseSwaggerUI(c =>
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "User API V1"));
}





app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();








// ─── Minimal API ──────────────────────────────────────────────────────────────
var auth = app.MapGroup("/auth").WithTags("Auth");

// ── POST /auth/register ───────────────────────────────────────────────────────
auth.MapPost("/register", async (RegisterRequest req, UserContext db, Jwtservice jwt) =>
{
    // Vérifications unicité
    if (await db.AppUsers.AnyAsync(u => u.Email == req.Email.ToLower()))
        return Results.Conflict(new { message = "Un compte avec cet email existe déjà." });

    if (await db.AppUsers.AnyAsync(u => u.Username == req.Username))
        return Results.Conflict(new { message = "Ce nom d'utilisateur est déjà pris." });

    // Cohérence métier : un élève ne doit pas avoir de spécialité, un enseignant pas de filière
    if (req.Role == UserRole.Student && req.Specialite is not null)
        return Results.BadRequest(new { message = "Un élève ne peut pas avoir de spécialité." });

    if (req.Role == UserRole.Teacher && req.Filiere is not null)
        return Results.BadRequest(new { message = "Un enseignant ne peut pas avoir de filière." });

    // Un parent n'a ni niveau, ni filière, ni spécialité : il ne suit pas de cours,
    // il suit des enfants.
    if (req.Role == UserRole.Parent && (req.Level is not null || req.Filiere is not null || req.Specialite is not null))
        return Results.BadRequest(new { message = "Un parent n'a ni niveau, ni filière, ni spécialité." });

    var user = new AppUser
    {
        Email = req.Email.ToLower(),
        Username = req.Username,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
        Role = req.Role,
        Level = req.Level,
        Specialite = req.Specialite,
        Filiere = req.Filiere,
        Language = req.Language,
    };

    db.AppUsers.Add(user);
    await db.SaveChangesAsync();

    var (token, expiresAt) = jwt.GenerateToken(user);
    return Results.Created("/auth/me", new AuthResponse(token, expiresAt, new UserProfileDto(user)));
})
.WithSummary("Créer un compte")
.Produces<AuthResponse>(201)
.Produces(409)
.Produces(400);

// ── POST /auth/login ──────────────────────────────────────────────────────────
auth.MapPost("/login", async (LoginRequest req, UserContext db, Jwtservice jwt) =>
{
    var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Email == req.Email.ToLower());

    // Message identique que l'email soit inconnu ou que le mdp soit faux
    // → ne pas indiquer à un attaquant lequel des deux est incorrect
    if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        return Results.Unauthorized();

    user.LastLoginAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    var (token, expiresAt) = jwt.GenerateToken(user, await LoadChildrenAsync(db, user));
    return Results.Ok(new AuthResponse(token, expiresAt, new UserProfileDto(user)));
})
.WithSummary("Se connecter")
.Produces<AuthResponse>()
.Produces(401);

// ── GET /auth/me ──────────────────────────────────────────────────────────────
// Endpoint protégé : le client envoie le JWT dans le header Authorization: Bearer <token>
// On lit les claims du token pour retrouver l'utilisateur
auth.MapGet("/me", async (ClaimsPrincipal principal, UserContext db) =>
{
    // "sub" est le claim standard qui contient l'Id utilisateur
    var userIdStr = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? principal.FindFirstValue("sub");

    if (userIdStr is null || !Guid.TryParse(userIdStr, out var userId))
        return Results.Unauthorized();

    var user = await db.AppUsers.FindAsync(userId);
    return user is null ? Results.NotFound() : Results.Ok(new UserProfileDto(user));
})
.RequireAuthorization()
.WithSummary("Profil de l'utilisateur connecté")
.Produces<UserProfileDto>()
.Produces(401);



// ── PUT /auth/me ──────────────────────────────────────────────────────────────
// Remplacement COMPLET du profil.
// Tous les champs doivent être envoyés — les champs omis seront mis à null.
// Différence avec PATCH : PATCH = modification partielle, PUT = remplacement total.
auth.MapPut("/me", async (UpdateProfileRequest req, ClaimsPrincipal principal, UserContext db) =>
{
    var userIdStr = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? principal.FindFirstValue("sub");

    if (userIdStr is null || !Guid.TryParse(userIdStr, out var userId))
        return Results.Unauthorized();

    var user = await db.AppUsers.FindAsync(userId);
    if (user is null) return Results.NotFound();

    // Cohérence métier : vérifier la combinaison Role/Specialite/Filiere
    if (user.Role == UserRole.Student && req.Specialite is not null)
        return Results.BadRequest(new { message = "Un élève ne peut pas avoir de spécialité." });

    if (user.Role == UserRole.Teacher && req.Filiere is not null)
        return Results.BadRequest(new { message = "Un enseignant ne peut pas avoir de filière." });

    // PUT remplace tout — même les valeurs null
    user.Username = req.Username ?? user.Username;
    user.Level = req.Level;
    user.Specialite = req.Specialite;
    user.Filiere = req.Filiere;
    user.Language = req.Language ?? user.Language;

    await db.SaveChangesAsync();
    return Results.Ok(new UserProfileDto(user));
})
.RequireAuthorization()
.WithSummary("Remplacer le profil complet (PUT)")
.Produces<UserProfileDto>()
.Produces(400)
.Produces(401);

// PATCH /auth/me → voir Controllers/UserController.cs

// ══════════════════════════════════════════════════════════════════════════════
//  Lien famille — un parent suit la progression de ses enfants
// ══════════════════════════════════════════════════════════════════════════════

// ── POST /auth/me/code-parent ─────────────────────────────────────────────────
//
// C'est l'ÉLÈVE qui émet le code, jamais le parent : le lien ne peut donc pas
// exister sans qu'il l'ait voulu.
auth.MapPost("/me/code-parent", async (ClaimsPrincipal principal, UserContext db) =>
{
    var user = await CurrentUserAsync(principal, db);
    if (user is null) return Results.Unauthorized();

    if (user.Role != UserRole.Student)
        return Results.BadRequest(new { message = "Seul un élève peut émettre un code de rattachement." });

    var now = DateTime.UtcNow;

    // Un code déjà émis et encore valable est réutilisé : un élève qui reclique ne
    // doit pas invalider celui qu'il vient de dicter à son parent.
    var existing = await db.StudentLinkCodes
        .Where(c => c.StudentId == user.Id && c.UsedAt == null && c.ExpiresAt > now)
        .OrderByDescending(c => c.CreatedAt)
        .FirstOrDefaultAsync();

    if (existing is not null)
        return Results.Ok(new LinkCodeResponse(existing.Code, existing.ExpiresAt));

    var code = new StudentLinkCode
    {
        StudentId = user.Id,
        Code = StudentLinkCode.NewCode(),
        CreatedAt = now,
        ExpiresAt = now.Add(StudentLinkCode.Lifetime),
    };

    db.StudentLinkCodes.Add(code);
    await db.SaveChangesAsync();

    return Results.Ok(new LinkCodeResponse(code.Code, code.ExpiresAt));
})
.RequireAuthorization()
.WithSummary("Générer un code de rattachement pour un parent")
.Produces<LinkCodeResponse>()
.Produces(400);

// ── POST /auth/parent/enfants ─────────────────────────────────────────────────
auth.MapPost("/parent/enfants", async (
    LinkChildRequest req,
    ClaimsPrincipal principal,
    UserContext db,
    Jwtservice jwt) =>
{
    var parent = await CurrentUserAsync(principal, db);
    if (parent is null) return Results.Unauthorized();

    if (parent.Role != UserRole.Parent)
        return Results.BadRequest(new { message = "Seul un compte parent peut rattacher un enfant." });

    var value = (req.Code ?? "").Trim().ToUpperInvariant();
    if (value.Length == 0) return Results.BadRequest(new { message = "Le code est vide." });

    var code = await db.StudentLinkCodes
        .Include(c => c.Student)
        .FirstOrDefaultAsync(c => c.Code == value);

    if (code is null)
        return Results.BadRequest(new { message = "Ce code n'existe pas." });

    // Messages distincts : un code déjà utilisé et un code périmé n'appellent pas la
    // même réaction du parent.
    if (code.UsedAt is not null)
        return Results.BadRequest(new { message = "Ce code a déjà été utilisé." });

    if (code.ExpiresAt <= DateTime.UtcNow)
        return Results.BadRequest(new { message = "Ce code a expiré. Demandez-en un nouveau à votre enfant." });

    var alreadyLinked = await db.ParentChildren
        .AnyAsync(l => l.ParentId == parent.Id && l.StudentId == code.StudentId);

    if (alreadyLinked)
        return Results.Conflict(new { message = "Cet enfant est déjà rattaché à votre compte." });

    var link = new ParentChild { ParentId = parent.Id, StudentId = code.StudentId };
    db.ParentChildren.Add(link);

    code.UsedAt = DateTime.UtcNow;
    code.UsedByParentId = parent.Id;

    await db.SaveChangesAsync();

    // Jeton rafraîchi : sans lui, le claim « children » resterait périmé jusqu'à une
    // heure et l'enfant n'apparaîtrait pas tout de suite.
    var (token, expiresAt) = jwt.GenerateToken(parent, await LoadChildrenAsync(db, parent));

    return Results.Ok(new LinkChildResponse(
        new ChildSummaryDto(code.Student.Id, code.Student.Username, code.Student.Level, code.Student.Filiere, link.LinkedAt),
        token,
        expiresAt));
})
.RequireAuthorization()
.WithSummary("Rattacher un enfant à partir de son code")
.Produces<LinkChildResponse>()
.Produces(400)
.Produces(409);

// ── GET /auth/parent/enfants ──────────────────────────────────────────────────
auth.MapGet("/parent/enfants", async (ClaimsPrincipal principal, UserContext db) =>
{
    var parent = await CurrentUserAsync(principal, db);
    if (parent is null) return Results.Unauthorized();

    if (parent.Role != UserRole.Parent)
        return Results.BadRequest(new { message = "Ce compte n'est pas un compte parent." });

    var children = await db.ParentChildren
        .Where(l => l.ParentId == parent.Id)
        .Include(l => l.Student)
        .OrderBy(l => l.Student.Username)
        .Select(l => new ChildSummaryDto(
            l.Student.Id, l.Student.Username, l.Student.Level, l.Student.Filiere, l.LinkedAt))
        .ToListAsync();

    return Results.Ok(children);
})
.RequireAuthorization()
.WithSummary("Les enfants rattachés au parent connecté")
.Produces<List<ChildSummaryDto>>();

// ── DELETE /auth/parent/enfants/{childId} ─────────────────────────────────────
auth.MapDelete("/parent/enfants/{childId:guid}", async (
    Guid childId, ClaimsPrincipal principal, UserContext db, Jwtservice jwt) =>
{
    var parent = await CurrentUserAsync(principal, db);
    if (parent is null) return Results.Unauthorized();

    var link = await db.ParentChildren
        .FirstOrDefaultAsync(l => l.ParentId == parent.Id && l.StudentId == childId);

    if (link is null) return Results.NotFound(new { message = "Ce lien n'existe pas." });

    db.ParentChildren.Remove(link);
    await db.SaveChangesAsync();

    var (token, expiresAt) = jwt.GenerateToken(parent, await LoadChildrenAsync(db, parent));
    return Results.Ok(new { token, expiresAt });
})
.RequireAuthorization()
.WithSummary("Délier un enfant")
.Produces(200)
.Produces(404);

// ── GET /auth/me/parents ──────────────────────────────────────────────────────
//
// L'élève doit pouvoir savoir qui suit sa progression, et le défaire.
auth.MapGet("/me/parents", async (ClaimsPrincipal principal, UserContext db) =>
{
    var user = await CurrentUserAsync(principal, db);
    if (user is null) return Results.Unauthorized();

    var parents = await db.ParentChildren
        .Where(l => l.StudentId == user.Id)
        .Include(l => l.Parent)
        .OrderBy(l => l.LinkedAt)
        .Select(l => new LinkedParentDto(l.Parent.Id, l.Parent.Username, l.Parent.Email, l.LinkedAt))
        .ToListAsync();

    return Results.Ok(parents);
})
.RequireAuthorization()
.WithSummary("Les parents qui suivent l'élève connecté")
.Produces<List<LinkedParentDto>>();

// ── DELETE /auth/me/parents/{parentId} ────────────────────────────────────────
auth.MapDelete("/me/parents/{parentId:guid}", async (
    Guid parentId, ClaimsPrincipal principal, UserContext db) =>
{
    var user = await CurrentUserAsync(principal, db);
    if (user is null) return Results.Unauthorized();

    var link = await db.ParentChildren
        .FirstOrDefaultAsync(l => l.StudentId == user.Id && l.ParentId == parentId);

    if (link is null) return Results.NotFound(new { message = "Ce lien n'existe pas." });

    db.ParentChildren.Remove(link);
    await db.SaveChangesAsync();

    // ⚠️ Le parent garde l'accès jusqu'à l'expiration de son jeton (60 min par défaut) :
    // le lien est porté par le claim, pas relu à chaque requête.
    return Results.NoContent();
})
.RequireAuthorization()
.WithSummary("Retirer à un parent le suivi de sa progression")
.Produces(204)
.Produces(404);













app.MapControllers();

app.Run();

// ── Helpers ───────────────────────────────────────────────────────────────────

/// <summary>Utilisateur du jeton courant, ou null si le jeton ne désigne personne.</summary>
static async Task<AppUser?> CurrentUserAsync(ClaimsPrincipal principal, UserContext db)
{
    var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? principal.FindFirstValue("sub");

    return value is not null && Guid.TryParse(value, out var id)
        ? await db.AppUsers.FindAsync(id)
        : null;
}

/// <summary>
/// Les élèves qu'un parent suit, pour le claim « children ».
/// Vide pour tout autre rôle : le claim n'est alors pas émis du tout.
/// </summary>
static async Task<List<Guid>> LoadChildrenAsync(UserContext db, AppUser user) =>
    user.Role != UserRole.Parent
        ? []
        : await db.ParentChildren
            .Where(l => l.ParentId == user.Id)
            .Select(l => l.StudentId)
            .ToListAsync();
