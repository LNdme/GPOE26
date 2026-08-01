using System.Text;
using Chat.Model;
using GPOE26.Ai;

namespace Chat.Agents;

/// <summary>
/// Pose la question ouverte finale : l'élève a-t-il saisi l'essentiel du cours ?
///
/// Distincte de l'exercice de consolidation, qui fait appliquer une notion. Ici on
/// cherche autre chose : ce que le cours dit dans son ensemble, à quoi il sert, comment
/// ses parties se tiennent. On peut réussir tous les QCM d'un cours découpé en cinq
/// parties sans jamais avoir compris ce qu'il raconte — c'est cet écart que la question
/// de synthèse met au jour.
/// </summary>
public sealed class SyntheseAgent(OpenRouterClient client)
{
    private const string SystemPrompt = """
        Tu poses à un élève de lycée UNE question ouverte de synthèse sur son cours.

        Cette question doit vérifier qu'il a compris l'ESSENTIEL, pas qu'il a retenu un détail.
        Elle porte sur :
        - ce que le cours cherche à établir, et pourquoi ;
        - le lien entre ses différentes parties ;
        - ce à quoi la notion sert, ou dans quelle situation on l'emploie ;
        - ce qui changerait si l'une des conditions n'était pas remplie.

        Elle ne doit PAS :
        - demander de réciter une définition ou une formule ;
        - avoir une réponse d'un mot, d'un nombre ou d'un oui/non ;
        - porter sur un exemple particulier du cours ;
        - être un calcul.

        Formule-la simplement, en une ou deux phrases, tutoiement, dans la langue du cours.
        Ajoute une phrase indiquant ce qu'on attend (« Explique avec tes mots, quelques
        lignes suffisent »).

        Réponds uniquement par la question, sans titre ni préambule.
        """;

    public async Task<string> AskAsync(
        string courseTitle,
        IReadOnlyList<CoursePassage> passages,
        CancellationToken ct = default)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine($"Cours : « {courseTitle} »");
        prompt.AppendLine();

        if (passages.Count > 0)
        {
            // On donne une vue large du cours : une question de synthèse construite sur
            // un seul passage retomberait sur un détail, ce qu'on cherche justement à éviter.
            prompt.AppendLine("Extraits du cours :");
            prompt.AppendLine();

            foreach (var passage in passages)
            {
                prompt.AppendLine($"--- [§ {passage.HeadingPath}] ---");
                prompt.AppendLine(passage.Content);
                prompt.AppendLine();
            }
        }

        prompt.Append("Pose la question de synthèse.");

        var question = await client.CompleteAsync(new AgentRequest
        {
            Kind = AgentKind.Exercice,
            SystemPrompt = SystemPrompt,
            Turns = [AgentTurn.User(prompt.ToString())],
            MaxTokens = 400,
        }, ct);

        return question.Trim();
    }
}
