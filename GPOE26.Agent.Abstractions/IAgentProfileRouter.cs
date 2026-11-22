namespace GPOE26.Agent.Abstractions;

/// <summary>
/// Resolves an optional profile identifier to a profile from the catalog.
/// </summary>
public interface IAgentProfileRouter
{
    AgentProfileDefinition Resolve(string? profileId);
}
