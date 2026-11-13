using System.Security.Claims;
using Chat.Data;
using Chat.Model;
using Chat.Service;
using Microsoft.EntityFrameworkCore;

namespace Chat.Endpoints;

/// <summary>Une notion fragile relevée chez un élève nommé (vue de l'enseignant).</summary>
public record StudentWeakPointsDto(Guid StudentId, string Username, List<string> Notions);

/// <summary>
/// Notions fragiles d'un cours publié pour la classe. Students : élèves de la classe ; Concerned : ceux chez qui
/// le tuteur en a relevé. ByStudent n'est rempli que pour un enseignant de la classe (vide pour un gestionnaire).
/// </summary>
public record CourseWeakPointsDto(Guid CourseId, string Title, string Subject, int Students, int Concerned,
    List<NotionCount> TopNotions, List<StudentWeakPointsDto> ByStudent);

/// <summary>Named : l'appelant enseigne dans la classe et voit les noms ; sinon des chiffres d'ensemble.</summary>
public record ClassWeakPointsDto(Guid ClassroomId, bool Named, List<CourseWeakPointsDto> Courses);

/// <summary>Notions fragiles d'un enfant pour un cours (vue du parent).</summary>
public record ChildCourseWeakPointsDto(Guid CourseId, string Title, string? Subject, List<string> Notions, DateTime LastStudiedAt);

/// <summary>
/// Données partagées (docs/PLAN-ETABLISSEMENT-2.md, lot 12, décision P6). Seules les notions fragiles relevées par
/// le tuteur sortent du service, jamais les discussions. Les droits sont demandés au service User (et la liste des
/// cours d'une classe au service Cours), avec le jeton de l'appelant. Un élève qui a coupé la mémoire du tuteur
/// n'apparaît nulle part.
/// </summary>
public static class InsightEndpoints
{
    private record ClassroomRef(Guid Id, string Name, string? MyRole, bool IsManager);
    private record MemberRef(Guid Id, string Username);
    private record ClassCourseRef(Guid Id, string Title, string Subject);
    private record ChildRef(Guid ChildId, string Username, string Status);

    public static void MapInsightEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/insights").WithTags("Données partagées").RequireAuthorization();

        // ── GET /insights/classrooms/{classroomId}/weak-points ────────────────────
        // Enseignant de la classe : ses élèves, nommés. Gestionnaire de l'établissement : chiffres, sans nom.
        // Seulement sur les cours publiés pour la classe : le travail personnel des élèves reste privé.
        group.MapGet("/classrooms/{classroomId:guid}/weak-points", async (Guid classroomId, TutorContext db, PlatformClients platform, CancellationToken ct) =>
        {
            // Droits lus sans cache : un enseignant retiré ou un élève parti ne doit plus compter, même une minute
            var classroom = await platform.GetJsonAsync<ClassroomRef>("user", $"/classrooms/{classroomId}", ct, useCache: false);
            var view = classroom is null ? InsightView.None : InsightRules.ViewFor(classroom.MyRole, classroom.IsManager);
            if (view == InsightView.None) return Error(404, "Classe introuvable.");

            var members = await platform.GetJsonAsync<List<MemberRef>>("user", $"/classrooms/{classroomId}/members", ct, useCache: false) ?? [];
            var courses = await platform.GetJsonAsync<List<ClassCourseRef>>("cours", $"/cours/classrooms/{classroomId}", ct, useCache: false) ?? [];
            var students = await WithMemoryAsync(db, members.Select(m => m.Id).ToList(), ct);
            var courseIds = courses.Select(c => c.Id).ToList();

            var states = await db.CourseMastery.AsNoTracking()
                .Where(m => students.Contains(m.UserId) && courseIds.Contains(m.CourseId) && m.WeakPoints != null && m.WeakPoints != "")
                .Select(m => new { m.UserId, m.CourseId, m.WeakPoints })
                .ToListAsync(ct);

            var names = members.ToDictionary(m => m.Id, m => m.Username);
            var result = courses.Select(c =>
            {
                var rows = states.Where(s => s.CourseId == c.Id).ToList();
                var byStudent = view == InsightView.Named
                    ? rows.Select(r => new StudentWeakPointsDto(r.UserId, names.GetValueOrDefault(r.UserId, "élève"), InsightRules.Notions(r.WeakPoints)))
                        .Where(s => s.Notions.Count > 0).OrderBy(s => s.Username).ToList()
                    : [];
                return new CourseWeakPointsDto(c.Id, c.Title, c.Subject, members.Count, rows.Count,
                    InsightRules.TopNotions(rows.Select(r => r.WeakPoints)), byStudent);
            }).ToList();

            return Results.Ok(new ClassWeakPointsDto(classroomId, view == InsightView.Named, result));
        })
        .WithSummary("Notions fragiles relevées par le tuteur dans une classe (enseignant : nommées ; gestionnaire : chiffres)")
        .Produces<ClassWeakPointsDto>()
        .Produces(404);

        // ── GET /insights/children/{childId}/weak-points ─────────────────────────
        // Parent dont le lien avec l'enfant est accepté : toutes ses notions fragiles (y compris sur ses cours personnels).
        group.MapGet("/children/{childId:guid}/weak-points", async (Guid childId, TutorContext db, PlatformClients platform, CancellationToken ct) =>
        {
            var children = await platform.GetJsonAsync<List<ChildRef>>("user", "/parents/children", ct, useCache: false) ?? [];
            if (!children.Any(c => c.ChildId == childId && c.Status == "Active")) return Error(404, "Enfant introuvable.");
            if ((await WithMemoryAsync(db, [childId], ct)).Count == 0) return Results.Ok(new List<ChildCourseWeakPointsDto>());

            var states = await db.CourseMastery.AsNoTracking()
                .Where(m => m.UserId == childId && m.WeakPoints != null && m.WeakPoints != "")
                .OrderByDescending(m => m.LastStudiedAt)
                .ToListAsync(ct);
            return Results.Ok(states
                .Select(s => new ChildCourseWeakPointsDto(s.CourseId, s.CourseTitle, s.Subject, InsightRules.Notions(s.WeakPoints), s.LastStudiedAt))
                .Where(s => s.Notions.Count > 0)
                .ToList());
        })
        .WithSummary("Notions fragiles d'un enfant (parent, lien accepté)")
        .Produces<List<ChildCourseWeakPointsDto>>()
        .Produces(404);
    }

    /// <summary>Élèves qui n'ont pas coupé la mémoire du tuteur : les autres n'ont rien à partager.</summary>
    private static async Task<HashSet<Guid>> WithMemoryAsync(TutorContext db, List<Guid> userIds, CancellationToken ct)
    {
        var off = await db.AgentProfiles.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId) && p.Kind == AgentKinds.Student && !p.MemoryEnabled)
            .Select(p => p.UserId)
            .ToListAsync(ct);
        return userIds.Except(off).ToHashSet();
    }

    private static IResult Error(int status, string message) => Results.Json(new { message }, statusCode: status);
}
