using System.ComponentModel.DataAnnotations;
using Quiz.Model;
using Quiz.Service;

namespace Quiz.DTOs;

// ══════════════════════════════════════════════════════════════════════════════
// RÈGLE : la vue élève (StudentQuestionDto) ne contient JAMAIS les bonnes réponses.
// Elles ne sont renvoyées qu'à l'enseignant, ou à l'élève dans la correction
// une fois les résultats publiés (Assessment.ResultsReleased).
// ══════════════════════════════════════════════════════════════════════════════

// ─── Saisie enseignant ────────────────────────────────────────────────────────

public record QuestionInput(
    QuestionType Type,
    [Required] string Text,
    [Range(0.25, 100)] decimal Points = 1,
    List<string>? Options = null,
    List<int>? CorrectOptions = null,
    bool? TrueFalseAnswer = null,
    List<string>? AcceptedAnswers = null,
    double? NumericAnswer = null,
    double? Tolerance = null,
    string? Unit = null,
    string? Criteria = null,
    string? Explanation = null,
    Guid? CourseId = null,
    Guid? SectionId = null
);

public record AssessmentInput(
    [Required, MinLength(1), MaxLength(200)] string Title,
    AssessmentKind Kind = AssessmentKind.Practice,
    string? Description = null,
    string? Subject = null,
    Guid? CourseId = null,
    List<Guid>? ClassroomIds = null,
    DateTime? OpensAt = null,
    DateTime? DueAt = null,
    [Range(1, 600)] int? DurationMinutes = null,
    [Range(1, 20)] int MaxAttempts = 1,
    bool Shuffle = false,
    bool ShowCorrection = true,
    bool AllowOffline = false,
    bool AiAssistedGrading = true,
    [Range(1, 100)] int ScaleTo = 20,
    List<QuestionInput>? Questions = null
);

public record GradeAnswerRequest([Range(0, 100)] decimal Points, string? Feedback = null);

public record SubmitAttemptRequest(List<AnswerInput> Answers);

// ─── Vue enseignant ───────────────────────────────────────────────────────────

public record AssessmentQuestionDto(
    Guid Id, int Order, QuestionType Type, string Text, decimal Points,
    List<string> Options, List<int> CorrectOptions, bool? TrueFalseAnswer, List<string> AcceptedAnswers,
    double? NumericAnswer, double? Tolerance, string? Unit, string? Criteria, string? Explanation,
    Guid? CourseId, Guid? SectionId)
{
    public AssessmentQuestionDto(AssessmentQuestion q) : this(
        q.Id, q.Order, q.Type, q.Text, q.Points, q.Options, q.CorrectOptions, q.TrueFalseAnswer, q.AcceptedAnswers,
        q.NumericAnswer, q.Tolerance, q.Unit, q.Criteria, q.Explanation, q.CourseId, q.SectionId)
    { }
}

public record AssessmentDto(
    Guid Id, string Title, string? Description, AssessmentKind Kind, AssessmentStatus Status,
    string? Subject, Guid? CourseId, List<Guid> ClassroomIds,
    DateTime? OpensAt, DateTime? DueAt, int? DurationMinutes, int MaxAttempts,
    bool Shuffle, bool ShowCorrection, bool AllowOffline, bool AiAssistedGrading, int ScaleTo,
    decimal MaxScore, int QuestionCount, DateTime CreatedAt, DateTime UpdatedAt, DateTime? PublishedAt,
    List<AssessmentQuestionDto>? Questions)
{
    public AssessmentDto(Assessment a, bool withQuestions) : this(
        a.Id, a.Title, a.Description, a.Kind, a.Status, a.Subject, a.CourseId, a.ClassroomIds,
        a.OpensAt, a.DueAt, a.DurationMinutes, a.MaxAttempts,
        a.Shuffle, a.ShowCorrection, a.AllowOffline, a.AiAssistedGrading, a.ScaleTo,
        a.MaxScore, a.Questions.Count, a.CreatedAt, a.UpdatedAt, a.PublishedAt,
        withQuestions ? a.Questions.OrderBy(q => q.Order).Select(q => new AssessmentQuestionDto(q)).ToList() : null)
    { }
}

