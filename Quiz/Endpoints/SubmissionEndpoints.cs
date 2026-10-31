using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Quiz.Data;
using Quiz.DTOs;
using Quiz.Model;
using Quiz.Service;

namespace Quiz.Endpoints;

/// <summary>Tentatives des élèves, copies et correction par l'enseignant.</summary>
public static class SubmissionEndpoints
{
    /// <summary>Marge accordée au-delà de la durée de composition (latence réseau, envoi hors ligne)</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    public static void MapSubmissionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/assessments/{id:guid}").WithTags("Copies").RequireAuthorization();

        // ═══ Élève ════════════════════════════════════════════════════════════════

        // ── POST /assessments/{id}/attempts ───────────────────────────────────────
        // Démarre une tentative, ou reprend celle en cours.
        group.MapPost("/attempts", async (Guid id, ClaimsPrincipal principal, EvaluationContext db, UserDirectoryClient users) =>
        {
            var studentId = principal.UserId();
            var assessment = (await AssessmentEndpoints.AssignedToStudentAsync(db, users)).FirstOrDefault(a => a.Id == id);
            if (assessment is null) return Results.NotFound();

            var attempts = await db.Submissions.Where(s => s.AssessmentId == id && s.StudentId == studentId).ToListAsync();
            if (attempts.FirstOrDefault(s => s.Status == SubmissionStatus.InProgress) is { } current)
                return Results.Ok(ToAttempt(current, assessment));

            var now = DateTime.UtcNow;
            if (assessment.Status == AssessmentStatus.Closed)
                return EndpointHelpers.Error(409, "Cette évaluation est clôturée.");
            if (assessment.OpensAt > now)
                return EndpointHelpers.Error(409, $"Cette évaluation ouvre le {assessment.OpensAt:dd/MM/yyyy HH:mm} (UTC).");
            if (assessment.DueAt <= now)
                return EndpointHelpers.Error(409, "La date limite est passée.");
            if (attempts.Count >= assessment.MaxAttempts)
                return EndpointHelpers.Error(409, "Vous avez utilisé toutes vos tentatives.");

            var submission = new Submission
            {
                AssessmentId = id,
                StudentId = studentId,
                AttemptNumber = attempts.Count + 1,
                MaxScore = assessment.MaxScore
            };
            db.Submissions.Add(submission);
            await db.SaveChangesAsync();
            return Results.Created($"/assessments/{id}/submissions/{submission.Id}", ToAttempt(submission, assessment));
        })
        .RequireAuthorization(Roles.Student)
        .WithSummary("Commencer (ou reprendre) une tentative")
        .Produces<AttemptDto>(201)
        .Produces(409);

