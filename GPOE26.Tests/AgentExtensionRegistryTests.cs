using GPOE26.Agent.Abstractions;

namespace GPOE26.Tests;

public sealed class AgentExtensionRegistryTests
{
    [Fact]
    public void RegistryFindsValidatedManifestWithoutGrantingItsPermissions()
    {
        var manifest = CreateManifest() with
        {
            RequestedPermissions = [new AgentExtensionPermission("course.read", "Read a course selected by the host.", Required: true)],
            Capabilities =
            [
                new AgentExtensionCapability(
                    "course.read_current",
                    "Read the authorized current course.",
                    """{"type":"object","properties":{},"additionalProperties":false}""",
                    ["course.read"])
            ]
        };
        var registry = new AgentExtensionRegistry([manifest]);

        Assert.True(registry.TryGet("sample.course", out var registered));
        Assert.Same(manifest, registered);
        Assert.Single(registered.RequestedPermissions);
    }

    [Fact]
    public void ValidatorRejectsUnsupportedSchemaVersionAndMalformedSchema()
    {
        var manifest = CreateManifest() with
        {
            ManifestVersion = 2,
            Capabilities = [new AgentExtensionCapability("search", "Search.", "{")]
        };

        var errors = AgentExtensionManifestValidator.Validate(manifest);

        Assert.Contains(errors, error => error.Contains("Unsupported manifest version", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("input schema is invalid JSON", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => new AgentExtensionRegistry([manifest]));
    }

    [Fact]
    public void ValidatorRejectsUndeclaredPermissionsAndNonHttpsPublisherSource()
    {
        var manifest = CreateManifest() with
        {
            Publisher = new AgentExtensionPublisher("publisher", "Publisher", "http://example.test/manifest"),
            Capabilities =
            [
                new AgentExtensionCapability("search", "Search.", RequiredPermissionIds: ["web.search"])
            ]
        };

        var errors = AgentExtensionManifestValidator.Validate(manifest);

        Assert.Contains(errors, error => error.Contains("HTTPS", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("undeclared permission 'web.search'", StringComparison.Ordinal));
    }

    [Fact]
    public void RegistryRejectsDuplicateExtensionIdsIgnoringCase()
    {
        var manifest = CreateManifest();

        Assert.Throws<ArgumentException>(() => new AgentExtensionRegistry(
        [
            manifest,
            manifest with { Id = "SAMPLE.COURSE" }
        ]));
    }

    private static AgentExtensionManifest CreateManifest() => new(
        AgentExtensionManifestValidator.CurrentManifestVersion,
        "sample.course",
        "1.0.0",
        "Sample course extension",
        "A test extension manifest.",
        AgentExtensionKind.McpServer,
        new AgentExtensionPublisher("sample.publisher", "Sample publisher", "https://example.test/extensions/course"),
        [new AgentExtensionCapability("course.read_current", "Read the current course.")],
        [new AgentExtensionParameter("endpoint", "MCP server endpoint.", AgentExtensionParameterType.Uri, Required: true)],
        []);
}
