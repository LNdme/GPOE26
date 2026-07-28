using System.Text;
using Chat.Model;
using GPOE26.Ai;

namespace Chat.Agents;

/// <summary>
/// Le répétiteur proprement dit : il explique, à partir des seuls passages du cours
/// que le Retriever lui a remis, et cite ses sources.
///
/// L'ancrage est le point important. L'ancienne version collait le cours entier dans
/// le prompt système et laissait le modèle improviser ; ici il travaille sur des
/// passages identifiés, ce qui rend la réponse vérifiable par l'élève.
/// </summary>
public sealed class TuteurAgent(OpenRouterClient client)
{
    private const string SystemPrompt = """
        Tu es un répétiteur bienveillant qui aide un élève de lycée à comprendre SON cours.

        Tu disposes d'extraits du cours de l'élève, chacun précédé de sa section.
        Ce sont tes seules sources.

        Règles :
        - Réponds en français, clairement, en t'adaptant au niveau lycée.
        - Fonde ta réponse sur les extraits. Si tu ajoutes une reformulation ou une
          analogie pour aider à comprendre, elle doit rester cohérente avec eux.
        - Cite la section utilisée sous la forme [§ Nom de la section], au fil du texte.
        - Si les extraits ne permettent pas de répondre, dis-le franchement et indique
          ce que l'élève devrait chercher dans son cours. N'invente jamais de contenu
          qui n'y figure pas.
        - Sois bref : trois à six phrases pour une explication simple. Un élève assimile
          mieux une réponse courte qu'un pavé.
        - Utilise le Markdown avec parcimonie : **gras** sur les termes-clés, une liste
          quand il y a des étapes, $...$ ou $$...$$ pour les formules.
        - N'ouvre pas par une formule de politesse. Va droit à l'explication.
        """;

    private const string ResumePrompt = """
        Tu es un répétiteur. Produis un résumé structuré du cours de l'élève à partir
        des extraits fournis.

        Format : une liste des points clés, groupés par section, chaque point en une
        phrase. Cite les sections sous la forme [§ Nom de la section].
        Reste fidèle aux extraits : n'ajoute aucune notion absente.
        Réponds en français, en Markdown.
        """;

    /// <summary>
    /// Rédige la réponse en flux. Les fragments sont renvoyés au fil de la génération
    /// pour que l'élève voie la réponse s'écrire plutôt que d'attendre le pipeline complet.
    /// </summary>
    public IAsyncEnumerable<string> StreamAnswerAsync(
        string question,
        StudentIntent intent,
        IReadOnlyList<CoursePassage> passages,
        IReadOnlyList<ConversationMessage> history,
        string courseTitle,
        CancellationToken ct = default)
    {
        var request = new AgentRequest
        {
            Kind = AgentKind.Tuteur,
            SystemPrompt = intent == StudentIntent.Resume ? ResumePrompt : SystemPrompt,
            Turns = BuildTurns(question, intent, passages, history, courseTitle),
            MaxTokens = 1200,
        };

        return client.StreamAsync(request, ct);
    }

    /// <summary>Variante non streamée, utilisée pour produire une révision après relecture.</summary>
    public Task<string> ReviseAsync(
        string question,
        StudentIntent intent,
        IReadOnlyList<CoursePassage> passages,
        IReadOnlyList<ConversationMessage> history,
        string courseTitle,
        string previousAnswer,
        string critique,
        CancellationToken ct = default)
    {
        var turns = BuildTurns(question, intent, passages, history, courseTitle).ToList();

        turns.Add(AgentTurn.Assistant(previousAnswer));
        turns.Add(AgentTurn.User(
            $"Un relecteur a repéré ce problème dans ta réponse : {critique}\n\n" +
            "Réécris la réponse en le corrigeant. Reste dans le périmètre des extraits fournis. " +
            "Ne mentionne ni le relecteur ni la correction : donne directement la réponse corrigée."));

        return client.CompleteAsync(new AgentRequest
        {
            Kind = AgentKind.Tuteur,
            SystemPrompt = intent == StudentIntent.Resume ? ResumePrompt : SystemPrompt,
            Turns = turns,
            MaxTokens = 1200,
        }, ct);
    }

    private static IReadOnlyList<AgentTurn> BuildTurns(
        string question,
        StudentIntent intent,
        IReadOnlyList<CoursePassage> passages,
        IReadOnlyList<ConversationMessage> history,
        string courseTitle)
    {
        var turns = new List<AgentTurn>();

        // On ne rejoue que les derniers échanges : au-delà, le coût grimpe sans que la
        // qualité suive, et les extraits portent déjà le contexte utile.
        foreach (var message in history.TakeLast(6))
        {
            turns.Add(message.Role == "user"
                ? AgentTurn.User(message.Content)
                : AgentTurn.Assistant(message.Content));
        }

        var prompt = new StringBuilder();
        prompt.AppendLine($"Cours : « {courseTitle} »");
        prompt.AppendLine();

        if (passages.Count > 0)
        {
            prompt.AppendLine("Extraits du cours :");
            prompt.AppendLine();

            foreach (var passage in passages)
            {
                prompt.AppendLine($"--- [§ {(string.IsNullOrWhiteSpace(passage.HeadingPath) ? "Cours" : passage.HeadingPath)}] ---");
                prompt.AppendLine(passage.Content);
                prompt.AppendLine();
            }
        }
        else
        {
            prompt.AppendLine("Aucun extrait pertinent n'a été trouvé dans le cours pour cette question.");
            prompt.AppendLine();
        }

        prompt.AppendLine(IntentInstruction(intent));
        prompt.AppendLine();
        prompt.Append("Question de l'élève : ").Append(question);

        turns.Add(AgentTurn.User(prompt.ToString()));
        return turns;
    }

    private static string IntentInstruction(StudentIntent intent) => intent switch
    {
        StudentIntent.Definition =>
            "L'élève demande une définition : commence par l'énoncé exact tiré du cours, puis reformule-le simplement.",
        StudentIntent.Exemple =>
            "L'élève veut un exemple concret : appuie-toi sur un exemple du cours s'il en contient un, sinon construis-en un cohérent avec les extraits et dis qu'il ne figure pas dans le cours.",
        StudentIntent.Methode =>
            "L'élève demande une démarche : donne les étapes numérotées, dans l'ordre.",
        StudentIntent.Resume =>
            "L'élève demande une vue d'ensemble : structure ta réponse par sections du cours.",
        _ =>
            "L'élève veut comprendre : explique la notion, puis vérifie sa compréhension par une question courte à la fin.",
    };
}
