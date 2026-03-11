using Chat.Model;
using System.Text;
using System.Text.Json;

namespace Chat.Service;

/// <summary>
/// Implémentation LLM via Google Gemini API
/// Config : Gemini:ApiKey, Gemini:Model (ex: "gemini-1.5-flash")
/// </summary>
/*public class GeminiService(IConfiguration configuration, IHttpClientFactory httpClientFactory) : ILlmService
{
    private readonly string _apiKey = configuration["Gemini:ApiKey"]
        ?? throw new InvalidOperationException("Gemini:ApiKey is not configured.");
    private readonly string _model = configuration["Gemini:Model"] ?? "gemini-1.5-flash";

    private const string ApiUrlFormat = "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent?key={1}";

    private const string TutorSystemPrompt = """
        Tu es un répétiteur virtuel intelligent et bienveillant.
        Tu aides les élèves à comprendre leur cours, à réviser, et à approfondir leur compréhension.
        Réponds toujours en français, de façon claire et pédagogique, adapté au niveau lycée (les réponse doivent assez courte pour les permettre de miex assimiler).
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
        var systemInstruction = new { parts = new[] { new { text = DraftSystemPrompt } } };
        var message = $"Génère un cours Markdown structuré sur le sujet suivant : {request.Subject}\nDirectives supplémentaires : {request.AdditionalInstructions ?? "Aucune"}";

        var contents = new List<object>
        {
            new { role = "user", parts = new[] { new { text = message } } }
        };

        return await CallApiAsync(systemInstruction, contents, ct);
    }

    public async Task<ChatMessageResponse> SendMessageAsync(ChatMessageRequest request, CancellationToken ct)
    {
        var courseContent = ResolveCourseContent(request.CourseContent, request.CourseId);
        var systemPrompt = string.Format(TutorSystemPrompt, courseContent);
        
        var systemInstruction = new { parts = new[] { new { text = systemPrompt } } };

        var contents = new List<object>();

        // Format history for Gemini (roles are "user" and "model")
        foreach (var h in request.History)
        {
            var role = h.Role == "assistant" ? "model" : h.Role;
            contents.Add(new { role = role, parts = new[] { new { text = h.Content } } });
        }

        contents.Add(new { role = "user", parts = new[] { new { text = request.Message } } });

        var reply = await CallApiAsync(systemInstruction, contents, ct);

        var updatedHistory = request.History.ToList();
        updatedHistory.Add(new ConversationMessage("user", request.Message));
        updatedHistory.Add(new ConversationMessage("assistant", reply));

        return new ChatMessageResponse(reply, updatedHistory);
    }

    public async Task<CourseSummaryResponse> GetSummaryAsync(CourseSummaryRequest request, CancellationToken ct)
    {
        var courseContent = ResolveCourseContent(request.CourseContent, request.CourseId);
        
        var systemInstruction = new { parts = new[] { new { text = SummarySystemPrompt } } };
        var contents = new List<object>
        {
            new { role = "user", parts = new[] { new { text = $"Voici le cours à analyser :\n\n{courseContent}" } } }
        };

        var json = await CallApiAsync(systemInstruction, contents, ct);

        try
        {
            // Remove markdown format if Gemini returns it
            if (json.StartsWith("```json"))
            {
                json = json.Substring(7);
                if (json.EndsWith("```")) json = json.Substring(0, json.Length - 3);
            }
            else if (json.StartsWith("```"))
            {
                json = json.Substring(3);
                if (json.EndsWith("```")) json = json.Substring(0, json.Length - 3);
            }

            var result = JsonSerializer.Deserialize<CourseSummaryResponse>(json.Trim(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? new CourseSummaryResponse("Cours sans titre", []);
        }
        catch
        {
            return new CourseSummaryResponse("Résumé indisponible", []);
        }
    }

    private async Task<string> CallApiAsync(object systemInstruction, List<object> contents, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("LlmClient");
        var url = string.Format(ApiUrlFormat, _model, _apiKey);

        var body = JsonSerializer.Serialize(new
        {
            system_instruction = systemInstruction,
            contents = contents,
            generationConfig = new
            {
                // Temperature config if needed
            }
        });

        var response = await client.PostAsync(url,
            new StringContent(body, Encoding.UTF8, "application/json"), ct);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        
        try 
        {
            return doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? "";
        }
        catch (KeyNotFoundException)
        {
            return "Désolé, je n'ai pas pu générer une réponse (Erreur de parsing API).";
        }
    }

    private static string ResolveCourseContent(string? content, string? courseId)
    {
        if (!string.IsNullOrWhiteSpace(content)) return content;
        if (!string.IsNullOrWhiteSpace(courseId)) return $"[Cours ID {courseId} — à connecter à la DB]";
        return "Aucun contenu de cours fourni.";
    }
}
*/