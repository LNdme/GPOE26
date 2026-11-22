using System.Security.Claims;
using System.Net.Http.Json;
using Chat.Service;
using GPOE26.Agent.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GPOE26.Tests;

public sealed class McpAuthorizationClientTests
{
    private const string ServiceKey = "test-only-mcp-service-key-32-bytes-minimum";

    [Fact]
    public async Task GrantLookupForwardsUserBearerAndSignsTheExactScopedRequest()
    {
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var handler = new CallbackHandler((request, _) =>
        {
            Assert.Equal("Bearer learner-token", request.Headers.Authorization?.ToString());
            Assert.True(request.Headers.TryGetValues(McpChatServiceProtocol.TimestampHeader, out var timestamp));
            Assert.True(request.Headers.TryGetValues(McpChatServiceProtocol.SignatureHeader, out var signature));
            Assert.False(request.Headers.Contains("X-GPOE26-Mcp-Chat-Key"));

            var target = request.RequestUri!.PathAndQuery;
            Assert.True(McpChatServiceProtocol.IsValidSignature(
                ServiceKey,
                request.Method.Method,
                target,
                userId,
                Assert.Single(timestamp),
                Assert.Single(signature),
                DateTimeOffset.UtcNow));
            Assert.Contains($"/internal/workspaces/{workspaceId:D}/mcp/grant", target);

            var grant = new McpPermissionGrant(userId, workspaceId, "sample", ["read"], []);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(grant)
            });
        });
        using var http = CreateHttpClient(handler);
        var client = CreateStore(http, userId, workspaceId);

        var result = await client.GetApprovedGrantAsync(userId, workspaceId, "sample");

        Assert.NotNull(result);
        Assert.Equal(userId, result.UserId);
        Assert.Equal(workspaceId, result.WorkspaceId);
    }

    [Fact]
    public async Task LookupWithIdentityOutsideTheAuthenticatedWorkspaceDoesNotCallUserService()
    {
        var userId = Guid.NewGuid();
        var activeWorkspaceId = Guid.NewGuid();
        var handler = new CallbackHandler((_, _) =>
            throw new InvalidOperationException("The User API must not be called for a mismatched workspace."));
        using var http = CreateHttpClient(handler);
        var client = CreateStore(http, userId, activeWorkspaceId);

        var result = await client.GetOptionsAsync(
            userId,
            Guid.NewGuid(),
            "sample");

        Assert.Null(result);
        Assert.Equal(0, handler.RequestCount);
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://user.example/") };

    private static McpAuthorizationClient CreateStore(HttpClient http, Guid userId, Guid workspaceId)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", userId.ToString()),
                new Claim("workspace_id", workspaceId.ToString()),
                new Claim("workspace_kind", "Personal"),
                new Claim("workspace_role", "Owner")
            ],
            "test"))
        };
        context.Request.Headers.Authorization = "Bearer learner-token";

        var accessor = new HttpContextAccessor { HttpContext = context };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mcp:ChatServiceKey"] = ServiceKey
            })
            .Build();
        return new McpAuthorizationClient(
            http,
            accessor,
            configuration,
            NullLogger<McpAuthorizationClient>.Instance);
    }

    private sealed class CallbackHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return callback(request, cancellationToken);
        }
    }
}

public sealed class McpChatServiceProtocolTests
{
    [Fact]
    public void SignatureRejectsChangedTargetAndExpiredTimestamp()
    {
        var now = DateTimeOffset.UtcNow;
        var timestamp = now.ToUnixTimeSeconds();
        var userId = Guid.NewGuid();
        var signature = McpChatServiceProtocol.CreateSignature(
            "test-key-with-at-least-thirty-two-bytes",
            "GET",
            "/internal/mcp?extensionId=sample",
            userId,
            timestamp);

        Assert.True(McpChatServiceProtocol.IsValidSignature(
            "test-key-with-at-least-thirty-two-bytes",
            "GET",
            "/internal/mcp?extensionId=sample",
            userId,
            timestamp.ToString(),
            signature,
            now));
        Assert.False(McpChatServiceProtocol.IsValidSignature(
            "test-key-with-at-least-thirty-two-bytes",
            "GET",
            "/internal/mcp?extensionId=sample",
            Guid.NewGuid(),
            timestamp.ToString(),
            signature,
            now));
        Assert.False(McpChatServiceProtocol.IsValidSignature(
            "test-key-with-at-least-thirty-two-bytes",
            "GET",
            "/internal/mcp?extensionId=other",
            userId,
            timestamp.ToString(),
            signature,
            now));
        Assert.False(McpChatServiceProtocol.IsValidSignature(
            "test-key-with-at-least-thirty-two-bytes",
            "GET",
            "/internal/mcp?extensionId=sample",
            userId,
            (timestamp - McpChatServiceProtocol.AllowedClockSkewSeconds - 1).ToString(),
            signature,
            now));
        Assert.False(McpChatServiceProtocol.IsValidSignature(
            "short-key",
            "GET",
            "/internal/mcp?extensionId=sample",
            userId,
            timestamp.ToString(),
            signature,
            now));
    }
}
