using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
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

        // ── GET /classrooms/mine?as=learner|teacher|all ───────────────────────────
        // Selon l'appartenance réelle, plus selon le rôle du compte (lot J1) : une personne peut enseigner dans une
        // classe et apprendre dans une autre. learner : classes rejointes ; teacher : classes où elle est affectée
        // (quelle que soit la matière) ; sans paramètre : les deux, chacune avec sa place (MyRole).
        // Les gestionnaires voient les classes de leur espace par /organizations/{id}/classes.
        // Lot J3 : ?include=former ajoute les classes quittées (même archivées) et les périodes de l'utilisateur (MyPeriods).
        group.MapGet("/mine", async ([FromQuery(Name = "as")] string? view, [FromQuery] string? include, ClaimsPrincipal principal, UserContext db) =>
        {
            if (!ClassroomViewRules.TryParse(view, out var wanted))
                return Results.BadRequest(new { message = $"Valeur inconnue pour « as » : {view} (attendu : {ClassroomViewRules.Expected})." });
            if (!TryParseInclude(include, out var former))
                return Results.BadRequest(new { message = $"Valeur inconnue pour « include » : {include} (attendu : former)." });

            var user = await db.CurrentUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            var taughtIds = ClassroomViewRules.WantsTaught(wanted)
                ? await db.ClassroomTeachers.Where(t => t.TeacherId == user.Id).Select(t => t.ClassroomId).Distinct().ToListAsync()
                : [];
            var periods = ClassroomViewRules.WantsJoined(wanted)
                ? await (former ? db.ClassroomMembers.IgnoreQueryFilters() : db.ClassroomMembers)
                    .Where(m => m.StudentId == user.Id).Select(m => new { m.ClassroomId, m.JoinedAt, m.LeftAt }).ToListAsync()
                : [];
            var joinedIds = periods.Select(p => p.ClassroomId).Distinct().ToList();
            var currentIds = periods.Where(p => p.LeftAt is null).Select(p => p.ClassroomId).Concat(taughtIds).ToHashSet();
            var ids = taughtIds.Union(joinedIds).ToList();

            // Une classe quittée reste dans l'historique même archivée ensuite ; une classe actuelle archivée, non
            var rows = (await db.Classrooms
                .Where(c => ids.Contains(c.Id))
                .OrderBy(c => c.CreatedAt)
                .Select(c => new { Classroom = c, Count = c.Members.Count })
                .ToListAsync())
                .Where(r => !r.Classroom.IsArchived || (former && !currentIds.Contains(r.Classroom.Id)))
                .ToList();

            var taught = taughtIds.ToHashSet();
            var dtos = await ToDtosAsync(db, rows.Select(r => (r.Classroom, r.Count)).ToList(), user.Id,
                c => ClassroomViewRules.RoleOf(wanted, taught.Contains(c.Id)));
            if (former)
                dtos = dtos.Select(d => d with
                {
                    MyPeriods = periods.Where(p => p.ClassroomId == d.Id).OrderBy(p => p.JoinedAt)
                        .Select(p => new MemberPeriodDto(p.JoinedAt, p.LeftAt)).ToList()
                }).ToList();
            return Results.Ok(dtos);
        })
        .WithSummary("Mes classes (?as=learner : rejointes, ?as=teacher : enseignées, sinon les deux ; &include=former : et passées)")
        .Produces<List<ClassroomDto>>()
        .Produces(400);

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
        // Lot J3 : chaque élève vient avec ses périodes et ce qu'il partage (Scopes). ?include=former ajoute les anciens
        // élèves encore visibles (conservation en cours ; pour un enseignant, seulement ceux qu'il a eus), sans niveau
        // ni dernière connexion.
        group.MapGet("/{id:guid}/members", async (Guid id, [FromQuery] string? include, ClaimsPrincipal principal, UserContext db,
            ClassroomService classrooms, VisibilityService visibility) =>
        {
            if (!TryParseInclude(include, out var former))
                return Results.BadRequest(new { message = $"Valeur inconnue pour « include » : {include} (attendu : former)." });
            var userId = principal.GetUserId();
            var classroom = await db.Classrooms.FirstOrDefaultAsync(c => c.Id == id);
            if (classroom is null || userId is null) return Results.NotFound();
            var role = await classrooms.RoleInAsync(classroom, userId.Value);
            if (role is not (ClassroomRole.Teacher or ClassroomRole.Manager)) return Results.NotFound();

            var periods = await (former ? db.ClassroomMembers.IgnoreQueryFilters() : db.ClassroomMembers)
                .Where(m => m.ClassroomId == id)
                .Join(db.AppUsers, m => m.StudentId, u => u.Id, (m, u) => new { Member = m, Student = u })
                .AsNoTracking()
                .ToListAsync();

            // Un gestionnaire voit tous les anciens élèves ; un enseignant, ceux qui étaient là depuis son affectation
            var teacherSince = await db.ClassroomTeachers.Where(t => t.ClassroomId == id && t.TeacherId == userId)
                .Select(t => (DateTime?)t.AssignedAt).MinAsync();
            var viewer = role == ClassroomRole.Manager ? ViewerKind.Manager : ViewerKind.Teacher;
            var options = visibility.Options;
            var now = DateTime.UtcNow;
            var optOuts = await visibility.OrganizationOptOutsAsync(classroom.OrganizationId,
                periods.Select(p => p.Student.Id).Distinct().ToList());

            var members = new List<ClassroomMemberDto>();
            foreach (var rows in periods.GroupBy(p => p.Student.Id))
            {
                var student = rows.First().Student;
                var current = rows.Any(p => p.Member.LeftAt is null);
                DateTime? leftAt = current ? null : rows.Max(p => p.Member.LeftAt);
                var stillVisible = VisibilityRules.FormerStillVisible(leftAt, now, options);
                var knew = viewer == ViewerKind.Manager
                           || (teacherSince is { } since && VisibilityRules.TeacherKnewMember(since, leftAt));
                if (!current && (!stillVisible || !knew)) continue;

                var scopes = VisibilityRules.VisibleScopes(viewer, current, stillVisible, optOuts.GetValueOrDefault(student.Id) ?? []);
                var memberPeriods = rows.OrderBy(p => p.Member.JoinedAt).Select(p => new MemberPeriodDto(p.Member.JoinedAt, p.Member.LeftAt)).ToList();
                members.Add(current
                    ? new ClassroomMemberDto(student.Id, student.Username, student.Level, student.Filiere,
                        rows.Where(p => p.Member.LeftAt is null).Min(p => p.Member.JoinedAt), student.LastLoginAt,
                        Periods: memberPeriods, Scopes: VisibilityService.Names(scopes))
                    // Ancien élève : ni niveau ni dernière connexion (M1)
                    : new ClassroomMemberDto(student.Id, student.Username, null, null,
                        rows.Min(p => p.Member.JoinedAt), null, leftAt, VisibilityRules.VisibleUntil(leftAt, options),
                        memberPeriods, VisibilityService.Names(scopes)));
            }

            return Results.Ok(members.OrderBy(m => m.LeftAt is not null).ThenBy(m => m.Username).ToList());
        })
        .WithSummary("Élèves d'une classe (enseignants et gestionnaires) ; ?include=former : et les anciens encore visibles")
        .Produces<List<ClassroomMemberDto>>()
        .Produces(400);

        // ── DELETE /classrooms/{id}/members/{studentId} ───────────────────────────
        // Un enseignant de la classe ou un gestionnaire retire un élève, ou l'élève quitte la classe.
        group.MapDelete("/{id:guid}/members/{studentId:guid}", async (Guid id, Guid studentId, ClaimsPrincipal principal,
            UserContext db, ClassroomService classrooms) =>
        {
            var userId = principal.GetUserId();
            if (studentId != userId && !await CanTeachOrManageAsync(db, classrooms, id, userId)) return Results.NotFound();

            // Lot J2 : la période est close, plus supprimée (historique de l'élève, résultats gardés par la classe)
            var ended = await db.ClassroomMembers
                .Where(m => m.ClassroomId == id && m.StudentId == studentId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.LeftAt, DateTime.UtcNow));
            return ended == 0 ? Results.NotFound() : Results.NoContent();
        })
        .WithSummary("Retirer un élève / quitter la classe (la période d'appartenance est close, rien n'est effacé)");

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

    /// <summary>Vide : membres actuels ; « former » : aussi les anciens (lot J3). Toute autre valeur est refusée.</summary>
    private static bool TryParseInclude(string? include, out bool former)
    {
        former = include?.Trim().Equals("former", StringComparison.OrdinalIgnoreCase) == true;
        return former || string.IsNullOrWhiteSpace(include);
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
