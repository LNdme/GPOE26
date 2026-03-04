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
}