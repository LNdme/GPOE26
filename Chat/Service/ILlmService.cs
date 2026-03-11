using Chat.Model;

namespace Chat.Service;

public interface ILlmService
{
    Task<ChatMessageResponse> SendMessageAsync(ChatMessageRequest request, CancellationToken ct);
    Task<CourseSummaryResponse> GetSummaryAsync(CourseSummaryRequest request, CancellationToken ct);
    Task<string> GenerateDraftAsync(CourseDraftRequest request, CancellationToken ct);
}