namespace GPOE26.Agent.Abstractions;

/// <summary>
/// Runtime-independent definition of an agent profile.
/// Instructions guide model behavior; tool access must also be enforced by the host.
/// </summary>
public sealed record AgentProfileDefinition(
    string Id,
    string Name,
    string Description,
    string Instructions,
    IReadOnlyList<string> AllowedToolNames);
