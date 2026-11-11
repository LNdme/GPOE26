using System.ComponentModel.DataAnnotations;
using User.Model;

namespace User.DTOs;

// ══════════════════════════════════════════════════════════════════════════════
// Espaces d'établissement et d'enseignant indépendant (docs/PLAN-ETABLISSEMENT.md, lot 2).
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>Un espace dont je fais partie, avec mes rôles actifs.</summary>
public record MySpaceDto(Guid Id, string Name, OrganizationType Type, string? City, List<OrganizationRole> Roles);

public record SchoolYearDto(Guid Id, string Label, DateOnly StartsOn, DateOnly EndsOn, bool IsCurrent)
{
    public SchoolYearDto(SchoolYear y) : this(y.Id, y.Label, y.StartsOn, y.EndsOn, y.IsCurrent) { }
}

/// <summary>
/// Tableau de bord du responsable. Seats : places élèves de la licence en cours (vide sans licence) ;
/// StudentsActive : élèves dont le compte est activé (ce que les places compteront au lot 3).
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
    DateTime? LicenseEndsAt
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
