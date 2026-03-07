using Cours.Model;
using System.ComponentModel.DataAnnotations;

namespace Cours.DTOs
{
    // ══════════════════════════════════════════════════════════════════════════════
    //  DTOs pour l'API Cours — modèle structuré avec sections
    // ══════════════════════════════════════════════════════════════════════════════

    // ── Requêtes ──────────────────────────────────────────────────────────────────

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

    // ── Réponses ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Détail complet d'un cours avec ses sections.
    /// ExtractedText est toujours présent (calculé ou extrait du PDF).
    /// </summary>
    public record CourseDto(
        Guid Id,
        string Title,
        string Subject,
        string? Description,
        ContentType ContentType,
        string? ExtractedText,
        string? PdfPath,
        List<SectionDto> Sections,
        DateTime CreatedAt,
        DateTime UpdatedAt
    )
    {
        public CourseDto(Course c) : this(
            c.Id,
            c.Title,
            c.Subject,
            c.Description,
            c.ContentType,
            c.ExtractedText,
            c.PdfPath,
            (c.Sections ?? new List<CourseSection>())
                .OrderBy(s => s.Order)
                .Select(s => new SectionDto(s.Id, s.Type, s.Content, s.Order, s.Level))
                .ToList(),
            c.CreatedAt,
            c.UpdatedAt
        )
        { }
    }

    public record SectionDto(
        Guid Id,
        SectionType Type,
        string Content,
        int Order,
        int Level
    );

    /// <summary>
    /// Version allégée pour la liste des cours (sans contenu ni sections)
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
        public CourseSummaryDto(Course c) : this(
            c.Id,
            c.Title,
            c.Subject,
            c.Description,
            c.ContentType,
            c.CreatedAt
        )
        { }
    }
}
