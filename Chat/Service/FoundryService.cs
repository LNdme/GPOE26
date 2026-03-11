using Chat.Model;
using Chat.Service;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Chat.Service;

/// <summary>
/// Implémentation LLM via Azure AI Foundry (anciennement Azure OpenAI / Azure AI Studio)
/// Config : AzureFoundry:Endpoint, AzureFoundry:ApiKey, AzureFoundry:DeploymentName
/// Exemple d'endpoint : https://YOUR_RESOURCE.openai.azure.com/
/// </summary>
public class FoundryService(IConfiguration configuration, IHttpClientFactory httpClientFactory) : ILlmService
{
    private readonly string _endpoint = configuration["AzureFoundry:Endpoint"]
        ?? throw new InvalidOperationException("AzureFoundry:Endpoint is not configured.");
    private readonly string _apiKey = configuration["AzureFoundry:ApiKey"]
        ?? throw new InvalidOperationException("AzureFoundry:ApiKey is not configured.");
    private readonly string _deploymentName = configuration["AzureFoundry:DeploymentName"]
        ?? throw new InvalidOperationException("AzureFoundry:DeploymentName is not configured.");
    private readonly string _apiVersion = configuration["AzureFoundry:ApiVersion"] ?? "2024-02-01";

    private const string TutorSystemPrompt = """
        Tu es un répétiteur virtuel intelligent et bienveillant.
        Tu aides les élèves à comprendre leur cours, à réviser, et à approfondir leur compréhension.
        Réponds toujours en français, de façon claire et pédagogique, adapté au niveau lycée.
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

    public async Task<ChatMessageResponse> SendMessageAsync(ChatMessageRequest request, CancellationToken ct)
    {
        var courseContent = ResolveCourseContent(request.CourseContent, request.CourseId);
        var systemPrompt = string.Format(TutorSystemPrompt, courseContent);

        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };

        foreach (var h in request.History)
            messages.Add(new { role = h.Role, content = h.Content });

        messages.Add(new { role = "user", content = request.Message });

        var reply = await CallApiAsync(messages, 1024, ct);

        var updatedHistory = request.History.ToList();
        updatedHistory.Add(new ConversationMessage("user", request.Message));
        updatedHistory.Add(new ConversationMessage("assistant", reply));

        return new ChatMessageResponse(reply, updatedHistory);
    }

    public async Task<CourseSummaryResponse> GetSummaryAsync(CourseSummaryRequest request, CancellationToken ct)
    {
        var courseContent = ResolveCourseContent(request.CourseContent, request.CourseId);

        var messages = new List<object>
        {
            new { role = "system", content = SummarySystemPrompt },
            new { role = "user", content = $"Voici le cours à analyser :\n\n{courseContent}" }
        };

        var json = await CallApiAsync(messages, 2048, ct);

        try
        {
            var result = JsonSerializer.Deserialize<CourseSummaryResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? new CourseSummaryResponse("Cours sans titre", []);
        }
        catch
        {
            return new CourseSummaryResponse("Résumé indisponible", []);
        }
    }

    private async Task<string> CallApiAsync(List<object> messages, int maxTokens, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("LlmClient");
        client.DefaultRequestHeaders.Add("api-key", _apiKey);

        // URL Azure Foundry : endpoint + deployment + version
        var url = $"{_endpoint.TrimEnd('/')}/openai/deployments/{_deploymentName}/chat/completions?api-version={_apiVersion}";

        var body = JsonSerializer.Serialize(new
        {
            max_tokens = maxTokens,
            messages
        });

        var response = await client.PostAsync(url,
            new StringContent(body, Encoding.UTF8, "application/json"), ct);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }

    public Task<string> GenerateDraftAsync(CourseDraftRequest request, CancellationToken ct)
    {
        throw new NotImplementedException("Draft generation not implemented for FoundryService yet.");
    }

    private static string ResolveCourseContent(string? content, string? courseId)
    {
        if (!string.IsNullOrWhiteSpace(content)) return content;
        if (!string.IsNullOrWhiteSpace(courseId)) return $"[Cours ID {courseId} — à connecter à la DB]";
        return "Aucun contenu de cours fourni.";
    }
}