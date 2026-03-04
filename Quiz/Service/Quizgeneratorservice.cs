using Quiz.Model;
using System.Text;
using System.Text.Json;

namespace Quiz.Service;

public interface IQuizGeneratorService
{
    Task<List<Question>> GenerateQuestionsAsync(string courseText, int count, CancellationToken ct = default);
}

public class OpenAiQuizGeneratorService : IQuizGeneratorService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<OpenAiQuizGeneratorService> _logger;

    public OpenAiQuizGeneratorService(HttpClient http, IConfiguration config, ILogger<OpenAiQuizGeneratorService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<List<Question>> GenerateQuestionsAsync(string courseText, int count, CancellationToken ct = default)
    {
        var apiKey = _config["LLM:ApiKey"] ?? throw new InvalidOperationException("LLM:ApiKey not configured.");
        var model = _config["LLM:Model"] ?? "gpt-4o-mini";
        var baseUrl = _config["LLM:BaseUrl"] ?? "https://api.openai.com/v1";

        var systemPrompt = """
            Tu es un assistant pédagogique expert. 
            À partir du texte de cours fourni, génère des questions QCM (Questionnaire à Choix Multiples).
            Chaque question doit avoir exactement 4 options de réponse.
            Réponds UNIQUEMENT avec un objet JSON valide, sans markdown ni texte autour.
            """;

        var userPrompt = $$"""
            Génère exactement {{count}} questions QCM à partir de ce cours :

            ---
            {{courseText}}
            ---

            Réponds avec ce format JSON exact :
            {
              "questions": [
                {
                  "text": "Texte de la question ?",
                  "options": ["Option A", "Option B", "Option C", "Option D"],
                  "correctOptionIndex": 0,
                  "explanation": "Explication courte de la bonne réponse."
                }
              ]
            }
            """;

        var requestBody = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.7,
            response_format = new { type = "json_object" }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);

        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        using var parsed = JsonDocument.Parse(content);
        var questionsArray = parsed.RootElement.GetProperty("questions");

        var questions = new List<Question>();
        foreach (var q in questionsArray.EnumerateArray())
        {
            var options = q.GetProperty("options").EnumerateArray()
                .Select(o => o.GetString() ?? "")
                .ToList();

            questions.Add(new Question
            {
                Text = q.GetProperty("text").GetString() ?? "",
                Options = options,
                CorrectOptionIndex = q.GetProperty("correctOptionIndex").GetInt32(),
                Explanation = q.GetProperty("explanation").GetString() ?? ""
            });
        }

        _logger.LogInformation("Generated {Count} questions from LLM.", questions.Count);
        return questions;
    }
}