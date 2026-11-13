using System.Text.RegularExpressions;

namespace Quiz.Service;

public record ProblemItem(string Statement, List<string> Hints, string Solution);

/// <summary>
/// Lit une série d'exercices écrite en Markdown balisé. Du JSON serait abîmé par le LaTeX :
/// chaque « \frac » y devient une séquence d'échappement invalide.
/// </summary>
public static partial class ProblemSetParser
{
    public static List<ProblemItem> Parse(string text)
    {
        var problems = new List<ProblemItem>();
        foreach (var block in ProblemHeader().Split(text.Replace("\r\n", "\n")).Skip(1))
        {
            var sections = Section().Split(block);
            var labels = Section().Matches(block).Select(m => m.Groups[1].Value.ToLowerInvariant()).ToList();

            string? statement = null, solution = null;
            var hints = new List<string>();
            for (var i = 0; i < labels.Count; i++)
            {
                var content = sections[(i + 1) * 2].Trim();
                if (content.Length == 0) continue;
                if (labels[i].StartsWith("énoncé") || labels[i].StartsWith("enonce")) statement = content;
                else if (labels[i].StartsWith("indice")) hints.Add(content);
                else if (labels[i].StartsWith("solution")) solution = content;
            }

            if (statement is not null && solution is not null)
                problems.Add(new ProblemItem(statement, hints, solution));
        }
        return problems;
    }

    /// <summary>
    /// Les modèles oublient de doubler les barres obliques du LaTeX dans le JSON.
    /// Les commandes qui commencent comme un échappement JSON (\frac, \theta, \nu…) sont reconnues par leur nom,
    /// pour qu'un vrai retour à la ligne « \nSuite » ne soit pas abîmé.
    /// </summary>
    public static string RepairLatexEscapes(string json) => LatexCommand().Replace(json, @"\\$1");

    [GeneratedRegex(@"^#{1,3}\s*Exercice\b.*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex ProblemHeader();

    [GeneratedRegex(@"^#{2,5}\s*(Énoncé|Enoncé|Enonce|Indice|Solution)\b[^\n]*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex Section();

    [GeneratedRegex(@"(?<!\\)\\([ac-eg-mo-qsv-zA-Z][a-zA-Z]*|(?:frac|forall|beta|bar|begin|big[a-zA-Z]*|boldsymbol|bmatrix|nu|neq|ne|nabla|notin|not|theta|tau|tan|times|text|to|tilde|rho|right[a-zA-Z]*|rangle|rm|upsilon|underline|uparrow)\b)")]
    private static partial Regex LatexCommand();
}
