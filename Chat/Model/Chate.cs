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
