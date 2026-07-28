using Pgvector;

namespace Cours.Model
{
    /// <summary>
    /// Un cours structuré : titre principal, sections hiérarchiques (titres, paragraphes, images),
    /// et/ou des documents uploadés (PDF, photos). ExtractedText est toujours calculé pour le LLM.
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
        /// Conservé pour compatibilité : les nouveaux uploads alimentent aussi <see cref="Assets"/>.
        /// </summary>
        public string? PdfPath { get; set; }

        /// <summary>
        /// Texte brut du cours : concaténation des sections (ContentType = Text),
        /// extraction PdfPig (Pdf) ou transcription vision (Images).
        /// C'est l'ENTRÉE de l'agent Structurateur, pas ce que l'élève lit.
        /// </summary>
        public string? ExtractedText { get; set; }

        /// <summary>
        /// Le cours remis en forme par l'agent Structurateur : grand titre, hiérarchie,
        /// encadrés pédagogiques, formules KaTeX. C'est CE champ que le canvas de lecture
        /// affiche, et c'est lui qui est découpé en <see cref="Chunks"/> pour la recherche.
        /// </summary>
        public string? FormattedMarkdown { get; set; }

        public FormatStatus FormatStatus { get; set; } = FormatStatus.None;

        public DateTime? FormattedAt { get; set; }

        /// <summary>Message d'erreur de la dernière mise en forme échouée, affiché à l'élève.</summary>
        public string? FormatError { get; set; }

        /// <summary>
        /// Sections ordonnées du cours (titres H1/H2/H3, paragraphes, images).
        /// Uniquement pour ContentType = Text.
        /// </summary>
        public List<CourseSection> Sections { get; set; } = new();

        /// <summary>Documents originaux déposés par l'élève : PDF et photos.</summary>
        public List<CourseAsset> Assets { get; set; } = new();

        /// <summary>Fragments vectorisés du cours, pour la recherche sémantique.</summary>
        public List<CourseChunk> Chunks { get; set; } = new();

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

        /// <summary>
        /// Ce que l'élève doit lire : le cours mis en forme si disponible,
        /// sinon le texte brut en repli.
        /// </summary>
        public string? ReadableContent =>
            !string.IsNullOrWhiteSpace(FormattedMarkdown) ? FormattedMarkdown : ExtractedText;
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

    /// <summary>
    /// Un document original déposé par l'élève. Distinct de <see cref="CourseSection"/> :
    /// les sections portent le contenu éditorial, les assets gardent la source telle qu'elle
    /// a été fournie, pour que l'onglet « Document original » puisse toujours l'afficher.
    /// </summary>
    public class CourseAsset
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid CourseId { get; set; }
        public Course Course { get; set; } = null!;

        public AssetKind Kind { get; set; }

        /// <summary>Chemin relatif dans wwwroot, ex. « uploads/cours/{guid}.pdf ».</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>Nom du fichier tel que déposé, affiché à l'élève.</summary>
        public string? OriginalFileName { get; set; }

        public string ContentType { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        /// <summary>Ordre d'affichage — pour des photos, l'ordre des pages du cours.</summary>
        public int Order { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Un fragment du cours mis en forme, avec son vecteur, pour la recherche sémantique.
    /// </summary>
    public class CourseChunk
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid CourseId { get; set; }
        public Course Course { get; set; } = null!;

        /// <summary>
        /// Chemin des titres menant au fragment, ex. « Les dérivées › Nombre dérivé ».
        /// Sert à la fois de contexte pour la vectorisation et de référence citable
        /// dans les réponses du tuteur.
        /// </summary>
        public string HeadingPath { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public int Order { get; set; }

        public int EstimatedTokens { get; set; }

        /// <summary>
        /// Vecteur du fragment. La dimension est figée dans le schéma (voir CoursContext) :
        /// changer de modèle d'embedding impose une migration et une réindexation.
        /// </summary>
        public Vector? Embedding { get; set; }
    }

    public enum ContentType
    {
        Text,
        Pdf,
        Images
    }

    public enum SectionType
    {
        Heading,
        Paragraph,
        Image
    }

    public enum AssetKind
    {
        Pdf,
        Photo
    }

    /// <summary>État de la mise en forme automatique du cours par l'agent Structurateur.</summary>
    public enum FormatStatus
    {
        /// <summary>Jamais lancée — cours texte saisi à la main, par exemple.</summary>
        None,

        /// <summary>En cours de traitement.</summary>
        Pending,

        /// <summary>FormattedMarkdown est à jour et indexé.</summary>
        Ready,

        /// <summary>La dernière tentative a échoué ; voir FormatError.</summary>
        Failed
    }
}
