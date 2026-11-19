using System.Security.Claims;
using System.Text.Json;
using Chat.Service;
using GPOE26.Agent.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace GPOE26.Tests;

public sealed class TutorCourseToolExecutorTests
{
    [Fact]
    public async Task ReadsOnlyTheAuthorizedCurrentCourseWithoutModelSuppliedIdentifiers()
    {
        var userId = Guid.NewGuid();
        var course = new CourseRef(
            Guid.NewGuid(),
            "Fractions",
            "Mathématiques",
            null,
            "Texte extrait",
            [new CourseSectionRef("Contenu autorisé", 1)]);
        var context = new TutorCourseToolContext();
        context.AuthorizeCourse(userId, course);
        var executor = CreateExecutor(userId, context);

        var result = await executor.ExecuteAsync(
            new AgentToolCall("call-1", "cours.read_current", "{}"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Contenu autorisé", result.Content);
        var tool = Assert.Single(executor.Tools);
        Assert.Equal("cours.read_current", tool.Name);
        Assert.DoesNotContain("userId", tool.InputSchemaJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("courseId", tool.InputSchemaJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExposesAndExecutesOnlyCapabilitiesReturnedAsApprovedByTheMcpAdapter()
    {
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var manifest = McpManifest();
        var approvedCapability = manifest.Capabilities[0];
        var adapter = new RecordingMcpAdapter(approvedCapability);
        var executor = new TutorCourseToolExecutor(
            new TutorCourseToolContext(),
            CreateHttpContextAccessor(userId, workspaceId),
            adapter,
            new AgentExtensionRegistry([manifest]),
            NullLogger<TutorCourseToolExecutor>.Instance);

        await executor.PrepareMcpToolsAsync(CancellationToken.None);

        var mcpTool = Assert.Single(executor.Tools, tool => tool.Name == AgentToolNames.McpRead);
        using var schema = JsonDocument.Parse(mcpTool.InputSchemaJson);
        var targetEnum = schema.RootElement.GetProperty("properties")
            .GetProperty("target").GetProperty("enum");
        Assert.Equal("research:papers.search", Assert.Single(targetEnum.EnumerateArray()).GetString());

        var result = await executor.ExecuteAsync(
            new AgentToolCall(
                "call-1",
                AgentToolNames.McpRead,
                """{"target":"research:papers.search","arguments":{"query":"learning"}}"""),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("""{"title":"Approved result"}""", result.Content);
        var read = Assert.Single(adapter.Reads);
        Assert.Equal("research", read.ExtensionId);
        Assert.Equal("papers.search", read.CapabilityId);
        Assert.Equal(userId, adapter.LastCallerId);
        Assert.Equal(workspaceId, adapter.LastWorkspaceId);
    }

    [Fact]
    public async Task RejectsModelSelectedMcpTargetsOutsideTheApprovedCapabilityList()
    {
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var manifest = McpManifest();
        var adapter = new RecordingMcpAdapter(manifest.Capabilities[0]);
        var executor = new TutorCourseToolExecutor(
            new TutorCourseToolContext(),
            CreateHttpContextAccessor(userId, workspaceId),
            adapter,
            new AgentExtensionRegistry([manifest]),
            NullLogger<TutorCourseToolExecutor>.Instance);

        await executor.PrepareMcpToolsAsync(CancellationToken.None);

        var result = await executor.ExecuteAsync(
            new AgentToolCall(
                "call-1",
                AgentToolNames.McpRead,
                """{"target":"research:papers.delete","arguments":{}}"""),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Empty(adapter.Reads);
    }

    [Theory]
    [InlineData("""{"courseId":"00000000-0000-0000-0000-000000000001"}""")]
    [InlineData("""{"userId":"00000000-0000-0000-0000-000000000001"}""")]
    [InlineData("[]")]
    [InlineData("{")]
    public async Task RejectsArgumentsThatCouldSelectAnotherResource(string arguments)
    {
        var userId = Guid.NewGuid();
        var context = new TutorCourseToolContext();
        context.AuthorizeCourse(userId, new CourseRef(Guid.NewGuid(), "Cours", "Maths", null, null, null));
        var executor = CreateExecutor(userId, context);

        var result = await executor.ExecuteAsync(
            new AgentToolCall("call-1", "cours.read_current", arguments),
            CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task RejectsExecutionOutsideTheAuthorizedUserScope()
    {
        var context = new TutorCourseToolContext();
        context.AuthorizeCourse(
            Guid.NewGuid(),
            new CourseRef(Guid.NewGuid(), "Cours privé", "Maths", null, "Privé", null));
        var executor = CreateExecutor(Guid.NewGuid(), context);

        var result = await executor.ExecuteAsync(
            new AgentToolCall("call-1", "cours.read_current", "{}"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.DoesNotContain("Privé", result.Content);
    }

    [Fact]
    public async Task RejectsExecutionBeforeAnyCourseWasAuthorized()
    {
        var executor = CreateExecutor(Guid.NewGuid(), new TutorCourseToolContext());

        var result = await executor.ExecuteAsync(
            new AgentToolCall("call-1", "cours.read_current", "{}"),
            CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public void DoesNotAllowChangingTheAuthorizedCourseInsideTheRequestScope()
    {
        var context = new TutorCourseToolContext();
        context.AuthorizeCourse(
            Guid.NewGuid(),
            new CourseRef(Guid.NewGuid(), "Cours autorisé", "Maths", null, null, null));

        Assert.Throws<InvalidOperationException>(() => context.AuthorizeCourse(
            Guid.NewGuid(),
            new CourseRef(Guid.NewGuid(), "Autre cours", "Maths", null, null, null)));
    }

    private static TutorCourseToolExecutor CreateExecutor(Guid userId, TutorCourseToolContext context)
    {
        return new TutorCourseToolExecutor(context, CreateHttpContextAccessor(userId, Guid.NewGuid()));
    }

    private static IHttpContextAccessor CreateHttpContextAccessor(Guid userId, Guid workspaceId)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("sub", userId.ToString()),
            new Claim("workspace_id", workspaceId.ToString()),
            new Claim("workspace_kind", "Personal"),
            new Claim("workspace_role", "Owner")
        ],
        authenticationType: "test");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return new HttpContextAccessor { HttpContext = httpContext };
    }

    private static AgentExtensionManifest McpManifest()
    {
        var approved = new AgentExtensionCapability(
            "papers.search",
            "Search approved research papers.",
            """{"type":"object","properties":{"query":{"type":"string"}},"required":["query"],"additionalProperties":false}""");
        var unapproved = new AgentExtensionCapability("papers.delete", "Delete a research paper.");
        return new AgentExtensionManifest(
            AgentExtensionManifestValidator.CurrentManifestVersion,
            "research",
            "1.0.0",
            "Research MCP",
            "Read-only research.",
            AgentExtensionKind.McpServer,
            new AgentExtensionPublisher("local", "Local"),
            [approved, unapproved],
            [],
            []);
    }

    private sealed class RecordingMcpAdapter(AgentExtensionCapability approvedCapability)
        : IMcpReadOnlyAdapter
    {
        public List<(string ExtensionId, string CapabilityId)> Reads { get; } = [];
        public Guid LastCallerId { get; private set; }
        public Guid LastWorkspaceId { get; private set; }

        public Task<McpCapabilityListingResult> ListApprovedCapabilitiesAsync(
            ClaimsPrincipal caller,
            string extensionId,
            CancellationToken cancellationToken = default)
        {
            LastCallerId = Guid.Parse(caller.FindFirstValue("sub")!);
            LastWorkspaceId = Guid.Parse(caller.FindFirstValue("workspace_id")!);
            return Task.FromResult(new McpCapabilityListingResult(true, null, [approvedCapability]));
        }

        public Task<McpReadOnlyResult> ReadAsync(
            ClaimsPrincipal caller,
            string extensionId,
            string capabilityId,
            JsonElement arguments,
            CancellationToken cancellationToken = default)
        {
            Reads.Add((extensionId, capabilityId));
            using var document = JsonDocument.Parse("""{"title":"Approved result"}""");
            return Task.FromResult(new McpReadOnlyResult(
                true,
                true,
                null,
                document.RootElement.Clone()));
        }
    }
}
