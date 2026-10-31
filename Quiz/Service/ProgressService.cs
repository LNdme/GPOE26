using Microsoft.EntityFrameworkCore;
using Quiz.Data;
using Quiz.DTOs;
using Quiz.Model;

namespace Quiz.Service;

/// <summary>
/// Résultats et maîtrise par cours, calculés à partir des copies.
///   - Résultat d'une évaluation = meilleure tentative corrigée (note ramenée au barème)
///   - Maîtrise d'un cours = points obtenus / points possibles sur les questions corrigées
///     liées à ce cours (question.CourseId, sinon assessment.CourseId), toutes tentatives confondues
///   - Moyenne = moyenne des résultats sur 20
/// Arrondis « à l'école » (81,25 % → 81,3 %), pas l'arrondi bancaire par défaut de .NET.
/// Pour un élève ou un parent, seuls les résultats publiés (ResultsReleased) sont pris en compte.
/// </summary>
public class ProgressService(EvaluationContext db)
{
    public async Task<Dictionary<Guid, StudentProgressDto>> ComputeAsync(
        IReadOnlyCollection<Guid> studentIds, IReadOnlyCollection<Assessment> assessments, bool onlyReleased)
    {
        var now = DateTime.UtcNow;
        var visible = assessments
            .Where(a => !onlyReleased || a.ResultsReleased(now))
            .ToDictionary(a => a.Id);
        var ids = visible.Keys.ToList();

        var submissions = await db.Submissions
            .Include(s => s.Answers)
            .Where(s => ids.Contains(s.AssessmentId) && studentIds.Contains(s.StudentId) && s.Status != SubmissionStatus.InProgress)
            .AsNoTracking()
            .ToListAsync();

        var result = new Dictionary<Guid, StudentProgressDto>();
        foreach (var studentId in studentIds)
        {
            var mine = submissions.Where(s => s.StudentId == studentId).ToList();

            var results = assessments.OrderBy(a => a.DueAt ?? a.PublishedAt ?? a.CreatedAt).Select(a =>
            {
                var attempts = mine.Where(s => s.AssessmentId == a.Id).ToList();
                var best = attempts.Where(s => s.Status == SubmissionStatus.Graded).MaxBy(s => s.Score);
                var shown = visible.ContainsKey(a.Id) ? best : null;
                var latest = attempts.MaxBy(s => s.SubmittedAt);
                return new AssessmentResultDto(a.Id, a.Title, a.Kind,
                    shown?.Status ?? latest?.Status,
                    shown is null ? null : Scale(shown.Score, shown.MaxScore, a.ScaleTo), a.ScaleTo,
                    latest?.SubmittedAt, latest?.IsLate ?? false);
            }).ToList();

            var mastery = mine
                .Where(s => visible.ContainsKey(s.AssessmentId))
                .SelectMany(s => s.Answers.Where(ans => ans.Points is not null).Select(ans =>
                {
                    var a = visible[s.AssessmentId];
                    var q = a.Questions.FirstOrDefault(q => q.Id == ans.QuestionId);
                    return (CourseId: q?.CourseId ?? a.CourseId, Earned: ans.Points!.Value, Max: q?.Points ?? 0);
                }))
                .Where(x => x.CourseId is not null && x.Max > 0)
                .GroupBy(x => x.CourseId!.Value)
                .Select(g =>
                {
                    var earned = g.Sum(x => x.Earned);
                    var max = g.Sum(x => x.Max);
                    return new CourseMasteryDto(g.Key, earned, max, (double)Math.Round(earned / max * 100, 1, MidpointRounding.AwayFromZero));
                })
                .ToList();

            var scored = results.Where(r => r.ScoreScaled is not null).ToList();
            double? average = scored.Count == 0
                ? null
                : Math.Round(scored.Average(r => (double)r.ScoreScaled!.Value / r.ScaleTo * 20), 2, MidpointRounding.AwayFromZero);

            result[studentId] = new StudentProgressDto(studentId, null, null, average, results, mastery);
        }
        return result;
    }

    public static decimal? Scale(decimal? score, decimal max, int scaleTo) =>
        score is null || max <= 0 ? null : Math.Round(score.Value / max * scaleTo, 2, MidpointRounding.AwayFromZero);
}
