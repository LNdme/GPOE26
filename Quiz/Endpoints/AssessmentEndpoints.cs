using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Quiz.Data;
using Quiz.DTOs;
using Quiz.Model;
using Quiz.Service;

namespace Quiz.Endpoints;

/// <summary>Création et cycle de vie des évaluations (brouillon → publiée → clôturée).</summary>
public static class AssessmentEndpoints
{
    public static void MapAssessmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/assessments").WithTags("Évaluations").RequireAuthorization();

        // ── POST /assessments ─────────────────────────────────────────────────────
        group.MapPost("", async (AssessmentInput input, ClaimsPrincipal principal, EvaluationContext db) =>
        {
            var assessment = new Assessment { TeacherId = principal.UserId(), Title = input.Title };
            Apply(assessment, input);
            db.Assessments.Add(assessment);
            await db.SaveChangesAsync();
            return Results.Created($"/assessments/{assessment.Id}", new AssessmentDto(assessment, withQuestions: true));
        })
        .RequireAuthorization(Roles.Teacher)
        .WithSummary("Créer une évaluation (brouillon)")
        .Produces<AssessmentDto>(201);

        // ── GET /assessments/mine ─────────────────────────────────────────────────
        // Enseignant : ses évaluations. Élève : celles publiées pour ses classes, avec son avancement.
        group.MapGet("/mine", async (ClaimsPrincipal principal, EvaluationContext db, UserDirectoryClient users) =>
        {
            var userId = principal.UserId();
            if (principal.IsInRole(Roles.Teacher))
            {
                var own = await db.Assessments.Include(a => a.Questions)
                    .Where(a => a.TeacherId == userId)
                    .OrderByDescending(a => a.UpdatedAt)
                    .AsNoTracking()
                    .ToListAsync();
                return Results.Ok(own.Select(a => new AssessmentDto(a, withQuestions: false)));
            }

            if (!principal.IsInRole(Roles.Student)) return Results.Forbid();

            var assigned = await AssignedToStudentAsync(db, users);
            var ids = assigned.Select(a => a.Id).ToList();
            var attempts = await db.Submissions
                .Where(s => s.StudentId == userId && ids.Contains(s.AssessmentId))
                .AsNoTracking()
                .ToListAsync();

            var now = DateTime.UtcNow;
            return Results.Ok(assigned.Select(a =>
            {
                var mine = attempts.Where(s => s.AssessmentId == a.Id).ToList();
                var best = a.ResultsReleased(now)
                    ? mine.Where(s => s.Status == SubmissionStatus.Graded).MaxBy(s => s.Score)
                    : null;
                return new StudentAssessmentDto(a.Id, a.Title, a.Description, a.Kind, a.Status, a.Subject,
                    a.OpensAt, a.DueAt, a.DurationMinutes, a.MaxAttempts,
                    mine.Count(s => s.Status != SubmissionStatus.InProgress),
                    mine.Any(s => s.Status == SubmissionStatus.InProgress),
                    a.Questions.Count, a.MaxScore, a.AllowOffline,
                    best is null ? null : ProgressService.Scale(best.Score, best.MaxScore, a.ScaleTo), a.ScaleTo);
            }));
        })
        .WithSummary("Mes évaluations")
        .Produces<List<AssessmentDto>>()
        .Produces<List<StudentAssessmentDto>>();

