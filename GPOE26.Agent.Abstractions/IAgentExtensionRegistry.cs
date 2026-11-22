namespace GPOE26.Agent.Abstractions;

/// <summary>
/// Runtime-independent view of extension manifests known to the host. A registry
/// entry is metadata only and must not be treated as an installation or grant.
/// </summary>
public interface IAgentExtensionRegistry
{
    IReadOnlyCollection<AgentExtensionManifest> Manifests { get; }

    bool TryGet(string? extensionId, out AgentExtensionManifest manifest);
}
