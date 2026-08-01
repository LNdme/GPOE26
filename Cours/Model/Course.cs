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

        // ─── Parcours d'apprentissage ─────────────────────────────────────────────

        /// <summary>
        /// Rythme du parcours, déduit de la longueur du cours à la mise en forme.
        /// </summary>
        public JourneyMode JourneyMode { get; set; } = JourneyMode.Whole;

        /// <summary>
        /// Les étapes ordonnées : Lire → Comprendre → Consolider, éventuellement
        /// répétées par partie pour un cours long.
        /// </summary>
        public List<CourseStep> Steps { get; set; } = new();

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

    /// <summary>
    /// Une étape du parcours de l'élève sur ce cours.
    ///
    /// C'est aussi la trace de sa progression : score, nombre de tentatives, date de
    /// validation. Ces trois informations, jointes au <see cref="HeadingPath"/>, sont la
    /// matière première du modèle de maîtrise qui suivra l'élève sur l'année — d'où
    /// l'insistance à toujours les renseigner, même quand l'étape porte sur tout le cours.
    /// </summary>
    public class CourseStep
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid CourseId { get; set; }
        public Course Course { get; set; } = null!;

        public StepKind Kind { get; set; }

        /// <summary>Position dans le parcours (0-based, croissant).</summary>
        public int Order { get; set; }

        /// <summary>Libellé affiché, ex. « Partie 2 — Le coefficient directeur ».</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Section du cours couverte par l'étape, ex. « Les dérivées › Nombre dérivé ».
        /// Vide pour une étape portant sur le cours entier.
        /// </summary>
        public string? HeadingPath { get; set; }

        public StepStatus Status { get; set; } = StepStatus.Locked;

        // ─── Résultat ─────────────────────────────────────────────────────────────

        public int? Score { get; set; }
        public int? Total { get; set; }

        /// <summary>Nombre de tentatives, pour distinguer l'acquis du réussi de justesse.</summary>
        public int Attempts { get; set; }

        /// <summary>
        /// Date de validation. Sans horodatage, aucune décroissance de maîtrise ne sera
        /// calculable rétroactivement quand le suivi annuel arrivera.
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Sections sur lesquelles l'élève a échoué au dernier essai, séparées par « | ».
        /// Sert à le renvoyer au bon endroit du cours, et plus tard à cibler les rappels.
        /// </summary>
        public string? WeakHeadings { get; set; }

        /// <summary>
        /// Ce que l'élève a écrit, pour les étapes rédigées (exercice ouvert, synthèse).
        ///
        /// Avec <see cref="CorrectionSummary"/>, c'est le signal le plus parlant d'un
        /// tableau de bord parent : la compréhension du cours dans les mots de l'enfant,
        /// là où un score de QCM ne dit que « 7 sur 10 ».
        /// </summary>
        public string? StudentAnswer { get; set; }

        /// <summary>Ce que la correction a retenu de cette réponse, en une ou deux phrases.</summary>
        public string? CorrectionSummary { get; set; }

        /// <summary>Seuil de validation d'une étape évaluée.</summary>
        public const double PassThreshold = 0.7;

        public bool IsEvaluated => Kind != StepKind.Lecture;

        public double? Percentage =>
            Score is { } score && Total is > 0 ? (double)score / Total.Value : null;
    }

    public enum StepKind
    {
        /// <summary>Lire le cours, ou une partie du cours.</summary>
        Lecture,

        /// <summary>Court test de compréhension sur une partie.</summary>
        MiniTest,

        /// <summary>Test de compréhension sur l'ensemble du cours.</summary>
        TestFinal,

        /// <summary>Exercice à rédiger, corrigé par l'agent.</summary>
        ExerciceOuvert,

        /// <summary>QCM d'application, plus exigeant que le test de compréhension.</summary>
        QcmApplication,

        /// <summary>
        /// Question ouverte finale : l'élève a-t-il saisi l'essentiel du cours ?
        ///
        /// On peut valider chaque QCM d'un cours en cinq parties sans jamais avoir
        /// compris ce que le cours dit dans son ensemble. Cette étape comble cet écart.
        /// </summary>
        Synthese
    }

    public enum StepStatus
    {
        /// <summary>Pas encore accessible : l'étape précédente n'est pas validée.</summary>
        Locked,

        /// <summary>Accessible, jamais commencée.</summary>
        Available,

        /// <summary>Commencée mais pas terminée.</summary>
        InProgress,

        /// <summary>Validée.</summary>
        Passed,

        /// <summary>Tentée sans atteindre le seuil.</summary>
        Failed
    }

    /// <summary>
    /// Une séance de révision : ce que l'élève a réellement fait, et pendant combien de temps.
    ///
    /// Sans elle, on peut dire qu'une étape a été validée mais pas qu'il y a eu vingt
    /// minutes de travail mardi soir — or c'est précisément ce qu'un parent veut savoir.
    /// </summary>
    public class StudySession
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid StudentId { get; set; }

        public Guid CourseId { get; set; }
        public Course Course { get; set; } = null!;

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Temps réellement actif, et non l'écart entre l'ouverture et la fermeture de
        /// l'onglet : un onglet oublié la nuit ne doit pas compter huit heures de révision.
        /// </summary>
        public int ActiveSeconds { get; set; }

        public int StepsCompleted { get; set; }
        public int ExercisesDone { get; set; }
        public int QuestionsAsked { get; set; }

        /// <summary>Au-delà de ce silence, l'élève est parti : la séance suivante est une autre séance.</summary>
        public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Crédit maximal accordé entre deux signaux. La page en émet un par minute ;
        /// plafonner évite qu'un signal tardif — onglet en arrière-plan, machine en
        /// veille — ne gonfle la durée d'un coup.
        /// </summary>
        public const int MaxSecondsPerSignal = 90;

        public bool IsOpenAt(DateTime now) => now - LastActivityAt < IdleTimeout;

        /// <summary>
        /// L'heure à retenir pour un signal.
        ///
        /// Un signal rejoué après une période hors ligne porte sa propre date, et c'est
        /// elle qui compte : la dater de la synchronisation ferait apparaître une nuit
        /// entière de révision. Un horodatage dans le futur est en revanche refusé —
        /// il ne peut venir que d'une horloge déréglée ou d'une tentative de gonfler un
        /// temps de travail. La minute de tolérance absorbe les décalages ordinaires.
        /// </summary>
        public static DateTime ResolveSignalTime(DateTime? declared, DateTime now) =>
            declared is { } d && d <= now.AddMinutes(1)
                ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
                : now;

        /// <summary>
        /// Temps à créditer pour un signal reçu à <paramref name="at"/>.
        ///
        /// Plafonné, parce qu'un signal tardif — onglet en arrière-plan, machine en
        /// veille, lot rejoué — ne doit pas gonfler la durée d'un seul coup. Jamais
        /// négatif, parce qu'une horloge peut reculer.
        /// </summary>
        public int CreditFor(DateTime at) =>
            Math.Clamp((int)(at - LastActivityAt).TotalSeconds, 0, MaxSecondsPerSignal);
    }

    /// <summary>Nature d'un signal d'activité, pour savoir ce qui a été fait dans la séance.</summary>
    public enum StudyActivity
    {
        /// <summary>Signal périodique de présence pendant la lecture.</summary>
        Lecture,

        /// <summary>Une question posée au répétiteur.</summary>
        Question,

        /// <summary>Une étape du parcours validée.</summary>
        Etape,

        /// <summary>Un exercice rédigé et soumis.</summary>
        Exercice
    }

    /// <summary>Rythme du parcours, déduit de la longueur du cours.</summary>
    public enum JourneyMode
    {
        /// <summary>Cours court : on lit tout, puis on teste, puis on consolide.</summary>
        Whole,

        /// <summary>Cours long : lecture et mini-test partie par partie.</summary>
        PerSection
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
