using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Quiz.Data;
using Quiz.Endpoints;
using Quiz.Model;
using Quiz.Service;

namespace Quiz.Controllers;

[ApiController]
[Route("api/[controller]")]
public class QuizController : ControllerBase
{
    private readonly IQuizGeneratorService _generator;
    private readonly IQuizStore _store;
    private readonly ILogger<QuizController> _logger;
    private readonly UserDirectoryClient _users;
    private readonly EvaluationContext _db;

    public QuizController(IQuizGeneratorService generator, IQuizStore store, ILogger<QuizController> logger,
        UserDirectoryClient users, EvaluationContext db)
    {
        _users = users;
        _db = db;
        _generator = generator;
        _store = store;
        _logger = logger;
    }

    private static string Difficulty(string? value) => value is "exigeant" or "approfondi" ? value : "standard";

    private string? Learner()
    {
        var parts = new[] { User.FindFirst("level")?.Value, User.FindFirst("filiere")?.Value }.Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join(", ", parts);
    }

    /// <summary>Quota mensuel de la formule (aucun quota : illimité), partagé entre quiz et séries d'exercices.</summary>
    private async Task<IActionResult?> QuotaExceededAsync(CancellationToken ct)
    {
        var userId = User.UserId();
        var month = DateTime.UtcNow.ToString("yyyy-MM");
        var feature = await _users.FeatureAsync("quiz-ai");
        if (feature is null)
            return StatusCode(403, new { message = "Les exercices générés par l'IA ne sont pas inclus dans votre formule." });
        if (feature.Limit is { } limit)
        {
            var used = await _db.Database
                .SqlQuery<int>($@"SELECT ""Count"" AS ""Value"" FROM ""QuizGenerationUsage"" WHERE ""UserId"" = {userId} AND ""Month"" = {month}")
                .FirstOrDefaultAsync(ct);
            if (used >= limit)
                return StatusCode(403, new { message = $"Vous avez utilisé vos {limit} générations gratuites de ce mois. Passez à Premium pour un accès illimité." });
        }
        return null;
    }

    private async Task CountGenerationAsync(CancellationToken ct)
    {
        var userId = User.UserId();
        var month = DateTime.UtcNow.ToString("yyyy-MM");
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO ""QuizGenerationUsage"" (""UserId"", ""Month"", ""Count"") VALUES ({userId}, {month}, 1)
               ON CONFLICT (""UserId"", ""Month"") DO UPDATE SET ""Count"" = ""QuizGenerationUsage"".""Count"" + 1", ct);
    }

    /// <summary>Série d'exercices corrigés : énoncés, indices et solutions détaillées.</summary>
    [Authorize]
    [HttpPost("problems")]
    public async Task<IActionResult> Problems([FromBody] GenerateProblemsRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CourseText))
            return BadRequest(new { message = "Le texte du cours est requis." });
        if (request.Count is < 1 or > 5)
            return BadRequest(new { message = "Le nombre d'exercices doit être entre 1 et 5." });
        if (await QuotaExceededAsync(ct) is { } refused) return refused;

        try
        {
            var problems = await _generator.GenerateProblemsAsync(request.CourseText, request.Count, Difficulty(request.Difficulty), Learner(), ct);
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
    [Authorize]
    [HttpPost("generate")]
    [ProducesResponseType<GenerateQuizResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Generate([FromBody] GenerateQuizRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CourseText))
            return BadRequest("Le texte du cours est requis.");

        if (request.NumberOfQuestions is < 1 or > 20)
            return BadRequest("Le nombre de questions doit être entre 1 et 20.");

        if (await QuotaExceededAsync(ct) is { } refused) return refused;

        _logger.LogInformation("Generating quiz '{Title}' with {N} questions.", request.Title, request.NumberOfQuestions);

        List<Question> questions;
        try
        {
            questions = await _generator.GenerateQuestionsAsync(request.CourseText, request.NumberOfQuestions, Difficulty(request.Difficulty), Learner(), ct);
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
            Title = request.Title,
            SourceText = request.CourseText,
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
        var session = _store.Get(id);
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
        var session = _store.Get(id);
        if (session is null) return NotFound();

        var answerMap = request.Answers.ToDictionary(a => a.QuestionId, a => a.SelectedOptionIndex);
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
    /// Retourne l'historique et les statistiques globales.
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType<StatsResponse>(StatusCodes.Status200OK)]
    public IActionResult GetStats()
    {
        var completed = _store.GetAll()
            .Where(s => s.Result is not null)
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