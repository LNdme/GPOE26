using Microsoft.EntityFrameworkCore;
using User.Data;
using User.Model;

namespace User.Service;

public enum CodeError
{
    None,
    NotFound,
    Expired,
    Used,
    Revoked,
    WrongRole,
    Full,
    NoLicense,
    AlreadyEnrolled,
    NameRequired,
    TeachersFull
}

/// <summary>Ce qu'un code donne, montré avant l'activation (« Nom Prénom — Tle C — Lycée X : c'est bien toi ? »).</summary>
public record CodeInfo(AccessCode Code, Organization Organization, OrganizationStudent? Student, Classroom? Classroom);

/// <summary>
/// Codes personnels à usage unique (docs/PLAN-ETABLISSEMENT.md, lot 3) : délivrance par le responsable,
/// activation par l'élève, l'enseignant ou le responsable désigné.
/// Activer un code élève consomme une place ; les places sont comptées sous verrou de l'espace.
/// </summary>
public class AccessCodeService(UserContext db, SpaceService spaces)
{
    /// <summary>Nouveau code pour un élève de la liste ; l'ancien code encore valide est révoqué (un seul à la fois).</summary>
    public async Task<AccessCode> IssueForStudentAsync(OrganizationStudent student, Guid? createdBy)
    {
        await db.AccessCodes
            .Where(c => c.OrganizationStudentId == student.Id && c.Status == AccessCodeStatus.Issued)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, AccessCodeStatus.Revoked));
        return await IssueAsync(student.OrganizationId, AccessCodeKind.Student, createdBy, studentId: student.Id);
    }

    /// <summary>Prépare un code (sans SaveChanges) : élève, élève « en blanc » d'une classe, enseignant ou responsable.</summary>
    public async Task<AccessCode> IssueAsync(Guid organizationId, AccessCodeKind kind, Guid? createdBy,
        Guid? studentId = null, Guid? classroomId = null)
    {
        var code = new AccessCode
        {
            Code = await UniqueCodeAsync(),
            OrganizationId = organizationId,
            Kind = kind,
            OrganizationStudentId = studentId,
            ClassroomId = classroomId,
            ExpiresAt = await spaces.CodeExpiryAsync(organizationId),
            CreatedByUserId = createdBy
        };
        db.AccessCodes.Add(code);
        return code;
    }

    /// <summary>Le code et ce qu'il donne, ou l'erreur qui empêche de l'activer (sans rien modifier).</summary>
    public async Task<(CodeError Error, CodeInfo? Info)> CheckAsync(string text)
    {
        var normalized = CodeGenerator.Normalize(text);
        var code = await db.AccessCodes.FirstOrDefaultAsync(c => c.Code == normalized);
        if (code is null) return (CodeError.NotFound, null);
        switch (code.Status)
        {
            case AccessCodeStatus.Used: return (CodeError.Used, null);
            case AccessCodeStatus.Revoked: return (CodeError.Revoked, null);
            case AccessCodeStatus.Expired: return (CodeError.Expired, null);
        }
        if (code.ExpiresAt <= DateTime.UtcNow) return (CodeError.Expired, null);

        var org = await db.Organizations.FirstAsync(o => o.Id == code.OrganizationId);
        var student = code.OrganizationStudentId is { } sid ? await db.OrganizationStudents.FirstOrDefaultAsync(s => s.Id == sid) : null;
        var classId = student?.ClassroomId ?? code.ClassroomId;
        var classroom = classId is { } cid ? await db.Classrooms.FirstOrDefaultAsync(c => c.Id == cid) : null;
        return (CodeError.None, new CodeInfo(code, org, student, classroom));
    }

    /// <summary>Le rôle du compte convient-il au code ? Élève ↔ code élève, enseignant ↔ code enseignant, pas d'élève responsable.</summary>
    public static bool RoleFits(AccessCodeKind kind, UserRole role) => kind switch
    {
        AccessCodeKind.Student => role == UserRole.Student,
        AccessCodeKind.Teacher => role == UserRole.Teacher,
        AccessCodeKind.Owner => role != UserRole.Student,
        _ => false
    };

    /// <summary>
    /// Active le code pour ce compte, dans la transaction de l'appelant (qui la valide).
    /// Code « en blanc » : lastName obligatoire. Revérifie tout sous verrou de l'espace.
    /// </summary>
    public async Task<CodeError> ApplyAsync(string text, AppUser user, string? lastName = null, string? firstName = null)
    {
        var (error, info) = await CheckAsync(text);
        if (error != CodeError.None) return error;
        var code = info!.Code;
        if (!RoleFits(code.Kind, user.Role)) return CodeError.WrongRole;

        await spaces.LockAsync(code.OrganizationId);
        await db.Entry(code).ReloadAsync();
        if (code.Status != AccessCodeStatus.Issued) return CodeError.Used;

        var orgId = code.OrganizationId;
        var school = info.Organization.Type != OrganizationType.Independent;
        switch (code.Kind)
        {
            case AccessCodeKind.Student:
            {
                var row = info.Student;
                // Un compte ne figure qu'une fois sur la liste d'un espace
                var otherRow = row?.Id ?? Guid.Empty;
                if (await db.OrganizationStudents.AnyAsync(s => s.OrganizationId == orgId && s.UserId == user.Id && s.Id != otherRow))
                    return CodeError.AlreadyEnrolled;

                if (row is null)
                {
                    if (string.IsNullOrWhiteSpace(lastName)) return CodeError.NameRequired;
                    row = new OrganizationStudent
                    {
                        OrganizationId = orgId,
                        Matricule = await spaces.NewMatriculeAsync(orgId),
                        LastName = lastName.Trim()[..Math.Min(lastName.Trim().Length, 100)],
                        FirstName = (firstName ?? "").Trim()[..Math.Min((firstName ?? "").Trim().Length, 100)],
                        ClassroomId = code.ClassroomId
                    };
                }

                if (row.Status != StudentStatus.Active)
                {
                    var limit = await spaces.SeatLimitAsync(orgId);
                    if (limit == 0) return CodeError.NoLicense;
                    if (await spaces.SeatsUsedAsync(orgId) >= limit) return CodeError.Full;
                }
                if (db.Entry(row).State == EntityState.Detached) db.OrganizationStudents.Add(row);

                row.UserId = user.Id;
                row.Status = StudentStatus.Active;
                row.ActivatedAt = DateTime.UtcNow;
                // E-mail donné par l'établissement : recopié sur un compte qui n'en a pas, s'il est libre (Q12)
                if (user.Email is null && row.Email is { } email && !await db.AppUsers.AnyAsync(u => u.Email == email && u.Id != user.Id))
                    user.Email = email;
                if (row.ClassroomId is { } classId)
                {
                    if (!await db.ClassroomMembers.AnyAsync(m => m.ClassroomId == classId && m.StudentId == user.Id))
                        db.ClassroomMembers.Add(new ClassroomMember { ClassroomId = classId, StudentId = user.Id });
                    user.Level ??= info.Classroom?.Level;
                    user.SubSystem ??= info.Classroom?.SubSystem;
                }
                if (school) user.OrganizationId = orgId;
                break;
            }
            case AccessCodeKind.Teacher:
                if (!await spaces.HasRoleAsync(orgId, user.Id, OrganizationRole.Teacher) && !await spaces.CanAddTeacherAsync(orgId))
                    return CodeError.TeachersFull;
                await spaces.GrantRoleAsync(orgId, user.Id, OrganizationRole.Teacher);
                // Ses classes créées ensuite vont dans cet établissement (et non dans un espace indépendant)
                if (school) user.OrganizationId = orgId;
                break;
            case AccessCodeKind.Owner:
                await spaces.GrantRoleAsync(orgId, user.Id, OrganizationRole.Owner);
                break;
        }

        code.Status = AccessCodeStatus.Used;
        code.UsedAt = DateTime.UtcNow;
        code.UsedByUserId = user.Id;
        await db.SaveChangesAsync();
        return CodeError.None;
    }

    /// <summary>
    /// Retire l'élève de l'espace (révoqué ou parti) : sa place est libérée, il quitte les classes de l'espace,
    /// son code encore valide est révoqué. Son compte reste, pour un usage individuel (choix 6).
    /// </summary>
    public async Task RemoveStudentAsync(OrganizationStudent row, StudentStatus status)
    {
        row.Status = status;
        if (row.UserId is { } userId)
        {
            var classIds = await db.Classrooms.Where(c => c.OrganizationId == row.OrganizationId).Select(c => c.Id).ToListAsync();
            await db.ClassroomMembers.Where(m => m.StudentId == userId && classIds.Contains(m.ClassroomId)).ExecuteDeleteAsync();
        }
        await db.AccessCodes.Where(c => c.OrganizationStudentId == row.Id && c.Status == AccessCodeStatus.Issued)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, AccessCodeStatus.Revoked));
    }

    /// <summary>Change la classe d'un élève ; s'il a un compte, il passe de l'ancienne classe à la nouvelle.</summary>
    public async Task MoveStudentAsync(OrganizationStudent row, Guid? classroomId)
    {
        if (row.ClassroomId == classroomId) return;
        if (row.UserId is { } userId && row.Status == StudentStatus.Active)
        {
            if (row.ClassroomId is { } old)
                await db.ClassroomMembers.Where(m => m.ClassroomId == old && m.StudentId == userId).ExecuteDeleteAsync();
            if (classroomId is { } next && !await db.ClassroomMembers.AnyAsync(m => m.ClassroomId == next && m.StudentId == userId))
                db.ClassroomMembers.Add(new ClassroomMember { ClassroomId = next, StudentId = userId });
        }
        row.ClassroomId = classroomId;
    }

    public static string Message(CodeError error) => error switch
    {
        CodeError.NotFound => "Code inconnu : vérifiez-le (lettres et chiffres, sans 0 ni O, sans 1 ni I).",
        CodeError.Expired => "Ce code a expiré : demandez-en un nouveau à votre établissement.",
        CodeError.Used => "Ce code a déjà servi. Si ce n'était pas vous, prévenez votre établissement.",
        CodeError.Revoked => "Ce code a été annulé : demandez le nouveau à votre établissement.",
        CodeError.WrongRole => "Ce code n'est pas fait pour ce type de compte (élève, enseignant).",
        CodeError.Full => "Toutes les places sont prises : prévenez votre établissement ou votre enseignant.",
        CodeError.NoLicense => "L'établissement n'a pas de licence en cours : prévenez-le.",
        CodeError.AlreadyEnrolled => "Ce compte figure déjà sur la liste de cet établissement.",
        CodeError.NameRequired => "Indiquez votre nom pour activer ce code.",
        CodeError.TeachersFull => "La licence de l'établissement ne couvre pas d'enseignant de plus : prévenez-le.",
        _ => ""
    };

    private async Task<string> UniqueCodeAsync()
    {
        while (true)
        {
            var code = CodeGenerator.AccessCode();
            if (!db.AccessCodes.Local.Any(c => c.Code == code) && !await db.AccessCodes.AnyAsync(c => c.Code == code)) return code;
        }
    }
}
