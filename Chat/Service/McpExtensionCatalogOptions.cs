using GPOE26.Agent.Abstractions;

namespace Chat.Service;

public sealed class McpExtensionCatalogOptions
{
    public List<McpExtensionManifestOptions> Extensions { get; set; } = [];
}

public sealed class McpExtensionManifestOptions
{
    public int ManifestVersion { get; set; } = AgentExtensionManifestValidator.CurrentManifestVersion;

    public string Id { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public AgentExtensionKind Kind { get; set; } = AgentExtensionKind.McpServer;

    public McpExtensionPublisherOptions Publisher { get; set; } = new();

    public List<McpExtensionCapabilityOptions> Capabilities { get; set; } = [];

    public List<McpExtensionParameterOptions> Parameters { get; set; } = [];

    public List<McpExtensionPermissionOptions> RequestedPermissions { get; set; } = [];

    public AgentExtensionManifest ToManifest() => new(
        ManifestVersion,
        Id,
        Version,
        Name,
        Description,
        Kind,
        new AgentExtensionPublisher(Publisher.Id, Publisher.Name, Publisher.SourceUri),
        Capabilities.Select(capability => new AgentExtensionCapability(
            capability.Id,
            capability.Description,
            capability.InputSchemaJson,
            capability.RequiredPermissionIds)).ToArray(),
        Parameters.Select(parameter => new AgentExtensionParameter(
            parameter.Name,
            parameter.Description,
            parameter.Type,
            parameter.Required,
            parameter.IsSecret)).ToArray(),
        RequestedPermissions.Select(permission => new AgentExtensionPermission(
            permission.Id,
            permission.Description,
            permission.Required)).ToArray());
}

public sealed class McpExtensionPublisherOptions
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? SourceUri { get; set; }
}

public sealed class McpExtensionCapabilityOptions
{
    public string Id { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? InputSchemaJson { get; set; }

    public List<string> RequiredPermissionIds { get; set; } = [];
}

public sealed class McpExtensionParameterOptions
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public AgentExtensionParameterType Type { get; set; }

    public bool Required { get; set; }

    public bool IsSecret { get; set; }
}

public sealed class McpExtensionPermissionOptions
{
    public string Id { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool Required { get; set; }
}
