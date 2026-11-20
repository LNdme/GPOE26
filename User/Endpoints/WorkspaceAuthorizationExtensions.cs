using GPOE26.Workspaces.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using User.Data;
using User.Helper;
using User.Model;
using User.Service;

namespace User.Endpoints;

public static class WorkspaceAuthorizationExtensions
{
    public static RouteGroupBuilder RequireCurrentWorkspaceMembership(this RouteGroupBuilder group) =>
        group.AddEndpointFilter(async (context, next) =>
        {
            if (await GetActiveMembershipAsync(context.HttpContext) is null)
                return Results.Unauthorized();

            return await next(context);
        });

    public static RouteHandlerBuilder RequireUserWorkspaceRoles(
        this RouteHandlerBuilder endpoint,
        params WorkspaceRole[] roles) =>
        endpoint.AddEndpointFilter((context, next) =>
        {
            if (!context.HttpContext.User.TryGetWorkspaceContext(out var workspace))
                return ValueTask.FromResult<object?>(Results.Unauthorized());
            if (!roles.Any(role => workspace!.Roles.Contains(role.ToString())))
                return ValueTask.FromResult<object?>(Results.Forbid());
            return next(context);
        });

    public static RouteHandlerBuilder RequireUserWorkspaceKindAndRoles(
        this RouteHandlerBuilder endpoint,
        WorkspaceKind kind,
        params WorkspaceRole[] roles) =>
        endpoint.AddEndpointFilter((context, next) =>
        {
            if (!context.HttpContext.User.TryGetWorkspaceContext(out var workspace))
                return ValueTask.FromResult<object?>(Results.Unauthorized());
            if (workspace!.Kind != kind.ToString() ||
                !roles.Any(role => workspace.Roles.Contains(role.ToString())))
                return ValueTask.FromResult<object?>(Results.Forbid());
            return next(context);
        });

    private static async Task<WorkspaceSummaryDto?> GetActiveMembershipAsync(HttpContext context)
    {
        var principal = context.User;
        if (!principal.TryGetWorkspaceContext(out var tokenContext) ||
            principal.GetUserId() is not { } userId)
            return null;

        var db = context.RequestServices.GetRequiredService<UserContext>();
        var workspaces = context.RequestServices.GetRequiredService<WorkspaceService>();
        var userRole = await db.AppUsers
            .Where(user => user.Id == userId)
            .Select(user => user.Role.ToString())
            .FirstOrDefaultAsync(context.RequestAborted);
        if (userRole is null ||
            !string.Equals(userRole, principal.FindFirst("role")?.Value, StringComparison.Ordinal))
            return null;

        var membership = await workspaces.GetMembershipAsync(
            tokenContext!.WorkspaceId,
            userId,
            context.RequestAborted);
        if (membership is null ||
            !string.Equals(membership.Kind.ToString(), tokenContext.Kind, StringComparison.Ordinal) ||
            !membership.Roles.Select(role => role.ToString()).ToHashSet(StringComparer.Ordinal)
                .SetEquals(tokenContext.Roles))
            return null;

        return membership;
    }
}
