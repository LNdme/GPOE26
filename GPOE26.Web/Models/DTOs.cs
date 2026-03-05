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
// gpo26 API SERVICE DTOs
// ============================================


// ══════════════════════════════════════════════════════════════════════════════
// RÈGLE DES DTOs :
//
//  AppUser (Model)      = structure de la TABLE en base de données
//                         → ne sort JAMAIS directement de l'API (contient PasswordHash !)
//
//  RegisterRequest      = données que le CLIENT envoie pour s'inscrire
//  LoginRequest         = données que le CLIENT envoie pour se connecter
//  AuthResponse         = ce qu'on renvoie après une auth réussie (token + infos de base)
//
//  UserProfileDto       = profil complet exposé par GET /auth/me
//                         → lu par le LLM pour personnaliser ses réponses
//
//  UpdateProfileRequest = données acceptées par PATCH /auth/me
// ══════════════════════════════════════════════════════════════════════════════

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
//  CreateCourseRequest  = données envoyées pour créer un cours en TEXTE
//  CourseDto            = ce qu'on renvoie au client (jamais le modèle brut)
//  UpdateCourseRequest  = données envoyées pour PUT (remplacement complet)
//
//  L'upload PDF a son propre endpoint : POST /cours/{id}/upload
//  pour ne pas mélanger JSON et multipart/form-data dans le même endpoint.
// ══════════════════════════════════════════════════════════════════════════════

public record CreateCourseRequest(
    [Required, MaxLength(200)] string Title,
    [Required, MaxLength(100)] string Subject,
    string? Description,
    // Si TextContent est fourni → cours en texte
    // Si TextContent est null  → le client uploadera un PDF ensuite via /upload
    string? TextContent
);

public record UpdateCourseRequest(
    [Required, MaxLength(200)] string Title,
    [Required, MaxLength(100)] string Subject,
    string? Description,
    string? TextContent
);

/// <summary>
/// Ce que l'API renvoie au client.
/// ExtractedText est inclus — le frontend peut l'afficher,
/// et le service Chat l'utilisera directement.
/// </summary>
public record CourseDto(
    Guid Id,
    string Title,
    string Subject,
    string? Description,
    ContentType ContentType,
    string? ExtractedText,
    string? PdfPath,
    DateTime CreatedAt,
    DateTime UpdatedAt
);


/// <summary>
/// Version allégée pour la liste des cours (sans ExtractedText qui peut être très long)
/// </summary>
public record CourseSummaryDto(
    Guid Id,
    string Title,
    string Subject,
    string? Description,
    ContentType ContentType,
    DateTime CreatedAt
);


public record PatchCourseRequest(
string? Title,
string? Subject,
string? Description
// Pas de TextContent — pour modifier le contenu, utiliser PUT ou /upload
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