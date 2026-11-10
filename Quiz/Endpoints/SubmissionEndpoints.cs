using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Quiz.Data;
using Quiz.DTOs;
using Quiz.Model;
using Quiz.Service;
using GPOE26.ServiceDefaults.Llm;

namespace Quiz.Endpoints;

/// <summary>Tentatives des élèves, copies et correction par l'enseignant.</summary>
public static class SubmissionEndpoints
{
    /// <summary>Marge accordée au-delà de la durée de composition (latence réseau, envoi hors ligne)</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    private const long MaxPhotoBytes = 6 * 1024 * 1024;
    private const int MaxPhotosPerQuestion = 4;
    private static readonly string[] PhotoTypes = ["image/jpeg", "image/png", "image/webp"];

    private static Task<List<AnswerPhoto>> PhotosAsync(EvaluationContext db, Guid submissionId) =>
        db.AnswerPhotos.AsNoTracking().Where(p => p.SubmissionId == submissionId)
            .OrderBy(p => p.QuestionId).ThenBy(p => p.Page)
            .Select(p => new AnswerPhoto { Id = p.Id, QuestionId = p.QuestionId, Page = p.Page, Transcription = p.Transcription, TranscriptionPending = p.TranscriptionPending })
            .ToListAsync();

    private static async Task<List<LlmImage>> LlmPhotosAsync(EvaluationContext db, Guid submissionId, Guid questionId, CancellationToken ct) =>
        (await db.AnswerPhotos.AsNoTracking()
            .Where(p => p.SubmissionId == submissionId && p.QuestionId == questionId)
            .OrderBy(p => p.Page).ToListAsync(ct))
        .Select(p => new LlmImage(p.ContentType, p.Data)).ToList();

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
                return Results.Ok(ToAttempt(current, assessment, await PhotosAsync(db, current.Id)));

