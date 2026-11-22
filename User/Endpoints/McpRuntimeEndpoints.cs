using System.Security.Claims;
using GPOE26.Agent.Abstractions;
using GPOE26.Workspaces.Abstractions;
using User.Helper;

namespace User.Endpoints;

public static class McpRuntimeEndpoints
{
    public static void MapMcpRuntimeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/workspaces/{workspaceId:guid}/mcp")
            .WithTags("MCP runtime (internal)")
            .RequireAuthorization()
            .RequireMcpChatService()
            .RequireCurrentWorkspaceMembership();

        group.MapGet("/grant", async (
            Guid workspaceId,
            string extensionId,
            ClaimsPrincipal principal,
            IMcpPermissionGrantStore grants,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetCaller(principal, workspaceId, out var userId))
                return Results.Unauthorized();

            var grant = await grants.GetApprovedGrantAsync(
                userId,
                workspaceId,
                extensionId,
                cancellationToken);
            return grant is null ? Results.NotFound() : Results.Ok(grant);
        })
        .WithSummary("Chat uniquement : lire l'autorisation MCP de l'utilisateur et de son espace actifs")
        .Produces<McpPermissionGrant>()
        .Produces(401)
        .Produces(404);

        group.MapGet("/options", async (
            Guid workspaceId,
            string extensionId,
            ClaimsPrincipal principal,
            IMcpServerProcessOptionsStore processOptions,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetCaller(principal, workspaceId, out var userId))
                return Results.Unauthorized();

            var options = await processOptions.GetOptionsAsync(
                userId,
                workspaceId,
                extensionId,
                cancellationToken);
            return options is null ? Results.NotFound() : Results.Ok(options);
        })
        .WithSummary("Chat uniquement : lire la configuration MCP vérifiée et sandboxée")
        .Produces<McpServerProcessOptions>()
        .Produces(401)
        .Produces(404);
    }

    private static bool TryGetCaller(ClaimsPrincipal principal, Guid workspaceId, out Guid userId)
    {
        userId = default;
        return principal.GetUserId() is { } authenticatedUserId &&
               principal.TryGetWorkspaceContext(out var workspace) &&
               workspace!.WorkspaceId == workspaceId &&
               (userId = authenticatedUserId) != Guid.Empty;
    }
}
