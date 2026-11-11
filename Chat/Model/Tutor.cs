namespace Chat.Model;

// ══════════════════════════════════════════════════════════════════════════════
// Discussions avec le tuteur IA, conservées par élève et par cours : l'élève peut
// relire les explications après avoir fermé le cours, et épingler les plus utiles.
// ══════════════════════════════════════════════════════════════════════════════

public class TutorConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid CourseId { get; set; }

    /// <summary>Copie du titre et de la matière : la liste des discussions s'affiche sans interroger le service Cours</summary>
    public string CourseTitle { get; set; } = "";
    public string? Subject { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Messages déjà relus par la mémoire de l'agent (consolidation par lots, cf. TutorReflectionScheduler) :
    /// ceux d'après restent à relire. Null : rien n'a encore été relu.
    /// </summary>
    public DateTime? ReflectedAt { get; set; }

    public List<TutorMessage> Messages { get; set; } = new();
}

/// <summary>Échange avec l'assistant enseignant, conservé : la conversation continue d'une question à l'autre.</summary>
public class AssistantMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    /// <summary>Classe dont l'enseignant parlait (suivi anonymisé joint à la question), le cas échéant</summary>
    public Guid? ClassroomId { get; set; }
    /// <summary>"user" ou "assistant"</summary>
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class TutorMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public TutorConversation Conversation { get; set; } = null!;

    /// <summary>"user" ou "assistant"</summary>
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";

    /// <summary>Photo jointe par l'élève (exercice traité sur papier)</summary>
    public Guid? ImageId { get; set; }

    /// <summary>Explication épinglée par l'élève pour la revoir</summary>
    public bool Pinned { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class TutorImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string ContentType { get; set; } = "image/jpeg";
    public byte[] Data { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ─── DTO ──────────────────────────────────────────────────────────────────────

public record TutorMessageDto(Guid Id, string Role, string Content, Guid? ImageId, bool Pinned, DateTime CreatedAt)
{
    public TutorMessageDto(TutorMessage m) : this(m.Id, m.Role, m.Content, m.ImageId, m.Pinned, m.CreatedAt) { }
}

public record ConversationSummaryDto(Guid Id, Guid CourseId, string CourseTitle, string? Subject,
    int MessageCount, int PinnedCount, string? LastMessage, DateTime UpdatedAt);

public record ConversationDto(Guid? Id, Guid CourseId, string? CourseTitle, List<TutorMessageDto> Messages);

public record AskTutorRequest(string? Message, Guid? ImageId);

public record AskTutorResponse(TutorMessageDto Question, TutorMessageDto Answer);

public record PinRequest(bool Pinned);

public record PinnedExplanationDto(Guid MessageId, Guid CourseId, string CourseTitle, string? Subject,
    string? Question, string Content, DateTime CreatedAt);

public record ImageUploadedDto(Guid Id);
