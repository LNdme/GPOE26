using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Quiz.Data;
using Quiz.DTOs;
using Quiz.Model;
using Quiz.Service;

namespace Quiz.Endpoints;

/// <summary>Suivi des résultats et de la maîtrise par cours (enseignant, élève, parent).</summary>
public static class ProgressEndpoints
{
    public static void MapProgressEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/progress").WithTags("Suivi").RequireAuthorization();

        // ── GET /progress/classrooms/{classroomId} ────────────────────────────────
        // Tableau d'une classe : élèves × évaluations, et maîtrise par cours. Le service User décide qui y a accès
        // (enseignants de la classe, gestionnaires de son espace) : il ne donne la liste des élèves qu'à eux.
        // Un enseignant voit ses évaluations (sa matière) ; un gestionnaire, celles de tous les enseignants (lot 6).
        group.MapGet("/classrooms/{classroomId:guid}", async (Guid classroomId, ClaimsPrincipal principal,
            EvaluationContext db, UserDirectoryClient users, ProgressService progress) =>
        {
            var classroom = users.ClassroomAsync(classroomId);
            // Lot J4 : élèves actuels, et anciens encore visibles (le service User applique conservation et affectation)
            var membersTask = users.MembersWithFormerAsync(classroomId);
            var members = await membersTask;
            if (members is null) return Results.NotFound();

            var userId = principal.UserId();
            var query = db.Assessments.Include(a => a.Questions)
                .Where(a => a.Status != AssessmentStatus.Draft && a.ClassroomIds.Contains(classroomId));
            if (!ClassroomRef.SeesAllAssessments(await classroom))
                query = query.Where(a => a.TeacherId == userId);
            var assessments = await query.AsNoTracking().ToListAsync();

            var current = members.Where(m => m.LeftAt is null).ToList();
            var assessmentIds = assessments.Select(a => a.Id).ToList();
            var computed = await progress.ComputeAsync(current.Select(m => m.Id).ToList(), assessments, onlyReleased: false);
            var students = current.Select(m => Shared(computed[m.Id] with { Username = m.Username, LastLoginAt = m.LastLoginAt }, m)).ToList();

            // Anciens élèves : seulement les évaluations de leur période (ou déjà composées), jusqu'à la fin de la conservation
            foreach (var m in members.Where(m => m.LeftAt is not null))
            {
                var composed = (await db.Submissions.Where(s => s.StudentId == m.Id && assessmentIds.Contains(s.AssessmentId))
                    .Select(s => s.AssessmentId).Distinct().ToListAsync()).ToHashSet();
                var counted = assessments.Where(a => AssignmentRules.CountsFor(a, m.Periods ?? [], composed.Contains(a.Id))).ToList();
                var own = (await progress.ComputeAsync([m.Id], counted, onlyReleased: false))[m.Id];
                students.Add(Shared(own with
                {
                    Username = m.Username,
                    LastLoginAt = null,
                    LeftAt = m.LeftAt,
                    VisibleUntil = m.VisibleUntil,
                    OutOfPeriod = assessments.Where(a => !counted.Contains(a)).Select(a => a.Id).ToList()
                }, m));
            }
            var headers = assessments.OrderBy(a => a.DueAt ?? a.PublishedAt ?? a.CreatedAt)
                .Select(a => new AssessmentHeaderDto(a.Id, a.Title, a.Kind, a.DueAt, a.CourseId, a.Subject)).ToList();

            return Results.Ok(new ClassroomProgressDto(classroomId, headers, students));
        })
        .WithSummary("Suivi d'une classe (ses enseignants ; ses gestionnaires voient toutes les évaluations ; anciens élèves par période)")
        .Produces<ClassroomProgressDto>()
        .Produces(404);

        // ── GET /progress/me ──────────────────────────────────────────────────────
        group.MapGet("/me", async (ClaimsPrincipal principal, EvaluationContext db, UserDirectoryClient users, ProgressService progress) =>
        {
            var studentId = principal.UserId();
            // Lot J4 : ma progression garde les classes quittées (le départ ne détruit rien)
            var (assessments, _) = await AssessmentEndpoints.LearnerHistoryAsync(db, users, studentId);
            var computed = await progress.ComputeAsync([studentId], assessments, onlyReleased: true);
            // Sans suivi détaillé : les résultats restent visibles, pas la maîtrise par cours
            var detailed = await users.FeatureAsync("detailed-progress") is not null;
            return Results.Ok(detailed ? computed[studentId] : computed[studentId] with { Mastery = [] });
        })
        .WithSummary("Mes résultats et ma maîtrise par cours")
        .Produces<StudentProgressDto>();

        // ── GET /progress/students/{studentId} ────────────────────────────────────
        // Parent rattaché (lien accepté) : résultats publiés des évaluations faites par l'enfant.
        group.MapGet("/students/{studentId:guid}", async (Guid studentId, EvaluationContext db, UserDirectoryClient users,
            ProgressService progress) =>
        {
            var child = await users.ActiveChildAsync(studentId);
            if (child is null) return Results.NotFound();

            var assessments = await db.Assessments.Include(a => a.Questions)
                .Where(a => db.Submissions.Any(s => s.AssessmentId == a.Id && s.StudentId == studentId))
                .AsNoTracking()
                .ToListAsync();
            var computed = await progress.ComputeAsync([studentId], assessments, onlyReleased: true);
            var detailed = await users.FeatureAsync("family-view") is not null;
            // Lot J3 : un enfant majeur peut ne plus partager sa maîtrise avec ce parent
            var shares = child.Shares("Progress");
            return Results.Ok(computed[studentId] with
            {
                Username = child.Username,
                Mastery = detailed && shares ? computed[studentId].Mastery : [],
                ProgressHidden = !shares
            });
        })
        .RequireAuthorization(Roles.Parent)
        .WithSummary("Résultats d'un enfant (parent)")
        .Produces<StudentProgressDto>()
        .Produces(404);
    }

    /// <summary>La maîtrise n'apparaît que si l'élève la partage avec l'espace (lot J3) ; les résultats officiels toujours.</summary>
    private static StudentProgressDto Shared(StudentProgressDto progress, MemberRef member) =>
        member.Shares("Progress") ? progress : progress with { Mastery = [], ProgressHidden = true };
}
