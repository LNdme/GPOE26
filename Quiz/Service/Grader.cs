using System.Globalization;
using System.Text;
using Quiz.Model;

namespace Quiz.Service;

/// <summary>Réponse d'un élève à une question, telle qu'envoyée par le client.</summary>
public record AnswerInput(
    Guid QuestionId,
    List<int>? SelectedOptions = null,
    bool? TrueFalse = null,
    string? Text = null,
    string? AttachmentUrl = null
);

/// <summary>Résultat de la correction automatique d'une réponse.</summary>
public record GradeResult(bool? IsCorrect, decimal? Points, bool NeedsManualGrading);

/// <summary>
/// Correction automatique et validation des évaluations — logique pure, sans base de données.
///
/// Règles :
///   - QCM / choix multiples : tout ou rien (l'ensemble coché doit être exactement l'ensemble correct)
///   - Vrai / Faux : égalité
///   - Réponse courte : comparée aux variantes acceptées, sans tenir compte de la casse,
///     des accents, des espaces superflus ni du point final
///   - Numérique : virgule ou point décimal, espaces de milliers et unité tolérés ; |réponse − attendu| ≤ tolérance
///   - Réponse ouverte : toujours corrigée par l'enseignant (l'IA peut proposer une note)
/// </summary>
public static class Grader
{
    public static GradeResult Grade(AssessmentQuestion q, AnswerInput? answer)
    {
        if (q.Type == QuestionType.OpenEnded)
        {
            // Une copie vide vaut 0 sans attendre l'enseignant
            var empty = string.IsNullOrWhiteSpace(answer?.Text) && string.IsNullOrWhiteSpace(answer?.AttachmentUrl);
            return empty ? new GradeResult(false, 0, false) : new GradeResult(null, null, true);
        }

        var correct = answer is not null && q.Type switch
        {
            QuestionType.SingleChoice or QuestionType.MultipleChoice =>
                answer.SelectedOptions is { Count: > 0 } selected
                && selected.Distinct().OrderBy(i => i).SequenceEqual(q.CorrectOptions.Distinct().OrderBy(i => i)),
            QuestionType.TrueFalse => answer.TrueFalse is not null && answer.TrueFalse == q.TrueFalseAnswer,
            QuestionType.ShortAnswer => answer.Text is not null
                && q.AcceptedAnswers.Any(a => NormalizeText(a) == NormalizeText(answer.Text)),
            QuestionType.Numeric => q.NumericAnswer is { } expected
                && ParseNumber(answer.Text, q.Unit) is { } value
                && Math.Abs(value - expected) <= (q.Tolerance ?? 0) + 1e-9,
            _ => false
        };

        return new GradeResult(correct, correct ? q.Points : 0, false);
    }

    /// <summary>Minuscules, sans accents, espaces normalisés, sans ponctuation finale.</summary>
    public static string NormalizeText(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
                continue;
            }
            sb.Append(c);
            lastWasSpace = false;
        }
        return sb.ToString().Trim().TrimEnd('.', '!', ';').Normalize(NormalizationForm.FormC);
    }

    /// <summary>"1 023", "3,5", "12.5 m/s" → nombre ; null si illisible.</summary>
    public static double? ParseNumber(string? text, string? unit = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();
        if (!string.IsNullOrWhiteSpace(unit) && s.EndsWith(unit.Trim(), StringComparison.OrdinalIgnoreCase))
            s = s[..^unit.Trim().Length];
        s = s.Replace(" ", "").Replace(" ", "").Replace(" ", "").Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>Erreurs qui empêchent la publication (liste vide = publiable).</summary>
    public static List<string> ValidateForPublish(Assessment a)
    {
        var errors = new List<string>();
        if (a.Questions.Count == 0) errors.Add("L'évaluation ne contient aucune question.");
        if (a.ClassroomIds.Count == 0) errors.Add("Aucune classe sélectionnée.");
        if (a.OpensAt is not null && a.DueAt is not null && a.DueAt <= a.OpensAt)
            errors.Add("La date limite doit être postérieure à l'ouverture.");

        foreach (var q in a.Questions.OrderBy(q => q.Order))
        {
            var n = $"Question {q.Order + 1}";
            if (string.IsNullOrWhiteSpace(q.Text)) errors.Add($"{n} : énoncé vide.");
            if (q.Points <= 0) errors.Add($"{n} : le nombre de points doit être positif.");

            switch (q.Type)
            {
                case QuestionType.SingleChoice or QuestionType.MultipleChoice:
                    if (q.Options.Count(o => !string.IsNullOrWhiteSpace(o)) < 2 || q.Options.Any(string.IsNullOrWhiteSpace))
                        errors.Add($"{n} : au moins deux options, toutes renseignées.");
                    if (q.CorrectOptions.Any(i => i < 0 || i >= q.Options.Count))
                        errors.Add($"{n} : une bonne réponse désigne une option inexistante.");
                    if (q.Type == QuestionType.SingleChoice && q.CorrectOptions.Distinct().Count() != 1)
                        errors.Add($"{n} : un QCM doit avoir exactement une bonne réponse.");
                    if (q.Type == QuestionType.MultipleChoice && q.CorrectOptions.Count == 0)
                        errors.Add($"{n} : cochez au moins une bonne réponse.");
                    break;
                case QuestionType.TrueFalse when q.TrueFalseAnswer is null:
                    errors.Add($"{n} : indiquez si l'affirmation est vraie ou fausse.");
                    break;
                case QuestionType.ShortAnswer when !q.AcceptedAnswers.Any(a => !string.IsNullOrWhiteSpace(a)):
                    errors.Add($"{n} : indiquez au moins une réponse acceptée.");
                    break;
                case QuestionType.Numeric when q.NumericAnswer is null:
                    errors.Add($"{n} : indiquez la valeur attendue.");
                    break;
                case QuestionType.Numeric when q.Tolerance < 0:
                    errors.Add($"{n} : la tolérance ne peut pas être négative.");
                    break;
            }
        }
        return errors;
    }

    /// <summary>Ordre de présentation stable pour une tentative (même ordre à chaque reprise).</summary>
    public static List<T> StableShuffle<T>(IEnumerable<T> items, Guid seed, int salt = 0)
    {
        var bytes = seed.ToByteArray();
        var random = new Random(BitConverter.ToInt32(bytes, 0) ^ BitConverter.ToInt32(bytes, 12) ^ salt);
        var list = items.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