        // ── GET /assessments/{id} ─────────────────────────────────────────────────
        // L'enseignant reçoit tout (questions et réponses). L'élève assigné ne reçoit que
        // la fiche : les questions s'obtiennent en démarrant une tentative.
        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal principal, EvaluationContext db, UserDirectoryClient users) =>
        {
            var userId = principal.UserId();
            var own = await db.OwnedAssessmentAsync(id, userId);
            if (own is not null) return Results.Ok(new AssessmentDto(own, withQuestions: true));

            if (!principal.IsInRole(Roles.Student)) return Results.NotFound();
            var assigned = (await AssignedToStudentAsync(db, users)).FirstOrDefault(a => a.Id == id);
            return assigned is null ? Results.NotFound() : Results.Ok(new AssessmentDto(assigned, withQuestions: false));
        })
        .WithSummary("Détail d'une évaluation")
        .Produces<AssessmentDto>()
        .Produces(404);

        // ── PUT /assessments/{id} ─────────────────────────────────────────────────
        // Modifiable tant qu'aucun élève n'a commencé à composer.
        group.MapPut("/{id:guid}", async (Guid id, AssessmentInput input, ClaimsPrincipal principal, EvaluationContext db) =>
        {
            var assessment = await db.OwnedAssessmentAsync(id, principal.UserId());
            if (assessment is null) return Results.NotFound();
            if (await db.Submissions.AnyAsync(s => s.AssessmentId == id))
                return EndpointHelpers.Error(409, "Des élèves ont déjà composé : l'évaluation ne peut plus être modifiée.");
            if (assessment.Status == AssessmentStatus.Closed)
                return EndpointHelpers.Error(409, "Une évaluation clôturée ne peut plus être modifiée.");

            db.AssessmentQuestions.RemoveRange(assessment.Questions);
            Apply(assessment, input);

            if (assessment.Status == AssessmentStatus.Published && Grader.ValidateForPublish(assessment) is { Count: > 0 } errors)
                return Results.BadRequest(new { message = "L'évaluation publiée doit rester complète.", errors });

            await db.SaveChangesAsync();
            return Results.Ok(new AssessmentDto(assessment, withQuestions: true));
        })
        .RequireAuthorization(Roles.Teacher)
        .WithSummary("Modifier une évaluation")
        .Produces<AssessmentDto>()
        .Produces(400)
        .Produces(409);

        // ── POST /assessments/{id}/publish ────────────────────────────────────────
        group.MapPost("/{id:guid}/publish", async (Guid id, ClaimsPrincipal principal, EvaluationContext db, UserDirectoryClient users) =>
        {
            var assessment = await db.OwnedAssessmentAsync(id, principal.UserId());
            if (assessment is null) return Results.NotFound();
            if (assessment.Status == AssessmentStatus.Closed)
                return EndpointHelpers.Error(409, "Cette évaluation est clôturée.");

            var errors = Grader.ValidateForPublish(assessment);
            var owned = (await users.MyClassroomsAsync()).Select(c => c.Id).ToHashSet();
            if (assessment.ClassroomIds.Any(c => !owned.Contains(c)))
                errors.Add("Vous ne pouvez assigner l'évaluation qu'à vos propres classes.");
            if (errors.Count > 0)
                return Results.BadRequest(new { message = "L'évaluation ne peut pas être publiée.", errors });

            if (assessment.Status == AssessmentStatus.Draft)
            {
                assessment.Status = AssessmentStatus.Published;
                assessment.PublishedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            return Results.Ok(new AssessmentDto(assessment, withQuestions: true));
        })
        .RequireAuthorization(Roles.Teacher)
        .WithSummary("Publier pour les classes sélectionnées")
        .Produces<AssessmentDto>()
        .Produces(400);

        // ── POST /assessments/{id}/close ──────────────────────────────────────────
        // Plus aucune tentative ne peut démarrer ; les résultats deviennent visibles aux élèves.
        group.MapPost("/{id:guid}/close", async (Guid id, ClaimsPrincipal principal, EvaluationContext db) =>
        {
            var assessment = await db.OwnedAssessmentAsync(id, principal.UserId());
            if (assessment is null) return Results.NotFound();
            if (assessment.Status != AssessmentStatus.Published)
                return EndpointHelpers.Error(409, "Seule une évaluation publiée peut être clôturée.");

            assessment.Status = AssessmentStatus.Closed;
            assessment.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new AssessmentDto(assessment, withQuestions: false));
        })
        .RequireAuthorization(Roles.Teacher)
        .WithSummary("Clôturer une évaluation");

        // ── DELETE /assessments/{id} ──────────────────────────────────────────────
        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal principal, EvaluationContext db) =>
        {
            var assessment = await db.OwnedAssessmentAsync(id, principal.UserId());
            if (assessment is null) return Results.NotFound();
            if (await db.Submissions.AnyAsync(s => s.AssessmentId == id))
                return EndpointHelpers.Error(409, "Des élèves ont composé : clôturez l'évaluation plutôt que de la supprimer.");

            db.Assessments.Remove(assessment);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(Roles.Teacher)
        .WithSummary("Supprimer une évaluation sans copie");
    }

    /// <summary>Évaluations publiées (ou clôturées) pour au moins une classe de l'élève connecté.</summary>
    public static async Task<List<Assessment>> AssignedToStudentAsync(EvaluationContext db, UserDirectoryClient users)
    {
        var classIds = (await users.MyClassroomsAsync()).Select(c => c.Id).ToList();
        if (classIds.Count == 0) return new();

        return await db.Assessments.Include(a => a.Questions)
            .Where(a => a.Status != AssessmentStatus.Draft && a.ClassroomIds.Any(c => classIds.Contains(c)))
            .OrderBy(a => a.DueAt ?? a.PublishedAt)
            .AsNoTracking()
            .ToListAsync();
    }

    private static void Apply(Assessment a, AssessmentInput input)
    {
        a.Title = input.Title.Trim();
        a.Kind = input.Kind;
        a.Description = input.Description;
        a.Subject = input.Subject;
        a.CourseId = input.CourseId;
        a.ClassroomIds = input.ClassroomIds?.Distinct().ToList() ?? new();
        a.OpensAt = input.OpensAt?.ToUniversalTime();
        a.DueAt = input.DueAt?.ToUniversalTime();
        a.DurationMinutes = input.DurationMinutes;
        a.MaxAttempts = input.MaxAttempts;
        a.Shuffle = input.Shuffle;
        a.ShowCorrection = input.ShowCorrection;
        a.AllowOffline = input.AllowOffline;
        a.AiAssistedGrading = input.AiAssistedGrading;
        a.ScaleTo = input.ScaleTo;
        a.UpdatedAt = DateTime.UtcNow;
        a.Questions = (input.Questions ?? new()).Select((q, i) => new AssessmentQuestion
        {
            AssessmentId = a.Id,
            Order = i,
            Type = q.Type,
            Text = q.Text.Trim(),
            Points = q.Points,
            Options = q.Options?.Select(o => o.Trim()).ToList() ?? new(),
            CorrectOptions = q.CorrectOptions?.Distinct().ToList() ?? new(),
            TrueFalseAnswer = q.TrueFalseAnswer,
            AcceptedAnswers = q.AcceptedAnswers?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList() ?? new(),
            NumericAnswer = q.NumericAnswer,
            Tolerance = q.Tolerance,
            Unit = q.Unit,
            Criteria = q.Criteria,
            Explanation = q.Explanation,
            CourseId = q.CourseId,
            SectionId = q.SectionId
        }).ToList();
    }
}