        // ── POST /assessments/{id}/attempts/{submissionId}/submit ─────────────────
        group.MapPost("/attempts/{submissionId:guid}/submit", async (Guid id, Guid submissionId, SubmitAttemptRequest req,
            ClaimsPrincipal principal, EvaluationContext db) =>
        {
            var studentId = principal.UserId();
            var submission = await db.Submissions.Include(s => s.Answers)
                .FirstOrDefaultAsync(s => s.Id == submissionId && s.AssessmentId == id && s.StudentId == studentId);
            if (submission is null) return Results.NotFound();
            if (submission.Status != SubmissionStatus.InProgress)
                return EndpointHelpers.Error(409, "Cette copie a déjà été rendue.");

            var assessment = await db.Assessments.Include(a => a.Questions).FirstAsync(a => a.Id == id);
            var now = DateTime.UtcNow;
            var inputs = (req.Answers ?? new())
                .GroupBy(a => a.QuestionId)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var question in assessment.Questions)
            {
                inputs.TryGetValue(question.Id, out var input);
                var grade = Grader.Grade(question, input);
                submission.Answers.Add(new SubmissionAnswer
                {
                    SubmissionId = submission.Id,
                    QuestionId = question.Id,
                    SelectedOptions = input?.SelectedOptions?.Distinct().ToList() ?? new(),
                    TrueFalse = input?.TrueFalse,
                    Text = input?.Text,
                    AttachmentUrl = input?.AttachmentUrl,
                    IsCorrect = grade.IsCorrect,
                    Points = grade.Points,
                    NeedsManualGrading = grade.NeedsManualGrading
                });
            }

            submission.SubmittedAt = now;
            submission.MaxScore = assessment.MaxScore;
            submission.IsLate = assessment.DueAt < now - Grace
                || (assessment.DurationMinutes is { } minutes && now > submission.StartedAt.AddMinutes(minutes) + Grace);
            Recompute(submission);

            await db.SaveChangesAsync();
            return Results.Ok(ToDto(submission, assessment, teacherView: false));
        })
        .RequireAuthorization(Roles.Student)
        .WithSummary("Rendre sa copie (correction automatique immédiate)")
        .Produces<SubmissionDto>()
        .Produces(409);

        // ═══ Enseignant et élève ══════════════════════════════════════════════════

        // ── GET /assessments/{id}/submissions/{submissionId} ──────────────────────
        group.MapGet("/submissions/{submissionId:guid}", async (Guid id, Guid submissionId, ClaimsPrincipal principal, EvaluationContext db) =>
        {
            var userId = principal.UserId();
            var submission = await db.Submissions.Include(s => s.Answers).Include(s => s.Assessment).ThenInclude(a => a.Questions)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == submissionId && s.AssessmentId == id);
            if (submission is null) return Results.NotFound();

            var isTeacher = submission.Assessment.TeacherId == userId;
            if (!isTeacher && submission.StudentId != userId) return Results.NotFound();
            return Results.Ok(ToDto(submission, submission.Assessment, teacherView: isTeacher));
        })
        .WithSummary("Une copie (correction selon les règles de publication pour l'élève)")
        .Produces<SubmissionDto>();

        // ═══ Enseignant ═══════════════════════════════════════════════════════════

        // ── GET /assessments/{id}/submissions ─────────────────────────────────────
        group.MapGet("/submissions", async (Guid id, ClaimsPrincipal principal, EvaluationContext db, UserDirectoryClient users) =>
        {
            var assessment = await db.OwnedAssessmentAsync(id, principal.UserId());
            if (assessment is null) return Results.NotFound();

            var submissions = await db.Submissions.Include(s => s.Answers)
                .Where(s => s.AssessmentId == id)
                .OrderBy(s => s.SubmittedAt ?? s.StartedAt)
                .AsNoTracking()
                .ToListAsync();

            var names = new Dictionary<Guid, string>();
            foreach (var classroomId in assessment.ClassroomIds)
                foreach (var m in await users.MembersAsync(classroomId) ?? new())
                    names[m.Id] = m.Username;

            return Results.Ok(submissions.Select(s => new SubmissionRowDto(
                s.Id, s.StudentId, names.GetValueOrDefault(s.StudentId, "(élève retiré de la classe)"), s.AttemptNumber, s.Status,
                s.StartedAt, s.SubmittedAt, s.IsLate, s.Score, s.MaxScore,
                ProgressService.Scale(s.Score, s.MaxScore, assessment.ScaleTo),
                s.Answers.Count(a => a.NeedsManualGrading))));
        })
        .RequireAuthorization(Roles.Teacher)
        .WithSummary("Copies rendues et en cours")
        .Produces<List<SubmissionRowDto>>();

        // ── PUT /assessments/{id}/submissions/{submissionId}/answers/{questionId}/grade ──
        // Note d'une réponse ouverte, ou correction manuelle d'une réponse notée automatiquement.
        group.MapPut("/submissions/{submissionId:guid}/answers/{questionId:guid}/grade", async (Guid id, Guid submissionId,
            Guid questionId, GradeAnswerRequest req, ClaimsPrincipal principal, EvaluationContext db) =>
        {
            var assessment = await db.OwnedAssessmentAsync(id, principal.UserId());
            if (assessment is null) return Results.NotFound();
            var question = assessment.Questions.FirstOrDefault(q => q.Id == questionId);
            var submission = await db.Submissions.Include(s => s.Answers)
                .FirstOrDefaultAsync(s => s.Id == submissionId && s.AssessmentId == id);
            var answer = submission?.Answers.FirstOrDefault(a => a.QuestionId == questionId);
            if (question is null || answer is null) return Results.NotFound();
            if (req.Points > question.Points)
                return EndpointHelpers.Error(400, $"La note ne peut pas dépasser {question.Points} point(s).");

            answer.Points = req.Points;
            answer.Feedback = req.Feedback;
            answer.NeedsManualGrading = false;
            answer.IsCorrect = req.Points == question.Points ? true : req.Points == 0 ? false : null;
            Recompute(submission!);

            await db.SaveChangesAsync();
            return Results.Ok(ToDto(submission!, assessment, teacherView: true));
        })
        .RequireAuthorization(Roles.Teacher)
        .WithSummary("Noter une réponse")
        .Produces<SubmissionDto>();

        // ── POST /assessments/{id}/submissions/{submissionId}/answers/{questionId}/ai-suggestion ──
        // Proposition de l'IA, enregistrée pour l'enseignant — jamais appliquée automatiquement.
        group.MapPost("/submissions/{submissionId:guid}/answers/{questionId:guid}/ai-suggestion", async (Guid id, Guid submissionId,
            Guid questionId, ClaimsPrincipal principal, EvaluationContext db, AiGradingService ai, ILogger<AiGradingService> logger,
            CancellationToken ct) =>
        {
            var assessment = await db.OwnedAssessmentAsync(id, principal.UserId());
            if (assessment is null) return Results.NotFound();
            if (!assessment.AiAssistedGrading)
                return EndpointHelpers.Error(400, "La correction assistée est désactivée pour cette évaluation.");

            var question = assessment.Questions.FirstOrDefault(q => q.Id == questionId);
            var answer = await db.SubmissionAnswers
                .FirstOrDefaultAsync(a => a.SubmissionId == submissionId && a.QuestionId == questionId && a.Submission.AssessmentId == id);
            if (question is null || answer is null) return Results.NotFound();
            if (question.Type != QuestionType.OpenEnded)
                return EndpointHelpers.Error(400, "La correction assistée concerne les réponses ouvertes.");
            if (string.IsNullOrWhiteSpace(answer.Text))
                return EndpointHelpers.Error(400, "La correction assistée ne lit pas encore les copies photographiées : réponse écrite requise.");

            try
            {
                var suggestion = await ai.SuggestAsync(question.Text, question.Criteria, question.Points, answer.Text, ct);
                answer.AiSuggestedPoints = suggestion.Points;
                answer.AiFeedback = suggestion.Feedback;
                await db.SaveChangesAsync(ct);
                return Results.Ok(new AiSuggestionDto(questionId, suggestion.Points, question.Points, suggestion.Feedback));
            }
            catch (Exception ex) when (ex is HttpRequestException or FormatException or System.Text.Json.JsonException or KeyNotFoundException)
            {
                logger.LogWarning(ex, "Correction assistée indisponible");
                return EndpointHelpers.Error(502, "L'IA n'a pas pu proposer de note. Réessayez ou notez manuellement.");
            }
        })
        .RequireAuthorization(Roles.Teacher)
        .WithSummary("Demander une proposition de note à l'IA")
        .Produces<AiSuggestionDto>()
        .Produces(502);
    }

    /// <summary>Note finale dès que toutes les réponses ont des points ; sinon copie en attente de correction.</summary>
    private static void Recompute(Submission s)
    {
        if (s.Answers.All(a => a.Points is not null))
        {
            s.Status = SubmissionStatus.Graded;
            s.Score = s.Answers.Sum(a => a.Points!.Value);
            s.GradedAt = DateTime.UtcNow;
        }
        else
        {
            s.Status = SubmissionStatus.Submitted;
            s.Score = null;
            s.GradedAt = null;
        }
    }

    private static AttemptDto ToAttempt(Submission s, Assessment a)
    {
        var questions = a.Questions.OrderBy(q => q.Order).ToList();
        if (a.Shuffle) questions = Grader.StableShuffle(questions, s.Id);

        DateTime? deadline = a.DurationMinutes is { } minutes ? s.StartedAt.AddMinutes(minutes) : null;
        if (a.DueAt is not null && (deadline is null || a.DueAt < deadline)) deadline = a.DueAt;

        return new AttemptDto(s.Id, a.Id, a.Title, s.AttemptNumber, s.StartedAt, deadline,
            questions.Select(q =>
            {
                // L'index d'origine est conservé : l'élève renvoie toujours l'index de l'option, même mélangée
                var options = q.Options.Select((text, index) => new OptionDto(index, text)).ToList();
                if (a.Shuffle) options = Grader.StableShuffle(options, s.Id, q.Order + 1);
                return new StudentQuestionDto(q.Id, q.Type, q.Text, q.Points, options, q.Unit);
            }).ToList());
    }

    private static SubmissionDto ToDto(Submission s, Assessment a, bool teacherView)
    {
        var released = a.ResultsReleased(DateTime.UtcNow);
        var showScore = teacherView || released;
        var showCorrection = teacherView || (released && a.ShowCorrection);
        var questions = a.Questions.ToDictionary(q => q.Id);

        var answers = s.Status == SubmissionStatus.InProgress ? null : s.Answers
            .Where(ans => questions.ContainsKey(ans.QuestionId))
            .OrderBy(ans => questions[ans.QuestionId].Order)
            .Select(ans =>
            {
                var q = questions[ans.QuestionId];
                return new AnswerReviewDto(q.Id, q.Order, q.Type, q.Text, q.Points,
                    ans.SelectedOptions, ans.TrueFalse, ans.Text, ans.AttachmentUrl,
                    showCorrection ? ans.IsCorrect : null,
                    showScore ? ans.Points : null,
                    ans.NeedsManualGrading,
                    showCorrection ? ans.Feedback : null,
                    showCorrection ? new AssessmentQuestionDto(q) : null,
                    teacherView ? ans.AiSuggestedPoints : null,
                    teacherView ? ans.AiFeedback : null);
            }).ToList();

        var score = showScore ? s.Score : null;
        return new SubmissionDto(s.Id, a.Id, a.Title, s.StudentId, s.AttemptNumber, s.Status,
            s.StartedAt, s.SubmittedAt, s.IsLate, released, score, s.MaxScore,
            ProgressService.Scale(score, s.MaxScore, a.ScaleTo), a.ScaleTo, answers);
    }
}
