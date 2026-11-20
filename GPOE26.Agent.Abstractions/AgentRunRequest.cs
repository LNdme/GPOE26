namespace GPOE26.Agent.Abstractions;

/// <summary>
/// Data required to run one agent turn. Platform authorization credentials are deliberately excluded.
/// </summary>
public sealed record AgentRunRequest(
    string RunId,
    Guid ConversationId,
    AgentProfileDefinition Profile,
    AgentPresentationContext PresentationContext,
    IReadOnlyList<AgentMessage> History,
    string UserMessage,
    IReadOnlyList<AgentAttachment> Attachments);

/// <summary>
/// Deliberately limited learner and subject context that may be included in model context.
/// Authorization data belongs to the host and must not be inferred from these values.
/// </summary>
public sealed record AgentPresentationContext(
    string? LearnerLevel = null,
    string? Language = null,
    string? Subject = null,
    string? CourseTitle = null,
    string? ReferenceContent = null);

public enum AgentMessageRole
{
    System,
    User,
    Assistant,
    Tool
}

public sealed record AgentMessage(
    AgentMessageRole Role,
    string Content,
    IReadOnlyList<AgentAttachment>? Attachments = null);

/// <summary>
/// Binary content supplied by the trusted application host, not a model-generated URL or path.
/// The host is responsible for validating size and media type before creating an attachment.
/// </summary>
public sealed record AgentAttachment(string ContentType, ReadOnlyMemory<byte> Content);
