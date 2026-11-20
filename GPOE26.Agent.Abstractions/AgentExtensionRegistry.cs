namespace GPOE26.Agent.Abstractions;

/// <summary>
/// Immutable in-memory registry for validated extension metadata.
/// </summary>
public sealed class AgentExtensionRegistry : IAgentExtensionRegistry
{
    private readonly IReadOnlyDictionary<string, AgentExtensionManifest> _manifestsById;

    public AgentExtensionRegistry(IEnumerable<AgentExtensionManifest> manifests)
    {
        ArgumentNullException.ThrowIfNull(manifests);

        var manifestMap = new Dictionary<string, AgentExtensionManifest>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in manifests)
        {
            var errors = AgentExtensionManifestValidator.Validate(manifest);
            if (errors.Count > 0)
                throw new ArgumentException(
                    $"Extension manifest is invalid: {string.Join(" ", errors)}",
                    nameof(manifests));

            if (!manifestMap.TryAdd(manifest.Id.Trim(), manifest))
                throw new ArgumentException($"Duplicate extension ID '{manifest.Id}'.", nameof(manifests));
        }

        _manifestsById = manifestMap;
        Manifests = Array.AsReadOnly(manifestMap.Values.ToArray());
    }

    public IReadOnlyCollection<AgentExtensionManifest> Manifests { get; }

    public bool TryGet(string? extensionId, out AgentExtensionManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(extensionId) &&
            _manifestsById.TryGetValue(extensionId.Trim(), out manifest!))
            return true;

        manifest = null!;
        return false;
    }
}
