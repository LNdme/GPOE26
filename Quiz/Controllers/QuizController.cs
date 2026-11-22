using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Quiz.Data;
using Quiz.Endpoints;
using Quiz.Model;
using Quiz.Service;

namespace Quiz.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class QuizController : ControllerBase
{
    private readonly IQuizGeneratorService _generator;
    private readonly IQuizStore _store;
    private readonly ILogger<QuizController> _logger;
    private readonly AiQuota _quota;
    private readonly CourseTextClient _courses;

    public QuizController(IQuizGeneratorService generator, IQuizStore store, ILogger<QuizController> logger,
        AiQuota quota, CourseTextClient courses)
    {
        _quota = quota;
        _courses = courses;
        _generator = generator;
        _store = store;
        _logger = logger;
    }

    /// <summary>Le quiz demandé, s'il appartient à l'utilisateur connecté (sinon null : même réponse qu'un quiz inexistant).</summary>
    private QuizSession? OwnSession(Guid id) =>
        _store.Get(id) is { } session && session.OwnerId == User.UserId() ? session : null;

    private static string Difficulty(string? value) => value is "exigeant" or "approfondi" ? value : "standard";

    private string? Learner()
    {
        var parts = new[] { User.FindFirst("level")?.Value, User.FindFirst("filiere")?.Value }.Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join(", ", parts);
    }

    /// <summary>Quota mensuel de la formule et plafond quotidien, partagés entre quiz et séries d'exercices.</summary>
    private async Task<IActionResult?> QuotaExceededAsync(CancellationToken ct) =>
        await _quota.RefusalAsync(User.UserId(), AiFeatures.QuizGeneration, ct) is { } refusal
            ? StatusCode(403, new { message = refusal })
            : null;

    private Task CountGenerationAsync(CancellationToken ct) => _quota.RecordAsync(User.UserId(), AiFeatures.QuizGeneration, ct);

    /// <summary>
    /// Texte à partir duquel générer : celui du cours, lu au service Cours (l'utilisateur doit y avoir accès),
    /// sinon le texte envoyé, borné. Renvoie un refus (400 / 404) ou le texte.
    /// </summary>
    private async Task<(IActionResult? Refused, string Text)> SourceTextAsync(Guid? courseId, string? courseText, CancellationToken ct)
    {
        if (courseId is { } id)
        {
            var course = await _courses.GetAsync(id, ct);
            if (course is null) return (NotFound(new { message = "Cours introuvable." }), "");
            if (string.IsNullOrWhiteSpace(course.Text))
                return (BadRequest(new { message = "Ce cours n'a pas encore de texte : impossible d'en tirer des exercices." }), "");
            return (null, course.Text);
        }

        if (string.IsNullOrWhiteSpace(courseText))
            return (BadRequest(new { message = "Indiquez le cours dont tirer les exercices." }), "");
        return (null, CourseTextClient.Limit(courseText));
    }

    /// <summary>Série d'exercices corrigés : énoncés, indices et solutions détaillées.</summary>
    [HttpPost("problems")]
    public async Task<IActionResult> Problems([FromBody] GenerateProblemsRequest request, CancellationToken ct)
    {
        if (request.Count is < 1 or > 5)
            return BadRequest(new { message = "Le nombre d'exercices doit être entre 1 et 5." });
        var (invalid, text) = await SourceTextAsync(request.CourseId, request.CourseText, ct);
        if (invalid is not null) return invalid;
        if (await QuotaExceededAsync(ct) is { } refused) return refused;

        try
        {
            var problems = await _generator.GenerateProblemsAsync(text, request.Count, Difficulty(request.Difficulty), Learner(), ct);
            if (problems.Count == 0)
                return StatusCode(502, new { message = "L'IA n'a pas pu préparer ces exercices. Réessayez dans un instant." });
            await CountGenerationAsync(ct);
            return Ok(new ProblemSetResponse(request.Title, problems));
        }
        catch (GPOE26.ServiceDefaults.Llm.LlmNotConfiguredException)
        {
            return StatusCode(503, new { message = "Les exercices par l'IA ne sont pas activés sur ce serveur : aucune clé d'IA n'est configurée." });
        }
        catch (GPOE26.ServiceDefaults.Llm.LlmUnavailableException ex)
        {
            _logger.LogWarning(ex, "Exercices non générés");
            return StatusCode(502, new { message = "L'IA ne répond pas pour le moment. Réessayez dans un instant." });
        }
    }

