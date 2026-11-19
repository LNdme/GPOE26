namespace GPOE26.Agent.Abstractions;

/// <summary>
/// Runtime-independent source of the profiles available to an agent host.
/// </summary>
public interface IAgentProfileCatalog
{
    IReadOnlyCollection<AgentProfileDefinition> Profiles { get; }

    bool TryGet(string? profileId, out AgentProfileDefinition profile);
}
