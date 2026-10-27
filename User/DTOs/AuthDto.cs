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
//  UpdateProfileRequest = données acceptées par PATCH /auth/me
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
    string? SubscriptionKey = null
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
    DateTime? PremiumUntil
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
        entitlement?.Until
    )
    { }
}

// ⚠️ BUG CRITIQUE CORRIGÉ
// UpdateProfileRequest était un "record positionnel" ce qui génère des propriétés
// init-only ({ get; init; }). JsonPatch utilise la réflexion pour SET les valeurs
// APRÈS construction → impossible sur des init-only → le patch s'appliquait sur
// un objet figé, les modifications étaient silencieusement ignorées.
//
// SOLUTION : classe normale avec { get; set; }
// On garde la même interface publique, seule l'implémentation change.
public class UpdateProfileRequest
{
    public string? Username { get; set; }
    public string? Level { get; set; }
    public string? Specialite { get; set; }
    public string? Filiere { get; set; }
    public string? Language { get; set; }

    // Constructeur utilisé dans le controller pour initialiser depuis l'entité
    public UpdateProfileRequest(
        string? username,
        string? level,
        string? specialite,
        string? filiere,
        string? language)
    {
        Username = username;
        Level = level;
        Specialite = specialite;
        Filiere = filiere;
        Language = language;
    }
}