namespace Quiz.Model;

// --- Entités principales ---

public class QuizSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
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

public record GenerateQuizRequest(
    string Title,
    string CourseText,
    int NumberOfQuestions = 5
);

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