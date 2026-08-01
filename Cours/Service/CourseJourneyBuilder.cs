using Cours.Model;
using GPOE26.Ai;

namespace Cours.Service;

/// <summary>
/// Construit le parcours d'un cours : Lire → Comprendre → Consolider.
///
/// Volontairement statique et sans dépendance : ce sont deux règles de décision, et
/// elles doivent pouvoir être testées sans base de données ni appel de modèle.
/// </summary>
public static class CourseJourneyBuilder
{
    /// <summary>Nombre de grandes parties à partir duquel on découpe le parcours.</summary>
    public const int PerSectionMinParts = 4;

    /// <summary>
    /// Longueur à partir de laquelle le découpage se justifie.
    ///
    /// Les deux conditions sont exigées ensemble : un cours de quatre titres mais d'une
    /// page se lit d'une traite, et le découper n'ajouterait que des clics.
    /// </summary>
    public const int PerSectionMinLength = 6_000;

    /// <summary>Nombre de questions d'un mini-test de partie.</summary>
    public const int MiniTestQuestions = 3;

    /// <summary>Nombre de questions d'un test portant sur tout le cours.</summary>
    public const int FinalTestQuestions = 8;

    public static JourneyMode DecideMode(string markdown, IReadOnlyList<MarkdownChunker.CoursePart> parts) =>
        parts.Count >= PerSectionMinParts && markdown.Length > PerSectionMinLength
            ? JourneyMode.PerSection
            : JourneyMode.Whole;

    /// <summary>
    /// Produit les étapes ordonnées d'un cours mis en forme.
    /// La première est immédiatement accessible, les suivantes attendent leur tour.
    /// </summary>
    public static (JourneyMode Mode, List<CourseStep> Steps) Build(Guid courseId, string markdown)
    {
        var parts = MarkdownChunker.TopParts(markdown);
        var mode = DecideMode(markdown, parts);
        var steps = new List<CourseStep>();
        var order = 0;

        CourseStep Add(StepKind kind, string title, string? headingPath = null) =>
            AddStep(steps, courseId, kind, title, headingPath, ref order);

        if (mode == JourneyMode.PerSection)
        {
            for (var i = 0; i < parts.Count; i++)
            {
                var label = $"Partie {i + 1} — {parts[i].Title}";
                Add(StepKind.Lecture, label, parts[i].Path);
                Add(StepKind.MiniTest, $"Test — {parts[i].Title}", parts[i].Path);
            }

            Add(StepKind.TestFinal, "Test de compréhension du cours");
        }
        else
        {
            Add(StepKind.Lecture, "Lire le cours");
            Add(StepKind.TestFinal, "Test de compréhension");
        }

        Add(StepKind.ExerciceOuvert, "Exercice de consolidation");
        Add(StepKind.QcmApplication, "QCM d'application");

        // Toujours en dernier : la question de synthèse ne vérifie pas un détail mais
        // ce que l'élève a retenu de l'ensemble. Elle n'a de sens qu'une fois le reste
        // du parcours derrière lui.
        Add(StepKind.Synthese, "Question de synthèse");

        // Seule la première étape est ouverte : le reste se déverrouille au fil des
        // validations, c'est ce qui donne au parcours sa direction.
        if (steps.Count > 0) steps[0].Status = StepStatus.Available;

        return (mode, steps);
    }

    /// <summary>Nombre de questions attendu pour une étape évaluée par QCM.</summary>
    public static int QuestionCountFor(StepKind kind) => kind switch
    {
        StepKind.MiniTest => MiniTestQuestions,
        StepKind.TestFinal => FinalTestQuestions,
        StepKind.QcmApplication => 5,
        _ => 0,
    };

    private static CourseStep AddStep(
        List<CourseStep> steps,
        Guid courseId,
        StepKind kind,
        string title,
        string? headingPath,
        ref int order)
    {
        var step = new CourseStep
        {
            CourseId = courseId,
            Kind = kind,
            Order = order++,
            Title = title.Length > 300 ? title[..300] : title,
            HeadingPath = headingPath,
            Status = StepStatus.Locked,
        };

        steps.Add(step);
        return step;
    }
}
