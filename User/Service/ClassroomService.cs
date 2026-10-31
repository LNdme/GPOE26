using Microsoft.EntityFrameworkCore;
using User.Data;
using User.Model;

namespace User.Service;

public class ClassroomService(UserContext db)
{
    /// <summary>
    /// Prépare une classe pour l'enseignant (sans SaveChanges). Un enseignant d'une institution
    /// non scolaire (entreprise, startup…) crée des groupes plutôt que des classes.
    /// </summary>
    public async Task<Classroom> CreateAsync(AppUser teacher, string name, string? level = null,
        SchoolSubSystem? subSystem = null, string? subject = null)
    {
        var classroom = new Classroom
        {
            Name = name.Trim(),
            Kind = teacher.TeachingContext == TeachingContext.Institution ? ClassroomKind.Group : ClassroomKind.Class,
            TeacherId = teacher.Id,
            OrganizationId = teacher.OrganizationId,
            Level = level,
            SubSystem = subSystem,
            Subject = subject ?? (teacher.TeachingSubjects.Count == 1 ? teacher.TeachingSubjects[0] : null),
            InviteCode = await UniqueInviteCodeAsync()
        };
        db.Classrooms.Add(classroom);
        return classroom;
    }

    public async Task<string> UniqueInviteCodeAsync()
    {
        while (true)
        {
            var code = CodeGenerator.InviteCode();
            var pending = db.ChangeTracker.Entries<Classroom>().Any(e => e.Entity.InviteCode == code);
            if (!pending && !await db.Classrooms.AnyAsync(c => c.InviteCode == code)) return code;
        }
    }

    /// <summary>Inscrit un élève dans la classe correspondant au code. Null si le code est invalide.</summary>
    public async Task<Classroom?> JoinAsync(AppUser student, string code)
    {
        var normalized = CodeGenerator.Normalize(code);
        var classroom = await db.Classrooms.FirstOrDefaultAsync(c => c.InviteCode == normalized && !c.IsArchived);
        if (classroom is null) return null;

        if (!await db.ClassroomMembers.AnyAsync(m => m.ClassroomId == classroom.Id && m.StudentId == student.Id))
            db.ClassroomMembers.Add(new ClassroomMember { ClassroomId = classroom.Id, StudentId = student.Id });

        return classroom;
    }
}
