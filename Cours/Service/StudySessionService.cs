using Cours.Data;
using Cours.Model;
using Microsoft.EntityFrameworkCore;

namespace Cours.Service;

/// <summary>
/// Tient le journal des séances de révision.
///
/// Une séance regroupe l'activité continue d'un élève sur un cours ; trente minutes de
/// silence la closent. Ce découpage par inactivité est le seul qui reflète le travail
/// réel : compter du début à la fin de l'onglet ferait passer une nuit d'onglet oublié
/// pour huit heures de révision.
/// </summary>
public sealed class StudySessionService(CoursContext db, ILogger<StudySessionService> logger)
{
    /// <summary>
    /// Enregistre un signal d'activité, en le rattachant à la séance en cours ou en
    /// ouvrant la suivante.
    /// </summary>
    public async Task<StudySession> RecordAsync(
        Guid studentId,
        Guid courseId,
        StudyActivity activity,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var session = await db.StudySessions
            .Where(s => s.StudentId == studentId && s.CourseId == courseId)
            .OrderByDescending(s => s.LastActivityAt)
            .FirstOrDefaultAsync(ct);

        if (session is null || !session.IsOpenAt(now))
        {
            session = new StudySession
            {
                StudentId = studentId,
                CourseId = courseId,
                StartedAt = now,
                LastActivityAt = now,
            };

            db.StudySessions.Add(session);
            logger.LogInformation("Nouvelle séance de révision : élève {StudentId}, cours {CourseId}",
                studentId, courseId);
        }
        else
        {
            // On crédite l'écart réel entre deux signaux, plafonné : un signal tardif
            // (onglet en arrière-plan, machine en veille) ne doit pas gonfler la durée
            // d'un seul coup.
            var elapsed = (int)(now - session.LastActivityAt).TotalSeconds;
            session.ActiveSeconds += Math.Clamp(elapsed, 0, StudySession.MaxSecondsPerSignal);
            session.LastActivityAt = now;
        }

        switch (activity)
        {
            case StudyActivity.Question: session.QuestionsAsked++; break;
            case StudyActivity.Etape: session.StepsCompleted++; break;
            case StudyActivity.Exercice: session.ExercisesDone++; break;
            case StudyActivity.Lecture: break; // le signal de présence ne compte que du temps
        }

        await db.SaveChangesAsync(ct);
        return session;
    }

    /// <summary>Séances d'un élève, les plus récentes d'abord.</summary>
    public Task<List<StudySession>> RecentAsync(Guid studentId, int take = 30, CancellationToken ct = default) =>
        db.StudySessions
            .Where(s => s.StudentId == studentId)
            .OrderByDescending(s => s.StartedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);

    /// <summary>Séances d'un élève depuis une date, pour les totaux de la semaine.</summary>
    public Task<List<StudySession>> SinceAsync(Guid studentId, DateTime since, CancellationToken ct = default) =>
        db.StudySessions
            .Where(s => s.StudentId == studentId && s.StartedAt >= since)
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync(ct);
}
