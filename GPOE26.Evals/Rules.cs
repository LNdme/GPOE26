using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GPOE26.Evals;

public record CheckResult(string Kind, bool Passed, string Detail);

/// <summary>Notation par règles : fonctions pures, testées dans GPOE26.Logic.Tests (EvalRulesTests).</summary>
public static partial class Rules
{
    public static List<CheckResult> Evaluate(IEnumerable<Check> checks, AgentOutput output) =>
        checks.Select(c => Evaluate(c, output)).ToList();

    public static CheckResult Evaluate(Check c, AgentOutput o)
    {
        var text = o.Text ?? "";
        switch (c.Kind)
        {
            case "contains":
            {
                var found = c.Values.FirstOrDefault(v => Fold(text).Contains(Fold(v)));
                return new(c.Kind, found is not null, found is null ? $"aucun de [{string.Join(" | ", c.Values)}]" : $"« {found} » trouvé");
            }
            case "not_contains":
            {
                var found = c.Values.FirstOrDefault(v => Fold(text).Contains(Fold(v)));
                return new(c.Kind, found is null, found is null ? "absent" : $"« {found} » présent");
            }
            case "regex":
            {
                var count = Regex.Matches(text, c.Pattern!, RegexOptions.Multiline | RegexOptions.IgnoreCase).Count;
                var ok = count >= (c.Min ?? 1) && (c.Max is null || count <= c.Max);
                return new(c.Kind, ok, $"/{c.Pattern}/ trouvé {count} fois (attendu {c.Min ?? 1}{(c.Max is null ? "+" : $" à {c.Max}")})");
            }
            case "not_regex":
            {
                var m = Regex.Match(text, c.Pattern!, RegexOptions.Multiline | RegexOptions.IgnoreCase);
                return new(c.Kind, !m.Success, m.Success ? $"« {Short(m.Value)} » présent" : "absent");
            }
            case "min_length":
            {
                var min = int.Parse(c.Value!, CultureInfo.InvariantCulture);
                return new(c.Kind, text.Length >= min, $"{text.Length} caractères (min {min})");
            }
            case "max_length":
            {
                var max = int.Parse(c.Value!, CultureInfo.InvariantCulture);
                return new(c.Kind, text.Length <= max, $"{text.Length} caractères (max {max})");
            }
            case "language":
            {
                var lang = DetectLanguage(text);
                return new(c.Kind, lang == c.Value, $"langue détectée : {lang ?? "?"} (attendu {c.Value})");
            }
            case "latex":
            {
                var ok = Latex().IsMatch(text);
                return new(c.Kind, ok, ok ? "formule $…$ présente" : "aucune formule $…$");
            }
            case "asks_question":
            {
                var tail = StripMath(text);
                tail = tail.Length > 300 ? tail[^300..] : tail;
                var ok = tail.Contains('?');
                return new(c.Kind, ok, ok ? "question finale présente" : "pas de question à la fin");
            }
            case "action_count":
            {
                var n = o.Actions.Count;
                var ok = n >= (c.Min ?? 0) && (c.Max is null || n <= c.Max);
                return new(c.Kind, ok, $"{n} action(s) [{string.Join(", ", o.Actions.Select(a => a.Type))}] (attendu {c.Min ?? 0} à {c.Max?.ToString() ?? "∞"})");
            }
            case "action_types":
            {
                var missing = c.Values.Where(t => !o.Actions.Any(a => a.Type.Equals(t, StringComparison.OrdinalIgnoreCase))).ToList();
                return new(c.Kind, missing.Count == 0, missing.Count == 0 ? "types attendus présents" : $"manquant : {string.Join(", ", missing)}");
            }
            case "action_field":
            {
                var actions = o.Actions.Where(a => c.Type is null || a.Type.Equals(c.Type, StringComparison.OrdinalIgnoreCase)).ToList();
                if (actions.Count == 0) return new(c.Kind, false, $"aucune action {c.Type}");
                var bad = actions.FirstOrDefault(a => !Regex.IsMatch(FieldText(a, c.Field!), c.Pattern!, RegexOptions.IgnoreCase));
                return new(c.Kind, bad is null, bad is null ? $"{c.Field} conforme" : $"{c.Field} = « {Short(FieldText(bad, c.Field!))} » ne vérifie pas /{c.Pattern}/");
            }
            case "destructive_flagged":
            {
                var bad = o.Actions.FirstOrDefault(a => a.Type.EndsWith(".delete", StringComparison.OrdinalIgnoreCase) && !a.Destructive);
                return new(c.Kind, bad is null, bad is null ? "suppressions marquées" : $"{bad.Type} non marquée à confirmer");
            }
            default:
                return new(c.Kind, false, "règle inconnue");
        }
    }

    /// <summary>Valeur d'un champ d'action en texte (chaîne brute, sinon JSON) ; vide si le champ est absent.</summary>
    public static string FieldText(ProposedAction a, string field)
    {
        var entry = a.Data.FirstOrDefault(d => d.Key.Equals(field, StringComparison.OrdinalIgnoreCase));
        if (entry.Key is null) return "";
        return entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() ?? "" : entry.Value.GetRawText();
    }

