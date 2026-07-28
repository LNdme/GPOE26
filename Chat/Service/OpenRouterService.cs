using System.Text.Json;
using Chat.Model;
using GPOE26.Ai;

namespace Chat.Service;

/// <summary>
/// Implémentation d'<see cref="ILlmService"/> au-dessus d'OpenRouter.
///
/// Couvre les endpoints historiques (/chat/message, /chat/summary, /chat/draft) qui
/// restent en place pour ne pas casser les appelants existants. Le vrai répétiteur,
/// lui, passe par <see cref="Agents.RepetiteurOrchestrator"/>.
/// </summary>
public sealed class OpenRouterService(OpenRouterClient client, CoursClient cours) : ILlmService
{
    private const string TutorSystemPrompt = """
        Tu es un répétiteur virtuel intelligent et bienveillant.
        Tu aides les élèves à comprendre leur cours, à réviser, et à approfondir leur compréhension.
        Réponds toujours en français, de façon claire et pédagogique, adapté au niveau lycée.
        Garde tes réponses courtes : un élève assimile mieux ainsi.
        Si l'élève pose une question hors du cours fourni, rappelle-lui gentiment de se concentrer sur son cours.

        Contenu du cours :
        ---
        {0}
        ---
        """;

    private const string SummarySystemPrompt = """
        Tu es un assistant pédagogique.
        Analyse le cours fourni et retourne UNIQUEMENT un JSON valide (sans markdown, sans backticks) avec cette structure exacte :
        {
          "title": "Titre du cours",
          "parts": [
            { "title": "Titre de la partie", "summary": "Résumé court de 1-2 phrases" }
          ]
        }
        """;

    private const string DraftSystemPrompt = """
        Tu es un professeur expert en la matière.
        Rédige un cours très détaillé au format Markdown. Utilise `# Titre`, `## Sous-titre`, `**Gras**`, et `- Puces` pour structurer le cours.
        Si cela est pertinent avec la matière (maths, physique, info...), utilise des blocs de code markdown (```python, etc.) et de belles équations avec la syntaxe KaTeX (`$$ x = 2 $$`).
        Ne mets aucune phrase d'introduction, fournis uniquement le cours Markdown complet prêt à l'emploi.
        """;

    public async Task<ChatMessageResponse> SendMessageAsync(ChatMessageRequest request, CancellationToken ct)
    {
        var courseContent = await ResolveCourseContentAsync(request.CourseContent, request.CourseId, ct);

        var turns = request.History
            .Select(m => new AgentTurn(m.Role == "user" ? "user" : "assistant", m.Content))
            .ToList();

        turns.Add(AgentTurn.User(request.Message));

        var reply = await client.CompleteAsync(new AgentRequest
        {
            Kind = AgentKind.Tuteur,
            SystemPrompt = string.Format(TutorSystemPrompt, courseContent),
            Turns = turns,
            MaxTokens = 1500,
        }, ct);

        var updatedHistory = request.History.ToList();
        updatedHistory.Add(new ConversationMessage("user", request.Message));
        updatedHistory.Add(new ConversationMessage("assistant", reply));

        return new ChatMessageResponse(reply, updatedHistory);
    }

    public async Task<CourseSummaryResponse> GetSummaryAsync(CourseSummaryRequest request, CancellationToken ct)
    {
        var courseContent = await ResolveCourseContentAsync(request.CourseContent, request.CourseId, ct);

        var json = await client.CompleteAsync(new AgentRequest
        {
            Kind = AgentKind.Structurateur,
            SystemPrompt = SummarySystemPrompt,
            Turns = [AgentTurn.User($"Voici le cours à analyser :\n\n{courseContent}")],
            MaxTokens = 2048,
            JsonMode = true,
        }, ct);

        try
        {
            var result = JsonSerializer.Deserialize<CourseSummaryResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return result ?? new CourseSummaryResponse("Cours sans titre", []);
        }
        catch (JsonException)
        {
            return new CourseSummaryResponse("Résumé indisponible", []);
        }
    }

    public Task<string> GenerateDraftAsync(CourseDraftRequest request, CancellationToken ct) =>
        client.CompleteAsync(new AgentRequest
        {
            Kind = AgentKind.Structurateur,
            SystemPrompt = DraftSystemPrompt,
            Turns =
            [
                AgentTurn.User(
                    $"Génère un cours Markdown structuré sur le sujet suivant : {request.Subject}\n" +
                    $"Directives supplémentaires : {request.AdditionalInstructions ?? "Aucune"}")
            ],
            MaxTokens = 4000,
        }, ct);

    /// <summary>
    /// Va chercher le cours quand seul son identifiant est fourni.
    ///
    /// L'implémentation précédente renvoyait ici un texte bouchon
    /// (« [Cours ID … — à connecter à la DB] ») : le service ne savait pas lire un cours,
    /// et le frontend devait donc lui réexpédier le contenu entier à chaque message.
    /// </summary>
    private async Task<string> ResolveCourseContentAsync(string? content, string? courseId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(content)) return content;

        if (Guid.TryParse(courseId, out var id))
        {
            var course = await cours.GetCourseAsync(id, ct);
            if (!string.IsNullOrWhiteSpace(course?.Content)) return course.Content;
        }

        return "Aucun contenu de cours fourni.";
    }
}
