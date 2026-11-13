using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using User.Data;
using User.DTOs;
using User.Helper;
using User.Model;
using User.Service;

namespace User.Endpoints;

/// <summary>
/// Classes vues par leurs enseignants (plusieurs, un par matière), leurs élèves et les gestionnaires de leur espace.
/// La gestion complète (créer pour l'établissement, affecter les enseignants) est dans OrganizationEndpoints.
/// Un élève entre dans une classe par son code personnel (AccessCodeEndpoints) : il n'y a plus de code de classe commun.
/// </summary>
public static class ClassroomEndpoints
{
    public static void MapClassroomEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/classrooms").WithTags("Classes").RequireAuthorization();

        // ── POST /classrooms ──────────────────────────────────────────────────────
        // Un enseignant crée sa classe dans son espace (son établissement, sinon son espace indépendant).
        group.MapPost("", async (CreateClassroomRequest req, ClaimsPrincipal principal, UserContext db, ClassroomService classrooms) =>
        {
            var teacher = await db.CurrentUserAsync(principal);
            if (teacher is null) return Results.Unauthorized();

            var classroom = await classrooms.CreateAsync(teacher, req.Name, req.Level, req.SubSystem, req.Subject);
            await db.SaveChangesAsync();
            var dto = (await ToDtosAsync(db, [(classroom, 0)], teacher.Id, _ => ClassroomRole.Teacher)).Single();
            return Results.Created($"/classrooms/{classroom.Id}", dto);
        })
        .RequireAuthorization(Policies.Teacher)
        .WithSummary("Créer une classe (ou un groupe)")
        .Produces<ClassroomDto>(201);