    // ─── Langue ──────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> French = ["le", "les", "des", "est", "une", "et", "pour", "dans", "du", "avec", "pas", "ce", "sont", "nous", "vous", "qui", "au", "aux", "cette", "il", "tu", "ton", "ta"];
    private static readonly HashSet<string> English = ["the", "is", "and", "of", "to", "that", "it", "for", "with", "this", "are", "you", "your", "what", "which", "be", "can", "an", "how"];
    private static readonly HashSet<string> Spanish = ["el", "los", "las", "es", "una", "y", "para", "del", "con", "por", "como", "pero", "está", "su", "lo", "al", "más", "tu", "qué"];

    /// <summary>Langue majoritaire (fr, en, es) d'après les mots courants, formules et code retirés ; null si le texte est trop court.</summary>
    public static string? DetectLanguage(string text)
    {
        var words = WordPattern().Matches(StripMath(text).ToLowerInvariant()).Select(m => m.Value).ToList();
        var scores = new Dictionary<string, int>
        {
            ["fr"] = words.Count(French.Contains),
            ["en"] = words.Count(English.Contains),
            ["es"] = words.Count(Spanish.Contains)
        };
        var best = scores.MaxBy(s => s.Value);
        return best.Value < 3 ? null : best.Key;
    }

    /// <summary>Texte sans formules ni blocs de code (ils fausseraient la langue et la question finale).</summary>
    public static string StripMath(string text) => MathOrCode().Replace(text, " ");

    /// <summary>Minuscules sans accents, espaces insécables normalisés : « Équations » trouve « equations ».</summary>
    public static string Fold(string text)
    {
        var normalized = text.Replace(' ', ' ').Replace(' ', ' ').ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string(normalized.Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray());
    }

    private static string Short(string s) => s.Length > 80 ? s[..80] + "…" : s;

    [GeneratedRegex(@"\$[^$\n]+\$")] private static partial Regex Latex();
    [GeneratedRegex(@"\$\$[\s\S]*?\$\$|\$[^$\n]*\$|```[\s\S]*?```")] private static partial Regex MathOrCode();
    [GeneratedRegex(@"\p{L}+")] private static partial Regex WordPattern();
}

// ─── Statistiques ────────────────────────────────────────────────────────────

public record Rate(int Passed, int Total)
{
    public double Value => Total == 0 ? 0 : (double)Passed / Total;
    public (double Low, double High) Interval => Stats.Wilson(Passed, Total);
}

public enum Verdict { Regression, AtLeastAsGood, Undetermined, Incomplete }

public record Comparison(string Agent, Rate Baseline, Rate Current, double DiffLow, double DiffHigh, Verdict Verdict);

public static class Stats
{
    private const double Z = 1.959964; // 95 %

    /// <summary>Intervalle de Wilson à 95 % d'une proportion (fiable même pour de petits effectifs ou 0 % / 100 %).</summary>
    public static (double Low, double High) Wilson(int passed, int total)
    {
        if (total == 0) return (0, 1);
        double n = total, p = passed / n, z2 = Z * Z;
        var center = (p + z2 / (2 * n)) / (1 + z2 / n);
        var half = Z * Math.Sqrt(p * (1 - p) / n + z2 / (4 * n * n)) / (1 + z2 / n);
        return (Math.Max(0, center - half), Math.Min(1, center + half));
    }

    /// <summary>
    /// Écart (actuel − référence) et son intervalle à 95 % (méthode de Newcombe, à partir des deux intervalles de Wilson).
    /// Régression : l'écart est négatif à coup sûr. Au moins aussi bon : une baisse de plus de <paramref name="margin"/>
    /// est exclue. Sinon : indéterminé (plus de cas ou de répétitions). Incomplet : trop d'essais non joués.
    /// </summary>
    public static Comparison Compare(string agent, Rate baseline, Rate current, double margin = 0.05, bool incomplete = false)
    {
        var (l1, u1) = baseline.Interval;
        var (l2, u2) = current.Interval;
        double p1 = baseline.Value, p2 = current.Value, d = p2 - p1;
        var low = d - Math.Sqrt(Math.Pow(p2 - l2, 2) + Math.Pow(u1 - p1, 2));
        var high = d + Math.Sqrt(Math.Pow(u2 - p2, 2) + Math.Pow(p1 - l1, 2));

        var verdict = incomplete || baseline.Total == 0 || current.Total == 0 ? Verdict.Incomplete
            : high < 0 ? Verdict.Regression
            : low > -margin ? Verdict.AtLeastAsGood
            : Verdict.Undetermined;
        return new Comparison(agent, baseline, current, low, high, verdict);
    }

    public static string Label(Verdict v) => v switch
    {
        Verdict.Regression => "RÉGRESSION",
        Verdict.AtLeastAsGood => "au moins aussi bon",
        Verdict.Undetermined => "indéterminé (plus de cas ou de répétitions)",
        _ => "incomplet (essais non joués)"
    };
}
