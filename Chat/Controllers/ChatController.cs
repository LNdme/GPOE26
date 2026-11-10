using Chat.Model;
using Chat.Service;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Controllers;

[ApiController]
[Route("[controller]")]
public class ChatController(ILlmService claudeService) : ControllerBase
{
    /// <summary>
    /// Envoie un message au répétiteur virtuel dans le contexte d'un cours.
    /// </summary>
    [HttpPost("message")]
    public async Task<ActionResult<ChatMessageResponse>> SendMessage(
        [FromBody] ChatMessageRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest("Le message ne peut pas être vide.");

        if (string.IsNullOrWhiteSpace(request.CourseContent) && string.IsNullOrWhiteSpace(request.CourseId))
            return BadRequest("Fournissez soit courseContent soit courseId.");

        var result = await claudeService.SendMessageAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Génère un résumé structuré du cours (titre + parties).
    /// </summary>
    [HttpPost("summary")]
    public async Task<ActionResult<CourseSummaryResponse>> GetSummary(
        [FromBody] CourseSummaryRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CourseContent) && string.IsNullOrWhiteSpace(request.CourseId))
            return BadRequest("Fournissez soit courseContent soit courseId.");

        var result = await claudeService.GetSummaryAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Génère un brouillon de cours Markdown via l'IA.
    /// </summary>
    [HttpPost("draft")]
    public async Task<ActionResult<string>> GenerateDraft(
        [FromBody] CourseDraftRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Subject))
            return BadRequest("Le sujet est requis pour générer le cours.");

        try
        {
            var result = await claudeService.GenerateDraftAsync(request, cancellationToken);
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