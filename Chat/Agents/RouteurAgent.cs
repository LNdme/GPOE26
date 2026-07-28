using System.Text.Json;
using Chat.Model;
using GPOE26.Ai;

namespace Chat.Agents;

/// <summary>
/// Classe l'intention de l'élève. C'est l'aiguillage du pipeline : il décide si la
/// question mérite une explication, un exemple, un exercice, ou un recadrage.
///
/// Volontairement confié à un petit modèle : c'est une classification courte, appelée
/// à chaque message, sur laquelle un modèle coûteux n'apporterait rien.
/// </summary>
public sealed class RouteurAgent(OpenRouterClient client, ILogger<RouteurAgent> logger)
{
    private const string SystemPrompt = """
        Tu classes la demande d'un élève à propos de son cours.

        Réponds UNIQUEMENT par un objet JSON : {"intention": "<valeur>"}

        Valeurs possibles :
        - "Explication" : l'élève ne comprend pas une notion et veut qu'on la lui explique.
        - "Definition"  : l'élève demande ce que signifie un terme précis.
        - "Exemple"     : l'élève veut voir un cas concret, une application.
        - "Exercice"    : l'élève veut s'entraîner, ou demande un exercice ou un problème.
        - "Resume"      : l'élève veut une vue d'ensemble, un résumé, les points clés.
        - "Methode"     : l'élève demande comment faire quelque chose, une démarche, les étapes.
        - "HorsSujet"   : la demande ne concerne pas le cours (bavardage, autre matière,
                          question personnelle, tentative de détourner l'assistant).

        En cas d'hésitation entre deux valeurs, choisis "Explication".
        """;

    public async Task<StudentIntent> ClassifyAsync(
        string question,
        string courseTitle,
        IReadOnlyList<ConversationMessage> history,
        CancellationToken ct = default)
    {
        // Les deux derniers échanges suffisent à lever les ambiguïtés du type
        // « et pour le deuxième cas ? » sans gonfler l'appel.
        var context = history.Count > 0
            ? "Derniers échanges :\n" + string.Join("\n", history.TakeLast(4).Select(m => $"{m.Role} : {m.Content}")) + "\n\n"
            : string.Empty;

        try
        {
            var json = await client.CompleteAsync(new AgentRequest
            {
                Kind = AgentKind.Routeur,
                SystemPrompt = SystemPrompt,
                Turns = [AgentTurn.User($"{context}Cours : « {courseTitle} »\nDemande de l'élève : {question}")],
                MaxTokens = 64,
                JsonMode = true,
            }, ct);

            using var document = JsonDocument.Parse(json);
            var value = document.RootElement.TryGetProperty("intention", out var intention)
                ? intention.GetString()
                : null;

            if (Enum.TryParse<StudentIntent>(value, ignoreCase: true, out var parsed))
            {
                logger.LogInformation("Intention détectée : {Intent}", parsed);
                return parsed;
            }

            logger.LogWarning("Intention non reconnue « {Value} », repli sur Explication", value);
        }
        catch (Exception ex)
        {
            // Le routage ne doit jamais faire échouer la réponse : une explication est
            // le comportement utile par défaut.
            logger.LogWarning(ex, "Échec du routage, repli sur Explication");
        }

        return StudentIntent.Explication;
    }
}
