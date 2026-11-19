using System.Text.Json;
using System.Security.Claims;
using GPOE26.Agent.Abstractions;
using GPOE26.Agent.Runtime.Mcp;
using GPOE26.Workspaces.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace GPOE26.Tests;

public sealed class McpReadOnlyAdapterTests
{
    [Fact]
    public void ReadOnlyPolicyRejectsMutatingCapability()
    {
        var (userId, workspaceId) = Scope();
        var manifest = McpManifest(
            "delete_course",
            [new AgentExtensionPermission("courses.read", "Lire les cours", true)]);
        var grant = Grant(userId, workspaceId, "delete_course", "courses.read");

        var authorized = new McpReadOnlyPolicy().TryAuthorize(
            manifest,
            grant,
            "delete_course",
            out _,
            out var error);

        Assert.False(authorized);
        Assert.Contains("Mutating", error);
    }

    [Fact]
    public void ReadOnlyPolicyRequiresExplicitCapabilityAndPermission()
    {
        var (userId, workspaceId) = Scope();
        var manifest = McpManifest(
            "search_courses",
            [new AgentExtensionPermission("courses.read", "Lire les cours", true)]);

        var policy = new McpReadOnlyPolicy();
        Assert.False(policy.TryAuthorize(
            manifest,
            Grant(userId, workspaceId, "search_courses", "courses.read"),
            "other_course",
            out _,
            out _));
        Assert.False(policy.TryAuthorize(
            manifest,
            new McpPermissionGrant(
                userId,
                workspaceId,
                manifest.Id,
                ["search_courses"],
                ["different.permission"]),
            "search_courses",
            out _,
            out var missingPermission));
        Assert.Contains("Missing approved permissions", missingPermission);
    }

    [Fact]
    public async Task ProcessAdapterDeniesCapabilityWithoutExplicitGrant()
    {
        var manifest = McpManifest(
            "search_courses",
            [new AgentExtensionPermission("courses.read", "Lire les cours", true)]);
        var registry = new AgentExtensionRegistry([manifest]);
        var caller = AuthenticatedCaller();
        await using var adapter = new ReadOnlyMcpProcessAdapter(
            registry,
            new FakeGrantStore(null),
            new FakeProcessOptionsStore(null),
            new CurrentWorkspaceValidator(WorkspaceContextValidationResult.Current),
            RequestContext(caller),
            NullLogger<ReadOnlyMcpProcessAdapter>.Instance);

        var result = await adapter.ReadAsync(caller, manifest.Id, "search_courses",
            JsonDocument.Parse("{}").RootElement);

        Assert.False(result.Allowed);
        Assert.False(result.Succeeded);
        Assert.Contains("No explicit permission grant", result.Error);
    }

    [Fact]
    public async Task ProcessAdapterRejectsWorkspaceWhenLiveValidationFails()
    {
        var (userId, workspaceId) = Scope();
        var caller = AuthenticatedCaller(userId, workspaceId);
        var manifest = McpManifest(
            "search_courses",
            [new AgentExtensionPermission("courses.read", "Lire les cours", true)]);
        await using var adapter = new ReadOnlyMcpProcessAdapter(
            new AgentExtensionRegistry([manifest]),
            new FakeGrantStore(Grant(userId, workspaceId, "search_courses", "courses.read")),
            new FakeProcessOptionsStore(null),
            new CurrentWorkspaceValidator(WorkspaceContextValidationResult.Invalid),
            RequestContext(caller),
            NullLogger<ReadOnlyMcpProcessAdapter>.Instance);

        var result = await adapter.ReadAsync(
            caller,
            manifest.Id,
            "search_courses",
            JsonDocument.Parse("{}").RootElement);

        Assert.False(result.Allowed);
        Assert.False(result.Succeeded);
        Assert.Contains("no longer valid", result.Error);
    }

    [Fact]
    public async Task ProcessAdapterRejectsGrantForDifferentWorkspace()
    {
        var (userId, workspaceId) = Scope();
        var caller = AuthenticatedCaller(userId, workspaceId);
        var manifest = McpManifest(
            "search_courses",
            [new AgentExtensionPermission("courses.read", "Lire les cours", true)]);
        var mismatched = Grant(userId, Guid.NewGuid(), "search_courses", "courses.read");
        await using var adapter = new ReadOnlyMcpProcessAdapter(
            new AgentExtensionRegistry([manifest]),
            new FakeGrantStore(mismatched),
            new FakeProcessOptionsStore(null),
            new CurrentWorkspaceValidator(WorkspaceContextValidationResult.Current),
            RequestContext(caller),
            NullLogger<ReadOnlyMcpProcessAdapter>.Instance);

        var result = await adapter.ReadAsync(
            caller,
            manifest.Id,
            "search_courses",
            JsonDocument.Parse("{}").RootElement);

        Assert.False(result.Allowed);
        Assert.Contains("No explicit permission grant", result.Error);
    }

