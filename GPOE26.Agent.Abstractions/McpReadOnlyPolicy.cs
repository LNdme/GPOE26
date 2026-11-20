namespace GPOE26.Agent.Abstractions;

public sealed class McpReadOnlyPolicy
{
    private static readonly string[] MutatingTokens =
    [
        "write", "create", "update", "delete", "remove", "insert",
        "mutate", "execute", "send", "post", "put", "patch",
        "publish", "commit", "merge", "approve", "reject"
    ];

    public bool TryAuthorize(
        AgentExtensionManifest manifest,
        McpPermissionGrant grant,
        string capabilityId,
        out AgentExtensionCapability capability,
        out string? error)
    {
        capability = null!;
        error = null;

        if (manifest.Kind != AgentExtensionKind.McpServer)
        {
            error = "The extension is not an MCP server.";
            return false;
        }
        if (!manifest.Id.Equals(grant.ExtensionId, StringComparison.OrdinalIgnoreCase))
        {
            error = "The permission grant does not belong to this MCP server.";
            return false;
        }
        if (grant.UserId == Guid.Empty || grant.WorkspaceId == Guid.Empty)
        {
            error = "The permission grant is not scoped to a user and workspace.";
            return false;
        }
        if (!grant.CapabilityIds.Any(id =>
                id.Equals(capabilityId, StringComparison.OrdinalIgnoreCase)))
        {
            error = "The capability was not explicitly approved.";
            return false;
        }
        var declaredCapability = manifest.Capabilities.FirstOrDefault(candidate =>
            candidate.Id.Equals(capabilityId, StringComparison.OrdinalIgnoreCase));
        if (declaredCapability is null)
        {
            error = "The capability is not declared by the MCP manifest.";
            return false;
        }
        if (MutatingTokens.Any(token =>
                declaredCapability.Id.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            error = "Mutating MCP capabilities are not allowed by the read-only adapter.";
            return false;
        }
        capability = declaredCapability;

        var missingPermissions = (declaredCapability.RequiredPermissionIds ?? [])
            .Where(required => !grant.PermissionIds.Contains(required, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (missingPermissions.Length > 0)
        {
            error = $"Missing approved permissions: {string.Join(", ", missingPermissions)}";
            return false;
        }
        return true;
    }
}
