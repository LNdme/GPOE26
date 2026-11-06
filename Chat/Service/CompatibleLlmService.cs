using System.Text.Json;
using Chat.Model;
using GPOE26.ServiceDefaults.Llm;

namespace Chat.Service;

/// <summary>
/// Anciens écrans (/learning) branchés sur le client LLM partagé (OpenRouter ou tout fournisseur compatible OpenAI).
/// LlmProvider = "OpenRouter" dans la configuration.
/// </summary>
public class CompatibleLlmService(LlmClient llm) : ILlmService
{
    public async Task<ChatMessageResponse> SendMessageAsync(ChatMessageRequest request, CancellationToken ct)
    {
        var messages = new List<LlmMessage>
        {
            new("system", $"""
                Tu es un répétiteur virtuel bienveillant pour des élèves du secondaire. Réponds en français, clairement
                et brièvement, formules en LaTeX ($...$). Reste centré sur le cours suivant :
                <cours>
                {request.CourseContent ?? "(aucun contenu)"}
                </cours>
                """)
        };
        messages.AddRange(request.History.Select(h => new LlmMessage(h.Role, h.Content)));
        messages.Add(new LlmMessage("user", request.Message));

        var reply = await llm.CompleteAsync(messages, ct: ct);
        var history = request.History.ToList();
        history.Add(new ConversationMessage("user", request.Message));
        history.Add(new ConversationMessage("assistant", reply));
        return new ChatMessageResponse(reply, history);
    }

    public async Task<CourseSummaryResponse> GetSummaryAsync(CourseSummaryRequest request, CancellationToken ct)
    {
        var json = await llm.CompleteAsync(
        [
            new("system", """Analyse le cours et réponds UNIQUEMENT avec un JSON brut : {"title": "...", "parts": [{"title": "...", "summary": "..."}]}"""),
            new("user", request.CourseContent ?? "")
        ], temperature: 0.2, ct: ct);
        try
        {
            var start = json.IndexOf('{');
            var end = json.LastIndexOf('}');
            return JsonSerializer.Deserialize<CourseSummaryResponse>(json[start..(end + 1)],
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new("Cours", []);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentOutOfRangeException)
        {
            return new CourseSummaryResponse("Résumé indisponible", []);
        }
    }

    public Task<string> GenerateDraftAsync(CourseDraftRequest request, CancellationToken ct) =>
        llm.CompleteAsync(
        [
            new("system", "Tu es un professeur expert. Rédige un cours détaillé en Markdown (titres, listes, formules KaTeX $$...$$, blocs de code si pertinent), sans phrase d'introduction."),
            new("user", $"Sujet : {request.Subject}\nDirectives : {request.AdditionalInstructions ?? "Aucune"}")
        ], ct: ct);
}
