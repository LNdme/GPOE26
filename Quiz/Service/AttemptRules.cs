using Microsoft.EntityFrameworkCore;
using Quiz.Data;
using Quiz.Model;

namespace Quiz.Service;

/// <summary>
/// Heure limite d'une tentative, imposée par le serveur (A8). Avant, une tentative laissée « en cours » pouvait être
/// reprise et rendue après l'échéance ou la clôture, alors que la correction était déjà visible des autres élèves.
///
/// Règles :
///   - l'heure limite est la plus proche de l'échéance de l'évaluation et de la fin de la durée de composition ;
///   - une marge de deux minutes couvre la latence du réseau et l'envoi automatique de la page ;
///   - passé cette heure (ou une fois l'évaluation clôturée), plus rien ne change dans la copie : elle est rendue
///     avec le dernier brouillon enregistré, enregistré au plus tard quelques secondes avant.
/// </summary>
public static class AttemptRules
{
    /// <summary>Marge accordée au-delà de l'heure limite (latence réseau, envoi automatique)</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    /// <summary>Heure limite affichée à l'élève (sans la marge) ; null si l'évaluation n'en a pas.</summary>
    public static DateTime? Deadline(Assessment assessment, DateTime startedAt)
    {
        DateTime? deadline = assessment.DurationMinutes is { } minutes ? startedAt.AddMinutes(minutes) : null;
        if (assessment.DueAt is { } due && (deadline is null || due < deadline)) deadline = due;
        return deadline;
    }

    /// <summary>Au-delà de cette heure, le serveur n'accepte plus aucune modification de la copie.</summary>
    public static DateTime? Cutoff(Assessment assessment, DateTime startedAt) => Deadline(assessment, startedAt) + Grace;

    /// <summary>La copie ne peut plus changer : évaluation clôturée, ou heure limite (marge comprise) dépassée.</summary>
    public static bool IsOver(Assessment assessment, DateTime startedAt, DateTime now) =>
        assessment.Status == AssessmentStatus.Closed || Cutoff(assessment, startedAt) <= now;

    /// <summary>Copie rendue après l'heure limite (marge comprise) : signalée « en retard » à l'enseignant.</summary>
    public static bool IsLate(Assessment assessment, DateTime startedAt, DateTime submittedAt) =>
        Cutoff(assessment, startedAt) < submittedAt;

    /// <summary>
    /// Rend la copie : corrige chaque question à partir des réponses données, enregistre les points et le statut.
    /// <paramref name="submission"/> doit être chargée avec ses réponses, <paramref name="assessment"/> avec ses questions.
    /// </summary>
    public static async Task FinalizeAsync(EvaluationContext db, Submission submission, Assessment assessment,
        IEnumerable<AnswerInput>? answers, DateTime now, CancellationToken ct = default)
    {
        var photographed = (await db.AnswerPhotos.Where(p => p.SubmissionId == submission.Id)
            .Select(p => p.QuestionId).ToListAsync(ct)).ToHashSet();
        var inputs = (answers ?? [])
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
        submission.IsLate = IsLate(assessment, submission.StartedAt, now);
        Recompute(submission);
    }

    /// <summary>Corrigée entièrement (toutes les réponses ont des points) ou en attente de l'enseignant.</summary>
    public static void Recompute(Submission s)
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

    /// <summary>
    /// Rend, avec leur dernier brouillon, les tentatives encore en cours d'une évaluation (à sa clôture),
    /// ou de toutes les évaluations dont l'heure limite est passée depuis <paramref name="olderThan"/>.
    /// Renvoie le nombre de copies rendues.
    /// </summary>
    public static async Task<int> FinalizeExpiredAsync(EvaluationContext db, DateTime now, Guid? assessmentId = null,
        TimeSpan? olderThan = null, CancellationToken ct = default)
    {
        var query = db.Submissions
            .Include(s => s.Answers)
            .Include(s => s.Assessment).ThenInclude(a => a.Questions)
            .Where(s => s.Status == SubmissionStatus.InProgress);
        if (assessmentId is { } id)
            query = query.Where(s => s.AssessmentId == id);
        else
            // Préfiltre en base : une évaluation sans échéance ni durée n'expire jamais
            query = query.Where(s => s.Assessment.Status == AssessmentStatus.Closed || s.Assessment.DueAt != null || s.Assessment.DurationMinutes != null);

        var limit = now - (olderThan ?? TimeSpan.Zero);
        var finalized = 0;
        foreach (var submission in await query.ToListAsync(ct))
        {
            if (assessmentId is null && !IsOver(submission.Assessment, submission.StartedAt, limit)) continue;
            await FinalizeAsync(db, submission, submission.Assessment, AnswerDrafts.Parse(submission.DraftAnswers), now, ct);
            finalized++;
        }
        if (finalized > 0) await db.SaveChangesAsync(ct);
        return finalized;
    }
}

/// <summary>
/// Chaque minute, rend les copies abandonnées (onglet fermé, téléphone éteint) une fois l'heure limite passée :
/// l'enseignant les voit avec le dernier brouillon, au lieu d'une tentative « en cours » pour toujours.
/// Le délai supplémentaire laisse d'abord la page de l'élève les envoyer elle-même.
/// </summary>
public class ExpiredAttemptsWorker(IServiceScopeFactory scopes, ILogger<ExpiredAttemptsWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan LeaveToPage = TimeSpan.FromMinutes(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<EvaluationContext>();
                var count = await AttemptRules.FinalizeExpiredAsync(db, DateTime.UtcNow, olderThan: LeaveToPage, ct: stoppingToken);
                if (count > 0) logger.LogInformation("{Count} copie(s) abandonnée(s) rendue(s) avec leur brouillon", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Copies expirées non traitées, nouvel essai dans une minute");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
