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
    /// <param name="occurredAt">
    /// Quand le signal a réellement eu lieu. Omis, c'est maintenant — le cas de la page
    /// d'étude, qui émet en direct.
    ///
    /// ⚠️ Ce paramètre est ce qui rend le travail hors ligne mesurable. L'application
    /// bureau remonte au retour du réseau des signaux vieux de plusieurs heures : les
    /// dater de l'instant de la synchronisation ferait apparaître une nuit entière de
    /// révision, et rangerait dans une même séance des travaux séparés par un sommeil.
    /// Toutes les règles — les trente minutes d'inactivité, le plafond de quatre-vingt-dix
    /// secondes — s'appliquent alors à l'heure déclarée.
    /// </param>
    public async Task<StudySession> RecordAsync(
        Guid studentId,
        Guid courseId,
        StudyActivity activity,
        DateTime? occurredAt = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var at = StudySession.ResolveSignalTime(occurredAt, now);

        // On rattache le signal à la séance ouverte À CE MOMENT-LÀ, pas à la dernière en
        // date : un lot rejoué dans le désordre ne doit pas coller un signal d'hier soir
        // à la séance de ce matin.
        var session = await db.StudySessions
            .Where(s => s.StudentId == studentId && s.CourseId == courseId && s.LastActivityAt <= at)
            .OrderByDescending(s => s.LastActivityAt)
            .FirstOrDefaultAsync(ct);

        if (session is null || !session.IsOpenAt(at))
        {
            session = new StudySession
            {
                StudentId = studentId,
                CourseId = courseId,
                StartedAt = at,
                LastActivityAt = at,
            };

            db.StudySessions.Add(session);
            logger.LogInformation("Nouvelle séance de révision : élève {StudentId}, cours {CourseId}, à {At}",
                studentId, courseId, at);
        }
        else
        {
            session.ActiveSeconds += session.CreditFor(at);
            session.LastActivityAt = at;
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
