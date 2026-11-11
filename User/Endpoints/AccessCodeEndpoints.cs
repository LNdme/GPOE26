using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using User.Data;
using User.DTOs;
using User.Model;
using User.Service;

namespace User.Endpoints;

/// <summary>
/// Activation des codes personnels (docs/PLAN-ETABLISSEMENT.md, lot 3) :
/// sans compte, le code crée le compte ; avec un compte, il le rattache à l'espace.
/// </summary>
public static class AccessCodeEndpoints
{
    public static void MapAccessCodeEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/auth/codes").WithTags("Codes personnels");

        // ── POST /auth/codes/check ────────────────────────────────────────────────
        // Montre à qui est le code avant de créer le compte (« c'est bien toi ? »). Ne modifie rien.
        auth.MapPost("/check", async (CodeRequest req, AccessCodeService codes) =>
        {
            var (error, info) = await codes.CheckAsync(req.Code);
            if (error != CodeError.None) return Results.NotFound(new { message = AccessCodeService.Message(error) });

            var student = info!.Student;
            return Results.Ok(new CodeInfoDto(info.Code.Kind, info.Organization.Name,
                student is null ? null : $"{student.LastName} {student.FirstName}".Trim(),
                student?.Matricule, info.Classroom?.Name,
                NeedsName: info.Code.Kind == AccessCodeKind.Student && student is null));
        })
        .WithSummary("Ce que donne un code personnel (sans compte)")
        .Produces<CodeInfoDto>()
        .Produces(404);

        // ── POST /auth/codes/activate ─────────────────────────────────────────────
        // Crée le compte (élève ou enseignant selon le code) et l'active, ou rien si le code ne passe pas.
        auth.MapPost("/activate", async (ActivateCodeRequest req, UserContext db, AccessCodeService codes,
            Jwtservice jwt, SubscriptionService subs) =>
        {
            var (error, info) = await codes.CheckAsync(req.Code);
            if (error != CodeError.None) return Results.BadRequest(new { message = AccessCodeService.Message(error) });

            var email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim().ToLower();
            var username = req.Username.Trim();
            if (email is not null && await db.AppUsers.AnyAsync(u => u.Email == email))
                return Results.Conflict(new { message = "Un compte avec cet e-mail existe déjà : connectez-vous, puis saisissez le code dans votre profil." });
            if (username.Contains('@') || await db.AppUsers.AnyAsync(u => u.Username == username))
                return Results.Conflict(new { message = "Ce nom d'utilisateur est déjà pris (ou contient @) : choisissez-en un autre." });

            var role = info!.Code.Kind == AccessCodeKind.Student ? UserRole.Student : UserRole.Teacher;
            var user = new AppUser
            {
                Email = email,
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
                Role = role,
                Language = req.Language,
                Stage = role == UserRole.Student ? EducationStage.Secondary : null,
                LinkCode = role == UserRole.Student ? await ParentEndpoints.UniqueLinkCodeAsync(db) : null,
                LastLoginAt = DateTime.UtcNow
            };

            await using var tx = await db.Database.BeginTransactionAsync();
            db.AppUsers.Add(user);
            await db.SaveChangesAsync();
            var applied = await codes.ApplyAsync(req.Code, user, req.LastName, req.FirstName);
            if (applied != CodeError.None)
            {
                await tx.RollbackAsync();   // pas de compte à moitié créé
                return Results.BadRequest(new { message = AccessCodeService.Message(applied) });
            }
            await tx.CommitAsync();

            var entitlement = await subs.GetEntitlementAsync(user.Id);
            var (token, expiresAt) = jwt.GenerateToken(user, entitlement);
            return Results.Created("/auth/me", new AuthResponse(token, expiresAt, new UserProfileDto(user, entitlement)));
        })
        .WithSummary("Créer son compte avec son code personnel")
        .Produces<AuthResponse>(201)
        .Produces(400)
        .Produces(409);

        // ── POST /access-codes/redeem ─────────────────────────────────────────────
        // Un compte existant (élève qui avait un compte individuel, enseignant, futur responsable) saisit son code.
        app.MapPost("/access-codes/redeem", async (CodeRequest req, ClaimsPrincipal principal, UserContext db, AccessCodeService codes) =>
        {
            var user = await db.CurrentUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            await using var tx = await db.Database.BeginTransactionAsync();
            // Code en blanc : le nom d'utilisateur tient lieu de nom, que l'établissement pourra corriger
            var applied = await codes.ApplyAsync(req.Code, user, lastName: user.Username);
            if (applied != CodeError.None)
            {
                await tx.RollbackAsync();
                return Results.BadRequest(new { message = AccessCodeService.Message(applied) });
            }
            await tx.CommitAsync();
            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithTags("Codes personnels")
        .WithSummary("Rattacher son compte avec un code personnel");
    }
}
