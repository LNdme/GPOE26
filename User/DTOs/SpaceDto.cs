using System.ComponentModel.DataAnnotations;
using User.Model;

namespace User.DTOs;

// ══════════════════════════════════════════════════════════════════════════════
// Espaces d'établissement et d'enseignant indépendant (docs/PLAN-ETABLISSEMENT.md, lot 2).
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>Un espace dont je fais partie, avec mes rôles actifs.</summary>
/// <summary>ShortCode : code de l'établissement, que l'élève saisit avec son matricule pour se connecter.</summary>
public record MySpaceDto(Guid Id, string Name, OrganizationType Type, string? City, List<OrganizationRole> Roles, string? ShortCode = null);

public record SchoolYearDto(Guid Id, string Label, DateOnly StartsOn, DateOnly EndsOn, bool IsCurrent)
{
    public SchoolYearDto(SchoolYear y) : this(y.Id, y.Label, y.StartsOn, y.EndsOn, y.IsCurrent) { }
}

/// <summary>
/// Tableau de bord du responsable. Seats : places élèves de la licence en cours (vide sans licence) ;
/// StudentsActive : élèves dont le compte est activé (ce que les places comptent).
/// StudentsPremium : ils ont l'accès Premium (licence en cours, ou formule de l'enseignant indépendant).
/// </summary>
public record SpaceOverviewDto(
    Guid Id,
    string Name,
    OrganizationType Type,
    string? City,
    SchoolYearDto? CurrentYear,
    int ClassCount,
    int TeacherCount,
    int StudentsActive,
    int StudentsPending,
    int? Seats,
    int? TeacherSeats,
    DateTime? LicenseEndsAt,
    bool StudentsPremium = false,
    string? ShortCode = null
);

/// <summary>Personne de l'espace : son rôle sur la plateforme (Role) et ses rôles dans l'espace (Roles).</summary>
public record SpaceMemberDto(Guid UserId, string Username, string? Email, UserRole Role, List<OrganizationRole> Roles, DateTime JoinedAt);

/// <summary>Ajout d'une personne qui a déjà un compte (les codes personnels arrivent au lot 3).</summary>
public record AddSpaceMemberRequest([Required, EmailAddress] string Email);

public record CreateSchoolYearRequest([Required] string Label);

public record SpaceClassRequest(
    [Required, MinLength(1), MaxLength(120)] string Name,
    string? Level = null,
    SchoolSubSystem? SubSystem = null,
    Guid? SchoolYearId = null
);

/// <summary>Subject vide : toutes matières. IsMain : professeur principal de la classe.</summary>
public record AssignTeacherRequest(Guid TeacherId, [MaxLength(80)] string? Subject = null, bool IsMain = false);

public record ClassTeacherDto(Guid TeacherId, string Username, string Subject, bool IsMain);

public record SpaceClassDto(
    Guid Id,
    string Name,
    ClassroomKind Kind,
    string? Level,
    SchoolSubSystem? SubSystem,
    Guid? SchoolYearId,
    int MemberCount,
    List<ClassTeacherDto> Teachers
);

/// <summary>Responsable d'un espace, vu par l'administration de la plateforme.</summary>
public record SpaceOwnerDto(Guid UserId, string Username, string? Email);

// ─── Élèves et codes personnels (lot 3) ───────────────────────────────────────

/// <summary>Élève de la liste. Code : son code encore valide (à imprimer), vide s'il n'en a pas.</summary>
public record StudentDto(
    Guid Id,
    string Matricule,
    string LastName,
    string FirstName,
    DateOnly? BirthDate,
    Guid? ClassroomId,
    string? ClassName,
    StudentStatus Status,
    Guid? UserId,
    string? Username,
    DateTime? ActivatedAt,
    string? Code,
    DateTime? CodeExpiresAt,
    string? Email = null,
    Dictionary<string, string>? Extra = null
);

/// <summary>Matricule vide : généré (AAAA-NNNN).</summary>
public record StudentRequest(
    [MaxLength(40)] string? Matricule,
    [Required, MinLength(1), MaxLength(100)] string LastName,
    [MaxLength(100)] string? FirstName = null,
    DateOnly? BirthDate = null,
    Guid? ClassroomId = null,
    [EmailAddress, MaxLength(256)] string? Email = null
);

/// <summary>Revoked : retiré par l'établissement ; Left : a quitté l'établissement ; Pending : réintégré (nouveau code à donner).</summary>
public record StudentStatusRequest(StudentStatus Status);

/// <summary>Commit faux : aperçu seulement. Vrai : enregistre, si aucune ligne n'est en erreur.</summary>
public record ImportStudentsRequest([Required, MaxLength(2_000_000)] string Csv, bool Commit = false);

public record ImportRowDto(int Line, string? Matricule, string LastName, string FirstName, string? ClassName, DateOnly? BirthDate, List<string> Errors,
    string? Email = null, Dictionary<string, string>? Extra = null);

/// <summary>
/// Résultat d'un import : Imported vrai quand les élèves ont été enregistrés.
/// Csv : le fichier Excel ou Word remis en CSV ; on le renvoie tel quel (ImportStudentsRequest) pour enregistrer après l'aperçu.
/// </summary>
public record ImportResultDto(int Total, int Valid, bool Imported, List<ImportRowDto> Rows, string? Csv = null, List<string>? ExtraColumns = null);

public record AccessCodeDto(
    Guid Id,
    string Code,
    AccessCodeKind Kind,
    AccessCodeStatus Status,
    DateTime ExpiresAt,
    Guid? StudentId,
    string? StudentName,
    string? Matricule,
    string? ClassName,
    DateTime CreatedAt
);

/// <summary>Codes « en blanc » pour une classe (enseignant indépendant, Q8) : l'élève saisit son nom à l'activation.</summary>
public record BlankCodesRequest(Guid ClassroomId, [Range(1, 100)] int Count);

public record CodeRequest([Required, MaxLength(32)] string Code);

/// <summary>Ce que donne un code, montré avant l'activation. NeedsName : code en blanc, le nom est à saisir.</summary>
public record CodeInfoDto(AccessCodeKind Kind, string OrganizationName, string? StudentName, string? Matricule, string? ClassName, bool NeedsName);

/// <summary>Connexion par matricule : School = code de l'établissement (ex. LYCDEMO) ; Password = mot de passe, ou code de la fiche.</summary>
public record MatriculeLoginRequest([Required, MaxLength(10)] string School, [Required, MaxLength(40)] string Matricule, [Required, MaxLength(128)] string Password);

/// <summary>Création du compte avec un code personnel. E-mail facultatif (Q4) ; nom pour un code en blanc.</summary>
public record ActivateCodeRequest(
    [Required, MaxLength(32)] string Code,
    [Required, MinLength(3), MaxLength(64)] string Username,
    [Required, MinLength(8)] string Password,
    [EmailAddress] string? Email = null,
    [MaxLength(100)] string? LastName = null,
    [MaxLength(100)] string? FirstName = null,
    string Language = "fr"
);
