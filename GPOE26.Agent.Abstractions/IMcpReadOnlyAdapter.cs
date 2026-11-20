using System.Security.Claims;
using System.Text.Json;

namespace GPOE26.Agent.Abstractions;

public sealed record McpReadOnlyResult(
    bool Allowed,
    bool Succeeded,
    string? Error,
    JsonElement? Data);

public sealed record McpCapabilityListingResult(
    bool Succeeded,
    string? Error,
    IReadOnlyCollection<AgentExtensionCapability> Capabilities);

public sealed record McpPermissionGrant(
    Guid UserId,
    Guid WorkspaceId,
    string ExtensionId,
    IReadOnlyCollection<string> CapabilityIds,
    IReadOnlyCollection<string> PermissionIds);

public interface IMcpPermissionGrantStore
{
    Task<McpPermissionGrant?> GetApprovedGrantAsync(
        Guid userId,
        Guid workspaceId,
        string extensionId,
        CancellationToken cancellationToken = default);
}

public interface IMcpServerProcessOptionsStore
{
    Task<McpServerProcessOptions?> GetOptionsAsync(
        Guid userId,
        Guid workspaceId,
        string extensionId,
        CancellationToken cancellationToken = default);
}

public sealed record McpServerProcessOptions(
    Guid UserId,
    Guid WorkspaceId,
    string ExtensionId,
    string ImageReference,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string?> EnvironmentVariables);

public interface IMcpReadOnlyAdapter
{
    Task<McpCapabilityListingResult> ListApprovedCapabilitiesAsync(
        ClaimsPrincipal caller,
        string extensionId,
        CancellationToken cancellationToken = default);

    Task<McpReadOnlyResult> ReadAsync(
        ClaimsPrincipal caller,
        string extensionId,
        string capabilityId,
        JsonElement arguments,
        CancellationToken cancellationToken = default);
}
