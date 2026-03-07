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

public enum ContentType { Text, Pdf }

public enum SectionType { Heading, Paragraph, Image }

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
    string? PdfPath,
    List<SectionDto>? Sections,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record SectionDto(
    Guid Id,
    SectionType Type,
    string Content,
    int Order,
    int Level
);

public record CourseSummaryDto(
    Guid Id,
    string Title,
    string Subject,
    string? Description,
    ContentType ContentType,
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