            if (assessment.Kind == AssessmentKind.MockExam && await users.FeatureAsync("mock-exams") is null)
                return EndpointHelpers.Error(403, "Les épreuves blanches sont réservées aux formules Premium.");

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
            return Results.Created($"/assessments/{id}/submissions/{submission.Id}", ToAttempt(submission, assessment, new()));
        })
        .RequireAuthorization(Roles.Student)
        .WithSummary("Commencer (ou reprendre) une tentative")
        .Produces<AttemptDto>(201)
        .Produces(409);

        // ── PUT /assessments/{id}/attempts/{submissionId}/draft ───────────────────
        // Enregistre les réponses en cours de saisie. Appelé toutes les quelques secondes par la page de composition :
        // une seule requête SQL, et seulement tant que la copie n'est pas rendue.
        group.MapPut("/attempts/{submissionId:guid}/draft", async (Guid id, Guid submissionId, SubmitAttemptRequest req,
            ClaimsPrincipal principal, EvaluationContext db) =>
        {
            var studentId = principal.UserId();
            var draft = AnswerDrafts.Serialize(req.Answers);
            var saved = await db.Submissions
                .Where(s => s.Id == submissionId && s.AssessmentId == id && s.StudentId == studentId && s.Status == SubmissionStatus.InProgress)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.DraftAnswers, draft));
            return saved == 0
                ? EndpointHelpers.Error(409, "Cette copie n'est plus en cours de composition.")
                : Results.NoContent();
        })
        .RequireAuthorization(Roles.Student)
        .WithSummary("Enregistrer le brouillon de sa copie")
        .Produces(204)
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
            var photographed = (await db.AnswerPhotos.Where(p => p.SubmissionId == submissionId)
                .Select(p => p.QuestionId).ToListAsync()).ToHashSet();
            var inputs = (req.Answers ?? new())
                .GroupBy(a => a.QuestionId)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var question in assessment.Questions)
            {
                inputs.TryGetValue(question.Id, out var input);
                var grade = Grader.Grade(question, input, photographed.Contains(question.Id));
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
            submission.DraftAnswers = null;
            submission.MaxScore = assessment.MaxScore;
            submission.IsLate = assessment.DueAt < now - Grace
                || (assessment.DurationMinutes is { } minutes && now > submission.StartedAt.AddMinutes(minutes) + Grace);
            Recompute(submission);

            await db.SaveChangesAsync();
            return Results.Ok(ToDto(submission, assessment, teacherView: false, await PhotosAsync(db, submission.Id)));
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
            return Results.Ok(ToDto(submission, submission.Assessment, teacherView: isTeacher, await PhotosAsync(db, submission.Id)));
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
            return Results.Ok(ToDto(submission!, assessment, teacherView: true, await PhotosAsync(db, submission!.Id)));
        })
        .RequireAuthorization(Roles.Teacher)
        .WithSummary("Noter une réponse")
        .Produces<SubmissionDto>();

        // ── POST /assessments/{id}/submissions/{submissionId}/answers/{questionId}/ai-suggestion ──
        // Proposition de l'IA, enregistrée pour l'enseignant — jamais appliquée automatiquement.
        group.MapPost("/submissions/{submissionId:guid}/answers/{questionId:guid}/ai-suggestion", async (Guid id, Guid submissionId,
            Guid questionId, ClaimsPrincipal principal, EvaluationContext db, AiGradingService ai, ILogger<AiGradingService> logger,
            AiQuota quota, CancellationToken ct) =>
        {
            if (await quota.RefusalAsync(principal.UserId(), AiFeatures.Grading, ct) is { } refusal)
                return EndpointHelpers.Error(403, refusal);
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
            var photos = await LlmPhotosAsync(db, submissionId, questionId, ct);
            if (string.IsNullOrWhiteSpace(answer.Text) && photos.Count == 0)
                return EndpointHelpers.Error(400, "Cette réponse est vide : rien à analyser.");

            try
            {
                var suggestion = await ai.SuggestAsync(question.Text, question.Criteria, question.Points, answer.Text, photos, ct);
                answer.AiSuggestedPoints = suggestion.Points;
                answer.AiFeedback = suggestion.Feedback;
                await db.SaveChangesAsync(ct);
                await quota.RecordAsync(principal.UserId(), AiFeatures.Grading, ct);
                return Results.Ok(new AiSuggestionDto(questionId, suggestion.Points, question.Points, suggestion.Feedback));
            }
            catch (Exception ex) when (ex is LlmUnavailableException or FormatException or System.Text.Json.JsonException)
            {
                logger.LogWarning(ex, "Correction assistée indisponible");
                return EndpointHelpers.Error(502, "L'IA n'a pas pu proposer de note. Réessayez ou notez manuellement.");
            }
        })
        .RequireAuthorization(Roles.Teacher)
        .WithSummary("Demander une proposition de note à l'IA")
        .Produces<AiSuggestionDto>()
        .Produces(502);

        // ═══ Copies photographiées (exercices traités sur papier) ═════════════════

        // ── POST /assessments/{id}/attempts/{submissionId}/questions/{questionId}/photos ──
        // L'élève photographie sa copie ; l'IA la lit pour qu'il vérifie la transcription avant de rendre.
        group.MapPost("/attempts/{submissionId:guid}/questions/{questionId:guid}/photos", async (Guid id, Guid submissionId,
            Guid questionId, IFormFile file, ClaimsPrincipal principal, EvaluationContext db, TranscriptionQueue transcriptions, CancellationToken ct) =>
        {
            var studentId = principal.UserId();
            var submission = await db.Submissions.Include(s => s.Assessment).ThenInclude(a => a.Questions)
                .FirstOrDefaultAsync(s => s.Id == submissionId && s.AssessmentId == id && s.StudentId == studentId, ct);
            if (submission is null) return Results.NotFound();
            if (submission.Status != SubmissionStatus.InProgress)
                return EndpointHelpers.Error(409, "Cette copie a déjà été rendue.");
            var question = submission.Assessment.Questions.FirstOrDefault(q => q.Id == questionId);
            if (question is null) return Results.NotFound();
            if (question.Type != QuestionType.OpenEnded)
                return EndpointHelpers.Error(400, "Les photos de copie concernent les réponses rédigées.");
            if (file.Length == 0 || file.Length > MaxPhotoBytes)
                return EndpointHelpers.Error(400, "La photo doit faire moins de 6 Mo.");
            if (!PhotoTypes.Contains(file.ContentType))
                return EndpointHelpers.Error(400, "Formats acceptés : JPEG, PNG ou WebP.");

            var pages = await db.AnswerPhotos.CountAsync(p => p.SubmissionId == submissionId && p.QuestionId == questionId, ct);
            if (pages >= MaxPhotosPerQuestion)
                return EndpointHelpers.Error(409, $"{MaxPhotosPerQuestion} photos au maximum par question.");

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct);
            // Enregistrée tout de suite : l'IA la lit en arrière-plan (l'élève continue de composer pendant ce temps)
            var photo = new AnswerPhoto
            {
                SubmissionId = submissionId, QuestionId = questionId, StudentId = studentId,
                Page = pages + 1, ContentType = file.ContentType, Data = stream.ToArray(), TranscriptionPending = true
            };
            db.AnswerPhotos.Add(photo);
            await db.SaveChangesAsync(ct);
            transcriptions.Enqueue(photo.Id);
            return Results.Ok(new AnswerPhotoDto(photo));
        })
        .DisableAntiforgery()
        .RequireAuthorization(Roles.Student)
        .WithSummary("Joindre la photo d'une copie à une réponse ouverte (lue ensuite par l'IA, en arrière-plan)")
        .Produces<AnswerPhotoDto>();

        // ── GET /assessments/{id}/attempts/{submissionId}/photos ──────────────────
        // Photos de la tentative et leur lecture : la page de composition l'interroge tant qu'une lecture est en cours.
        group.MapGet("/attempts/{submissionId:guid}/photos", async (Guid id, Guid submissionId, ClaimsPrincipal principal, EvaluationContext db) =>
        {
            var studentId = principal.UserId();
            if (!await db.Submissions.AnyAsync(s => s.Id == submissionId && s.AssessmentId == id && s.StudentId == studentId))
                return Results.NotFound();
            return Results.Ok((await PhotosAsync(db, submissionId)).Select(p => new AnswerPhotoDto(p)));
        })
        .RequireAuthorization(Roles.Student)
        .WithSummary("Photos de ma tentative et leur lecture par l'IA")
        .Produces<List<AnswerPhotoDto>>();

        // ── DELETE /assessments/{id}/attempts/{submissionId}/photos/{photoId} ─────
        group.MapDelete("/attempts/{submissionId:guid}/photos/{photoId:guid}", async (Guid id, Guid submissionId, Guid photoId,
            ClaimsPrincipal principal, EvaluationContext db) =>
        {
            var studentId = principal.UserId();
            var photo = await db.AnswerPhotos.FirstOrDefaultAsync(p => p.Id == photoId && p.SubmissionId == submissionId && p.StudentId == studentId);
            var submission = await db.Submissions.FirstOrDefaultAsync(s => s.Id == submissionId && s.AssessmentId == id);
            if (photo is null || submission is null) return Results.NotFound();
            if (submission.Status != SubmissionStatus.InProgress)
                return EndpointHelpers.Error(409, "Cette copie a déjà été rendue.");

            db.AnswerPhotos.Remove(photo);
            // Pages renumérotées pour rester continues
            var others = await db.AnswerPhotos.Where(p => p.SubmissionId == submissionId && p.QuestionId == photo.QuestionId && p.Id != photoId)
                .OrderBy(p => p.Page).ToListAsync();
            for (var i = 0; i < others.Count; i++) others[i].Page = i + 1;
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(Roles.Student)
        .WithSummary("Retirer une photo (avant de rendre la copie)");

        // ── GET /assessments/{id}/submissions/{submissionId}/photos/{photoId} ─────
        group.MapGet("/submissions/{submissionId:guid}/photos/{photoId:guid}", async (Guid id, Guid submissionId, Guid photoId,
            ClaimsPrincipal principal, EvaluationContext db) =>
        {
            var userId = principal.UserId();
            var photo = await db.AnswerPhotos.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == photoId && p.SubmissionId == submissionId);
            if (photo is null) return Results.NotFound();
            var teacherId = await db.Submissions.Where(s => s.Id == submissionId && s.AssessmentId == id)
                .Select(s => (Guid?)s.Assessment.TeacherId).FirstOrDefaultAsync();
            if (teacherId is null || (photo.StudentId != userId && teacherId != userId)) return Results.NotFound();
            return Results.File(photo.Data, photo.ContentType);
        })
        .WithSummary("Image d'une copie photographiée (l'élève auteur ou l'enseignant)");

        // ── POST /assessments/{id}/submissions/{submissionId}/answers/{questionId}/ai-feedback ──
        // L'élève demande à l'IA d'analyser sa démarche, une fois les résultats publiés (jamais pendant l'épreuve).
        group.MapPost("/submissions/{submissionId:guid}/answers/{questionId:guid}/ai-feedback", async (Guid id, Guid submissionId,
            Guid questionId, ClaimsPrincipal principal, EvaluationContext db, AiGradingService ai, ILogger<AiGradingService> logger,
            AiQuota quota, CancellationToken ct) =>
        {
            var studentId = principal.UserId();
            if (await quota.RefusalAsync(studentId, AiFeatures.Feedback, ct) is { } refusal)
                return EndpointHelpers.Error(403, refusal);
            var submission = await db.Submissions.Include(s => s.Assessment).ThenInclude(a => a.Questions)
                .FirstOrDefaultAsync(s => s.Id == submissionId && s.AssessmentId == id && s.StudentId == studentId, ct);
            if (submission is null) return Results.NotFound();
            var assessment = submission.Assessment;
            if (submission.Status == SubmissionStatus.InProgress || !assessment.ResultsReleased(DateTime.UtcNow))
                return EndpointHelpers.Error(409, "L'analyse sera disponible quand les résultats seront publiés.");
            if (!assessment.AiAssistedGrading || !assessment.ShowCorrection)
                return EndpointHelpers.Error(403, "Votre enseignant n'a pas activé l'analyse par l'IA pour cette évaluation.");

            var question = assessment.Questions.FirstOrDefault(q => q.Id == questionId);
            var answer = await db.SubmissionAnswers.FirstOrDefaultAsync(a => a.SubmissionId == submissionId && a.QuestionId == questionId, ct);
            if (question is null || answer is null) return Results.NotFound();
            if (question.Type != QuestionType.OpenEnded)
                return EndpointHelpers.Error(400, "L'analyse concerne les réponses rédigées ou photographiées.");

            var photos = await LlmPhotosAsync(db, submissionId, questionId, ct);
            if (string.IsNullOrWhiteSpace(answer.Text) && photos.Count == 0)
                return EndpointHelpers.Error(400, "Tu n'as pas répondu à cette question : rien à analyser.");

            try
            {
                var feedback = await ai.FeedbackAsync(question.Text, question.Explanation, question.Criteria, answer.Text, photos, ct);
                answer.StudentAiFeedback = feedback;
                await db.SaveChangesAsync(ct);
                await quota.RecordAsync(studentId, AiFeatures.Feedback, ct);
                return Results.Ok(new AiFeedbackDto(questionId, feedback));
            }
            catch (LlmUnavailableException ex)
            {
                logger.LogWarning(ex, "Analyse de copie indisponible");
                return EndpointHelpers.Error(502, "L'IA n'a pas pu analyser ta copie. Réessaie dans un instant.");
            }
        })
        .RequireAuthorization(Roles.Student)
        .WithSummary("Analyse de ma démarche par l'IA (après publication des résultats)")
        .Produces<AiFeedbackDto>();
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

    private static AttemptDto ToAttempt(Submission s, Assessment a, List<AnswerPhoto> photos)
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
            }).ToList(),
            photos.Select(p => new AnswerPhotoDto(p)).ToList(),
            AnswerDrafts.Parse(s.DraftAnswers));
    }

    private static SubmissionDto ToDto(Submission s, Assessment a, bool teacherView, List<AnswerPhoto> photos)
    {
        var photosByQuestion = photos.ToLookup(p => p.QuestionId);
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
                    teacherView ? ans.AiFeedback : showCorrection ? ans.StudentAiFeedback : null,
                    photosByQuestion[q.Id].OrderBy(p => p.Page).Select(p => new AnswerPhotoDto(p)).ToList());
            }).ToList();

        var score = showScore ? s.Score : null;
        return new SubmissionDto(s.Id, a.Id, a.Title, s.StudentId, s.AttemptNumber, s.Status,
            s.StartedAt, s.SubmittedAt, s.IsLate, released, score, s.MaxScore,
            ProgressService.Scale(score, s.MaxScore, a.ScaleTo), a.ScaleTo, answers);
    }
}
