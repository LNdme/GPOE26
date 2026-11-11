using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Chat.Service;

/// <summary>
/// Profil renvoyé par GET /auth/me (service User). Les énumérations y sont écrites en texte.
/// Rempli à l'inscription guidée, il n'arrivait jamais aux agents : seuls le niveau et « supérieur » étaient lus dans le jeton.
/// </summary>
public record LearnerProfileRef(
    string? Role, string? Level, string? Specialite, string? Filiere, string? Language, string? SubSystem,
    string? ExamTarget, DateOnly? ExamDate, List<string>? FocusSubjects, int? DailyStudyMinutes,
    List<string>? TeachingSubjects, string? TeachingContext, string? OrganizationName, string? Stage);

/// <summary>Morceaux de consignes construits à partir du profil et du cours (fonctions pures, testées).</summary>
public static partial class AgentPrompts
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// Ce que l'agent doit savoir de l'apprenant pour adapter ses exemples, son niveau et sa langue.
    /// Les lignes sont des données (présentées entre balises), jamais des consignes.
    /// </summary>
    public static List<string> LearnerLines(LearnerProfileRef? p, DateOnly today)
    {
        var lines = new List<string>();
        if (p is null) return lines;

        var level = p.Level is { Length: > 0 } ? p.Level : null;
        var system = p.SubSystem switch { "Anglophone" => "sous-système anglophone", "Francophone" => "sous-système francophone", _ => null };
        if (level is not null || system is not null)
            lines.Add("Classe ou niveau : " + string.Join(", ", new[] { level, system }.Where(x => x is not null)));
        if (p.Stage == "Higher") lines.Add("Enseignement supérieur");
        if (p.Filiere is { Length: > 0 }) lines.Add($"Filière : {p.Filiere}");

        if (p.ExamTarget is { Length: > 0 })
        {
            var exam = $"Examen visé : {p.ExamTarget}";
            if (p.ExamDate is { } date)
            {
                var days = date.DayNumber - today.DayNumber;
                exam += days switch
                {
                    < 0 => $" (passé le {date.ToString("d MMMM yyyy", Fr)})",
                    0 => " (aujourd'hui)",
                    <= 60 => $", dans {days} jour(s) : période de révisions, privilégie la méthode et les exercices type examen",
                    _ => $", le {date.ToString("d MMMM yyyy", Fr)} (dans {days} jours)"
                };
            }
            lines.Add(exam);
        }

        if (p.FocusSubjects is { Count: > 0 } focus) lines.Add($"Matières à renforcer : {string.Join(", ", focus)}");
        if (p.DailyStudyMinutes is > 0 and var minutes) lines.Add($"Temps d'étude quotidien visé : {minutes} min");
        return lines;
    }

    /// <summary>Ce que l'assistant doit savoir de l'enseignant (il n'est pas toujours dans le secondaire camerounais).</summary>
    public static List<string> TeacherLines(LearnerProfileRef? p)
    {
        var lines = new List<string>();
        if (p is null) return lines;
        var subjects = p.TeachingSubjects is { Count: > 0 } list ? string.Join(", ", list) : p.Specialite;
        if (subjects is { Length: > 0 }) lines.Add($"Matières enseignées : {subjects}");
        var context = p.TeachingContext switch
        {
            "School" => "Enseigne dans un établissement scolaire",
            "Institution" => "Forme dans une institution (entreprise, centre de formation…)",
            "Independent" => "Enseignant indépendant (répétiteur, cours particuliers)",
            _ => null
        };
        if (context is not null) lines.Add(context + (p.OrganizationName is { Length: > 0 } org ? $" : {org}" : ""));
        return lines;
    }

    /// <summary>Langue des réponses : l'anglais pour un apprenant anglophone, sinon le français (ou la langue de la question).</summary>
    public static string LanguageRule(LearnerProfileRef? p) =>
        p?.Language == "en" || p?.SubSystem == "Anglophone"
            ? "Réponds en anglais : l'apprenant suit le sous-système anglophone (en français s'il t'écrit en français)."
            : "Réponds en français (en anglais si l'élève écrit en anglais).";

    // ─── Cours trop long pour le contexte du modèle ──────────────────────────────

    public record CourseExcerpt(string Text, string? Note);

    private record Part(string Title, string Content, int Index);

    /// <summary>
    /// Le cours entier s'il tient dans <paramref name="max"/> caractères. Sinon : le plan complet, puis les parties
    /// les plus proches de la question (dans l'ordre du cours), et une note pour que le tuteur sache ce qui manque.
    /// Avant, le cours était coupé à 40 000 caractères sans prévenir : sur un long PDF, la fin était invisible.
    /// </summary>
    public static CourseExcerpt SelectCourse(string text, string question, int max = 40_000)
    {
        if (text.Length <= max) return new CourseExcerpt(text, null);

        var parts = SplitParts(text);
        if (parts.Count <= 1)
            return new CourseExcerpt(text[..max] + "\n[…]",
                "Le cours est trop long : seul son début t'est donné. Si la question porte sur la suite, dis-le à l'élève.");

        var words = Significant(question);
        var plan = "Plan complet du cours :\n" + string.Join("\n", parts.Select(p => "- " + p.Title));
        var budget = max - plan.Length - 200;

        var chosen = new List<Part>();
        foreach (var part in parts.OrderByDescending(p => Score(p, words)).ThenBy(p => p.Index))
        {
            if (part.Content.Length <= budget)
            {
                chosen.Add(part);
                budget -= part.Content.Length + 2;
            }
            else if (chosen.Count == 0)
            {
                // La partie la plus utile est à elle seule trop longue : son début plutôt que rien
                // (avant, la première partie du cours était donnée, même si la question portait sur une autre)
                chosen.Add(part with { Content = part.Content[..Math.Max(1000, budget)] + "\n[…]" });
                budget = 0;
            }
        }

        var body = string.Join("\n\n", chosen.OrderBy(p => p.Index).Select(p => p.Content));
        var note = $"Le cours est trop long pour t'être donné en entier : tu as son plan complet et {chosen.Count} partie(s) sur {parts.Count}, "
            + "choisies d'après la question. Si la réponse se trouve dans une partie absente, dis-le et indique laquelle relire.";
        return new CourseExcerpt(plan + "\n\n" + body, note);
    }

    private static List<Part> SplitParts(string text)
    {
        var parts = new List<Part>();
        var matches = Heading().Matches(text);
        if (matches.Count == 0) return [new Part("(sans titre)", text, 0)];

        if (matches[0].Index > 0 && text[..matches[0].Index].Trim().Length > 0)
            parts.Add(new Part("Introduction", text[..matches[0].Index].Trim(), 0));
        for (var i = 0; i < matches.Count; i++)
        {
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            parts.Add(new Part(matches[i].Groups[1].Value.Trim(), text[matches[i].Index..end].Trim(), parts.Count));
        }
        return parts;
    }

    private static int Score(Part part, HashSet<string> words)
    {
        if (words.Count == 0) return 0;
        var content = Significant(part.Content);
        var title = Significant(part.Title);
        return words.Count(content.Contains) + 3 * words.Count(title.Contains);
    }

    private static readonly HashSet<string> StopWords =
    [
        "dans", "pour", "avec", "sans", "sont", "cette", "comment", "quelle", "quel", "quels", "quelles", "peux", "explique",
        "moi", "donne", "faire", "fait", "plus", "moins", "aussi", "mais", "donc", "alors", "tout", "tous", "elle", "elles",
        "nous", "vous", "leur", "leurs", "être", "etre", "avoir", "entre", "chaque", "cours", "partie", "what", "with", "this", "that"
    ];

    /// <summary>Mots d'au moins quatre lettres, en minuscules et sans accents, hors mots courants.</summary>
    public static HashSet<string> Significant(string text)
    {
        var normalized = new string(text.ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return Word().Matches(normalized).Select(m => m.Value).Where(w => w.Length >= 4 && !StopWords.Contains(w)).ToHashSet();
    }

    [GeneratedRegex(@"^#{1,3}[ \t]+(.+)$", RegexOptions.Multiline)] private static partial Regex Heading();
    [GeneratedRegex(@"[a-z0-9]+")] private static partial Regex Word();
}
