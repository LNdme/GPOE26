using Chat.Model;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Chat.Service;

/// <summary>
/// Implémentation LLM via OpenAI (GPT-4o, GPT-4-turbo, etc.)
/// Nécessite : PackageReference Include="Azure.AI.OpenAI" ou "OpenAI"
/// Config : OpenAI:ApiKey, OpenAI:Model (ex: "gpt-4o")
/// </summary>
public class GptService(IConfiguration configuration, IHttpClientFactory httpClientFactory) : ILlmService
{
    private readonly string _apiKey = configuration["OpenAI:ApiKey"]
        ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
    private readonly string _model = configuration["OpenAI:Model"] ?? "gpt-4o";

    private const string ApiUrl = "https://api.openai.com/v1/chat/completions";

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

    private const string DraftSystemPrompt = """
        Tu es un professeur expert en la matière. 
        Rédige un cours très détaillé au format Markdown. Utilise `# Titre`, `## Sous-titre`, `**Gras**`, et `- Puces` pour structurer le cours.
        Si cela est pertinent avec la matière (maths, physique, info...), utilise ABSOLUMENT des blocs de code markdown (```css, ```python, etc.) et de belles équations avec la syntaxe KaTeX (`$$ x = 2 $$`). 
        Ne mets aucune phrase d'introduction, fournis uniquement le cours Markdown complet prêt à l'emploi.
        """;

    public async Task<string> GenerateDraftAsync(CourseDraftRequest request, CancellationToken ct)
    {
        var messages = new List<object>
        {
            new { role = "system", content = DraftSystemPrompt },
            new { role = "user", content = $"Génère un cours Markdown structuré sur le sujet suivant : {request.Subject}\nDirectives supplémentaires : {request.AdditionalInstructions ?? "Aucune"}" }
        };

        return await CallApiAsync(messages, 2500, ct);
    }

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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var body = JsonSerializer.Serialize(new
        {
            model = _model,
            max_tokens = maxTokens,
            messages
        });

        var response = await client.PostAsync(ApiUrl,
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

    private static string ResolveCourseContent(string? content, string? courseId)
    {
        if (!string.IsNullOrWhiteSpace(content)) return content;
        if (!string.IsNullOrWhiteSpace(courseId)) return $"[Cours ID {courseId} — à connecter à la DB]";
        return "Aucun contenu de cours fourni.";
    }
}
