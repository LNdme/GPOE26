using GPOE26.UI.Services;

namespace GPOE26.UI.Models;

// ══════════════════════════════════════════════════════════════════════════════
// Contrats des services Cours (enums numériques) et Quiz (enums en texte).
// ══════════════════════════════════════════════════════════════════════════════

// ─── Cours ────────────────────────────────────────────────────────────────────

public enum CourseContentType { Text, Pdf }
public enum CourseSectionType { Heading, Paragraph, Image }
public enum CourseStatus { Draft, Published }

public record CourseSummary(Guid Id, string Title, string Subject, string? Description, CourseContentType ContentType,
    DateTime CreatedAt, CourseStatus Status, List<Guid>? ClassroomIds, Guid OwnerId);

public record CourseSection(Guid Id, CourseSectionType Type, string Content, int Order, int Level);

public record CourseDetail(Guid Id, string Title, string Subject, string? Description, CourseContentType ContentType,
    string? ExtractedText, string? PdfPath, List<CourseSection>? Sections, DateTime CreatedAt, DateTime UpdatedAt,
    CourseStatus Status, List<Guid>? ClassroomIds, Guid OwnerId, string? StructuringStatus = null, string? SourceUrl = null)
{
    /// <summary>Contenu Markdown du cours, reconstitué à partir de ses sections.</summary>
    public string ToMarkdown()
    {
        if (Sections is not { Count: > 0 }) return ExtractedText ?? "";
        var sb = new System.Text.StringBuilder();
        foreach (var s in Sections.OrderBy(s => s.Order))
        {
            switch (s.Type)
            {
                case CourseSectionType.Heading:
                    sb.Append(new string('#', Math.Clamp(s.Level <= 0 ? 2 : s.Level + 1, 2, 4))).Append(' ').AppendLine(s.Content);
                    break;
                case CourseSectionType.Image:
                    sb.AppendLine($"![]({s.Content})");
                    break;
                default:
                    sb.AppendLine(s.Content);
                    break;
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}

public record ShareCourseRequest(bool Publish, List<Guid> ClassroomIds);

public record CourseSectionInput(CourseSectionType Type, string Content, int Order, int Level = 0);

/// <summary>Création ou modification d'un cours (le contenu rédigé tient dans une section Markdown).</summary>
public record CourseInput(string Title, string Subject, string? Description, List<CourseSectionInput>? Sections);

public record UploadedFile(string Url);

/// <summary>Labo proposé par l'agent. Block : le bloc Markdown, affichable pour un essai.</summary>
public record LabSuggestion(Guid Id, string Anchor, string Title, string Reason, string Block);

// ─── Quiz d'entraînement généré à partir d'un cours ───────────────────────────

/// <summary>Le service Quiz lit lui-même le texte du cours (CourseId) : le navigateur n'envoie plus le texte.</summary>
public record GeneratePracticeQuizRequest(string Title, Guid CourseId, int NumberOfQuestions, string Difficulty);
public record GeneratePracticeProblemsRequest(string Title, Guid CourseId, int Count, string Difficulty);
public record PracticeProblem(string Statement, List<string> Hints, string Solution);
public record PracticeProblemSet(string Title, List<PracticeProblem> Problems);
public record PracticeQuestion(Guid Id, string Text, List<string> Options);
public record PracticeQuiz(Guid QuizId, string Title, List<PracticeQuestion> Questions);
public record PracticeAnswer(Guid QuestionId, int SelectedOptionIndex);
public record PracticeFeedback(Guid QuestionId, string QuestionText, int SelectedOptionIndex, int CorrectOptionIndex, bool IsCorrect, string Explanation);
public record PracticeResult(Guid QuizId, int Score, int Total, double Percentage, List<PracticeFeedback> Feedbacks);

// ─── Évaluations ──────────────────────────────────────────────────────────────

public enum AssessmentKind { Practice, Homework, GradedExam, MockExam }
public enum AssessmentStatus { Draft, Published, Closed }
public enum QuestionType { SingleChoice, MultipleChoice, TrueFalse, ShortAnswer, Numeric, OpenEnded }
public enum SubmissionStatus { InProgress, Submitted, Graded }

/// <summary>Question telle que saisie dans l'éditeur (classe mutable pour le binding des formulaires).</summary>
public class QuestionDraft
{
    public QuestionType Type { get; set; } = QuestionType.SingleChoice;
    public string Text { get; set; } = "";
    public decimal Points { get; set; } = 1;
    public List<string> Options { get; set; } = ["", "", ""];
    public List<int> CorrectOptions { get; set; } = [];
    public bool? TrueFalseAnswer { get; set; } = true;
    public List<string> AcceptedAnswers { get; set; } = [];
    public double? NumericAnswer { get; set; }
    public double? Tolerance { get; set; }
    public string? Unit { get; set; }
    public string? Criteria { get; set; }
    public string? Explanation { get; set; }
    public Guid? CourseId { get; set; }
    public Guid? SectionId { get; set; }
}

public class AssessmentDraft
{
    public string Title { get; set; } = "";
    public AssessmentKind Kind { get; set; } = AssessmentKind.Practice;
    public string? Description { get; set; }
    public string? Subject { get; set; }
    public Guid? CourseId { get; set; }
    public List<Guid> ClassroomIds { get; set; } = [];
    public DateTime? OpensAt { get; set; }
    public DateTime? DueAt { get; set; }
    public int? DurationMinutes { get; set; }
    public int MaxAttempts { get; set; } = 1;
    public bool Shuffle { get; set; }
    public bool ShowCorrection { get; set; } = true;
    public bool AllowOffline { get; set; }
    public bool AiAssistedGrading { get; set; } = true;
    public int ScaleTo { get; set; } = 20;
    public List<QuestionDraft> Questions { get; set; } = [];
}

public record AssessmentQuestion(
    Guid Id, int Order, QuestionType Type, string Text, decimal Points,
    List<string> Options, List<int> CorrectOptions, bool? TrueFalseAnswer, List<string> AcceptedAnswers,
    double? NumericAnswer, double? Tolerance, string? Unit, string? Criteria, string? Explanation,
    Guid? CourseId, Guid? SectionId);

public record Assessment(
    Guid Id, string Title, string? Description, AssessmentKind Kind, AssessmentStatus Status,
    string? Subject, Guid? CourseId, List<Guid> ClassroomIds,
    DateTime? OpensAt, DateTime? DueAt, int? DurationMinutes, int MaxAttempts,
    bool Shuffle, bool ShowCorrection, bool AllowOffline, bool AiAssistedGrading, int ScaleTo,
    decimal MaxScore, int QuestionCount, DateTime CreatedAt, DateTime UpdatedAt, DateTime? PublishedAt,
    List<AssessmentQuestion>? Questions)
{
    public AssessmentDraft ToDraft() => new()
    {
        Title = Title, Kind = Kind, Description = Description, Subject = Subject, CourseId = CourseId,
        ClassroomIds = [.. ClassroomIds], OpensAt = OpensAt is { } o ? AppTime.ToLocal(o) : null, DueAt = DueAt is { } d ? AppTime.ToLocal(d) : null,
        DurationMinutes = DurationMinutes, MaxAttempts = MaxAttempts, Shuffle = Shuffle, ShowCorrection = ShowCorrection,
        AllowOffline = AllowOffline, AiAssistedGrading = AiAssistedGrading, ScaleTo = ScaleTo,
        Questions = (Questions ?? []).OrderBy(q => q.Order).Select(q => new QuestionDraft
        {
            Type = q.Type, Text = q.Text, Points = q.Points, Options = [.. q.Options], CorrectOptions = [.. q.CorrectOptions],
            TrueFalseAnswer = q.TrueFalseAnswer, AcceptedAnswers = [.. q.AcceptedAnswers], NumericAnswer = q.NumericAnswer,
            Tolerance = q.Tolerance, Unit = q.Unit, Criteria = q.Criteria, Explanation = q.Explanation,
            CourseId = q.CourseId, SectionId = q.SectionId
        }).ToList()
    };
}

public record StudentAssessment(
    Guid Id, string Title, string? Description, AssessmentKind Kind, AssessmentStatus Status, string? Subject,
    DateTime? OpensAt, DateTime? DueAt, int? DurationMinutes, int MaxAttempts, int AttemptsUsed,
    bool HasAttemptInProgress, int QuestionCount, decimal MaxScore, bool AllowOffline,
    decimal? BestScoreScaled, int ScaleTo, Guid? LastSubmissionId = null)
{
    public bool CanStart(DateTime now) =>
        Status == AssessmentStatus.Published
        && (OpensAt is null || OpensAt <= now)
        && (DueAt is null || DueAt > now)
        && (HasAttemptInProgress || AttemptsUsed < MaxAttempts);
}

public record AttemptOption(int Index, string Text);
public record AttemptQuestion(Guid Id, QuestionType Type, string Text, decimal Points, List<AttemptOption> Options, string? Unit);
public record Attempt(Guid SubmissionId, Guid AssessmentId, string Title, int AttemptNumber, DateTime StartedAt,
    DateTime? Deadline, List<AttemptQuestion> Questions, List<AnswerPhoto>? Photos = null, List<AnswerDraft>? Draft = null);

/// <summary>Photo d'une copie traitée sur papier ; Transcription = lecture par l'IA (null si elle n'a pas pu la lire).</summary>
public record AnswerPhoto(Guid Id, Guid QuestionId, int Page, string? Transcription, bool Pending = false);

public record AiFeedback(Guid QuestionId, string Feedback);

/// <summary>Réponse en cours de saisie par l'élève (mutable pour le binding).</summary>
public class AnswerDraft
{
    public Guid QuestionId { get; set; }
    public List<int> SelectedOptions { get; set; } = [];
    public bool? TrueFalse { get; set; }
    public string? Text { get; set; }
    public string? AttachmentUrl { get; set; }
}

public record SubmitAttemptRequest(List<AnswerDraft> Answers);

public record AnswerReview(
    Guid QuestionId, int Order, QuestionType Type, string Text, decimal MaxPoints,
    List<int> SelectedOptions, bool? TrueFalse, string? AnswerText, string? AttachmentUrl,
    bool? IsCorrect, decimal? Points, bool NeedsManualGrading, string? Feedback,
    AssessmentQuestion? Correction, decimal? AiSuggestedPoints, string? AiFeedback, List<AnswerPhoto>? Photos = null);

public record SubmissionReview(
    Guid SubmissionId, Guid AssessmentId, string Title, Guid StudentId, int AttemptNumber, SubmissionStatus Status,
    DateTime StartedAt, DateTime? SubmittedAt, bool IsLate,
    bool ResultsReleased, decimal? Score, decimal MaxScore, decimal? ScoreScaled, int ScaleTo,
    List<AnswerReview>? Answers);

public record SubmissionRow(
    Guid SubmissionId, Guid StudentId, string StudentName, int AttemptNumber, SubmissionStatus Status,
    DateTime StartedAt, DateTime? SubmittedAt, bool IsLate, decimal? Score, decimal MaxScore, decimal? ScoreScaled,
    int PendingManualAnswers);

public record GradeAnswerRequest(decimal Points, string? Feedback);
public record AiSuggestion(Guid QuestionId, decimal Points, decimal MaxPoints, string Feedback);

// ─── Suivi ────────────────────────────────────────────────────────────────────

public record AssessmentResult(Guid AssessmentId, string Title, AssessmentKind Kind, SubmissionStatus? Status,
    decimal? ScoreScaled, int ScaleTo, DateTime? SubmittedAt, bool IsLate);

public record CourseMastery(Guid CourseId, decimal Earned, decimal Max, double Percent);

public record StudentProgress(Guid StudentId, string? Username, DateTime? LastLoginAt, double? AverageOn20,
    List<AssessmentResult> Results, List<CourseMastery> Mastery);

public record AssessmentHeader(Guid Id, string Title, AssessmentKind Kind, DateTime? DueAt, Guid? CourseId);

public record ClassroomProgress(Guid ClassroomId, List<AssessmentHeader> Assessments, List<StudentProgress> Students);

// ─── Tuteur IA ────────────────────────────────────────────────────────────────

public record TutorMessage(Guid Id, string Role, string Content, Guid? ImageId, bool Pinned, DateTime CreatedAt)
{
    public bool FromStudent => Role == "user";
}

public record TutorConversation(Guid? Id, Guid CourseId, string? CourseTitle, List<TutorMessage> Messages);

public record TutorConversationSummary(Guid Id, Guid CourseId, string CourseTitle, string? Subject,
    int MessageCount, int PinnedCount, string? LastMessage, DateTime UpdatedAt);

public record TutorExchange(TutorMessage Question, TutorMessage Answer);

public record PinnedExplanation(Guid MessageId, Guid CourseId, string CourseTitle, string? Subject,
    string? Question, string Content, DateTime CreatedAt);

public record UploadedImage(Guid Id);

/// <summary>Image prête à envoyer (déjà redimensionnée par le navigateur).</summary>
public record ImageUpload(string FileName, string ContentType, byte[] Data);
