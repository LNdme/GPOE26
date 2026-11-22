using GPOE26.UI.Models;

namespace GPOE26.UI.Services;

/// <summary>
/// Inscription par objectif (docs/PLAN-PUBLICS.md, lot P2) : toute personne qui veut apprendre passe par le même
/// parcours, quel que soit son âge ou son métier. Ce qu'on lui demande dépend de son niveau (collège ou lycée,
/// supérieur, vie active) et de son objectif (examen, compétence, curiosité). Règles pures, testées sans interface.
/// </summary>
public static class OnboardingRules
{
    // ── Où en est la personne (clé « niveau » du parcours) ──
    public const string Secondary = "secondaire";
    public const string Higher = "superieur";
    public const string Adult = "adulte";

    // ── Qui lui ouvre l'accès (clé « source ») ──
    /// <summary>La personne s'inscrit pour elle-même : gratuit, ou Premium quand elle veut.</summary>
    public const string Self = "moi";
    /// <summary>Un parent ou un proche lui a donné une clé familiale.</summary>
    public const string Family = "famille";
    /// <summary>École, entreprise ou formateur : code personnel, activé sur /code (il crée le compte).</summary>
    public const string Organization = "code";

    public static EducationStage? Stage(string? level) => level switch
    {
        Secondary => EducationStage.Secondary,
        Higher => EducationStage.Higher,
        _ => null
    };

    public static LearningGoal? Goal(string? goal) => Enum.TryParse<LearningGoal>(goal, out var g) ? g : null;

    /// <summary>
    /// Les questions d'examen (BEPC, bac, concours, certification…) ne sont posées qu'à qui en prépare un :
    /// un collégien curieux ou un développeur qui veut une compétence n'a pas à choisir « pas d'examen ».
    /// </summary>
    public static bool AsksExam(string? goal) => Goal(goal) == LearningGoal.Exam;

    /// <summary>Examen retenu : seulement pour l'objectif « examen », et jamais « Pas d'examen cette année ».</summary>
    public static string? ExamTarget(string? goal, string? chosen) =>
        AsksExam(goal) && !string.IsNullOrWhiteSpace(chosen) && !chosen.Trim().StartsWith("Pas d'", StringComparison.Ordinal)
            ? chosen.Trim()
            : null;

    /// <summary>Tranche de niveau envoyée à l'inscription : l'adulte est toujours « Adult » ; les autres, selon le pays.</summary>
    public static LevelBand? Band(string? level, LevelBand? chosenAtSchool) => level switch
    {
        Adult => LevelBand.Adult,
        Higher => LevelBand.Higher,
        Secondary => chosenAtSchool,
        _ => null
    };

    /// <summary>
    /// Les réponses qui dépendent du niveau, à effacer quand la personne change de niveau
    /// (sinon une classe de lycée restait enregistrée pour un adulte qui avait hésité).
    /// </summary>
    public static readonly string[] LevelAnswers =
        ["sys", "classe", "serie", "exam", "bande", "etab", "cycle", "filiere", "examtxt", "themes", "temps"];
}
