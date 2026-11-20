using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GPOE26.Workspaces.Abstractions;

public static class WorkspaceClaimTypes
{
    public const string Id = "workspace_id";
    public const string Kind = "workspace_kind";
    public const string Role = "workspace_role";
}

public static class WorkspaceKinds
{
    public const string Personal = "Personal";
    public const string Family = "Family";
    public const string Organization = "Organization";
}

public static class WorkspaceRoles
{
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Guardian = "Guardian";
    public const string Learner = "Learner";
    public const string Instructor = "Instructor";
    public const string Member = "Member";
}

public sealed record WorkspaceTokenContext(
    Guid WorkspaceId,
    string Kind,
    IReadOnlySet<string> Roles)
{
    public bool IsPersonal => Kind == WorkspaceKinds.Personal;

    public bool HasAnyRole(params string[] roles) => roles.Any(Roles.Contains);
}

public static class WorkspacePrincipalExtensions
{
    private static readonly HashSet<string> KnownKinds =
        [WorkspaceKinds.Personal, WorkspaceKinds.Family, WorkspaceKinds.Organization];

    private static readonly HashSet<string> KnownRoles =
    [
        WorkspaceRoles.Owner,
        WorkspaceRoles.Admin,
        WorkspaceRoles.Guardian,
        WorkspaceRoles.Learner,
        WorkspaceRoles.Instructor,
        WorkspaceRoles.Member
    ];

    public static bool TryGetWorkspaceContext(
        this ClaimsPrincipal principal,
        out WorkspaceTokenContext? context)
    {
        context = null;
        var workspaceIds = principal.FindAll(WorkspaceClaimTypes.Id).Select(claim => claim.Value).ToList();
        var workspaceKinds = principal.FindAll(WorkspaceClaimTypes.Kind).Select(claim => claim.Value).ToList();
        if (workspaceIds.Count != 1 ||
            !Guid.TryParse(workspaceIds[0], out var workspaceId) ||
            workspaceKinds.Count != 1 ||
            !KnownKinds.Contains(workspaceKinds[0]))
        {
            return false;
        }

        var roles = principal.FindAll(WorkspaceClaimTypes.Role)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);
        if (roles.Count == 0 || roles.Any(role => !KnownRoles.Contains(role)))
            return false;

        context = new WorkspaceTokenContext(workspaceId, workspaceKinds[0], roles);
        return true;
    }

    public static WorkspaceTokenContext GetWorkspaceContext(this ClaimsPrincipal principal) =>
        principal.TryGetWorkspaceContext(out var context)
            ? context!
            : throw new UnauthorizedAccessException("A valid active workspace is required.");
}

public static class WorkspaceEndpointExtensions
{
    public static RouteGroupBuilder RequireWorkspaceContext(this RouteGroupBuilder group) =>
        group.AddEndpointFilter(async (context, next) =>
        {
            if (!context.HttpContext.User.TryGetWorkspaceContext(out _))
                return Results.Unauthorized();

            var validator = context.HttpContext.RequestServices
                .GetRequiredService<IWorkspaceContextValidator>();
            var validation = await validator.ValidateAsync(context.HttpContext.RequestAborted);
            return validation switch
            {
                WorkspaceContextValidationResult.Current => await next(context),
                WorkspaceContextValidationResult.Invalid => Results.Unauthorized(),
                _ => Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
            };
        });

    public static RouteHandlerBuilder RequireWorkspaceRoles(
        this RouteHandlerBuilder endpoint,
        params string[] roles) =>
        endpoint.AddEndpointFilter((context, next) =>
        {
            if (!context.HttpContext.User.TryGetWorkspaceContext(out var workspace))
                return ValueTask.FromResult<object?>(Results.Unauthorized());
            if (!workspace!.HasAnyRole(roles))
                return ValueTask.FromResult<object?>(Results.Forbid());
            return next(context);
        });

    public static RouteHandlerBuilder RequireWorkspaceKindAndRoles(
        this RouteHandlerBuilder endpoint,
        string kind,
        params string[] roles) =>
        endpoint.AddEndpointFilter((context, next) =>
        {
            if (!context.HttpContext.User.TryGetWorkspaceContext(out var workspace))
                return ValueTask.FromResult<object?>(Results.Unauthorized());
            if (!string.Equals(workspace!.Kind, kind, StringComparison.Ordinal) ||
                !workspace.HasAnyRole(roles))
                return ValueTask.FromResult<object?>(Results.Forbid());
            return next(context);
        });
}