        // ── GET /classrooms/mine ──────────────────────────────────────────────────
        // Enseignant : les classes où il est affecté (quelle que soit la matière). Élève : celles qu'il a rejointes.
        // Les gestionnaires voient les classes de leur espace par /organizations/{id}/classes.
        group.MapGet("/mine", async (ClaimsPrincipal principal, UserContext db) =>
        {
            var user = await db.CurrentUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            var asTeacher = user.Role == UserRole.Teacher;
            var query = asTeacher
                ? db.Classrooms.Where(c => db.ClassroomTeachers.Any(t => t.ClassroomId == c.Id && t.TeacherId == user.Id))
                : db.Classrooms.Where(c => c.Members.Any(m => m.StudentId == user.Id));

            var rows = await query
                .Where(c => !c.IsArchived)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new { Classroom = c, Count = c.Members.Count })
                .ToListAsync();

            var role = asTeacher ? ClassroomRole.Teacher : ClassroomRole.Student;
            return Results.Ok(await ToDtosAsync(db, rows.Select(r => (r.Classroom, r.Count)).ToList(), user.Id, _ => role));
        })
        .WithSummary("Mes classes")
        .Produces<List<ClassroomDto>>();

        // ── GET /classrooms/{id} ──────────────────────────────────────────────────
        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal principal, UserContext db, ClassroomService classrooms, SpaceService spaces) =>
        {
            var userId = principal.GetUserId();
            var classroom = await db.Classrooms.FirstOrDefaultAsync(c => c.Id == id);
            if (classroom is null || userId is null || await classrooms.RoleInAsync(classroom, userId.Value) is not { } role)
                return Results.NotFound();

            var count = await db.ClassroomMembers.CountAsync(m => m.ClassroomId == id);
            var isManager = classroom.OrganizationId is { } orgId
                            && await spaces.HasRoleAsync(orgId, userId.Value, OrganizationRole.Owner, OrganizationRole.Manager);
            return Results.Ok((await ToDtosAsync(db, [(classroom, count)], userId.Value, _ => role)).Single() with { IsManager = isManager });
        })
        .WithSummary("Détail d'une classe (enseignant, gestionnaire ou membre)")
        .Produces<ClassroomDto>()
        .Produces(404);

        // ── GET /classrooms/{id}/members ──────────────────────────────────────────
        group.MapGet("/{id:guid}/members", async (Guid id, ClaimsPrincipal principal, UserContext db, ClassroomService classrooms) =>
        {
            if (!await CanTeachOrManageAsync(db, classrooms, id, principal.GetUserId())) return Results.NotFound();

            var members = await db.ClassroomMembers
                .Where(m => m.ClassroomId == id)
                .Join(db.AppUsers, m => m.StudentId, u => u.Id, (m, u) => new { Member = m, Student = u })
                .OrderBy(r => r.Student.Username)
                .Select(r => new ClassroomMemberDto(r.Student.Id, r.Student.Username, r.Student.Level,
                    r.Student.Filiere, r.Member.JoinedAt, r.Student.LastLoginAt))
                .ToListAsync();

            return Results.Ok(members);
        })
        .WithSummary("Élèves d'une classe (ses enseignants et les gestionnaires de l'espace)")
        .Produces<List<ClassroomMemberDto>>();

        // ── DELETE /classrooms/{id}/members/{studentId} ───────────────────────────
        // Un enseignant de la classe ou un gestionnaire retire un élève, ou l'élève quitte la classe.
        group.MapDelete("/{id:guid}/members/{studentId:guid}", async (Guid id, Guid studentId, ClaimsPrincipal principal,
            UserContext db, ClassroomService classrooms) =>
        {
            var userId = principal.GetUserId();
            if (studentId != userId && !await CanTeachOrManageAsync(db, classrooms, id, userId)) return Results.NotFound();

            var deleted = await db.ClassroomMembers
                .Where(m => m.ClassroomId == id && m.StudentId == studentId)
                .ExecuteDeleteAsync();
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        })
        .WithSummary("Retirer un élève / quitter la classe");

        // ── DELETE /classrooms/{id} ───────────────────────────────────────────────
        // Archivage par le professeur principal ou un gestionnaire : les élèves et l'historique sont conservés.
        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal principal, UserContext db, ClassroomService classrooms) =>
        {
            var userId = principal.GetUserId();
            var classroom = await db.Classrooms.FirstOrDefaultAsync(c => c.Id == id);
            if (classroom is null || userId is null) return Results.NotFound();

            var isMain = await db.ClassroomTeachers.AnyAsync(t => t.ClassroomId == id && t.TeacherId == userId && t.IsMain);
            if (!isMain && await classrooms.RoleInAsync(classroom, userId.Value) != ClassroomRole.Manager)
                return Results.NotFound();

            classroom.IsArchived = true;
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithSummary("Archiver une classe");
    }

    private static async Task<bool> CanTeachOrManageAsync(UserContext db, ClassroomService classrooms, Guid classroomId, Guid? userId)
    {
        if (userId is null) return false;
        var classroom = await db.Classrooms.FirstOrDefaultAsync(c => c.Id == classroomId);
        return classroom is not null
               && await classrooms.RoleInAsync(classroom, userId.Value) is ClassroomRole.Teacher or ClassroomRole.Manager;
    }

    /// <summary>DTO de chaque classe, avec ses enseignants et la place qu'y occupe l'utilisateur.</summary>
    private static async Task<List<ClassroomDto>> ToDtosAsync(UserContext db, List<(Classroom Classroom, int Count)> rows,
        Guid userId, Func<Classroom, ClassroomRole> roleOf)
    {
        var teachers = await OrganizationEndpoints.TeachersOfAsync(db, rows.Select(r => r.Classroom.Id).ToList());
        var orgIds = rows.Select(r => r.Classroom.OrganizationId).OfType<Guid>().Distinct().ToList();
        var spaces = await db.Organizations.Where(o => orgIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => new { o.Type, o.Name });
        return rows.Select(r =>
        {
            var c = r.Classroom;
            var list = teachers.GetValueOrDefault(c.Id) ?? [];
            var main = list.FirstOrDefault(t => t.IsMain);
            var role = roleOf(c);
            return new ClassroomDto(c.Id, c.Name, c.Kind, c.Level, c.SubSystem, c.Subject,
                main?.TeacherId ?? c.TeacherId ?? Guid.Empty, main?.Username ?? "", r.Count, c.CreatedAt,
                role,
                list.Where(t => t.TeacherId == userId).Select(t => t.Subject).ToList(),
                list, c.OrganizationId, c.SchoolYearId,
                c.OrganizationId is { } orgId && spaces.TryGetValue(orgId, out var space) ? space.Type : null,
                // Nom de l'établissement ou de l'espace : le tuteur le cite dans son contexte (lot 12)
                OrganizationName: c.OrganizationId is { } id && spaces.TryGetValue(id, out var named) ? named.Name : null);
        }).ToList();
    }
}
