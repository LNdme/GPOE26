namespace Cours.Model
{

    /// <summary>
    /// Un cours peut contenir du texte saisi directement OU un PDF uploadé.
    /// Dans les deux cas, ExtractedText est toujours rempli —
    /// c'est ce champ que le service Chat enverra au LLM comme contexte.
    /// </summary>
    public class Course
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public required string Title { get; set; }

        /// <summary>
        /// Matière du cours (ex: "Mathématiques", "Physique", "Français").
        /// Utilisé pour filtrer et pour contextualiser le LLM.
        /// </summary>
        public required string Subject { get; set; }

        /// <summary>Description courte visible dans la liste des cours</summary>
        public string? Description { get; set; }

        // ─── Contenu ──────────────────────────────────────────────────────────────

        public ContentType ContentType { get; set; } = ContentType.Text;

        /// <summary>
        /// Contenu saisi directement par l'utilisateur (si ContentType = Text).
        /// </summary>
        public string? TextContent { get; set; }

        /// <summary>
        /// Chemin du PDF stocké sur le serveur (si ContentType = Pdf).
        /// Ex: "uploads/cours/abc123.pdf"
        /// </summary>
        public string? PdfPath { get; set; }

        /// <summary>
        /// Texte extrait du PDF via PdfPig.
        /// Toujours présent quelle que soit la source (Text ou Pdf) —
        /// c'est CE champ que le LLM reçoit comme contexte.
        /// </summary>
        public string? ExtractedText { get; set; }

        // ─── Propriétaire ─────────────────────────────────────────────────────────

        /// <summary>
        /// Id de l'utilisateur propriétaire — lu depuis le claim "sub" du JWT.
        /// Un utilisateur ne voit et ne modifie QUE ses propres cours.
        /// </summary>
        public Guid OwnerId { get; set; }

        // ─── Métadonnées ──────────────────────────────────────────────────────────
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public enum ContentType
    {
        Text,
        Pdf
    }






}
