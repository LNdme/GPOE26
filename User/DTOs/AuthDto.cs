using System.ComponentModel.DataAnnotations;
using User.Model;

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

public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(3), MaxLength(64)] string Username,
    [Required, MinLength(8)] string Password,
    UserRole Role = UserRole.Student,
    string? Level = null,
    string? Specialite = null,
    string? Filiere = null,
    string Language = "fr"
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
    DateTime CreatedAt
)
{
    public UserProfileDto(AppUser user) : this(
        user.Id,
        user.Email,
        user.Username,
        user.Role,
        user.Level,
        user.Specialite,
        user.Filiere,
        user.Language,
        user.CreatedAt
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