namespace GPOE26.Agent.Abstractions;

/// <summary>
/// Declarative metadata for an installable agent extension. A valid manifest does not
/// imply that the extension is trusted, installed, or authorized to run.
/// </summary>
public sealed record AgentExtensionManifest(
    int ManifestVersion,
    string Id,
    string Version,
    string Name,
    string Description,
    AgentExtensionKind Kind,
    AgentExtensionPublisher Publisher,
    IReadOnlyList<AgentExtensionCapability> Capabilities,
    IReadOnlyList<AgentExtensionParameter> Parameters,
    IReadOnlyList<AgentExtensionPermission> RequestedPermissions);

public enum AgentExtensionKind
{
    McpServer,
    Skill,
    Profile
}

public sealed record AgentExtensionPublisher(
    string Id,
    string Name,
    string? SourceUri = null);

/// <summary>
/// A tool or other capability the extension advertises. Input schemas describe data
/// sent to the capability; they never grant access to platform resources.
/// </summary>
public sealed record AgentExtensionCapability(
    string Id,
    string Description,
    string? InputSchemaJson = null,
    IReadOnlyList<string>? RequiredPermissionIds = null);

/// <summary>
/// A setting required to configure an extension. Secret values are stored separately
/// by the host and are never part of a manifest.
/// </summary>
public sealed record AgentExtensionParameter(
    string Name,
    string Description,
    AgentExtensionParameterType Type,
    bool Required,
    bool IsSecret = false);

public enum AgentExtensionParameterType
{
    String,
    Boolean,
    Integer,
    Uri
}

/// <summary>
/// A permission an extension requests. Host policy and user consent must decide whether
/// it is granted; the manifest itself cannot grant permissions.
/// </summary>
public sealed record AgentExtensionPermission(
    string Id,
    string Description,
    bool Required);
