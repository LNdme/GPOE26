namespace Cours.Model
{
    /// <summary>
    /// Un cours structuré : titre principal, sections hiérarchiques (titres, paragraphes, images),
    /// et/ou un PDF uploadé. ExtractedText est toujours calculé pour le LLM.
    /// </summary>
    public class Course
    {
        /// <summary>Matière d'un cours importé, en attendant que l'agent la détermine.</summary>
        public const string UnclassifiedSubject = "À classer";

        /// <summary>Début du titre d'un cours créé à partir de photos, remplacé ensuite par l'agent.</summary>
        public const string PhotosTitlePrefix = "Photos de cours";

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

        /// <summary>Mise en forme d'un PDF par l'IA : Pending, Done ou Failed (vide pour un cours rédigé).</summary>
        public string? StructuringStatus { get; set; }

        /// <summary>Adresse d'origine d'un cours importé par lien : elle reste affichée dans le lecteur.</summary>
        public string? SourceUrl { get; set; }

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

        // ─── Partage ──────────────────────────────────────────────────────────────

        /// <summary>Brouillon : visible du seul auteur. Publié : visible des classes ci-dessous.</summary>
        public CourseStatus Status { get; set; } = CourseStatus.Draft;

        /// <summary>Classes ou groupes (service User) avec lesquels le cours est partagé</summary>
        public List<Guid> ClassroomIds { get; set; } = new();

        public DateTime? PublishedAt { get; set; }

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
                // Le texte donné à l'IA n'a besoin ni des descriptions de labos ni des adresses d'images
                sb.AppendLine(System.Text.RegularExpressions.Regex.Replace(s.Content, @"```simulation[\s\S]*?```|!\[[^\]]*\]\([^)]+\)", ""));
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

    /// <summary>Labo proposé par l'agent, en attente de la décision du propriétaire du cours.</summary>
    public class CourseLabSuggestion
    {
        public const string Pending = "Pending";
        public const string Accepted = "Accepted";
        public const string Refused = "Refused";

        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CourseId { get; set; }
        public string Anchor { get; set; } = "";
        public string Title { get; set; } = "";
        public string Reason { get; set; } = "";

        /// <summary>Bloc Markdown du labo, construit et contrôlé par LabProposalRules.</summary>
        public string Block { get; set; } = "";
        public string Status { get; set; } = Pending;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// « Ce cours vous a-t-il été utile ? » : un avis par lecteur et par cours, qu'il peut changer ou retirer.
    /// L'auteur du cours voit le total ; personne ne voit qui a répondu quoi.
    /// </summary>
    public class CourseFeedback
    {
        public Guid CourseId { get; set; }
        public Guid UserId { get; set; }
        public bool Useful { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public enum CourseStatus
    {
        Draft,
        Published
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
