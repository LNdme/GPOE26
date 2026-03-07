namespace Cours.Model
{
    /// <summary>
    /// Un cours structuré : titre principal, sections hiérarchiques (titres, paragraphes, images),
    /// et/ou un PDF uploadé. ExtractedText est toujours calculé pour le LLM.
    /// </summary>
    public class Course
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public required string Title { get; set; }

        /// <summary>
        /// Matière du cours (ex: "Mathématiques", "Physique", "Français").
        /// </summary>
        public required string Subject { get; set; }

        /// <summary>Description courte visible dans la liste des cours</summary>
        public string? Description { get; set; }

        // ─── Contenu ──────────────────────────────────────────────────────────────

        public ContentType ContentType { get; set; } = ContentType.Text;

        /// <summary>
        /// Chemin du PDF stocké sur le serveur (si ContentType = Pdf).
        /// </summary>
        public string? PdfPath { get; set; }

        /// <summary>
        /// Texte extrait / concaténé — c'est CE champ que le LLM reçoit comme contexte.
        /// Pour un cours Text, il est calculé en concaténant les sections.
        /// Pour un cours PDF, il est extrait via PdfPig.
        /// </summary>
        public string? ExtractedText { get; set; }

        /// <summary>
        /// Sections ordonnées du cours (titres H1/H2/H3, paragraphes, images).
        /// Uniquement pour ContentType = Text.
        /// </summary>
        public List<CourseSection> Sections { get; set; } = new();

        // ─── Propriétaire ─────────────────────────────────────────────────────────

        /// <summary>
        /// Id de l'utilisateur propriétaire — lu depuis le claim "sub" du JWT.
        /// </summary>
        public Guid OwnerId { get; set; }

        // ─── Métadonnées ──────────────────────────────────────────────────────────
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Recalcule ExtractedText à partir des sections (pour ContentType = Text).
        /// </summary>
        public void RebuildExtractedText()
        {
            if (ContentType != ContentType.Text || Sections.Count == 0) return;

            var sb = new System.Text.StringBuilder();
            foreach (var s in Sections.OrderBy(s => s.Order))
            {
                if (s.Type == SectionType.Image) continue; // les images ne sont pas du texte
                sb.AppendLine(s.Content);
                sb.AppendLine();
            }
            ExtractedText = sb.ToString().Trim();
        }
    }

    /// <summary>
    /// Section d'un cours : un titre (H1/H2/H3), un paragraphe ou une image.
    /// </summary>
    public class CourseSection
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid CourseId { get; set; }
        public Course Course { get; set; } = null!;

        /// <summary>Type de section : Heading, Paragraph, Image</summary>
        public SectionType Type { get; set; } = SectionType.Paragraph;

        /// <summary>
        /// Contenu textuel (titre, paragraphe) ou URL d'image.
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>Position dans le cours (0-based, croissant)</summary>
        public int Order { get; set; }

        /// <summary>
        /// Niveau de titre : 1 = H1, 2 = H2, 3 = H3.
        /// 0 pour les paragraphes et images.
        /// </summary>
        public int Level { get; set; } = 0;
    }

    public enum ContentType
    {
        Text,
        Pdf
    }

    public enum SectionType
    {
        Heading,
        Paragraph,
        Image
    }
}
