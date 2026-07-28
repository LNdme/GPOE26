using Chat.Model;
using Chat.Service;
using GPOE26.Ai;

namespace Chat.Agents;

/// <summary>
/// Trouve dans le cours les passages qui permettent de répondre.
///
/// Deux étapes : reformuler la question en requête autonome, puis interroger la
/// recherche vectorielle du service Cours. La reformulation est indispensable en
/// conversation — « et pour le deuxième cas ? » ne ramène rien tel quel, alors que
/// « deuxième cas de la dérivation d'une fonction composée » ramène le bon passage.
/// </summary>
public sealed class RetrieverAgent(
    OpenRouterClient client,
    CoursClient cours,
    ILogger<RetrieverAgent> logger)
{
    private const string SystemPrompt = """
        Tu reformules la question d'un élève en une requête de recherche autonome,
        destinée à retrouver les passages pertinents de son cours.

        Règles :
        - Remplace les pronoms et les références implicites par ce à quoi ils renvoient,
          en t'appuyant sur les échanges précédents.
        - Garde les termes techniques du cours ; n'introduis pas de vocabulaire absent.
        - Une seule phrase, sans guillemets, sans préfixe.

        Réponds uniquement par la requête reformulée.
        """;

    public async Task<IReadOnlyList<CoursePassage>> RetrieveAsync(
        Guid courseId,
        string question,
        IReadOnlyList<ConversationMessage> history,
        int k = 5,
        CancellationToken ct = default)
    {
        var query = await BuildQueryAsync(question, history, ct);
        var passages = await cours.SearchAsync(courseId, query, k, ct);

        logger.LogInformation("Recherche « {Query} » → {Count} passage(s)", query, passages.Count);
        return passages;
    }

    private async Task<string> BuildQueryAsync(
        string question,
        IReadOnlyList<ConversationMessage> history,
        CancellationToken ct)
    {
        // Premier message d'une conversation : rien à désambiguïser, on économise un appel.
        if (history.Count == 0) return question;

        try
        {
            var context = string.Join("\n", history.TakeLast(4).Select(m => $"{m.Role} : {m.Content}"));

            var reformulated = await client.CompleteAsync(new AgentRequest
            {
                Kind = AgentKind.Retriever,
                SystemPrompt = SystemPrompt,
                Turns = [AgentTurn.User($"Échanges précédents :\n{context}\n\nNouvelle question : {question}")],
                MaxTokens = 120,
            }, ct);

            var cleaned = reformulated.Trim().Trim('"');
            return string.IsNullOrWhiteSpace(cleaned) ? question : cleaned;
        }
        catch (Exception ex)
        {
            // Une reformulation ratée ne doit pas priver l'élève de réponse : la question
            // brute reste une requête acceptable.
            logger.LogWarning(ex, "Échec de la reformulation, utilisation de la question brute");
            return question;
        }
    }
}
