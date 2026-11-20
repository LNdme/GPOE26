using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using GPOE26.Agent.Abstractions;
using GPOE26.Workspaces.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Chat.Service;

public sealed class McpAuthorizationClient(
    HttpClient http,
    IHttpContextAccessor httpContextAccessor,
    IConfiguration configuration,
    ILogger<McpAuthorizationClient> logger) : IMcpPermissionGrantStore, IMcpServerProcessOptionsStore
{
    private const string ServiceKeyConfigurationPath = "Mcp:ChatServiceKey";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<McpPermissionGrant?> GetApprovedGrantAsync(
        Guid userId,
        Guid workspaceId,
        string extensionId,
        CancellationToken cancellationToken = default) =>
        GetAsync<McpPermissionGrant>(userId, workspaceId, extensionId, "grant", cancellationToken);

    public Task<McpServerProcessOptions?> GetOptionsAsync(
        Guid userId,
        Guid workspaceId,
        string extensionId,
        CancellationToken cancellationToken = default) =>
        GetAsync<McpServerProcessOptions>(userId, workspaceId, extensionId, "options", cancellationToken);

    private async Task<T?> GetAsync<T>(
        Guid userId,
        Guid workspaceId,
        string extensionId,
        string resource,
        CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext;
        if (!TryGetScopedCaller(context?.User, userId, workspaceId) ||
            !AuthenticationHeaderValue.TryParse(
                context?.Request.Headers.Authorization.ToString(),
                out var authorization) ||
            !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Denied MCP authorization lookup for user {UserId}, workspace {WorkspaceId}: request identity is missing or mismatched",
                userId,
                workspaceId);
            return default;
        }

        var serviceKey = configuration[ServiceKeyConfigurationPath];
        if (!McpChatServiceProtocol.IsValidSharedKey(serviceKey))
        {
            logger.LogError("MCP Chat-to-User service authentication requires a key of at least 32 UTF-8 bytes");
            return default;
        }

        var query = QueryString.Create("extensionId", extensionId);
        var pathAndQuery =
            $"/internal/workspaces/{workspaceId:D}/mcp/{resource}{query.ToUriComponent()}";
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            pathAndQuery);
        request.Headers.Authorization = authorization;
        var unixTimeSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        request.Headers.TryAddWithoutValidation(
            McpChatServiceProtocol.TimestampHeader,
            unixTimeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(
            McpChatServiceProtocol.SignatureHeader,
            McpChatServiceProtocol.CreateSignature(
                serviceKey,
                request.Method.Method,
                pathAndQuery,
                userId,
                unixTimeSeconds));

        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogInformation(
                "MCP authorization lookup returned {StatusCode} for user {UserId}, workspace {WorkspaceId}",
                (int)response.StatusCode,
                userId,
                workspaceId);
            return default;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
    }

    private static bool TryGetScopedCaller(ClaimsPrincipal? principal, Guid userId, Guid workspaceId) =>
        principal?.Identity?.IsAuthenticated == true &&
        userId != Guid.Empty &&
        workspaceId != Guid.Empty &&
        Guid.TryParse(
            principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier),
            out var authenticatedUserId) &&
        authenticatedUserId != Guid.Empty &&
        authenticatedUserId == userId &&
        principal.TryGetWorkspaceContext(out var workspace) &&
        workspace!.WorkspaceId == workspaceId;
}
