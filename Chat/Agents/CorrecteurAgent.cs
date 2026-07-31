using System.Text;
using System.Text.Json;
using Chat.Model;
using GPOE26.Ai;

namespace Chat.Agents;

/// <summary>
/// Corrige la réponse rédigée par l'élève à un exercice ouvert.
///
/// C'est ce qui distingue la consolidation d'un QCM : l'élève produit au lieu de
/// reconnaître, et la correction lui dit ce qui est acquis et ce qui manque, pas
/// seulement s'il a bon. Comme le reste du répétiteur, l'agent ne juge qu'à l'aune
/// des passages du cours — pas de ce qu'il croit savoir de la matière.
/// </summary>
public sealed class CorrecteurAgent(OpenRouterClient client, ILogger<CorrecteurAgent> logger)
{
    private const string SystemPrompt = """
        Tu corriges la réponse d'un élève de lycée à un exercice portant sur son cours.

        On te donne des extraits du cours, l'énoncé, et la réponse de l'élève.

        Corrige avec bienveillance et exigence :
        - Juge le FOND, pas l'orthographe ni le style.
        - Une réponse juste mais formulée autrement qu'au cours reste juste.
        - Une réponse incomplète n'est pas fausse : distingue ce qui est acquis de ce qui manque.
        - Ne reproche pas l'absence de ce que l'énoncé ne demandait pas.
        - Appuie-toi sur les extraits fournis, pas sur des connaissances extérieures.

        Réponds UNIQUEMENT par un objet JSON :
        {
          "acquis": true | false,
          "score": <entier de 0 à 100>,
          "points_forts": ["<ce que l'élève a réussi, une phrase par point>"],
          "points_manquants": ["<ce qui manque ou est inexact, une phrase par point>"],
          "correction": "<la correction attendue, en Markdown, avec $...$ pour les formules>",
          "conseil": "<une phrase : quoi revoir, ou quoi faire ensuite>"
        }

        "acquis" est vrai quand l'essentiel de l'attendu est là, même imparfaitement.
        """;

    public async Task<ExerciseCorrection> CorrectAsync(
        string statement,
        string studentAnswer,
        IReadOnlyList<CoursePassage> passages,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnswer))
            return new ExerciseCorrection(false, 0, [], ["Aucune réponse n'a été rédigée."], "", "Prenez le temps de rédiger, même partiellement.");

        var prompt = new StringBuilder();

        if (passages.Count > 0)
        {
            prompt.AppendLine("Extraits du cours :");
            prompt.AppendLine();

            foreach (var passage in passages)
            {
                prompt.AppendLine($"--- [§ {passage.HeadingPath}] ---");
                prompt.AppendLine(passage.Content);
                prompt.AppendLine();
            }
        }

        prompt.AppendLine("Énoncé :");
        prompt.AppendLine(statement);
        prompt.AppendLine();
        prompt.AppendLine("Réponse de l'élève :");
        prompt.Append(studentAnswer);

        try
        {
            var json = await client.CompleteAsync(new AgentRequest
            {
                Kind = AgentKind.Relecteur,
                SystemPrompt = SystemPrompt,
                Turns = [AgentTurn.User(prompt.ToString())],
                MaxTokens = 1200,
                JsonMode = true,
            }, ct);

            return Parse(json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Échec de la correction d'un exercice ouvert");

            // Ne jamais laisser l'élève devant un écran vide après avoir rédigé :
            // on le dit franchement plutôt que de prétendre corriger.
            return new ExerciseCorrection(
                false, 0, [], [],
                "",
                "La correction n'a pas pu être établie. Réessayez dans un instant.");
        }
    }

    private static ExerciseCorrection Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var acquis = root.TryGetProperty("acquis", out var a) && a.ValueKind == JsonValueKind.True;

        var score = root.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number
            ? Math.Clamp(s.GetInt32(), 0, 100)
            : (acquis ? 100 : 0);

        return new ExerciseCorrection(
            acquis,
            score,
            ReadStrings(root, "points_forts"),
            ReadStrings(root, "points_manquants"),
            root.TryGetProperty("correction", out var c) ? c.GetString() ?? "" : "",
            root.TryGetProperty("conseil", out var t) ? t.GetString() : null);
    }

    private static List<string> ReadStrings(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            return [];

        return array.EnumerateArray()
            .Select(e => e.GetString())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList();
    }
}
