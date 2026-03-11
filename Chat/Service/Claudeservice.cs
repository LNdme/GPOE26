using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Chat.Model;

namespace Chat.Service;

public class ClaudeService(IConfiguration configuration) : ILlmService
{
    private readonly AnthropicClient _client = new(configuration["Anthropic:ApiKey"]
        ?? throw new InvalidOperationException("Anthropic:ApiKey is not configured."));

    private const string Model = "claude-sonnet-4-6";

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

        var messages = request.History
            .Select(m => new Message
            {
                Role = m.Role == "user" ? RoleType.User : RoleType.Assistant,
                Content = [new TextContent { Text = m.Content }]
            })
            .ToList();

        messages.Add(new Message
        {
            Role = RoleType.User,
            Content = [new TextContent { Text = request.Message }]
        });

        var response = await _client.Messages.GetClaudeMessageAsync(new MessageParameters
        {
            Model = Model,
            MaxTokens = 1024,
            System = [new SystemMessage(systemPrompt)],
            Messages = messages
        }, ct);

        var reply = response.Content.OfType<TextContent>().FirstOrDefault()?.Text ?? "";

        var updatedHistory = request.History.ToList();
        updatedHistory.Add(new ConversationMessage("user", request.Message));
        updatedHistory.Add(new ConversationMessage("assistant", reply));

        return new ChatMessageResponse(reply, updatedHistory);
    }

    public async Task<CourseSummaryResponse> GetSummaryAsync(CourseSummaryRequest request, CancellationToken ct)
    {
        var courseContent = ResolveCourseContent(request.CourseContent, request.CourseId);

        var response = await _client.Messages.GetClaudeMessageAsync(new MessageParameters
        {
            Model = Model,
            MaxTokens = 2048,
            System = [new SystemMessage(SummarySystemPrompt)],
            Messages =
            [
                new Message
                {
                    Role = RoleType.User,
                    Content = [new TextContent { Text = $"Voici le cours à analyser :\n\n{courseContent}" }]
                }
            ]
        }, ct);

        var json = response.Content.OfType<TextContent>().FirstOrDefault()?.Text ?? "{}";

        try
        {
            var result = System.Text.Json.JsonSerializer.Deserialize<CourseSummaryResponse>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? new CourseSummaryResponse("Cours sans titre", []);
        }
        catch
        {
            return new CourseSummaryResponse("Résumé indisponible", []);
        }
    }

    public Task<string> GenerateDraftAsync(CourseDraftRequest request, CancellationToken ct)
    {
        throw new NotImplementedException("Draft generation not implemented for ClaudeService yet.");
    }

    private static string ResolveCourseContent(string? content, string? courseId)
    {
        if (!string.IsNullOrWhiteSpace(content)) return content;
        if (!string.IsNullOrWhiteSpace(courseId)) return $"[Cours ID {courseId} — à connecter à la DB]";
        return "Aucun contenu de cours fourni.";
    }
}
