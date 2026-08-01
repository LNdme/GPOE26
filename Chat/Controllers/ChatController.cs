using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chat.Agents;
using Chat.Model;
using Chat.Service;
using GPOE26.Ai;
using GPOE26.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

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

    /// <summary>
    /// Pose la question de synthèse finale : l'élève a-t-il saisi l'essentiel du cours ?
    /// </summary>
    [Authorize]
    [HttpPost("synthese")]
    public async Task<ActionResult<ExerciceResponse>> GenerateSynthesis(
        [FromBody] ExerciceRequest request,
        [FromServices] SyntheseAgent synthese,
        [FromServices] CoursClient cours,
        CancellationToken cancellationToken)
    {
        var course = await cours.GetCourseAsync(request.CourseId, cancellationToken);
        if (course is null) return NotFound(new { message = "Cours introuvable." });

        // Vue large du cours, jamais d'une seule section : une question de synthèse
        // bâtie sur un seul passage retomberait sur un détail.
        var passages = await cours.SearchAsync(request.CourseId, course.Title, 12, cancellationToken);

        var question = await synthese.AskAsync(course.Title, passages, cancellationToken);

        return string.IsNullOrWhiteSpace(question)
            ? StatusCode(StatusCodes.Status502BadGateway, new { message = "Aucune question n'a pu être produite." })
            : Ok(new ExerciceResponse(question));
    }

    /// <summary>
    /// Le bilan rédigé pour un parent, sur un enfant et un cours.
    ///
    /// La réponse ne contient QUE du texte de bilan : pas de champ « échanges », pas de
    /// citation. La garantie de confidentialité du répétiteur tient dans la forme de
    /// cette réponse autant que dans le prompt de l'agent — ce qui n'existe pas ici ne
    /// peut pas fuir dans une interface.
    /// </summary>
    [Authorize]
    [HttpPost("bilan")]
    public async Task<ActionResult<BilanResponse>> GetBilan(
        [FromBody] BilanRequest request,
        [FromServices] BilanAgent bilan,
        [FromServices] MemoireAgent memoire,
        [FromServices] CoursClient cours,
        [FromServices] IMemoryCache cache,
        CancellationToken cancellationToken)
    {
        // Même règle que le service Cours, lue au même endroit : un parent ne voit que
        // les enfants qui se sont rattachés à lui, un élève ne voit que lui-même.
        if (User.GetUserId() is null) return Unauthorized();
        if (!User.CanViewStudent(request.StudentId)) return Forbid();

        // Une génération par élève, par cours et par jour : un parent qui rafraîchit sa
        // page ne doit pas relancer un appel de modèle à chaque fois.
        var key = $"bilan:{request.StudentId}:{request.CourseId}:{DateTime.UtcNow:yyyy-MM-dd}";

        if (cache.TryGetValue(key, out BilanResponse? cached) && cached is not null)
            return Ok(cached);

        var detail = await cours.GetChildCourseAsync(request.StudentId, request.CourseId, cancellationToken);
        if (detail is null)
            return NotFound(new { message = "Aucun suivi disponible pour ce cours." });

        // La fiche du répétiteur n'est lue que pour en tirer les notions difficiles ;
        // l'agent a consigne de ne jamais la reprendre telle quelle.
        var notes = await memoire.GetNotesAsync(request.StudentId, request.CourseId, cancellationToken);

        string text;
        try
        {
            text = await bilan.WriteAsync(detail, notes, cancellationToken);
        }
        catch (OpenRouterException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
        }

        if (string.IsNullOrWhiteSpace(text))
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Aucun bilan n'a pu être rédigé." });

        var response = new BilanResponse(text, DateTime.UtcNow);

        cache.Set(key, response, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12),
            // Le cache du service déclare une taille limite : sans Size, Set lève.
            Size = text.Length,
        });

        return Ok(response);
    }

    /// <summary>
    /// Produit un exercice ouvert sur un cours, ou sur une de ses parties.
    /// </summary>
    [Authorize]
    [HttpPost("exercice")]
    public async Task<ActionResult<ExerciceResponse>> GenerateExercise(
        [FromBody] ExerciceRequest request,
        [FromServices] ExerciceAgent exercice,
        [FromServices] CoursClient cours,
        CancellationToken cancellationToken)
    {
        var course = await cours.GetCourseAsync(request.CourseId, cancellationToken);
        if (course is null) return NotFound(new { message = "Cours introuvable." });

        var passages = await FindPassagesAsync(cours, request.CourseId, course.Title, request.HeadingPath, cancellationToken);

        // On agrège le flux : l'énoncé est court, et l'élève ne gagnerait rien à le voir
        // s'écrire mot à mot avant de pouvoir répondre.
        var builder = new StringBuilder();
        await foreach (var token in exercice.StreamExerciseAsync(
            BuildExercisePrompt(request.HeadingPath), passages, course.Title, cancellationToken))
        {
            builder.Append(token);
        }

        var statement = builder.ToString().Trim();

        return statement.Length == 0
            ? StatusCode(StatusCodes.Status502BadGateway, new { message = "Aucun exercice n'a pu être produit." })
            : Ok(new ExerciceResponse(statement));
    }

    /// <summary>
    /// Corrige la réponse rédigée par l'élève à un exercice ouvert.
    /// </summary>
    [Authorize]
    [HttpPost("exercice/corriger")]
    public async Task<ActionResult<ExerciseCorrection>> CorrectExercise(
        [FromBody] CorrectionRequest request,
        [FromServices] CorrecteurAgent correcteur,
        [FromServices] CoursClient cours,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Answer))
            return BadRequest(new { message = "La réponse est vide." });

        var course = await cours.GetCourseAsync(request.CourseId, cancellationToken);
        if (course is null) return NotFound(new { message = "Cours introuvable." });

        // La correction s'appuie sur les passages qui traitent de l'énoncé, pas sur la
        // seule section : un exercice mobilise souvent plusieurs notions du cours.
        var passages = await cours.SearchAsync(request.CourseId, request.Statement, 6, cancellationToken);

        if (passages.Count == 0)
            passages = await FindPassagesAsync(cours, request.CourseId, course.Title, request.HeadingPath, cancellationToken);

        return Ok(await correcteur.CorrectAsync(request.Statement, request.Answer, passages, cancellationToken));
    }

    private static string BuildExercisePrompt(string? headingPath) =>
        string.IsNullOrWhiteSpace(headingPath)
            ? "Propose un exercice de consolidation sur l'ensemble de ce cours."
            : $"Propose un exercice de consolidation sur la partie « {headingPath.Split('›').Last().Trim()} » de ce cours.";

    private static Task<IReadOnlyList<CoursePassage>> FindPassagesAsync(
        CoursClient cours, Guid courseId, string courseTitle, string? headingPath, CancellationToken ct) =>
        cours.SearchAsync(
            courseId,
            string.IsNullOrWhiteSpace(headingPath) ? courseTitle : headingPath,
            string.IsNullOrWhiteSpace(headingPath) ? 10 : 6,
            ct);

    /// <summary>
    /// Lit une explication à voix haute.
    ///
    /// Le résultat est mis en cache sur l'empreinte du texte : un élève réécoute
    /// volontiers la même explication deux ou trois fois, et le TTS se facture au
    /// caractère — sans cache, on paierait chaque écoute.
    /// </summary>
    [Authorize]
    [HttpPost("voix")]
    [Produces("audio/mpeg")]
    public async Task<IActionResult> Speak(
        [FromBody] SpeakRequest request,
        [FromServices] OpenRouterClient client,
        [FromServices] IMemoryCache cache,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { message = "Le texte à lire est vide." });

        var key = "voix:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(request.Text)));

        if (!cache.TryGetValue(key, out byte[]? audio))
        {
            try
            {
                audio = await client.SynthesizeSpeechAsync(request.Text, cancellationToken);
            }
            catch (OpenRouterException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
            }

            if (audio is not { Length: > 0 })
                return StatusCode(StatusCodes.Status502BadGateway, new { message = "Aucun audio n'a été produit." });

            cache.Set(key, audio, new MemoryCacheEntryOptions
            {
                // Glissante : une explication réécoutée reste chaude, une explication
                // oubliée libère sa place.
                SlidingExpiration = TimeSpan.FromHours(2),
                Size = audio.Length,
            });
        }

        return File(audio!, "audio/mpeg");
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
