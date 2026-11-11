using Quiz.Model;

namespace Quiz.Service;

/// <summary>
/// Règles de la bibliothèque d'établissement pour les devoirs et épreuves (phase 2, lot 8) :
/// qui voit un dépôt, et la copie, qui ne dépend jamais de son original (P2).
/// </summary>
public static class AssessmentLibrary
{
    /// <summary>
    /// Un dépôt n'est visible que si son auteur est encore enseignant actif de l'établissement :
    /// l'auteur qui part garde ses évaluations, et elles quittent la bibliothèque avec lui.
    /// </summary>
    public static bool IsVisibleIn(Assessment assessment, LibrarySpaceRef space) =>
        assessment.LibraryOrganizationIds.Contains(space.Id) && space.IsTeacher(assessment.TeacherId);

    /// <summary>
    /// Copie pour un nouvel auteur : brouillon, assignée à aucune classe, déposée nulle part, sans dates,
    /// reliée à son original. Les liens vers un cours sont retirés : ce cours appartient à l'auteur de
    /// l'original, et la copie ne doit pas en dépendre.
    /// </summary>
    public static Assessment Copy(Assessment source, Guid newTeacherId)
    {
        var copy = new Assessment
        {
            TeacherId = newTeacherId,
            Title = source.Title,
            Description = source.Description,
            Kind = source.Kind,
            Status = AssessmentStatus.Draft,
            Subject = source.Subject,
            DurationMinutes = source.DurationMinutes,
            MaxAttempts = source.MaxAttempts,
            Shuffle = source.Shuffle,
            ShowCorrection = source.ShowCorrection,
            AllowOffline = source.AllowOffline,
            AiAssistedGrading = source.AiAssistedGrading,
            ScaleTo = source.ScaleTo,
            SourceAssessmentId = source.Id,
        };
        copy.Questions = source.Questions.OrderBy(q => q.Order).Select(q => new AssessmentQuestion
        {
            AssessmentId = copy.Id,
            Order = q.Order,
            Type = q.Type,
            Text = q.Text,
            Points = q.Points,
            Options = [.. q.Options],
            CorrectOptions = [.. q.CorrectOptions],
            TrueFalseAnswer = q.TrueFalseAnswer,
            AcceptedAnswers = [.. q.AcceptedAnswers],
            NumericAnswer = q.NumericAnswer,
            Tolerance = q.Tolerance,
            Unit = q.Unit,
            Criteria = q.Criteria,
            Explanation = q.Explanation,
        }).ToList();
        return copy;
    }
}
