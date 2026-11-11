namespace GPOE26.UI.Models;

// ══════════════════════════════════════════════════════════════════════════════
// Espaces d'établissement et d'enseignant indépendant (docs/PLAN-ETABLISSEMENT.md) :
// contrats du service User, enums en texte.
// ══════════════════════════════════════════════════════════════════════════════

public enum OrganizationType { School, Institution, Independent }
public enum OrganizationRole { Owner, Manager, Teacher }
public enum ClassroomRole { Teacher, Manager, Student }
public enum StudentStatus { Pending, Active, Revoked, Left }
public enum AccessCodeKind { Student, Teacher, Owner }
public enum AccessCodeStatus { Issued, Used, Revoked, Expired }

public record MySpace(Guid Id, string Name, OrganizationType Type, string? City, List<OrganizationRole> Roles)
{
    /// <summary>Responsable ou gestionnaire : peut ouvrir « Mon établissement ».</summary>
    public bool CanManage => Roles.Contains(OrganizationRole.Owner) || Roles.Contains(OrganizationRole.Manager);
    public bool IsOwner => Roles.Contains(OrganizationRole.Owner);
}

public record SchoolYear(Guid Id, string Label, DateOnly StartsOn, DateOnly EndsOn, bool IsCurrent);

public record SpaceOverview(Guid Id, string Name, OrganizationType Type, string? City, SchoolYear? CurrentYear,
    int ClassCount, int TeacherCount, int StudentsActive, int StudentsPending, int? Seats, int? TeacherSeats, DateTime? LicenseEndsAt);

public record SpaceMember(Guid UserId, string Username, string? Email, UserRole Role, List<OrganizationRole> Roles, DateTime JoinedAt);

public record ClassTeacher(Guid TeacherId, string Username, string Subject, bool IsMain);

public record SpaceClass(Guid Id, string Name, ClassroomKind Kind, string? Level, SchoolSubSystem? SubSystem, Guid? SchoolYearId,
    int MemberCount, List<ClassTeacher> Teachers);

public record SpaceClassRequest(string Name, string? Level = null, SchoolSubSystem? SubSystem = null, Guid? SchoolYearId = null);

public record AssignTeacherRequest(Guid TeacherId, string? Subject, bool IsMain);

public record SpaceStudent(Guid Id, string Matricule, string LastName, string FirstName, DateOnly? BirthDate, Guid? ClassroomId,
    string? ClassName, StudentStatus Status, Guid? UserId, string? Username, DateTime? ActivatedAt, string? Code, DateTime? CodeExpiresAt)
{
    public string FullName => $"{LastName} {FirstName}".Trim();
}

public record StudentRequest(string? Matricule, string LastName, string? FirstName = null, DateOnly? BirthDate = null, Guid? ClassroomId = null);

public record ImportRow(int Line, string? Matricule, string LastName, string FirstName, string? ClassName, DateOnly? BirthDate, List<string> Errors);

public record ImportResult(int Total, int Valid, bool Imported, List<ImportRow> Rows);

public record AccessCodeInfo(Guid Id, string Code, AccessCodeKind Kind, AccessCodeStatus Status, DateTime ExpiresAt,
    Guid? StudentId, string? StudentName, string? Matricule, string? ClassName, DateTime CreatedAt);

/// <summary>Ce que donne un code personnel, montré avant l'activation. NeedsName : code en blanc.</summary>
public record CodeInfo(AccessCodeKind Kind, string OrganizationName, string? StudentName, string? Matricule, string? ClassName, bool NeedsName);

public record ActivateCodeRequest(string Code, string Username, string Password, string? Email, string? LastName, string? FirstName, string Language = "fr");