/// <summary>Ligne du tableau des copies (vue enseignant)</summary>
public record SubmissionRowDto(
    Guid SubmissionId, Guid StudentId, string StudentName, int AttemptNumber, SubmissionStatus Status,
    DateTime StartedAt, DateTime? SubmittedAt, bool IsLate, decimal? Score, decimal MaxScore, decimal? ScoreScaled,
    int PendingManualAnswers);

// ─── Vue élève ────────────────────────────────────────────────────────────────

public record OptionDto(int Index, string Text);

public record StudentQuestionDto(Guid Id, QuestionType Type, string Text, decimal Points, List<OptionDto> Options, string? Unit);

/// <summary>Évaluation dans la liste de l'élève, avec son avancement</summary>
public record StudentAssessmentDto(
    Guid Id, string Title, string? Description, AssessmentKind Kind, AssessmentStatus Status, string? Subject,
    DateTime? OpensAt, DateTime? DueAt, int? DurationMinutes, int MaxAttempts, int AttemptsUsed,
    bool HasAttemptInProgress, int QuestionCount, decimal MaxScore, bool AllowOffline,
    decimal? BestScoreScaled, int ScaleTo);

public record AttemptDto(
    Guid SubmissionId, Guid AssessmentId, string Title, int AttemptNumber, DateTime StartedAt,
    DateTime? Deadline, List<StudentQuestionDto> Questions);

// ─── Copie corrigée ───────────────────────────────────────────────────────────

/// <summary>
/// Champs de correction (IsCorrect, Points, CorrectAnswer…) à null tant que l'élève
/// ne doit pas les voir. L'enseignant voit toujours tout, y compris la proposition de l'IA.
/// </summary>
public record AnswerReviewDto(
    Guid QuestionId, int Order, QuestionType Type, string Text, decimal MaxPoints,
    List<int> SelectedOptions, bool? TrueFalse, string? AnswerText, string? AttachmentUrl,
    bool? IsCorrect, decimal? Points, bool NeedsManualGrading, string? Feedback,
    AssessmentQuestionDto? Correction, decimal? AiSuggestedPoints, string? AiFeedback);

public record SubmissionDto(
    Guid SubmissionId, Guid AssessmentId, string Title, Guid StudentId, int AttemptNumber, SubmissionStatus Status,
    DateTime StartedAt, DateTime? SubmittedAt, bool IsLate,
    bool ResultsReleased, decimal? Score, decimal MaxScore, decimal? ScoreScaled, int ScaleTo,
    List<AnswerReviewDto>? Answers);

// ─── Suivi de la maîtrise ─────────────────────────────────────────────────────

public record AssessmentResultDto(
    Guid AssessmentId, string Title, AssessmentKind Kind, SubmissionStatus? Status,
    decimal? ScoreScaled, int ScaleTo, DateTime? SubmittedAt, bool IsLate);

/// <summary>Part des points obtenus sur les questions liées à un cours</summary>
public record CourseMasteryDto(Guid CourseId, decimal Earned, decimal Max, double Percent);

public record StudentProgressDto(
    Guid StudentId, string? Username, DateTime? LastLoginAt, double? AverageOn20,
    List<AssessmentResultDto> Results, List<CourseMasteryDto> Mastery);

public record AssessmentHeaderDto(Guid Id, string Title, AssessmentKind Kind, DateTime? DueAt, Guid? CourseId);

public record ClassroomProgressDto(Guid ClassroomId, List<AssessmentHeaderDto> Assessments, List<StudentProgressDto> Students);

public record AiSuggestionDto(Guid QuestionId, decimal Points, decimal MaxPoints, string Feedback);
