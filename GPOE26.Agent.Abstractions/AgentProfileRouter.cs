namespace GPOE26.Agent.Abstractions;

/// <summary>
/// Neutral profile selection with a configured fallback for absent or unknown IDs.
/// </summary>
public sealed class AgentProfileRouter(
    IAgentProfileCatalog catalog,
    string fallbackProfileId = AgentProfileIds.Pedagogical) : IAgentProfileRouter
{
    private readonly AgentProfileDefinition _fallbackProfile =
        catalog.TryGet(fallbackProfileId, out var fallback)
            ? fallback
            : throw new ArgumentException(
                $"Fallback profile '{fallbackProfileId}' is not present in the catalog.",
                nameof(fallbackProfileId));

    public AgentProfileDefinition Resolve(string? profileId) =>
        catalog.TryGet(profileId, out var profile) ? profile : _fallbackProfile;
}
