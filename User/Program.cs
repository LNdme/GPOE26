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
    ?? throw new InvalidOperationException("votre_cle_secrete_tres_longue_et_aleatoire_ici_changez_moi_en_production_cle_256_bits'");

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

    var (token, expiresAt) = jwt.GenerateToken(user);
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


// PATCH /auth/me → voir Controllers/UserController.cs













app.MapControllers();





app.Run();