    /// <summary>
    /// Génère un quiz à partir d'un texte de cours via l'IA.
    /// </summary>
    [HttpPost("generate")]
    [ProducesResponseType<GenerateQuizResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Generate([FromBody] GenerateQuizRequest request, CancellationToken ct)
    {
        if (request.NumberOfQuestions is < 1 or > 20)
            return BadRequest(new { message = "Le nombre de questions doit être entre 1 et 20." });

        var (invalid, text) = await SourceTextAsync(request.CourseId, request.CourseText, ct);
        if (invalid is not null) return invalid;
        if (await QuotaExceededAsync(ct) is { } refused) return refused;

        _logger.LogInformation("Generating quiz '{Title}' with {N} questions.", request.Title, request.NumberOfQuestions);

        List<Question> questions;
        try
        {
            questions = await _generator.GenerateQuestionsAsync(text, request.NumberOfQuestions, Difficulty(request.Difficulty), Learner(), ct);
        }
        catch (GPOE26.ServiceDefaults.Llm.LlmNotConfiguredException)
        {
            return StatusCode(503, new { message = "Les quiz par l'IA ne sont pas activés sur ce serveur : aucune clé d'IA n'est configurée." });
        }
        catch (Exception ex) when (ex is GPOE26.ServiceDefaults.Llm.LlmUnavailableException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Quiz non généré");
            return StatusCode(502, new { message = "L'IA n'a pas pu préparer ce quiz. Réessayez dans un instant." });
        }

        var session = new QuizSession
        {
            OwnerId = User.UserId(),
            Title = request.Title,
            SourceText = text,
            Questions = questions
        };

        _store.Save(session);
        await CountGenerationAsync(ct);

        var response = new GenerateQuizResponse(
            session.Id,
            session.Title,
            session.Questions.Select(q => new QuestionDto(q.Id, q.Text, q.Options)).ToList()
        );

        return CreatedAtAction(nameof(GetQuiz), new { id = session.Id }, response);
    }

    /// <summary>
    /// Récupère un quiz (sans les réponses correctes).
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<GenerateQuizResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetQuiz(Guid id)
    {
        var session = OwnSession(id);
        if (session is null) return NotFound();

        var response = new GenerateQuizResponse(
            session.Id,
            session.Title,
            session.Questions.Select(q => new QuestionDto(q.Id, q.Text, q.Options)).ToList()
        );

        return Ok(response);
    }

    /// <summary>
    /// Soumet les réponses d'un élève et retourne la correction avec le score.
    /// </summary>
    [HttpPost("{id:guid}/submit")]
    [ProducesResponseType<SubmitAnswersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Submit(Guid id, [FromBody] SubmitAnswersRequest request)
    {
        var session = OwnSession(id);
        if (session is null) return NotFound();

        // Une question répétée dans l'envoi ne doit pas faire échouer la correction : la première réponse compte
        var answerMap = (request.Answers ?? [])
            .GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => g.First().SelectedOptionIndex);
        var feedbacks = new List<AnswerFeedback>();
        int score = 0;

        foreach (var question in session.Questions)
        {
            var selected = answerMap.TryGetValue(question.Id, out var idx) ? idx : -1;
            var isCorrect = selected == question.CorrectOptionIndex;
            if (isCorrect) score++;

            feedbacks.Add(new AnswerFeedback
            {
                QuestionId = question.Id,
                QuestionText = question.Text,
                SelectedOptionIndex = selected,
                CorrectOptionIndex = question.CorrectOptionIndex,
                IsCorrect = isCorrect,
                Explanation = question.Explanation
            });
        }

        var result = new QuizResult
        {
            Score = score,
            Total = session.Questions.Count,
            Feedbacks = feedbacks
        };

        session.Result = result;
        _store.Save(session);

        _logger.LogInformation("Quiz {Id} submitted. Score: {Score}/{Total}.", id, score, result.Total);

        return Ok(new SubmitAnswersResponse(
            session.Id,
            result.Score,
            result.Total,
            result.Percentage,
            feedbacks
        ));
    }

    /// <summary>
    /// Retourne l'historique et les statistiques de l'utilisateur connecté.
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType<StatsResponse>(StatusCodes.Status200OK)]
    public IActionResult GetStats()
    {
        var userId = User.UserId();
        var completed = _store.GetAll()
            .Where(s => s.OwnerId == userId && s.Result is not null)
            .ToList();

        var history = completed.Select(s => new QuizSummary(
            s.Id,
            s.Title,
            s.Result!.Score,
            s.Result.Total,
            s.Result.Percentage,
            s.Result.SubmittedAt
        )).ToList();

        var stats = new StatsResponse(
            TotalQuizzesTaken: completed.Count,
            AverageScore: completed.Count == 0 ? 0 : Math.Round(completed.Average(s => s.Result!.Percentage), 1),
            BestScore: completed.Count == 0 ? 0 : completed.Max(s => s.Result!.Percentage),
            History: history
        );

        return Ok(stats);
    }
}