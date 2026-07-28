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

    /// <summary>Requête de recherche sémantique dans un cours.</summary>
    public record SearchCourseRequest(
        [Required] string Query,
        int K = 5
    );

    // ── Réponses ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Détail complet d'un cours.
    ///
    /// FormattedMarkdown est ce que l'élève lit ; ExtractedText reste le texte brut
    /// (entrée de la mise en forme), conservé pour diagnostic et repli.
    /// </summary>
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
        List<SectionDto> Sections,
        List<AssetDto> Assets,
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
            c.FormattedMarkdown,
            c.FormatStatus,
            c.FormatError,
            c.FormattedAt,
            c.PdfPath,
            (c.Sections ?? new List<CourseSection>())
                .OrderBy(s => s.Order)
                .Select(s => new SectionDto(s.Id, s.Type, s.Content, s.Order, s.Level))
                .ToList(),
            (c.Assets ?? new List<CourseAsset>())
                .OrderBy(a => a.Order)
                .Select(a => new AssetDto(a.Id, a.Kind, a.Path, a.OriginalFileName, a.ContentType, a.Order))
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
    /// Un document original. <paramref name="Path"/> est relatif à wwwroot ; le frontend
    /// le sert via son proxy /uploads/cours/{path}.
    /// </summary>
    public record AssetDto(
        Guid Id,
        AssetKind Kind,
        string Path,
        string? OriginalFileName,
        string ContentType,
        int Order
    );

    /// <summary>Un fragment retrouvé par la recherche sémantique.</summary>
    public record SearchHitDto(
        Guid ChunkId,
        string HeadingPath,
        string Content,
        int Order,
        double Score
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
        FormatStatus FormatStatus,
        DateTime CreatedAt
    )
    {
        public CourseSummaryDto(Course c) : this(
            c.Id,
            c.Title,
            c.Subject,
            c.Description,
            c.ContentType,
            c.FormatStatus,
            c.CreatedAt
        )
        { }
    }
}