    [Fact]
    public async Task ProcessAdapterBindsCallerToAuthenticatedHttpRequest()
    {
        var (userId, workspaceId) = Scope();
        var manifest = McpManifest(
            "search_courses",
            [new AgentExtensionPermission("courses.read", "Lire les cours", true)]);
        var requestCaller = AuthenticatedCaller(userId, workspaceId);
        var forgedCaller = AuthenticatedCaller(Guid.NewGuid(), workspaceId);
        await using var adapter = new ReadOnlyMcpProcessAdapter(
            new AgentExtensionRegistry([manifest]),
            new FakeGrantStore(Grant(
                Guid.Parse(forgedCaller.FindFirst("sub")!.Value),
                workspaceId,
                "search_courses",
                "courses.read")),
            new FakeProcessOptionsStore(null),
            new CurrentWorkspaceValidator(WorkspaceContextValidationResult.Current),
            RequestContext(requestCaller),
            NullLogger<ReadOnlyMcpProcessAdapter>.Instance);

        var result = await adapter.ReadAsync(
            forgedCaller,
            manifest.Id,
            "search_courses",
            JsonDocument.Parse("{}").RootElement);

        Assert.False(result.Allowed);
        Assert.Contains("authenticated user", result.Error);
    }

    [Fact]
    public async Task ProcessAdapterRejectsUnauthenticatedRequest()
    {
        var caller = new ClaimsPrincipal(new ClaimsIdentity());
        var manifest = McpManifest(
            "search_courses",
            [new AgentExtensionPermission("courses.read", "Lire les cours", true)]);
        await using var adapter = new ReadOnlyMcpProcessAdapter(
            new AgentExtensionRegistry([manifest]),
            new FakeGrantStore(null),
            new FakeProcessOptionsStore(null),
            new CurrentWorkspaceValidator(WorkspaceContextValidationResult.Current),
            RequestContext(caller),
            NullLogger<ReadOnlyMcpProcessAdapter>.Instance);

        var result = await adapter.ReadAsync(
            caller,
            manifest.Id,
            "search_courses",
            JsonDocument.Parse("{}").RootElement);

        Assert.False(result.Allowed);
        Assert.Contains("authenticated user", result.Error);
    }

    [Fact]
    public async Task ProcessAdapterRejectsProcessConfigurationForDifferentUser()
    {
        var (userId, workspaceId) = Scope();
        var caller = AuthenticatedCaller(userId, workspaceId);
        var manifest = McpManifest(
            "search_courses",
            [new AgentExtensionPermission("courses.read", "Lire les cours", true)]);
        var processOptions = new McpServerProcessOptions(
            Guid.NewGuid(),
            workspaceId,
            manifest.Id,
            "registry.example/mcp@sha256:" + new string('a', 64),
            [],
            new Dictionary<string, string?>());
        await using var adapter = new ReadOnlyMcpProcessAdapter(
            new AgentExtensionRegistry([manifest]),
            new FakeGrantStore(Grant(userId, workspaceId, "search_courses", "courses.read")),
            new FakeProcessOptionsStore(processOptions),
            new CurrentWorkspaceValidator(WorkspaceContextValidationResult.Current),
            RequestContext(caller),
            NullLogger<ReadOnlyMcpProcessAdapter>.Instance);

        var result = await adapter.ReadAsync(
            caller,
            manifest.Id,
            "search_courses",
            JsonDocument.Parse("{}").RootElement);

        Assert.False(result.Allowed);
        Assert.Contains("not available", result.Error);
    }

