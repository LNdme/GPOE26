using GPOE26.Web.Services;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Reflection;

namespace GPOE26.Web.Models;




#region GPOE26 API SERVICE DTOs
// ============================================
// gpo26 API SERVICE DTOs
// ============================================
public record class Hierarchy
{
    public int Id { get; set; }
    public string Role { get; set; } = string.Empty; // ex: "Administration", "Enseignants", "Étudiants"
    public string Description { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Specialization { get; set; }
    public string? ImageUrl { get; set; }
    public string? Name { get; set; }
    public string? PreName { get; set; }
    public string? Email { get; set; }
    public string? Citation { get; set; }
}


public record class NewArticle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Excerpt { get; set; }
    public string? ImageUrl { get; set; }
    public string Category { get; set; } = "Général"; // Général, Sport, Culturel, Administratif
    public bool IsPublished { get; set; } = false;
    public DateTime PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public record class SchoolActivity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;           // ex: "Club Robotique"
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;       // Sport, Art, Science, Club
    public string? Schedule { get; set; }                      // ex: "Mardi 15h-17h"
    public string? ResponsibleTeacher { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public record class SchoolEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Location { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Type { get; set; } = "Général"; // JPOE, Sportif, Culturel, Administratif, Voyage
    public bool IsPublic { get; set; } = true;
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}


public class Speech
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AuthorName { get; set; } = string.Empty;   // ex: "M. Ndongo"
    public string AuthorRole { get; set; } = string.Empty;   // ex: "Proviseur"
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Excerpt { get; set; }
    public string? AvatarUrl { get; set; }
    public string Occasion { get; set; } = string.Empty;     // ex: "Rentrée 2025-2026"
    public DateTime DeliveredAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Contact
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }


}
#endregion



#region GPOE26 API USER DTOs
// ============================================
// gpo26 API USER DTOs
// ============================================

public record LoginRequest(string Email, string Password);

public record RegisterRequest(
    string Email,
    string Username,
    string Password,
    string Role = "Student",
    string? Level = null,
    string? Specialite = null,
    string? Filiere = null,
    string Language = "fr"
);

public record UserProfileDto(
    Guid Id,
    string Email,
    string Username,
    string Role,
    string? Level,
    string? Specialite,
    string? Filiere,
    string Language,
    DateTime CreatedAt
);

public record AuthResponse(
    string Token,
    DateTime ExpiresAt,
    UserProfileDto Profile
);


#endregion

#region  COURS App DTOs
// ══════════════════════════════════════════════════════════════════════════════
//  DTOs Cours — modèle structuré avec sections
// ══════════════════════════════════════════════════════════════════════════════

public enum ContentType { Text, Pdf, Images }

public enum SectionType { Heading, Paragraph, Image }

public enum AssetKind { Pdf, Photo }

/// <summary>État de la mise en forme automatique du cours par l'agent Structurateur.</summary>
public enum FormatStatus { None, Pending, Ready, Failed }

public record CreateCourseRequest(
    [Required, MaxLength(200)] string Title,
    [Required, MaxLength(100)] string Subject,
    string? Description,
    List<CreateSectionDto>? Sections
);

public record UpdateCourseRequest(
    [Required, MaxLength(200)] string Title,
    [Required, MaxLength(100)] string Subject,
    string? Description,
    List<CreateSectionDto>? Sections
);

public record PatchCourseRequest(
    string? Title,
    string? Subject,
    string? Description
);

public record CreateSectionDto(
    SectionType Type,
    string Content,
    int Order,
    int Level = 0
);

public record CourseDto(
    Guid Id,
    string Title,
    string Subject,
    string? Description,
    ContentType ContentType,
    string? ExtractedText,
    string? FormattedMarkdown,
    FormatStatus FormatStatus,
    string? FormatError,
    DateTime? FormattedAt,
    string? PdfPath,
    List<SectionDto>? Sections,
    List<AssetDto>? Assets,
    DateTime CreatedAt,
    DateTime UpdatedAt
)
{
    /// <summary>
    /// Ce que le canvas de lecture affiche : le cours mis en forme si l'agent
    /// Structurateur a produit un résultat, sinon le texte brut en repli.
    /// </summary>
    public string? ReadableContent =>
        !string.IsNullOrWhiteSpace(FormattedMarkdown) ? FormattedMarkdown : ExtractedText;

    public IEnumerable<AssetDto> Photos =>
        (Assets ?? []).Where(a => a.Kind == AssetKind.Photo).OrderBy(a => a.Order);

    public AssetDto? Pdf =>
        (Assets ?? []).FirstOrDefault(a => a.Kind == AssetKind.Pdf);

    /// <summary>Y a-t-il un document original à proposer à côté du cours mis en forme ?</summary>
    public bool HasOriginalDocument => Pdf is not null || Photos.Any() || !string.IsNullOrEmpty(PdfPath);
}

public record SectionDto(
    Guid Id,
    SectionType Type,
    string Content,
    int Order,
    int Level
);

public record AssetDto(
    Guid Id,
    AssetKind Kind,
    string Path,
    string? OriginalFileName,
    string ContentType,
    int Order
)
{
    /// <summary>URL servie par le proxy /uploads/cours du frontend.</summary>
    public string Url => "/" + this.Path.TrimStart('/');
}

public record SearchHitDto(
    Guid ChunkId,
    string HeadingPath,
    string Content,
    int Order,
    double Score
);

// ── Parcours d'apprentissage ──────────────────────────────────────────────────

