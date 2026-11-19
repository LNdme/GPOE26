namespace GPOE26.Agent.Abstractions;

public static class AgentToolNames
{
    public const string McpRead = "mcp.read";
}

/// <summary>
/// Provider-neutral tool description. The host must enforce authorization independently
/// of the tool list exposed to the model.
/// </summary>
public sealed record AgentToolDefinition(
    string Name,
    string Description,
    string InputSchemaJson);

public sealed record AgentToolCall(
    string CallId,
    string Name,
    string ArgumentsJson);

public sealed record AgentToolResult(string Content, bool IsError = false);

/// <summary>
/// Executes tools in the trusted application host. Implementations should be scoped to
/// the authenticated request so tool calls cannot select another user's identity.
/// </summary>
public interface IAgentToolExecutor
{
    IReadOnlyList<AgentToolDefinition> Tools { get; }

    Task<AgentToolResult> ExecuteAsync(AgentToolCall call, CancellationToken cancellationToken);
}
