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
        DateTime UpdatedAt,
        CourseStatus Status,
        List<Guid> ClassroomIds,
        Guid OwnerId,
        string? StructuringStatus = null,
        string? SourceUrl = null,
        List<Guid>? LibraryOrganizationIds = null,
        Guid? SourceCourseId = null
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
            c.UpdatedAt,
            c.Status,
            c.ClassroomIds,
            c.OwnerId,
            c.StructuringStatus,
            c.SourceUrl,
            c.LibraryOrganizationIds,
            c.SourceCourseId
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

    public record ImportLinkRequest(string? Url);

    public record LabSuggestionDto(Guid Id, string Anchor, string Title, string Reason, string Block);

    /// <summary>
    /// Version allégée pour la liste des cours (sans contenu ni sections)
    /// </summary>
    public record CourseSummaryDto(
        Guid Id,
        string Title,
        string Subject,
        string? Description,
        ContentType ContentType,
        DateTime CreatedAt,
        CourseStatus Status,
        List<Guid> ClassroomIds,
        Guid OwnerId,
        List<Guid>? LibraryOrganizationIds = null,
        Guid? SourceCourseId = null
    )
    {
        public CourseSummaryDto(Course c) : this(
            c.Id,
            c.Title,
            c.Subject,
            c.Description,
            c.ContentType,
            c.CreatedAt,
            c.Status,
            c.ClassroomIds,
            c.OwnerId,
            c.LibraryOrganizationIds,
            c.SourceCourseId
        )
        { }
    }

    /// <summary>Partage d'un cours : publié pour ces classes, ou repassé en brouillon (Publish = false)</summary>
    public record ShareCourseRequest(bool Publish, List<Guid>? ClassroomIds);

    /// <summary>Avis « utile » : Mine pour le lecteur ; les totaux seulement pour l'auteur (null sinon).</summary>
    public record CourseFeedbackDto(bool? Mine, int? Useful, int? NotUseful);

    public record CourseFeedbackRequest(bool Useful);

    // ── Bibliothèque d'établissement ──────────────────────────────────────────────

    /// <summary>Dépôt d'un cours : la liste complète des bibliothèques où il doit figurer (vide = retiré partout).</summary>
    public record LibraryDepositRequest(List<Guid>? OrganizationIds);

    /// <summary>Cours d'une bibliothèque : AuthorName pour « d'après le cours de… », Mine si c'est le mien.</summary>
    public record LibraryCourseDto(
        Guid Id,
        string Title,
        string Subject,
        string? Description,
        ContentType ContentType,
        DateTime UpdatedAt,
        Guid OwnerId,
        string AuthorName,
        bool Mine,
        int Copies
    );

    /// <summary>Bibliothèque d'un établissement : CanManage = le lecteur peut retirer les dépôts des autres.</summary>
    public record LibraryCoursesDto(Guid OrganizationId, string Name, bool CanManage, List<LibraryCourseDto> Courses);
}