public enum StepKind { Lecture, MiniTest, TestFinal, ExerciceOuvert, QcmApplication }

public enum StepStatus { Locked, Available, InProgress, Passed, Failed }

public enum JourneyMode { Whole, PerSection }

public record StepDto(
    Guid Id,
    StepKind Kind,
    int Order,
    string Title,
    string? HeadingPath,
    StepStatus Status,
    int? Score,
    int? Total,
    int Attempts,
    DateTime? CompletedAt,
    List<string> WeakHeadings,
    int QuestionCount
)
{
    public bool IsReading => Kind == StepKind.Lecture;
    public bool IsQuiz => Kind is StepKind.MiniTest or StepKind.TestFinal or StepKind.QcmApplication;
    public bool IsOpenExercise => Kind == StepKind.ExerciceOuvert;

    public bool IsLocked => Status == StepStatus.Locked;
    public bool IsPassed => Status == StepStatus.Passed;

    /// <summary>Les trois temps du parcours, pour la barre de progression.</summary>
    public JourneyPhase Phase => Kind switch
    {
        StepKind.Lecture => JourneyPhase.Lire,
        StepKind.MiniTest or StepKind.TestFinal => JourneyPhase.Comprendre,
        _ => JourneyPhase.Consolider,
    };

    public string ScoreLabel => Score is { } s && Total is { } t ? $"{s}/{t}" : "";
}

/// <summary>Les trois temps affichés dans la barre de parcours.</summary>
public enum JourneyPhase { Lire, Comprendre, Consolider }

public record JourneyDto(
    Guid CourseId,
    JourneyMode Mode,
    List<StepDto> Steps,
    Guid? ActiveStepId,
    int PassedCount,
    double PassThresholdPercent
)
{
    public StepDto? Active => Steps.FirstOrDefault(s => s.Id == ActiveStepId);

    public StepDto? StepById(Guid id) => Steps.FirstOrDefault(s => s.Id == id);

    /// <summary>État global d'un des trois temps, pour la barre de parcours.</summary>
    public StepStatus PhaseStatus(JourneyPhase phase)
    {
        var steps = Steps.Where(s => s.Phase == phase).ToList();
        if (steps.Count == 0) return StepStatus.Locked;

        if (steps.All(s => s.IsPassed)) return StepStatus.Passed;
        if (steps.All(s => s.IsLocked)) return StepStatus.Locked;

        return steps.Any(s => s.Status == StepStatus.Failed) ? StepStatus.Failed : StepStatus.InProgress;
    }

    /// <summary>« Partie 2 sur 5 », pour un parcours découpé.</summary>
    public string? PartLabel(StepDto step)
    {
        if (Mode != JourneyMode.PerSection || step.HeadingPath is null) return null;

        var parts = Steps.Where(s => s.Kind == StepKind.Lecture).ToList();
        var index = parts.FindIndex(s => s.HeadingPath == step.HeadingPath);

        return index >= 0 ? $"Partie {index + 1} sur {parts.Count}" : null;
    }
}

public record StepResultRequest(int Score, int Total, List<string>? WeakHeadings);

// ── Exercice de consolidation ─────────────────────────────────────────────────

public record ExerciceResponse(string Statement);

/// <param name="Acquis">L'essentiel de l'attendu est-il là ?</param>
public record ExerciseCorrection(
    bool Acquis,
    int Score,
    List<string> PointsForts,
    List<string> PointsManquants,
    string Correction,
    string? Conseil
);

public record CourseSummaryDto(
    Guid Id,
    string Title,
    string Subject,
    string? Description,
    ContentType ContentType,
    FormatStatus FormatStatus,
    DateTime CreatedAt
);

#endregion

#region CHAT SERVICE DTOs

public record ChatMessageRequest(
    string Message,
    List<ConversationMessage> History,
    string? CourseContent = null,
    string? CourseId = null
);

public record ChatMessageResponse(
    string Reply,
    List<ConversationMessage> UpdatedHistory
);

public record ConversationMessage(string Role, string Content);

// --- Summary ---

public record CourseSummaryRequest(
    string? CourseContent = null,
    string? CourseId = null
);

public record CourseSummaryResponse(
    string Title,
    List<CoursePart> Parts
);

public record CoursePart(string Title, string Summary);

// --- Répétiteur multi-agents ---

/// <summary>Question posée au pipeline d'agents. Le contenu du cours n'est plus transmis :
/// le service Chat va le chercher lui-même et n'en récupère que les passages utiles.</summary>
public record TutorRequest(
    Guid CourseId,
    string Message,
    List<ConversationMessage> History
);

/// <summary>
/// Un évènement du flux SSE du répétiteur.
///
/// <c>Type</c> vaut :
///   • "step"  → une étape du pipeline vient de commencer (<c>Step</c>)
///   • "token" → un fragment de la réponse (<c>Token</c>)
///   • "done"  → réponse complète (<c>Reply</c>) et passages cités (<c>Citations</c>)
///   • "error" → échec (<c>Error</c>)
/// </summary>
public record TutorStreamEvent(
    string Type,
    string? Step = null,
    string? Token = null,
    string? Reply = null,
    List<string>? Citations = null,
    string? Intent = null,
    string? Error = null
);

#endregion


#region GPOE26 API QUIZ DTOs


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

public record AnswerFeedback(Guid QuestionId, string QuestionText, int SelectedOptionIndex, int CorrectOptionIndex, bool IsCorrect, string Explanation);

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

#endregion