    [Fact]
    public async Task ProcessAdapterFailsClosedWhenSandboxProviderIsNotConfigured()
    {
        var (userId, workspaceId) = Scope();
        var caller = AuthenticatedCaller(userId, workspaceId);
        var manifest = McpManifest(
            "search_courses",
            [new AgentExtensionPermission("courses.read", "Lire les cours", true)]);
        var processOptions = new McpServerProcessOptions(
            userId,
            workspaceId,
            manifest.Id,
            "registry.example/mcp@sha256:" + new string('a', 64),
            [],
            new Dictionary<string, string?>());
        await using var adapter = new ReadOnlyMcpProcessAdapter(
            new AgentExtensionRegistry([manifest]),
            new FakeGrantStore(Grant(userId, workspaceId, "search_courses", "courses.read")),
            new FakeProcessOptionsStore(processOptions),
            new CurrentWorkspaceValidator(WorkspaceContextValidationResult.Current),
            RequestContext(caller),
            NullLogger<ReadOnlyMcpProcessAdapter>.Instance);

        var result = await adapter.ReadAsync(
            caller,
            manifest.Id,
            "search_courses",
            JsonDocument.Parse("{}").RootElement);

        Assert.False(result.Allowed);
        Assert.False(result.Succeeded);
        Assert.Contains("not available", result.Error);
    }

    [Fact]
    public async Task ProcessAdapterRejectsOversizedArgumentsBeforeStartingServer()
    {
        var (userId, workspaceId) = Scope();
        var caller = AuthenticatedCaller(userId, workspaceId);
        var manifest = McpManifest(
            "search_courses",
            [new AgentExtensionPermission("courses.read", "Lire les cours", true)]);
        await using var adapter = new ReadOnlyMcpProcessAdapter(
            new AgentExtensionRegistry([manifest]),
            new FakeGrantStore(Grant(userId, workspaceId, "search_courses", "courses.read")),
            new FakeProcessOptionsStore(null),
            new CurrentWorkspaceValidator(WorkspaceContextValidationResult.Current),
            RequestContext(caller),
            NullLogger<ReadOnlyMcpProcessAdapter>.Instance,
            maxRequestBytes: 2);

        var result = await adapter.ReadAsync(
            caller,
            manifest.Id,
            "search_courses",
            JsonDocument.Parse("""{"query":"lesson"}""").RootElement);

        Assert.False(result.Allowed);
        Assert.Contains("size limit", result.Error);
    }

    private static McpPermissionGrant Grant(
        Guid userId,
        Guid workspaceId,
        string capabilityId,
        string permissionId) =>
        new(userId, workspaceId, "library-mcp", [capabilityId], [permissionId]);

    private static (Guid UserId, Guid WorkspaceId) Scope() => (Guid.NewGuid(), Guid.NewGuid());

    private static ClaimsPrincipal AuthenticatedCaller(Guid? userId = null, Guid? workspaceId = null) =>
        new(new ClaimsIdentity(
        [
            new Claim("sub", (userId ?? Guid.NewGuid()).ToString()),
            new Claim(WorkspaceClaimTypes.Id, (workspaceId ?? Guid.NewGuid()).ToString()),
            new Claim(WorkspaceClaimTypes.Kind, WorkspaceKinds.Personal),
            new Claim(WorkspaceClaimTypes.Role, WorkspaceRoles.Owner),
            new Claim(WorkspaceClaimTypes.Role, WorkspaceRoles.Learner)
        ],
        "Bearer"));

    private static IHttpContextAccessor RequestContext(ClaimsPrincipal caller) =>
        new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = caller }
        };

    private static AgentExtensionManifest McpManifest(
        string capabilityId,
        IReadOnlyList<AgentExtensionPermission> permissions) =>
        new(
            1,
            "library-mcp",
            "1.0.0",
            "Library MCP",
            "Read-only library server",
            AgentExtensionKind.McpServer,
            new AgentExtensionPublisher("gpoe26", "GPOE26"),
            [new AgentExtensionCapability(
                capabilityId,
                "Read-only capability",
                "{}",
                permissions.Select(permission => permission.Id).ToArray())],
            [],
            permissions);

    private sealed class FakeGrantStore(McpPermissionGrant? grant) : IMcpPermissionGrantStore
    {
        public Task<McpPermissionGrant?> GetApprovedGrantAsync(
            Guid userId,
            Guid workspaceId,
            string extensionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(grant);
    }

    private sealed class CurrentWorkspaceValidator(WorkspaceContextValidationResult result)
        : IWorkspaceContextValidator
    {
        public Task<WorkspaceContextValidationResult> ValidateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class FakeProcessOptionsStore(McpServerProcessOptions? options)
        : IMcpServerProcessOptionsStore
    {
        public Task<McpServerProcessOptions?> GetOptionsAsync(
            Guid userId,
            Guid workspaceId,
            string extensionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(options);
    }
}
