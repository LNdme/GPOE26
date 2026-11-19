using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using User.Data;
using User.DTOs;
using User.Helper;
using User.Service;
using GPOE26.Workspaces.Abstractions;

namespace User.Endpoints;

public static class WorkspaceEndpoints
{
    public static void MapWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/workspaces")
            .WithTags("Espaces")
            .RequireAuthorization();

        group.MapGet("/mine", async (
            ClaimsPrincipal principal,
            WorkspaceService workspaces,
            UserContext db,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();
            if (!await db.AppUsers.AnyAsync(user => user.Id == userId.Value, cancellationToken))
                return Results.NotFound();

            return Results.Ok(await workspaces.GetMineAsync(userId.Value, cancellationToken));
        })
        .WithSummary("Lister les espaces auxquels appartient l'utilisateur connecté")
        .Produces<IReadOnlyList<WorkspaceSummaryDto>>()
        .Produces(401);

        group.MapGet("/{workspaceId:guid}/membership", async (
            Guid workspaceId,
            ClaimsPrincipal principal,
            WorkspaceService workspaces,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            var membership = await workspaces.GetMembershipAsync(workspaceId, userId.Value, cancellationToken);
            return membership is null ? Results.NotFound() : Results.Ok(membership);
        })
        .WithSummary("Vérifier l'adhésion active de l'utilisateur connecté à un espace")
        .Produces<WorkspaceSummaryDto>()
        .Produces(404);

        group.MapGet("/context", async (
            ClaimsPrincipal principal,
            UserContext db,
            WorkspaceService workspaces,
            CancellationToken cancellationToken) =>
        {
            if (!principal.TryGetWorkspaceContext(out var tokenContext) ||
                principal.GetUserId() is not { } userId)
                return Results.Unauthorized();

            var userRole = await db.AppUsers
                .Where(user => user.Id == userId)
                .Select(user => user.Role.ToString())
                .FirstOrDefaultAsync(cancellationToken);
            if (userRole is null ||
                !string.Equals(userRole, principal.FindFirstValue("role"), StringComparison.Ordinal))
                return Results.Unauthorized();

            var membership = await workspaces.GetMembershipAsync(
                tokenContext!.WorkspaceId,
                userId,
                cancellationToken);
            if (membership is null ||
                !string.Equals(membership.Kind.ToString(), tokenContext.Kind, StringComparison.Ordinal) ||
                !membership.Roles.Select(role => role.ToString()).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(tokenContext.Roles))
                return Results.Unauthorized();

            return Results.NoContent();
        })
        .WithSummary("Vérifier que le contexte du JWT correspond toujours aux rôles actifs")
        .Produces(204)
        .Produces(401);

        group.MapPost("/{workspaceId:guid}/select", async (
            Guid workspaceId,
            ClaimsPrincipal principal,
            UserContext db,
            WorkspaceService workspaces,
            SubscriptionService subscriptions,
            Jwtservice jwt,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            var membership = await workspaces.GetMembershipAsync(workspaceId, userId.Value, cancellationToken);
            if (membership is null)
                return Results.NotFound();

            var user = await db.AppUsers.FirstOrDefaultAsync(
                item => item.Id == userId.Value,
                cancellationToken);
            if (user is null)
                return Results.NotFound();

            var entitlement = await subscriptions.GetEntitlementAsync(user.Id);
            var (token, expiresAt) = jwt.GenerateToken(user, entitlement, membership);
            return Results.Ok(new AuthResponse(token, expiresAt, new UserProfileDto(user, entitlement), membership));
        })
        .WithSummary("Sélectionner un espace accessible et émettre un nouveau JWT limité à ce contexte")
        .Produces<AuthResponse>()
        .Produces(401)
        .Produces(404);

        group.MapGet("/{workspaceId:guid}/mcp/members", async (
            Guid workspaceId,
            string? query,
            ClaimsPrincipal principal,
            McpAuthorizationService authorization,
            CancellationToken cancellationToken) =>
        {
            var result = await authorization.GetManageableMembersAsync(
                principal, workspaceId, query, cancellationToken);
            return result.Outcome == McpAuthorizationOutcome.Success
                ? Results.Ok(result.Members)
                : ToMcpAuthorizationResult(result.Outcome);
        })
        .WithSummary("Lister les membres dont l'utilisateur peut gérer le consentement MCP")
        .Produces<IReadOnlyList<McpManageableMember>>()
        .Produces(400)
        .Produces(401)
        .Produces(403);

        group.MapGet("/{workspaceId:guid}/mcp/grants/{targetUserId:guid}", async (
            Guid workspaceId,
            Guid targetUserId,
            ClaimsPrincipal principal,
            McpAuthorizationService authorization,
            CancellationToken cancellationToken) =>
        {
            var result = await authorization.GetActiveGrantsAsync(
                principal, workspaceId, targetUserId, cancellationToken);
            return result.Outcome == McpAuthorizationOutcome.Success
                ? Results.Ok(result.Grants)
                : ToMcpAuthorizationResult(result.Outcome);
        })
        .WithSummary("Lister les autorisations MCP actives visibles par l'utilisateur")
        .Produces<IReadOnlyList<McpGrantSummary>>()
        .Produces(401)
        .Produces(403)
        .Produces(404);

        group.MapPost("/{workspaceId:guid}/mcp/extensions/{extensionId}/approve", async (
            Guid workspaceId,
            string extensionId,
            McpGrantApprovalRequest request,
            ClaimsPrincipal principal,
            McpAuthorizationService authorization,
            CancellationToken cancellationToken) =>
        {
            var outcome = await authorization.ApproveAsync(
                principal,
                workspaceId,
                request.TargetUserId,
                extensionId,
                request.CapabilityIds ?? [],
                request.PermissionIds ?? [],
                cancellationToken);
            return ToMcpAuthorizationResult(outcome);
        })
        .WithSummary("Approuver les capacités MCP pour un membre de l'espace actif")
        .Produces(204)
        .Produces(400)
        .Produces(401)
        .Produces(403)
        .Produces(404);

        group.MapPost("/{workspaceId:guid}/mcp/extensions/{extensionId}/revoke", async (
            Guid workspaceId,
            string extensionId,
            McpGrantRevocationRequest request,
            ClaimsPrincipal principal,
            McpAuthorizationService authorization,
            CancellationToken cancellationToken) =>
        {
            var outcome = await authorization.RevokeAsync(
                principal,
                workspaceId,
                request.TargetUserId,
                extensionId,
                cancellationToken);
            return ToMcpAuthorizationResult(outcome);
        })
        .WithSummary("Révoquer les capacités MCP approuvées pour un membre de l'espace")
        .Produces(204)
        .Produces(400)
        .Produces(401)
        .Produces(403)
        .Produces(404);
    }

    private static IResult ToMcpAuthorizationResult(McpAuthorizationOutcome outcome) => outcome switch
    {
        McpAuthorizationOutcome.Success => Results.NoContent(),
        McpAuthorizationOutcome.InvalidRequest => Results.BadRequest(),
        McpAuthorizationOutcome.Unauthorized => Results.Unauthorized(),
        McpAuthorizationOutcome.Forbidden => Results.Forbid(),
        McpAuthorizationOutcome.TargetNotMember or McpAuthorizationOutcome.GrantNotFound => Results.NotFound(),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
    };
}

public sealed record McpGrantApprovalRequest(
    Guid TargetUserId,
    IReadOnlyList<string>? CapabilityIds,
    IReadOnlyList<string>? PermissionIds);

public sealed record McpGrantRevocationRequest(Guid TargetUserId);
