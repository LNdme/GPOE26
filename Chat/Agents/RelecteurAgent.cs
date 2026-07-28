using System.Text;
using System.Text.Json;
using Chat.Model;
using GPOE26.Ai;

namespace Chat.Agents;

/// <summary>Verdict du relecteur sur une réponse du tuteur.</summary>
/// <param name="IsFaithful">La réponse tient-elle dans ce que dit le cours ?</param>
/// <param name="Critique">Ce qui cloche, à transmettre au tuteur pour révision.</param>
public readonly record struct ReviewVerdict(bool IsFaithful, string? Critique);

/// <summary>
/// Vérifie que la réponse ne dit rien que le cours ne dise pas.
///
/// C'est le garde-fou du RAG : un modèle sait produire une explication parfaitement
/// plausible et absente du cours de l'élève, ce qui est précisément le pire résultat
/// pour un outil de révision. Le relecteur ne tourne que sur les intentions où ce
/// risque compte (explication, définition), pour ne pas doubler la latence partout.
/// </summary>
public sealed class RelecteurAgent(OpenRouterClient client, ILogger<RelecteurAgent> logger)
{
    private const string SystemPrompt = """
        Tu vérifies la fidélité d'une réponse au cours d'un élève.

        On te donne des extraits de cours et une réponse rédigée à partir d'eux.
        Tu cherches uniquement deux défauts :
        1. une affirmation de fond que les extraits ne soutiennent pas ;
        2. une citation [§ …] qui renvoie à une section absente des extraits.

        Ne signale PAS : une reformulation plus simple, une analogie explicative,
        une question posée à l'élève, un choix de plan, un style trop concis ou trop long.
        Un exemple explicitement présenté comme n'étant pas dans le cours est acceptable.

        Réponds UNIQUEMENT par un objet JSON :
        {"fidele": true}
        ou
        {"fidele": false, "probleme": "<ce qui n'est pas soutenu par les extraits, en une phrase>"}
        """;

    public async Task<ReviewVerdict> ReviewAsync(
        string answer,
        IReadOnlyList<CoursePassage> passages,
        CancellationToken ct = default)
    {
        // Sans extrait, il n'y a pas de référentiel : le tuteur a normalement déjà
        // annoncé qu'il ne trouvait pas la réponse dans le cours.
        if (passages.Count == 0 || string.IsNullOrWhiteSpace(answer))
            return new ReviewVerdict(true, null);

        try
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("Extraits du cours :");
            prompt.AppendLine();

            foreach (var passage in passages)
            {
                prompt.AppendLine($"--- [§ {passage.HeadingPath}] ---");
                prompt.AppendLine(passage.Content);
                prompt.AppendLine();
            }

            prompt.AppendLine("Réponse à vérifier :");
            prompt.AppendLine();
            prompt.Append(answer);

            var json = await client.CompleteAsync(new AgentRequest
            {
                Kind = AgentKind.Relecteur,
                SystemPrompt = SystemPrompt,
                Turns = [AgentTurn.User(prompt.ToString())],
                MaxTokens = 200,
                JsonMode = true,
            }, ct);

            using var document = JsonDocument.Parse(json);

            var faithful = !document.RootElement.TryGetProperty("fidele", out var fidele)
                           || fidele.ValueKind != JsonValueKind.False;

            if (faithful) return new ReviewVerdict(true, null);

            var critique = document.RootElement.TryGetProperty("probleme", out var probleme)
                ? probleme.GetString()
                : null;

            logger.LogInformation("Relecture : réponse à réviser — {Critique}", critique);
            return new ReviewVerdict(false, critique);
        }
        catch (Exception ex)
        {
            // Un relecteur en panne ne doit pas bloquer la réponse : on laisse passer
            // plutôt que de priver l'élève d'une explication probablement correcte.
            logger.LogWarning(ex, "Échec de la relecture, réponse conservée telle quelle");
            return new ReviewVerdict(true, null);
        }
    }
}
