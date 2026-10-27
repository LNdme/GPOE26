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
        // Tableau de l'enseignant : élèves × évaluations, et maîtrise par cours.
        group.MapGet("/classrooms/{classroomId:guid}", async (Guid classroomId, ClaimsPrincipal principal,
            EvaluationContext db, UserDirectoryClient users, ProgressService progress) =>
        {
            var members = await users.MembersAsync(classroomId);
            if (members is null) return Results.NotFound();

            var teacherId = principal.UserId();
            var assessments = await db.Assessments.Include(a => a.Questions)
                .Where(a => a.TeacherId == teacherId && a.Status != AssessmentStatus.Draft && a.ClassroomIds.Contains(classroomId))
                .AsNoTracking()
                .ToListAsync();

            var computed = await progress.ComputeAsync(members.Select(m => m.Id).ToList(), assessments, onlyReleased: false);
            var students = members.Select(m => computed[m.Id] with { Username = m.Username, LastLoginAt = m.LastLoginAt }).ToList();
            var headers = assessments.OrderBy(a => a.DueAt ?? a.PublishedAt ?? a.CreatedAt)
                .Select(a => new AssessmentHeaderDto(a.Id, a.Title, a.Kind, a.DueAt, a.CourseId)).ToList();

            return Results.Ok(new ClassroomProgressDto(classroomId, headers, students));
        })
        .RequireAuthorization(Roles.Teacher)
        .WithSummary("Suivi d'une classe")
        .Produces<ClassroomProgressDto>()
        .Produces(404);

        // ── GET /progress/me ──────────────────────────────────────────────────────
        group.MapGet("/me", async (ClaimsPrincipal principal, EvaluationContext db, UserDirectoryClient users, ProgressService progress) =>
        {
            var studentId = principal.UserId();
            var assessments = await AssessmentEndpoints.AssignedToStudentAsync(db, users);
            var computed = await progress.ComputeAsync([studentId], assessments, onlyReleased: true);
            return Results.Ok(computed[studentId]);
        })
        .RequireAuthorization(Roles.Student)
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
            return Results.Ok(computed[studentId] with { Username = child.Username });
        })
        .RequireAuthorization(Roles.Parent)
        .WithSummary("Résultats d'un enfant (parent)")
        .Produces<StudentProgressDto>()
        .Produces(404);
    }
}
