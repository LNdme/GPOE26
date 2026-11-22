namespace Quiz.Model;

// --- Entités principales ---

public class QuizSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Utilisateur qui a demandé ce quiz : lui seul peut le lire, y répondre et le retrouver dans ses statistiques</summary>
    public Guid OwnerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string SourceText { get; set; } = string.Empty;
    public List<Question> Questions { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public QuizResult? Result { get; set; }
}

public class Question
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = string.Empty;
    public List<string> Options { get; set; } = [];
    public int CorrectOptionIndex { get; set; }
    public string Explanation { get; set; } = string.Empty;
}

public class QuizResult
{
    public int Score { get; set; }
    public int Total { get; set; }
    public double Percentage => Total == 0 ? 0 : Math.Round((double)Score / Total * 100, 1);
    public List<AnswerFeedback> Feedbacks { get; set; } = [];
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}

public class AnswerFeedback
{
    public Guid QuestionId { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public int SelectedOptionIndex { get; set; }
    public int CorrectOptionIndex { get; set; }
    public bool IsCorrect { get; set; }
    public string Explanation { get; set; } = string.Empty;
}

// --- DTOs Requêtes ---

/// <summary>CourseId : le texte est lu au service Cours (accès vérifié). CourseText n'est plus accepté que sans CourseId, borné.</summary>
public record GenerateQuizRequest(
    string Title,
    string? CourseText = null,
    int NumberOfQuestions = 5,
    string Difficulty = "standard",
    Guid? CourseId = null
);

public record GenerateProblemsRequest(
    string Title,
    string? CourseText = null,
    int Count = 3,
    string Difficulty = "exigeant",
    Guid? CourseId = null
);

public record ProblemSetResponse(string Title, List<Quiz.Service.ProblemItem> Problems);

public record SubmitAnswersRequest(
    List<StudentAnswer> Answers
);

public record StudentAnswer(
    Guid QuestionId,
    int SelectedOptionIndex
);

// --- DTOs Réponses ---

public record GenerateQuizResponse(
    Guid QuizId,
    string Title,
    List<QuestionDto> Questions
);

public record QuestionDto(
    Guid Id,
    string Text,
    List<string> Options
);

public record SubmitAnswersResponse(
    Guid QuizId,
    int Score,
    int Total,
    double Percentage,
    List<AnswerFeedback> Feedbacks
);

public record StatsResponse(
    int TotalQuizzesTaken,
    double AverageScore,
    double BestScore,
    List<QuizSummary> History
);

public record QuizSummary(
    Guid QuizId,
    string Title,
    int Score,
    int Total,
    double Percentage,
    DateTime Date
);