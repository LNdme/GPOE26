namespace Chat.Model;

// ══════════════════════════════════════════════════════════════════════════════
// Discussions avec le tuteur IA, conservées par élève et par cours : l'élève peut
// relire les explications après avoir fermé le cours, et épingler les plus utiles.
// ══════════════════════════════════════════════════════════════════════════════

public class TutorConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? WorkspaceId { get; set; }
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
    /// <summary>Modèle d'IA qui a écrit la réponse (null pour une question)</summary>
    public string? Model { get; set; }
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

    /// <summary>Modèle d'IA qui a écrit la réponse (null pour une question) : pour savoir d'où vient une erreur</summary>
    public string? Model { get; set; }

    /// <summary>Tokens envoyés au modèle pour cette réponse (place occupée dans sa fenêtre de contexte)</summary>
    public int? PromptTokens { get; set; }
    /// <summary>Tokens écrits par le modèle (réponse et réflexion)</summary>
    public int? CompletionTokens { get; set; }
    /// <summary>Dont tokens de réflexion, si le fournisseur les compte à part</summary>
    public int? ReasoningTokens { get; set; }

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

/// <summary>
/// Passage d'un cours indexé pour le tuteur (H12) : texte, titre de sa partie et vecteur de sens.
/// Refait quand le texte du cours change (TextHash) ou que le modèle d'embeddings change.
/// </summary>
public class CourseChunk
{
    public Guid CourseId { get; set; }
    public int Position { get; set; }
    public string TextHash { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public float[]? Embedding { get; set; }
    public string? EmbeddingModel { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ─── DTO ──────────────────────────────────────────────────────────────────────

/// <param name="Model">Modèle qui a rédigé la réponse (absent pour les questions de l'élève).</param>
/// <param name="PromptTokens">Tokens envoyés au modèle pour écrire cette réponse ; CompletionTokens, ceux qu'il a écrits.</param>
public record TutorMessageDto(Guid Id, string Role, string Content, Guid? ImageId, bool Pinned, DateTime CreatedAt, string? Model = null,
    int? PromptTokens = null, int? CompletionTokens = null, int? ReasoningTokens = null)
{
    public TutorMessageDto(TutorMessage m) : this(m.Id, m.Role, m.Content, m.ImageId, m.Pinned, m.CreatedAt, m.Model,
        m.PromptTokens, m.CompletionTokens, m.ReasoningTokens) { }
}

public record ConversationSummaryDto(Guid Id, Guid CourseId, string CourseTitle, string? Subject,
    int MessageCount, int PinnedCount, string? LastMessage, DateTime UpdatedAt);

public record ConversationDto(Guid? Id, Guid CourseId, string? CourseTitle, List<TutorMessageDto> Messages);

/// <param name="Model">Modèle choisi dans le sélecteur ; null ou absent : ordre du serveur (« Automatique »).</param>
/// <param name="ProfileId">Profil d'agent demandé (routage des profils, harnais d'agent).</param>
public record AskTutorRequest(string? Message, Guid? ImageId, string? Model = null, string? ProfileId = null);

/// <summary>Modèles proposés par le serveur, dans leur ordre de repli : texte, et lecture de photos.</summary>
/// <param name="Context">Taille de la fenêtre de contexte de chaque modèle, en tokens (quand le fournisseur la publie).</param>
public record TutorModelsDto(IReadOnlyList<string> Text, IReadOnlyList<string> Vision, IReadOnlyDictionary<string, int>? Context = null);

public record AskTutorResponse(TutorMessageDto Question, TutorMessageDto Answer);

public record PinRequest(bool Pinned);

public record PinnedExplanationDto(Guid MessageId, Guid CourseId, string CourseTitle, string? Subject,
    string? Question, string Content, DateTime CreatedAt);

public record ImageUploadedDto(Guid Id);
