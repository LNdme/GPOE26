using System.Security.Claims;
using User.Data;
using User.DTOs;
using User.Model;
using User.Service;

namespace User.Endpoints;

/// <summary>
/// Modification du profil depuis la page « Mon profil ». Chaque rôle ne modifie que ce qui le concerne :
/// le rôle, l'e-mail, l'établissement et les droits d'édition du site restent hors de portée.
/// Les champs absents ou null conservent leur valeur.
/// </summary>
public record EditProfileRequest(
    string? Username,
    string? Language = null,
    // Élève
    string? Level = null,
    string? Filiere = null,
    SchoolSubSystem? SubSystem = null,
    string? ExamTarget = null,
    DateOnly? ExamDate = null,
    List<string>? FocusSubjects = null,
    int? DailyStudyMinutes = null,
    // Enseignant
    string? Specialite = null,
    List<string>? TeachingSubjects = null,
    TeachingContext? TeachingContext = null);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public static class ProfileRules
{
    public const int MinPasswordLength = 8;
    private const int MaxListItems = 12;

    /// <summary>Applique la demande au compte. Renvoie un message d'erreur, ou null si tout est valide.</summary>
    public static string? Apply(AppUser user, EditProfileRequest req)
    {
        var username = req.Username?.Trim();
        if (username is not null && username.Length is < 3 or > 64)
            return "Le nom affiché doit contenir entre 3 et 64 caractères.";
        if (req.Language is { } lang && lang is not ("fr" or "en"))
            return "Langue inconnue : choisissez le français ou l'anglais.";
        if (req.DailyStudyMinutes is < 0 or > 600)
            return "Le temps d'étude quotidien doit être compris entre 0 et 600 minutes.";
        if (req.ExamTarget?.Trim().Length > 100 || req.Level?.Trim().Length > 64 ||
            req.Filiere?.Trim().Length > 64 || req.Specialite?.Trim().Length > 64)
            return "Un des champs est trop long.";

        if (username is { Length: > 0 }) user.Username = username;
        if (req.Language is { } language) user.Language = language;

        switch (user.Role)
        {
            case UserRole.Student:
                if (req.Level is not null) user.Level = Clean(req.Level);
                if (req.Filiere is not null) user.Filiere = Clean(req.Filiere);
                if (req.SubSystem is { } subSystem) user.SubSystem = subSystem;
                if (req.ExamTarget is not null) user.ExamTarget = Clean(req.ExamTarget);
                if (req.ExamDate is { } examDate) user.ExamDate = examDate;
                if (req.FocusSubjects is not null) user.FocusSubjects = CleanList(req.FocusSubjects);
                if (req.DailyStudyMinutes is { } minutes) user.DailyStudyMinutes = minutes;
                break;
            case UserRole.Teacher:
                if (req.Specialite is not null) user.Specialite = Clean(req.Specialite);
                if (req.TeachingSubjects is not null) user.TeachingSubjects = CleanList(req.TeachingSubjects);
                if (req.TeachingContext is { } context) user.TeachingContext = context;
                break;
        }
        return null;
    }

    /// <summary>Vérifie le changement de mot de passe. Renvoie un message d'erreur, ou null si valide.</summary>
    public static string? CheckPasswordChange(AppUser user, ChangePasswordRequest req)
    {
        if (string.IsNullOrEmpty(req.CurrentPassword) || !BCrypt.Net.BCrypt.Verify(req.CurrentPassword, user.PasswordHash))
            return "Le mot de passe actuel est incorrect.";
        if (string.IsNullOrEmpty(req.NewPassword) || req.NewPassword.Length < MinPasswordLength)
            return $"Le nouveau mot de passe doit contenir au moins {MinPasswordLength} caractères.";
        if (req.NewPassword == req.CurrentPassword)
            return "Le nouveau mot de passe doit être différent de l'actuel.";
        return null;
    }

    // Un champ vidé dans le formulaire efface la valeur
    private static string? Clean(string value) => value.Trim() is { Length: > 0 } v ? v : null;

    private static List<string> CleanList(IEnumerable<string> values) => values
        .Select(v => v.Trim())
        .Where(v => v.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(MaxListItems)
        .ToList();
}

public static class ProfileEndpoints
{
    public static void MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/auth/me").WithTags("Profil").RequireAuthorization();

        // ── PUT /auth/me/profile ──────────────────────────────────────────────────
        me.MapPut("/profile", async (EditProfileRequest req, ClaimsPrincipal principal, UserContext db, SubscriptionService subs) =>
        {
            var user = await db.CurrentUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            if (ProfileRules.Apply(user, req) is { } error)
                return Results.BadRequest(new { message = error });

            await db.SaveChangesAsync();
            return Results.Ok(new UserProfileDto(user, await subs.GetEntitlementAsync(user.Id)));
        })
        .WithSummary("Modifier son profil (champs selon le rôle)")
        .Produces<UserProfileDto>()
        .Produces(400);

        // ── POST /auth/me/password ────────────────────────────────────────────────
        me.MapPost("/password", async (ChangePasswordRequest req, ClaimsPrincipal principal, UserContext db) =>
        {
            var user = await db.CurrentUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            if (ProfileRules.CheckPasswordChange(user, req) is { } error)
                return Results.BadRequest(new { message = error });

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithSummary("Changer son mot de passe")
        .Produces(204)
        .Produces(400);
    }
}
