using Chat.Model;
using Microsoft.Extensions.Logging;

namespace Chat.Service;

/// <summary>
/// Service décorateur qui tente d'utiliser le LLM principal (ex: Ollama local).
/// En cas d'échec (timeout, refus de connexion, erreur interne), il bascule automatiquement
/// sur un LLM de secours (ex: DeepSeek ou GPT).
/// </summary>
public class FallbackLlmService(ILlmService primaryService, ILlmService fallbackService, ILogger<FallbackLlmService> logger) : ILlmService
{
    public async Task<ChatMessageResponse> SendMessageAsync(ChatMessageRequest request, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Tentative via le LLM principal...");
            return await primaryService.SendMessageAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Le LLM principal a échoué. Basculement sur le LLM de secours...");
            try 
            {
                return await fallbackService.SendMessageAsync(request, ct);
            }
            catch (Exception fallbackEx)
            {
                logger.LogError(fallbackEx, "Le LLM de secours a également échoué.");
                throw;
            }
        }
    }

    public async Task<CourseSummaryResponse> GetSummaryAsync(CourseSummaryRequest request, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Tentative via le LLM principal pour le résumé...");
            return await primaryService.GetSummaryAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Le LLM principal a échoué pour le résumé. Basculement sur le LLM de secours...");
            return await fallbackService.GetSummaryAsync(request, ct);
        }
    }

    public async Task<string> GenerateDraftAsync(CourseDraftRequest request, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Tentative via le LLM principal pour le brouillon...");
            return await primaryService.GenerateDraftAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Le LLM principal a échoué pour le brouillon. Basculement sur le LLM de secours...");
            return await fallbackService.GenerateDraftAsync(request, ct);
        }
    }
}
