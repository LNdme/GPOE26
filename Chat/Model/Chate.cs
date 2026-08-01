namespace Chat.Model
{
    public record ChatMessageRequest(
      string Message,
      List<ConversationMessage>? History,
      string? CourseContent = null,
      string? CourseId = null
  )
    {
        public List<ConversationMessage> History { get; init; } = History ?? [];
    }

    public record ChatMessageResponse(
        string Reply,
        List<ConversationMessage> UpdatedHistory
    );

    public record ConversationMessage(string Role, string Content);

    // --- Summary ---

    public record CourseSummaryRequest(
        string? CourseContent = null,
        string? CourseId = null
    );

    public record CourseSummaryResponse(
        string Title,
        List<CoursePart> Parts
    );

    public record CoursePart(string Title, string Summary);

    // --- Draft Generation ---
    public record CourseDraftRequest(
        string Subject,
        string? AdditionalInstructions = null
    );

    // ══════════════════════════════════════════════════════════════════════════
    //  Répétiteur multi-agents
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>Vue du cours renvoyée par le service Cours.</summary>
    public record CourseSnapshot(
        Guid Id,
        string Title,
        string Subject,
        string? Description,
        string? ExtractedText,
        string? FormattedMarkdown
    )
    {
        /// <summary>Le contenu à donner aux agents : mis en forme si disponible.</summary>
        public string? Content =>
            !string.IsNullOrWhiteSpace(FormattedMarkdown) ? FormattedMarkdown : ExtractedText;
    }

    /// <summary>Un passage du cours retrouvé par la recherche vectorielle.</summary>
    public record CoursePassage(
        Guid ChunkId,
        string HeadingPath,
        string Content,
        int Order,
        double Score
    );

    /// <summary>Ce que l'élève cherche à obtenir — détermine le chemin dans le pipeline.</summary>
    public enum StudentIntent
    {
        /// <summary>Comprendre une notion du cours.</summary>
        Explication,

        /// <summary>Obtenir la définition précise d'un terme.</summary>
        Definition,

        /// <summary>Voir un cas concret d'application.</summary>
        Exemple,

        /// <summary>S'entraîner sur un exercice.</summary>
        Exercice,

        /// <summary>Avoir une vue d'ensemble du cours.</summary>
        Resume,

        /// <summary>Savoir comment procéder, étape par étape.</summary>
        Methode,

        /// <summary>La question ne porte pas sur le cours.</summary>
        HorsSujet
    }

    /// <summary>Question adressée au répétiteur.</summary>
    public record TutorRequest(
        Guid CourseId,
        string Message,
        List<ConversationMessage>? History
    )
    {
        public List<ConversationMessage> History { get; init; } = History ?? [];
    }

    /// <summary>Texte à lire à voix haute.</summary>
    public record SpeakRequest(string Text);

    // --- Exercice de consolidation ---

    /// <summary>Demande d'un exercice ouvert sur un cours, éventuellement sur une seule partie.</summary>
    public record ExerciceRequest(Guid CourseId, string? HeadingPath);

    public record ExerciceResponse(string Statement);

    /// <summary>Réponse rédigée par l'élève, à corriger.</summary>
    public record CorrectionRequest(Guid CourseId, string? HeadingPath, string Statement, string Answer);

    /// <summary>
    /// Correction d'un exercice ouvert.
    /// </summary>
    /// <param name="Acquis">L'essentiel de l'attendu est-il là ?</param>
    /// <param name="Score">Appréciation sur 100, pour alimenter la progression.</param>
    public record ExerciseCorrection(
        bool Acquis,
        int Score,
        List<string> PointsForts,
        List<string> PointsManquants,
        string Correction,
        string? Conseil
    );

    // ── Bilan parental ────────────────────────────────────────────────────────

    /// <summary>Demande d'un bilan sur un enfant et un cours.</summary>
    public record BilanRequest(Guid StudentId, Guid CourseId);

    /// <summary>
    /// Le bilan rendu au parent : du texte, et rien d'autre.
    ///
    /// Pas de champ « échanges », pas de champ « messages » : la garantie de cette
    /// phase tient dans la forme du DTO autant que dans le prompt de l'agent.
    /// </summary>
    public record BilanResponse(string Text, DateTime GeneratedAt);

    /// <summary>
    /// Miroir de Cours.DTOs.ChildCourseDetailDto, réduit à ce dont le bilan a besoin.
    ///
    /// ⚠️ Les enums transitent en nombres entre les services : l'ordre de StepKind
    /// doit rester identique à celui de Cours.Model.StepKind.
    /// </summary>
    public record ChildCourseDetail(
        ChildCourseProgress Progress,
        List<ChildWrittenAnswer> WrittenAnswers
    );

    public record ChildCourseProgress(
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
        List<string> WeakHeadings
    );

    public record ChildWrittenAnswer(
        int Kind,
        string StepTitle,
        int? ScorePercent,
        DateTime? CompletedAt,
        string Answer,
        string? CorrectionSummary
    )
    {
        /// <summary>StepKind.Synthese vaut 5 côté Cours ; les enums passent en nombres.</summary>
        public const int SyntheseKind = 5;

        public bool IsSynthesis => Kind == SyntheseKind;
    }

    /// <summary>
    /// Évènement du flux SSE. <c>Type</c> vaut "step", "token", "done" ou "error".
    /// </summary>
    public record TutorStreamEvent(
        string Type,
        string? Step = null,
        string? Token = null,
        string? Reply = null,
        List<string>? Citations = null,
        string? Intent = null,
        string? Error = null
    );




}
