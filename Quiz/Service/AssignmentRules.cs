using Quiz.Model;

namespace Quiz.Service;

/// <summary>
/// Évaluations qu'une personne compose comme apprenante (docs/PLAN-APPRENANT-AU-CENTRE.md, lot J1).
/// Tout compte peut apprendre ; mais l'auteur d'une évaluation ne la compose jamais, même s'il a rejoint
/// une classe qu'elle vise : une seule copie suffirait à bloquer sa modification et sa suppression.
/// </summary>
public static class AssignmentRules
{
    public static List<Assessment> ForLearner(IEnumerable<Assessment> assigned, Guid userId) =>
        assigned.Where(a => a.TeacherId != userId).ToList();

    /// <summary>Début de la fenêtre d'une évaluation : ouverture, sinon publication, sinon création.</summary>
    public static DateTime WindowStart(Assessment a) => a.OpensAt ?? a.PublishedAt ?? a.CreatedAt;

    /// <summary>Fin de la fenêtre : échéance, sinon clôture (dernière mise à jour d'une évaluation clôturée), sinon ouverte.</summary>
    public static DateTime? WindowEnd(Assessment a) => a.DueAt ?? (a.Status == AssessmentStatus.Closed ? a.UpdatedAt : null);

    /// <summary>
    /// Une évaluation compte pour une période passée d'un élève si sa fenêtre la chevauche, ou s'il a déjà une copie
    /// (H2 : un élève arrivé en cours d'année garde les évaluations encore ouvertes qu'il a composées). Les
    /// évaluations publiées après son départ ne le concernent pas : l'enseignant les voit « hors période ».
    /// </summary>
    public static bool CountsFor(Assessment a, IEnumerable<PeriodRef> periods, bool hasSubmission)
    {
        if (hasSubmission) return true;
        var start = WindowStart(a);
        var end = WindowEnd(a);
        return periods.Any(p => start <= (p.LeftAt ?? DateTime.MaxValue) && (end ?? DateTime.MaxValue) >= p.JoinedAt);
    }
}
