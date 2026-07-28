using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Chat.Agents;
using Chat.Model;
using Chat.Service;
using GPOE26.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Controllers;

[ApiController]
[Route("[controller]")]
public class ChatController(ILlmService llmService) : ControllerBase
{
    private static readonly JsonSerializerOptions StreamJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Envoie un message au répétiteur virtuel dans le contexte d'un cours.
    /// Endpoint historique, conservé pour ne pas casser les appelants existants ;
    /// le pipeline multi-agents est sur /chat/tuteur/stream.
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

        var result = await llmService.SendMessageAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Le répétiteur multi-agents, en flux Server-Sent Events.
    ///
    /// Le pipeline enchaîne plusieurs appels de modèle : sans flux, l'élève resterait
    /// une dizaine de secondes devant un écran figé. On diffuse donc les étapes puis
    /// la réponse au fil de sa rédaction.
    /// </summary>
    [Authorize]
    [HttpPost("tuteur/stream")]
    public async Task StreamTutor(
        [FromBody] TutorRequest request,
        [FromServices] RepetiteurOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        // Sans cet en-tête, un proxy inverse peut mettre le flux en tampon et anéantir
        // tout l'intérêt du streaming.
        Response.Headers["X-Accel-Buffering"] = "no";

        var studentId = GetStudentId();
        if (studentId is null)
        {
            await WriteEventAsync(new TutorStreamEvent("error", Error: "Session expirée. Reconnectez-vous."), cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            await WriteEventAsync(new TutorStreamEvent("error", Error: "La question est vide."), cancellationToken);
            return;
        }

        try
        {
            await foreach (var evt in orchestrator.RunAsync(studentId.Value, request, cancellationToken))
            {
                await WriteEventAsync(evt, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // L'élève a fermé l'onglet ou changé de page : rien à signaler.
            return;
        }
        catch (OpenRouterException ex)
        {
            await WriteEventAsync(new TutorStreamEvent("error", Error: ex.Message), cancellationToken);
        }
        catch (Exception)
        {
            await WriteEventAsync(
                new TutorStreamEvent("error", Error: "Une erreur interne est survenue."), cancellationToken);
        }

        await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>Liste les modèles disponibles sur OpenRouter, pour les choisir depuis l'application.</summary>
    [HttpGet("models")]
    public async Task<ActionResult<IReadOnlyList<AgentModelInfo>>> GetModels(
        [FromServices] OpenRouterClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await client.ListModelsAsync(cancellationToken));
        }
        catch (OpenRouterException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
        }
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

        var result = await llmService.GetSummaryAsync(request, cancellationToken);
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

        var result = await llmService.GenerateDraftAsync(request, cancellationToken);
        return Ok(result);
    }

    // ── Interne ──────────────────────────────────────────────────────────────

    private async Task WriteEventAsync(TutorStreamEvent evt, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(evt, StreamJson);

        // Un évènement SSE se termine par une ligne vide ; le flush explicite est
        // indispensable, sinon ASP.NET met la réponse en tampon jusqu'à la fin.
        await Response.WriteAsync($"data: {json}\n\n", Encoding.UTF8, ct);
        await Response.Body.FlushAsync(ct);
    }

    /// <summary>
    /// Identifiant de l'élève, lu dans le JWT. Le service Cours filtre sur le même
    /// claim : c'est ce qui garantit qu'un élève ne peut travailler que ses cours.
    /// </summary>
    private Guid? GetStudentId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? User.FindFirstValue("sub")
                    ?? User.FindFirstValue(ClaimTypes.Name);

        return Guid.TryParse(value, out var id) ? id : null;
    }
}
