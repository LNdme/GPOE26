using Microsoft.EntityFrameworkCore;
using User.Data;
using User.Model;

namespace User.Service;

public class ClassroomService(UserContext db, SpaceService spaces)
{
    /// <summary>
    /// Prépare une classe pour l'enseignant (sans SaveChanges). Un enseignant d'une institution
    /// non scolaire (entreprise, startup…) crée des groupes plutôt que des classes.
    /// La classe est rangée dans l'espace de l'enseignant (son établissement, sinon son espace indépendant),
    /// dans l'année en cours, et il en devient le professeur principal.
    /// </summary>
    public async Task<Classroom> CreateAsync(AppUser teacher, string name, string? level = null,
        SchoolSubSystem? subSystem = null, string? subject = null)
    {
        var (organizationId, schoolYearId) = await spaces.TeacherSpaceAsync(teacher);
        var classroom = new Classroom
        {
            Name = name.Trim(),
            Kind = teacher.TeachingContext == TeachingContext.Institution ? ClassroomKind.Group : ClassroomKind.Class,
            TeacherId = teacher.Id,
            OrganizationId = organizationId,
            SchoolYearId = schoolYearId,
            Level = level,
            SubSystem = subSystem,
            Subject = subject ?? (teacher.TeachingSubjects.Count == 1 ? teacher.TeachingSubjects[0] : null)
        };
        db.Classrooms.Add(classroom);
        db.ClassroomTeachers.Add(new ClassroomTeacher
        {
            ClassroomId = classroom.Id,
            TeacherId = teacher.Id,
            Subject = SubjectKey(classroom.Subject),
            IsMain = true
        });
        return classroom;
    }

    /// <summary>
    /// Affecte un enseignant à la classe pour une matière (ou change son statut de professeur principal).
    /// Le professeur principal est aussi Classroom.TeacherId, que lisent encore les écrans et les anciens clients.
    /// Une classe sans professeur principal prend le premier enseignant affecté.
    /// </summary>
    public async Task AssignTeacherAsync(Classroom classroom, Guid teacherId, string? subject, bool isMain)
    {
        var key = SubjectKey(subject);
        var rows = await db.ClassroomTeachers.Where(t => t.ClassroomId == classroom.Id).ToListAsync();
        var row = rows.FirstOrDefault(t => t.TeacherId == teacherId && t.Subject == key);
        if (row is null)
        {
            row = new ClassroomTeacher { ClassroomId = classroom.Id, TeacherId = teacherId, Subject = key };
            db.ClassroomTeachers.Add(row);
            rows.Add(row);
        }

        if (isMain || classroom.TeacherId is null || !rows.Any(t => t.IsMain))
        {
            foreach (var other in rows) other.IsMain = other == row;
            classroom.TeacherId = teacherId;
        }
    }

    /// <summary>
    /// Retire un enseignant de la classe, pour une matière ou pour toutes (subject null).
    /// S'il était professeur principal, le rôle passe au premier enseignant restant, sinon la classe n'en a plus.
    /// </summary>
    public async Task<bool> UnassignTeacherAsync(Classroom classroom, Guid teacherId, string? subject)
    {
        var rows = await db.ClassroomTeachers.Where(t => t.ClassroomId == classroom.Id).ToListAsync();
        var removed = rows.Where(t => t.TeacherId == teacherId && (subject is null || t.Subject == SubjectKey(subject))).ToList();
        if (removed.Count == 0) return false;

        db.ClassroomTeachers.RemoveRange(removed);
        var remaining = rows.Except(removed).OrderBy(t => t.AssignedAt).ToList();
        if (!remaining.Any(t => t.IsMain))
        {
            var next = remaining.FirstOrDefault(t => t.TeacherId == teacherId) ?? remaining.FirstOrDefault();
            if (next is not null) next.IsMain = true;
            classroom.TeacherId = next?.TeacherId;
        }
        return true;
    }

    /// <summary>
    /// Place de la personne dans la classe : enseignant (affecté), gestionnaire de l'espace, élève membre, ou rien (null).
    /// </summary>
    public async Task<ClassroomRole?> RoleInAsync(Classroom classroom, Guid userId)
    {
        if (await db.ClassroomTeachers.AnyAsync(t => t.ClassroomId == classroom.Id && t.TeacherId == userId))
            return ClassroomRole.Teacher;
        if (classroom.OrganizationId is { } orgId && await spaces.HasRoleAsync(orgId, userId, OrganizationRole.Owner, OrganizationRole.Manager))
            return ClassroomRole.Manager;
        if (await db.ClassroomMembers.AnyAsync(m => m.ClassroomId == classroom.Id && m.StudentId == userId))
            return ClassroomRole.Student;
        return null;
    }

    /// <summary>Matière d'une affectation : "" pour « toutes matières », 80 caractères au plus.</summary>
    public static string SubjectKey(string? subject) =>
        subject?.Trim() is { Length: > 0 } s ? s[..Math.Min(s.Length, 80)] : "";
}
