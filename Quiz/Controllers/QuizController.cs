using Microsoft.AspNetCore.Mvc;
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

    public QuizController(IQuizGeneratorService generator, IQuizStore store, ILogger<QuizController> logger)
    {
        _generator = generator;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Génère un quiz à partir d'un texte de cours via l'IA.
    /// </summary>
    [HttpPost("generate")]
    [ProducesResponseType<GenerateQuizResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Generate([FromBody] GenerateQuizRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CourseText))
            return BadRequest("Le texte du cours est requis.");

        if (request.NumberOfQuestions is < 1 or > 20)
            return BadRequest("Le nombre de questions doit être entre 1 et 20.");

        _logger.LogInformation("Generating quiz '{Title}' with {N} questions.", request.Title, request.NumberOfQuestions);

        var questions = await _generator.GenerateQuestionsAsync(request.CourseText, request.NumberOfQuestions, ct);

        var session = new QuizSession
        {
            Title = request.Title,
            SourceText = request.CourseText,
            Questions = questions
        };

        _store.Save(session);

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