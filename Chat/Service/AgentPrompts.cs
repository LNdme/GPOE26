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

    // ─── Réglage du modèle selon la matière (H11) ────────────────────────────────

    /// <summary>
    /// Température du tuteur : 0,2 pour les matières de calcul (une réponse sûre plutôt que variée), 0,4 ailleurs.
    /// </summary>
    public static double TemperatureFor(string? subject)
    {
        var normalized = new string((subject ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        var words = Word().Matches(normalized).Select(m => m.Value).ToList();
        return words.Any(w => w.StartsWith("math") || w.StartsWith("physi") || w.StartsWith("chimi") || w.StartsWith("statisti")
                              || w.StartsWith("algebr") || w.StartsWith("geometr") || w is "pct" or "calculus")
            ? 0.2
            : 0.4;
    }

    // ─── Passages du cours donnés au tuteur (H12) ─────────────────────────────────

    /// <summary>Au-delà, le tuteur reçoit le plan et les passages utiles plutôt que le cours entier.</summary>
    public const int CourseBudget = 16_000;

    /// <summary>Taille visée d'un passage : une partie plus longue est coupée entre deux paragraphes.</summary>
    public const int PassageChars = 1_800;

    public record CourseExcerpt(string Text, string? Note);

    /// <summary>Un passage du cours : sa place, le titre de sa partie, son texte, et son vecteur de sens s'il est connu.</summary>
    public record CoursePassage(int Position, string Title, string Content, float[]? Embedding = null);

    /// <summary>
    /// Le cours découpé en passages : une partie par titre (#, ##, ###), coupée entre deux paragraphes au-delà de
    /// <paramref name="size"/> caractères. Un texte sans titre est coupé de la même façon.
    /// </summary>
    public static List<CoursePassage> Passages(string text, int size = PassageChars)
    {
        var passages = new List<CoursePassage>();
        foreach (var (title, content) in SplitParts(text))
        {
            var piece = new StringBuilder();
            var continued = false;
            void Flush()
            {
                if (piece.Length == 0) return;
                passages.Add(new CoursePassage(passages.Count, continued ? title + " (suite)" : title, piece.ToString().Trim()));
                piece.Clear();
                continued = true;
            }

            foreach (var paragraph in content.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            {
                if (piece.Length > 0 && piece.Length + paragraph.Length > size) Flush();
                // Un paragraphe démesuré (texte d'un PDF sans saut de ligne) est coupé net
                for (var rest = paragraph; rest.Length > 0;)
                {
                    var take = Math.Min(rest.Length, size);
                    piece.Append(rest[..take]).Append("\n\n");
                    rest = rest[take..];
                    if (rest.Length > 0) Flush();
                }
            }
            Flush();
        }
        return passages;
    }

    /// <summary>Cours entier s'il tient dans <paramref name="max"/> caractères ; sinon plan et passages choisis par les mots.</summary>
    public static CourseExcerpt SelectCourse(string text, string question, int max = CourseBudget) =>
        text.Length <= max ? new CourseExcerpt(text, null) : SelectPassages(Passages(text), question, null, max);

    /// <summary>
    /// Plan complet du cours, puis les passages les plus proches de la question, remis dans l'ordre du cours sous le
    /// titre de leur partie, et une note pour que le tuteur sache ce qui manque. La proximité combine le sens
    /// (vecteurs, quand le modèle d'embeddings répond) et les mots de la question, les titres comptant davantage.
    /// </summary>
    public static CourseExcerpt SelectPassages(IReadOnlyList<CoursePassage> passages, string question, float[]? questionEmbedding, int max = CourseBudget)
    {
        if (passages.Count == 0) return new CourseExcerpt("", null);

        var titles = passages.Select(p => BaseTitle(p.Title)).Distinct().ToList();
        var plan = titles.Count > 1 ? Truncate("Plan complet du cours :\n" + string.Join("\n", titles.Select(t => "- " + t)), 3_000) + "\n\n" : "";
        var budget = max - plan.Length - 200;

        var scores = Rank(passages, question, questionEmbedding);
        var chosen = new List<CoursePassage>();
        foreach (var passage in passages.OrderByDescending(p => scores[p.Position]).ThenBy(p => p.Position))
        {
            var length = passage.Content.Length + passage.Title.Length + 8;
            if (length <= budget)
            {
                chosen.Add(passage);
                budget -= length;
            }
            else if (chosen.Count == 0)
            {
                chosen.Add(passage with { Content = passage.Content[..Math.Max(500, budget - passage.Title.Length - 8)] + "\n[…]" });
                budget = 0;
            }
        }

        var body = string.Join("\n\n", chosen.OrderBy(p => p.Position).Select(p =>
            p.Content.StartsWith('#') ? p.Content : $"### {p.Title}\n{p.Content}"));
        var note = titles.Count > 1
            ? $"Le cours est trop long pour t'être donné en entier : tu as son plan complet et {chosen.Count} passage(s) sur {passages.Count}, "
              + "choisis d'après la question. Si la réponse se trouve dans une partie absente, dis-le et indique laquelle relire."
            : $"Le cours est trop long pour t'être donné en entier : tu as {chosen.Count} passage(s) sur {passages.Count}, choisis d'après la question. "
              + "Si la réponse n'y est pas, dis-le à l'élève plutôt que de deviner ce que dit le cours.";
        return new CourseExcerpt(plan + body, note);
    }

    /// <summary>
    /// Note de chaque passage (par position) : 65 % le sens, 35 % la part des mots de la question qu'il contient
    /// (ceux du titre comptent double), ou ces mots seuls sans vecteurs. Les mots comptent en proportion de la question :
    /// rapportés au meilleur passage, un seul mot commun (« temps ») suffisait à l'emporter sur le sens.
    /// </summary>
    public static Dictionary<int, double> Rank(IReadOnlyList<CoursePassage> passages, string question, float[]? questionEmbedding)
    {
        var words = Significant(question);
        var semantic = questionEmbedding is null ? null
            : passages.ToDictionary(p => p.Position, p => p.Embedding is { } e && e.Length == questionEmbedding.Length ? Cosine(e, questionEmbedding) : 0);
        return passages.ToDictionary(p => p.Position, p =>
        {
            var lexical = Lexical(p.Title, p.Content, words);
            return semantic is null ? lexical : 0.65 * semantic[p.Position] + 0.35 * lexical;
        });
    }

    public static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    private static string BaseTitle(string title) => title.EndsWith(" (suite)") ? title[..^" (suite)".Length] : title;

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "\n- …";

    private static List<(string Title, string Content)> SplitParts(string text)
    {
        var parts = new List<(string, string)>();
        var matches = Heading().Matches(text);
        if (matches.Count == 0) return [("Cours", text)];

        if (matches[0].Index > 0 && text[..matches[0].Index].Trim().Length > 0)
            parts.Add(("Introduction", text[..matches[0].Index].Trim()));
        for (var i = 0; i < matches.Count; i++)
        {
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            parts.Add((matches[i].Groups[1].Value.Trim(), text[matches[i].Index..end].Trim()));
        }
        return parts;
    }

    /// <summary>Entre 0 et 1 : 0,5 quand tous les mots sont dans le texte, 1 quand ils sont aussi dans le titre.</summary>
    private static double Lexical(string title, string content, HashSet<string> words)
    {
        if (words.Count == 0) return 0;
        var inContent = Significant(content);
        var inTitle = Significant(title);
        return Math.Min(1, (words.Count(inContent.Contains) + 2.0 * words.Count(inTitle.Contains)) / (2.0 * words.Count));
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
