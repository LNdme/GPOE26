using System.Globalization;
using System.Text;

namespace Chat.Service;

// ══════════════════════════════════════════════════════════════════════════════
// Données partagées (docs/PLAN-ETABLISSEMENT-2.md, lot 12, décision P6) : qui voit quoi des notions fragiles
// relevées par le tuteur, et ce que le tuteur sait de l'école de l'élève. Règles pures, testées sans base.
// Les discussions avec le tuteur ne sortent jamais d'ici : seules les notions fragiles sont partagées.
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>Ce qu'une personne voit des notions fragiles d'une classe.</summary>
public enum InsightView
{
    /// <summary>Rien : élève, ou personne sans lien avec la classe.</summary>
    None,
    /// <summary>Enseignant de la classe : ses élèves, avec leur nom.</summary>
    Named,
    /// <summary>Responsable ou gestionnaire de l'établissement : chiffres d'ensemble, sans nom d'élève.</summary>
    Anonymous
}

/// <summary>Une notion fragile et le nombre d'élèves chez qui le tuteur l'a relevée.</summary>
public record NotionCount(string Notion, int Students);

/// <summary>Évaluation de l'élève, telle que la donne Quiz (/assessments/mine).</summary>
public record StudentAssessmentRef(Guid Id, string Title, string? Status, string? Kind, string? Subject, DateTime? DueAt,
    int AttemptsUsed, bool HasAttemptInProgress, decimal? BestScoreScaled = null, int ScaleTo = 20);

public static class InsightRules
{
    /// <summary>Un enseignant de la classe voit les noms ; un gestionnaire (qui n'y enseigne pas) voit les chiffres.</summary>
    public static InsightView ViewFor(string? myRole, bool isManager) =>
        myRole == "Teacher" ? InsightView.Named : isManager ? InsightView.Anonymous : InsightView.None;

    /// <summary>
    /// Notions d'un texte de notions fragiles (« dérivée d'un produit ; signe d'une fonction, limites »).
    /// Le tuteur les écrit en une phrase : on coupe sur les points-virgules, virgules, retours à la ligne et puces.
    /// </summary>
    public static List<string> Notions(string? weakPoints) =>
        string.IsNullOrWhiteSpace(weakPoints)
            ? []
            : weakPoints.Split([';', ',', '\n', '•', '·'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(n => n.Trim(' ', '.', '-', '*').Trim())
                .Where(n => n.Length > 1)
                .DistinctBy(Key)
                .ToList();

    /// <summary>
    /// Notions les plus fréquentes d'un groupe d'élèves (un élève compte une fois par notion), écrites comme
    /// le premier élève chez qui elles apparaissent ; à égalité, par ordre alphabétique.
    /// </summary>
    public static List<NotionCount> TopNotions(IEnumerable<string?> weakPointsByStudent, int take = 5) =>
        weakPointsByStudent
            .SelectMany(w => Notions(w))
            .GroupBy(Key)
            .Select(g => new NotionCount(Capitalize(g.First()), g.Count()))
            .OrderByDescending(n => n.Students).ThenBy(n => n.Notion, StringComparer.Create(new CultureInfo("fr-FR"), true))
            .Take(take)
            .ToList();

    /// <summary>
    /// Lignes ajoutées au contexte du tuteur : épreuves à venir (14 jours, pas encore rendues) et derniers
    /// résultats publiés. Les dates sont écrites à l'heure locale de l'élève.
    /// </summary>
    public static List<string> AssessmentLines(IEnumerable<StudentAssessmentRef> assessments, DateTime nowUtc, TimeZoneInfo zone)
    {
        var fr = new CultureInfo("fr-FR");
        var list = assessments.ToList();
        var lines = new List<string>();

        var upcoming = list
            .Where(a => a.Status == "Published" && a.DueAt is { } d && d > nowUtc && d <= nowUtc.AddDays(14)
                        && (a.AttemptsUsed == 0 || a.HasAttemptInProgress))
            .OrderBy(a => a.DueAt)
            .Take(5)
            .Select(a => $"{a.Title}{(a.Subject is { Length: > 0 } s ? $" ({s})" : "")}, {KindLabel(a.Kind)} pour le "
                         + TimeZoneInfo.ConvertTimeFromUtc(a.DueAt!.Value, zone).ToString("dddd d MMMM", fr))
            .ToList();
        if (upcoming.Count > 0) lines.Add($"Épreuves à venir : {string.Join(" ; ", upcoming)}");

        var results = list
            .Where(a => a.BestScoreScaled is not null)
            .OrderByDescending(a => a.DueAt ?? DateTime.MinValue)
            .Take(4)
            .Select(a => $"{a.Title} : {a.BestScoreScaled!.Value.ToString("0.#", fr)}/{a.ScaleTo}")
            .ToList();
        if (results.Count > 0) lines.Add($"Derniers résultats : {string.Join(" ; ", results)}");
        return lines;
    }

    /// <summary>« Terminale C (Lycée de démonstration) » ; l'établissement n'est pas répété pour plusieurs classes.</summary>
    public static string? ClassLine(IEnumerable<(string Name, string? Organization)> classes)
    {
        var list = classes.ToList();
        if (list.Count == 0) return null;
        var names = string.Join(", ", list.Select(c => c.Name));
        var schools = list.Select(c => c.Organization).OfType<string>().Where(o => o.Length > 0).Distinct().ToList();
        return schools.Count == 0 ? $"Classe : {names}" : $"Classe : {names} ({string.Join(", ", schools)})";
    }

    private static string KindLabel(string? kind) => kind switch
    {
        "GradedExam" => "épreuve notée",
        "MockExam" => "épreuve blanche",
        "Homework" => "devoir",
        _ => "entraînement"
    };

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], new CultureInfo("fr-FR")) + s[1..];

    /// <summary>Clé de regroupement : minuscules, sans accents ni espaces doublés.</summary>
    private static string Key(string notion)
    {
        var normalized = notion.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.IsWhiteSpace(ch) ? ' ' : ch);
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
