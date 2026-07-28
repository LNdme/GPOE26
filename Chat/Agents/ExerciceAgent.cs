using System.Text;
using Chat.Model;
using GPOE26.Ai;

namespace Chat.Agents;

/// <summary>
/// Produit un exercice ciblé sur les notions du cours, avec son corrigé replié.
///
/// Distinct du service Quiz, qui génère des QCM notés sur l'ensemble d'un cours :
/// ici l'exercice porte sur ce dont l'élève vient de parler, dans le fil de la
/// conversation.
/// </summary>
public sealed class ExerciceAgent(OpenRouterClient client)
{
    private const string SystemPrompt = """
        Tu proposes un exercice à un élève de lycée, sur SON cours.

        Tu disposes d'extraits de son cours. L'exercice doit porter uniquement sur des
        notions qui y figurent, et rester faisable avec ce que le cours contient.

        Format de ta réponse, en Markdown :

        **Exercice**

        <énoncé, court et précis>

        <details>
        <summary>Voir la correction</summary>

        <correction détaillée, étape par étape>

        </details>

        Contraintes :
        - Un seul exercice, de difficulté raisonnable pour un premier entraînement.
        - Utilise $...$ ou $$...$$ pour les formules.
        - Indique entre crochets la section du cours travaillée : [§ Nom de la section].
        - N'introduis aucune notion absente des extraits.
        """;

    public IAsyncEnumerable<string> StreamExerciseAsync(
        string question,
        IReadOnlyList<CoursePassage> passages,
        string courseTitle,
        CancellationToken ct = default)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine($"Cours : « {courseTitle} »");
        prompt.AppendLine();

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

        prompt.Append("Demande de l'élève : ").Append(question);

        return client.StreamAsync(new AgentRequest
        {
            Kind = AgentKind.Exercice,
            SystemPrompt = SystemPrompt,
            Turns = [AgentTurn.User(prompt.ToString())],
            MaxTokens = 1200,
        }, ct);
    }
}
