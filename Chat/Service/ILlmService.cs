using Chat.Model;

namespace Chat.Service;

/// <summary>Rédaction d'un brouillon de cours. Les autres échanges passent par LlmClient (tuteur, assistant, mémoire).</summary>
public interface ILlmService
{
    Task<string> GenerateDraftAsync(CourseDraftRequest request, CancellationToken ct);
}
