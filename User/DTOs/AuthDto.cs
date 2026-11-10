using System.ComponentModel.DataAnnotations;
using User.Model;
using User.Service;

namespace User.DTOs;

// ══════════════════════════════════════════════════════════════════════════════
// RÈGLE DES DTOs :
//
//  AppUser (Model)      = structure de la TABLE en base de données
//                         → ne sort JAMAIS directement de l'API (contient PasswordHash !)
//
//  RegisterRequest      = données que le CLIENT envoie pour s'inscrire
//  LoginRequest         = données que le CLIENT envoie pour se connecter
//  AuthResponse         = ce qu'on renvoie après une auth réussie (token + infos de base)
//
//  UserProfileDto       = profil complet exposé par GET /auth/me
//                         → lu par le LLM pour personnaliser ses réponses
//
//  EditProfileRequest   = données acceptées par PUT /auth/me/profile (Endpoints/ProfileEndpoints.cs)
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Inscription guidée. Seuls Email, Username et Password sont obligatoires ;
/// les autres champs correspondent aux étapes de l'onboarding selon le rôle.
/// </summary>
public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(3), MaxLength(64)] string Username,
    [Required, MinLength(8)] string Password,
    UserRole Role = UserRole.Student,
    string? Level = null,
    string? Specialite = null,
    string? Filiere = null,
    string Language = "fr",
    // ── Élève ──
    SchoolSubSystem? SubSystem = null,
    string? ExamTarget = null,
    DateOnly? ExamDate = null,
    List<string>? FocusSubjects = null,
    int? DailyStudyMinutes = null,
    string? InviteCode = null,
    // ── Enseignant ──
    List<string>? TeachingSubjects = null,
    TeachingContext? TeachingContext = null,
    string? OrganizationName = null,
    List<string>? InitialClasses = null,
    bool StartTrial = false,
    // ── Élève ou enseignant : clé familiale / d'établissement / d'institution ──
    string? SubscriptionKey = null,
    // ── Secondaire (élève) ou supérieur (étudiant) ──
    EducationStage? Stage = null
);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password
);

public record AuthResponse(
    string Token,
    DateTime ExpiresAt,
    UserProfileDto Profile
);

public record UserProfileDto(
    Guid Id,
    string Email,
    string Username,
    UserRole Role,
    string? Level,
    string? Specialite,
    string? Filiere,
    string Language,
    DateTime CreatedAt,
    SchoolSubSystem? SubSystem,
    string? ExamTarget,
    DateOnly? ExamDate,
    List<string> FocusSubjects,
    int? DailyStudyMinutes,
    string? LinkCode,
    List<string> TeachingSubjects,
    TeachingContext? TeachingContext,
    Guid? OrganizationId,
    string? OrganizationName,
    bool IsPremium,
    SubscriptionPlan? Plan,
    DateTime? PremiumUntil,
    List<EntitledFeature>? Features = null,
    EducationStage? Stage = null,
    bool SiteEditor = false,
    bool IsAdmin = false
)
{
    public UserProfileDto(AppUser user, Entitlement? entitlement = null) : this(
        user.Id,
        user.Email,
        user.Username,
        user.Role,
        user.Level,
        user.Specialite,
        user.Filiere,
        user.Language,
        user.CreatedAt,
        user.SubSystem,
        user.ExamTarget,
        user.ExamDate,
        user.FocusSubjects,
        user.DailyStudyMinutes,
        user.LinkCode,
        user.TeachingSubjects,
        user.TeachingContext,
        user.OrganizationId,
        user.OrganizationName,
        entitlement?.IsPremium ?? false,
        entitlement?.Plan,
        entitlement?.Until,
        entitlement?.Features,
        user.Stage,
        user.SiteEditor,
        user.IsAdmin
    )
    { }
}
