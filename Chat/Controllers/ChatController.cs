using Chat.Model;
using Chat.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Controllers;

/// <summary>
/// Brouillon de cours rédigé par l'IA. Connexion obligatoire : sans elle, n'importe qui consommait la clé d'IA.
/// Les anciens endpoints /chat/message et /chat/summary (remplacés par /tutor) ont été retirés :
/// ils acceptaient un cours quelconque et contournaient l'abonnement.
/// </summary>
[ApiController]
[Authorize]
[Route("[controller]")]
public class ChatController(ILlmService claudeService, AiQuota quota) : ControllerBase
{
    /// <summary>
    /// Génère un brouillon de cours Markdown via l'IA.
    /// </summary>
    [HttpPost("draft")]
    public async Task<ActionResult<string>> GenerateDraft(
        [FromBody] CourseDraftRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Subject))
            return BadRequest(new { message = "Le sujet est requis pour générer le cours." });

        var userId = Guid.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : Guid.Empty;
        if ((await quota.CheckAsync(userId, AiFeatures.CourseDraft, cancellationToken, requireFeature: false)).Refusal is { } refusal)
            return StatusCode(403, new { message = refusal });

        try
        {
            var result = await claudeService.GenerateDraftAsync(request, cancellationToken);
            await quota.RecordAsync(userId, AiFeatures.CourseDraft, cancellationToken);
            return Ok(result);
        }
        catch (GPOE26.ServiceDefaults.Llm.LlmUnavailableException ex)
        {
            return ex is GPOE26.ServiceDefaults.Llm.LlmNotConfiguredException
                ? StatusCode(503, new { message = "La rédaction par l'IA n'est pas activée sur ce serveur : aucune clé d'IA n'est configurée." })
                : StatusCode(502, new { message = "L'IA ne répond pas pour le moment. Réessayez dans un instant." });
        }
    }
}