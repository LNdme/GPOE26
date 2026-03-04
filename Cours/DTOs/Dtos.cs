using Cours.Model;
using System.ComponentModel.DataAnnotations;

namespace Cours.DTOs
{
    

    
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
    )
    {
        public CourseDto(Model.Course c) : this(
            c.Id,
            c.Title,
            c.Subject,
            c.Description,
            c.ContentType,
            c.ExtractedText,
            c.PdfPath,
            c.CreatedAt,
            c.UpdatedAt
        )
        { }
    }

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
    )
    {
        public CourseSummaryDto(Model.Course c) : this(
            c.Id,
            c.Title,
            c.Subject,
            c.Description,
            c.ContentType,
            c.CreatedAt
        )
        { }
    }

    public record PatchCourseRequest(
    string? Title,
    string? Subject,
    string? Description
    // Pas de TextContent — pour modifier le contenu, utiliser PUT ou /upload
);
}
