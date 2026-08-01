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

    // ── Parcours d'apprentissage ──────────────────────────────────────────────────

    /// <summary>Enregistrement du résultat d'une étape évaluée.</summary>
    /// <param name="WeakHeadings">Sections sur lesquelles l'élève a échoué, pour l'y renvoyer.</param>
    /// <param name="StudentAnswer">Ce que l'élève a rédigé, pour les étapes ouvertes.</param>
    /// <param name="CorrectionSummary">Ce que la correction en a retenu, en une ou deux phrases.</param>
    public record StepResultRequest(
        int Score,
        int Total,
        List<string>? WeakHeadings,
        string? StudentAnswer = null,
        string? CorrectionSummary = null
    );

    /// <param name="QuestionCount">Nombre de questions à générer ; 0 si l'étape n'est pas un QCM.</param>
    public record StepDto(
        Guid Id,
        StepKind Kind,
        int Order,
        string Title,
        string? HeadingPath,
        StepStatus Status,
        int? Score,
        int? Total,
        int Attempts,
        DateTime? CompletedAt,
        List<string> WeakHeadings,
        int QuestionCount,
        string? StudentAnswer,
        string? CorrectionSummary
    )
    {
        public StepDto(CourseStep s) : this(
            s.Id,
            s.Kind,
            s.Order,
            s.Title,
            s.HeadingPath,
            s.Status,
            s.Score,
            s.Total,
            s.Attempts,
            s.CompletedAt,
            string.IsNullOrWhiteSpace(s.WeakHeadings)
                ? []
                : s.WeakHeadings.Split(" | ", StringSplitOptions.RemoveEmptyEntries).ToList(),
            Service.CourseJourneyBuilder.QuestionCountFor(s.Kind),
            s.StudentAnswer,
            s.CorrectionSummary
        )
        { }
    }

    /// <summary>Le parcours complet d'un cours et l'étape où l'élève en est.</summary>
    /// <param name="ActiveStepId">Première étape non validée : celle sur laquelle ouvrir la page.</param>
    public record JourneyDto(
        Guid CourseId,
        JourneyMode Mode,
        List<StepDto> Steps,
        Guid? ActiveStepId,
        int PassedCount,
        double PassThresholdPercent
    );

    // ── Séances de révision ───────────────────────────────────────────────────────

    /// <summary>Signal d'activité émis par la page d'étude ou par l'application bureau.</summary>
    /// <param name="OccurredAt">
    /// Quand le signal a eu lieu. Omis pour un signal émis en direct ; renseigné pour un
    /// signal rejoué après une période hors ligne, sans quoi il serait daté de la
    /// synchronisation et ferait apparaître une nuit de révision.
    /// </param>
    public record ActivityRequest(StudyActivity Activity, DateTime? OccurredAt = null);

    /// <summary>Une séance de révision, telle que la voit un parent.</summary>
    public record StudySessionDto(
        Guid Id,
        Guid CourseId,
        string CourseTitle,
        string Subject,
        DateTime StartedAt,
        DateTime LastActivityAt,
        int ActiveSeconds,
        int StepsCompleted,
        int ExercisesDone,
        int QuestionsAsked
    )
    {
        public int ActiveMinutes => (int)Math.Round(ActiveSeconds / 60.0);
    }

    // ── Suivi parental ────────────────────────────────────────────────────────────
    //
    // Ces DTOs sont la garantie centrale du suivi parental : ils ne portent aucun
    // contenu d'échange avec le répétiteur. Ce qui n'existe pas dans l'API ne peut pas
    // fuir dans une interface. Le parent reçoit des faits mesurés — du temps, des
    // scores, des notions fragiles — et ce que son enfant a lui-même rédigé.

    /// <summary>
    /// La réponse d'un élève à une étape rédigée, avec ce que la correction en a dit.
    /// C'est le signal le plus parlant du tableau de bord : l'enfant explique son cours
    /// avec ses mots, là où un score ne dit que « 7 sur 10 ».
    /// </summary>
    public record WrittenAnswerDto(
        StepKind Kind,
        string StepTitle,
        int? ScorePercent,
        DateTime? CompletedAt,
        string Answer,
        string? CorrectionSummary
    );

    /// <summary>Où en est un élève sur un cours donné.</summary>
    /// <param name="LastStudiedAt">Dernière séance de révision sur ce cours, s'il y en a eu.</param>
    /// <param name="WeakHeadings">Notions sur lesquelles il a échoué, dédupliquées.</param>
    public record ChildCourseProgressDto(
        Guid CourseId,
        string Title,
        string Subject,
        int StepsTotal,
        int StepsPassed,
        int StepsFailed,
        string? CurrentStepTitle,
        DateTime? LastStudiedAt,
        int ActiveSeconds,
        int ExercisesDone,
        List<string> WeakHeadings,
        WrittenAnswerDto? Synthesis
    )
    {
        public double CompletionPercent =>
            StepsTotal == 0 ? 0 : Math.Round((double)StepsPassed / StepsTotal * 100, 0);

        public bool IsFinished => StepsTotal > 0 && StepsPassed == StepsTotal;
    }

    /// <summary>Vue d'ensemble d'un enfant, pour la page d'accueil de l'espace famille.</summary>
    /// <param name="Since">Début de la fenêtre sur laquelle portent les totaux hebdomadaires.</param>
    public record ChildOverviewDto(
        Guid StudentId,
        DateTime Since,
        DateTime? LastSessionAt,
        int SessionsThisWeek,
        int ActiveSecondsThisWeek,
        int ExercisesThisWeek,
        int QuestionsThisWeek,
        int CoursesStarted,
        int CoursesFinished,
        List<ChildCourseProgressDto> Courses
    )
    {
        public int ActiveMinutesThisWeek => (int)Math.Round(ActiveSecondsThisWeek / 60.0);

        /// <summary>A-t-il travaillé cette semaine ? La première question d'un parent.</summary>
        public bool StudiedThisWeek => SessionsThisWeek > 0;
    }

    /// <summary>Le détail d'un cours pour un parent : le parcours et les séances qui l'ont produit.</summary>
    public record ChildCourseDetailDto(
        ChildCourseProgressDto Progress,
        JourneyDto Journey,
        List<StudySessionDto> Sessions,
        List<WrittenAnswerDto> WrittenAnswers
    );

    // ── Rendu du cours ────────────────────────────────────────────────────────────

    /// <summary>Une entrée du sommaire.</summary>
    /// <param name="Anchor">Ancre du titre dans le HTML, pour le défilement.</param>
    public record OutlineEntryDto(int Level, string Title, string Anchor);

    /// <summary>
    /// Un cours rendu, prêt à afficher.
    ///
    /// Le Web et l'application bureau montrent le même cours : le rendre deux fois, c'est
    /// garantir qu'ils divergeront — et la divergence porterait sur les ancres du
    /// sommaire, donc sur des liens morts d'un seul côté.
    /// </summary>
    public record RenderedCourseDto(
        Guid CourseId,
        string Title,
        string Subject,
        string Html,
        List<OutlineEntryDto> Outline,
        DateTime RenderedAt
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
