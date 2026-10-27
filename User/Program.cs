using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Security.Claims;
using System.Text;
using User.Data;
using User.DTOs;
using User.Endpoints;
using User.Helper;
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
var userConnStr = builder.Configuration.GetConnectionString("UserDB")
    ?? throw new InvalidOperationException("No connection string for UserDB");

builder.Services.AddDbContext<UserContext>(options =>
    options.UseNpgsql(userConnStr));



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
builder.Services.AddScoped<SubscriptionService>();
builder.Services.AddScoped<ClassroomService>();

// Applique les [Required], [MinLength]… des DTOs aux Minimal APIs (sinon ils sont ignorés)
builder.Services.AddValidation();

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
            ClockSkew = TimeSpan.Zero,
            // Les claims gardent leur nom court du JWT ("sub", "role"…)
            RoleClaimType = "role",
            NameClaimType = "username"
        };
        options.MapInboundClaims = false;
    });

builder.Services.AddRolePolicies();




var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.


// Création / mise à niveau du schéma au démarrage (idempotent, cf. Data/UserSchema.cs).
// En prod, préférer une migration explicite dans le pipeline CI/CD.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<UserContext>();
    await UserSchema.EnsureAsync(db);
    await UserSeeder.SeedAsync(db, app.Environment.IsDevelopment());
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
// Inscription guidée (élève, enseignant ou parent). Les clés et codes sont vérifiés
// AVANT la création du compte : une clé invalide ne laisse pas de compte à moitié créé.
auth.MapPost("/register", async (RegisterRequest req, UserContext db, Jwtservice jwt,
    SubscriptionService subs, ClassroomService classrooms) =>
{
    if (req.Role == UserRole.Admin)
        return Results.BadRequest(new { message = "Un compte administrateur ne peut pas être créé par inscription." });

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

    if (req.InviteCode is not null && req.Role != UserRole.Student)
        return Results.BadRequest(new { message = "Seul un élève peut rejoindre une classe." });

    if (req.StartTrial && req.Role != UserRole.Teacher)
        return Results.BadRequest(new { message = "L'essai gratuit est réservé aux enseignants." });

    if (req.SubscriptionKey is not null)
    {
        var (error, _) = await subs.CheckKeyAsync(req.SubscriptionKey, req.Role);
        if (error != RedeemError.None)
            return Results.BadRequest(new { message = SubscriptionService.Message(error) });
    }

    if (req.InviteCode is not null)
    {
        var code = CodeGenerator.Normalize(req.InviteCode);
        if (!await db.Classrooms.AnyAsync(c => c.InviteCode == code && !c.IsArchived))
            return Results.BadRequest(new { message = "Code d'invitation de classe invalide." });
    }

    var teachingSubjects = req.TeachingSubjects ?? new();
    var user = new AppUser
    {
        Email = req.Email.ToLower(),
        Username = req.Username,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
        Role = req.Role,
        Level = req.Level,
        // La spécialité (claim JWT lu par le LLM) reprend les matières enseignées
        Specialite = req.Specialite ?? (teachingSubjects.Count > 0 ? string.Join(", ", teachingSubjects) : null),
        Filiere = req.Filiere,
        Language = req.Language,
        SubSystem = req.SubSystem,
        ExamTarget = req.ExamTarget,
        ExamDate = req.ExamDate,
        FocusSubjects = req.FocusSubjects ?? new(),
        DailyStudyMinutes = req.DailyStudyMinutes,
        TeachingSubjects = teachingSubjects,
        TeachingContext = req.TeachingContext,
        OrganizationName = req.TeachingContext == TeachingContext.Independent ? null : req.OrganizationName,
        LinkCode = req.Role == UserRole.Student ? await ParentEndpoints.UniqueLinkCodeAsync(db) : null
    };
    db.AppUsers.Add(user);

    // Enseignant : une classe (ou un groupe) par niveau choisi pendant l'inscription
    if (req.Role == UserRole.Teacher && req.InitialClasses is { Count: > 0 })
    {
        var isSchool = req.TeachingContext != TeachingContext.Institution;
        foreach (var name in req.InitialClasses.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
        {
            var subSystem = !isSchool ? (SchoolSubSystem?)null
                : name.StartsWith("Form", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Sixth", StringComparison.OrdinalIgnoreCase)
                    ? SchoolSubSystem.Anglophone : SchoolSubSystem.Francophone;
            await classrooms.CreateAsync(user, name, isSchool ? name.Trim() : null, subSystem);
        }
    }

    if (req.InviteCode is not null)
        await classrooms.JoinAsync(user, req.InviteCode);

    await db.SaveChangesAsync();

    if (req.SubscriptionKey is not null)
        await subs.RedeemAsync(user, req.SubscriptionKey);

    if (req.StartTrial)
        await subs.StartTrialAsync(user.Id, SubscriptionPlan.Teacher);

    var entitlement = await subs.GetEntitlementAsync(user.Id);
    var (token, expiresAt) = jwt.GenerateToken(user, entitlement);
    return Results.Created("/auth/me", new AuthResponse(token, expiresAt, new UserProfileDto(user, entitlement)));
})
.WithSummary("Créer un compte")
.Produces<AuthResponse>(201)
.Produces(409)
.Produces(400);

// ── POST /auth/login ──────────────────────────────────────────────────────────
auth.MapPost("/login", async (LoginRequest req, UserContext db, Jwtservice jwt, SubscriptionService subs) =>
{
    var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Email == req.Email.ToLower());

    // Message identique que l'email soit inconnu ou que le mdp soit faux
    // → ne pas indiquer à un attaquant lequel des deux est incorrect
    if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        return Results.Unauthorized();

    user.LastLoginAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    var entitlement = await subs.GetEntitlementAsync(user.Id);
    var (token, expiresAt) = jwt.GenerateToken(user, entitlement);
    return Results.Ok(new AuthResponse(token, expiresAt, new UserProfileDto(user, entitlement)));
})
.WithSummary("Se connecter")
.Produces<AuthResponse>()
.Produces(401);

// ── GET /auth/me ──────────────────────────────────────────────────────────────
// Endpoint protégé : le client envoie le JWT dans le header Authorization: Bearer <token>
// On lit les claims du token pour retrouver l'utilisateur
auth.MapGet("/me", async (ClaimsPrincipal principal, UserContext db, SubscriptionService subs) =>
{
    var user = await db.CurrentUserAsync(principal);
    return user is null
        ? Results.NotFound()
        : Results.Ok(new UserProfileDto(user, await subs.GetEntitlementAsync(user.Id)));
})
.RequireAuthorization()
.WithSummary("Profil de l'utilisateur connecté")
.Produces<UserProfileDto>()
.Produces(401);

// ── PUT /auth/me ──────────────────────────────────────────────────────────────
// Remplacement COMPLET du profil.
// Tous les champs doivent être envoyés — les champs omis seront mis à null.
// Différence avec PATCH : PATCH = modification partielle, PUT = remplacement total.
auth.MapPut("/me", async (UpdateProfileRequest req, ClaimsPrincipal principal, UserContext db, SubscriptionService subs) =>
{
    var user = await db.CurrentUserAsync(principal);
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
    return Results.Ok(new UserProfileDto(user, await subs.GetEntitlementAsync(user.Id)));
})
.RequireAuthorization()
.WithSummary("Remplacer le profil complet (PUT)")
.Produces<UserProfileDto>()
.Produces(400)
.Produces(401);

// PATCH /auth/me → voir Controllers/UsersController.cs

// ─── Classes, parents, abonnements, administration ────────────────────────────
app.MapClassroomEndpoints();
app.MapParentEndpoints();
app.MapSubscriptionEndpoints();
app.MapAdminEndpoints();

app.MapControllers();





app.Run();
