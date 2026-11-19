using Microsoft.EntityFrameworkCore;
using User.Data;
using User.Model;

namespace User.Service;

public sealed class WorkspaceService(UserContext db)
{
    public async Task<WorkspaceSummaryDto> EnsurePersonalWorkspaceAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var workspace = await db.Workspaces.FirstOrDefaultAsync(
            item => item.Kind == WorkspaceKind.Personal && item.OwnerUserId == userId,
            cancellationToken);
        if (workspace is null)
        {
            var user = await db.AppUsers
                .Where(item => item.Id == userId)
                .Select(item => new { item.Role })
                .FirstOrDefaultAsync(cancellationToken);
            if (user is null)
                throw new InvalidOperationException("Cannot create a personal workspace for an unknown user.");

            workspace = new Workspace
            {
                Name = "Espace personnel",
                Kind = WorkspaceKind.Personal,
                OwnerUserId = userId,
                CreatedByUserId = userId
            };
            db.Workspaces.Add(workspace);
            await db.SaveChangesAsync(cancellationToken);
        }

        var userRole = await db.AppUsers
            .Where(item => item.Id == userId)
            .Select(item => item.Role)
            .FirstOrDefaultAsync(cancellationToken);
        var personalRoles = userRole == UserRole.Teacher
            ? new[] { WorkspaceRole.Owner, WorkspaceRole.Learner, WorkspaceRole.Instructor }
            : new[] { WorkspaceRole.Owner, WorkspaceRole.Learner };
        await EnsureMembershipAsync(workspace, userId, personalRoles, cancellationToken);
        return await GetMembershipAsync(workspace.Id, userId, cancellationToken)
            ?? throw new InvalidOperationException("The personal workspace membership could not be loaded.");
    }

    public async Task<Workspace> EnsureOrganizationWorkspaceAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var organization = await db.Organizations.FirstOrDefaultAsync(
            item => item.Id == organizationId,
            cancellationToken)
            ?? throw new InvalidOperationException("Cannot create a workspace for an unknown organization.");

        var workspace = await db.Workspaces.FirstOrDefaultAsync(
            item => item.Kind == WorkspaceKind.Organization && item.OrganizationId == organizationId,
            cancellationToken);
        if (workspace is not null)
            return workspace;

        workspace = new Workspace
        {
            Name = organization.Name,
            Kind = WorkspaceKind.Organization,
            OrganizationId = organization.Id
        };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync(cancellationToken);
        return workspace;
    }

    public async Task EnsureOrganizationMembershipAsync(
        AppUser user,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var workspace = await EnsureOrganizationWorkspaceAsync(organizationId, cancellationToken);
        var role = user.Role switch
        {
            UserRole.Teacher => WorkspaceRole.Instructor,
            UserRole.Student => WorkspaceRole.Learner,
            _ => WorkspaceRole.Member
        };
        await EnsureMembershipAsync(workspace, user.Id, [role], cancellationToken);
    }

    public async Task EnsureFamilyWorkspaceAsync(
        Guid parentId,
        Guid? activeChildId = null,
        CancellationToken cancellationToken = default)
    {
        var parentExists = await db.AppUsers.AnyAsync(user => user.Id == parentId, cancellationToken);
        if (!parentExists)
            throw new InvalidOperationException("Cannot create a family workspace for an unknown parent.");

        if (activeChildId is { } childId)
        {
            var hasActiveParentLink = await db.ParentChildLinks.AnyAsync(
                link => link.ParentId == parentId &&
                        link.ChildId == childId &&
                        link.Status == LinkStatus.Active,
                cancellationToken);
            if (!hasActiveParentLink)
                throw new InvalidOperationException("A child must have an active guardian link to join a family workspace.");
        }

        var workspace = await db.Workspaces.FirstOrDefaultAsync(
            item => item.Kind == WorkspaceKind.Family && item.OwnerUserId == parentId,
            cancellationToken);
        if (workspace is null)
        {
            workspace = new Workspace
            {
                Name = "Espace familial",
                Kind = WorkspaceKind.Family,
                OwnerUserId = parentId,
                CreatedByUserId = parentId
            };
            db.Workspaces.Add(workspace);
            await db.SaveChangesAsync(cancellationToken);
        }

        await EnsureMembershipAsync(
            workspace,
            parentId,
            [WorkspaceRole.Owner, WorkspaceRole.Guardian],
            cancellationToken);

        if (activeChildId is { } memberChildId)
        {
            await EnsureMembershipAsync(workspace, memberChildId, [WorkspaceRole.Learner], cancellationToken);
        }
    }

    public async Task RemoveFamilyMembershipAsync(
        Guid parentId,
        Guid childId,
        CancellationToken cancellationToken = default)
    {
        var membership = await db.WorkspaceMemberships
            .Include(item => item.Workspace)
            .FirstOrDefaultAsync(
                item => item.Workspace.Kind == WorkspaceKind.Family &&
                        item.Workspace.OwnerUserId == parentId &&
                        item.UserId == childId,
                cancellationToken);
        if (membership is null)
            return;

        membership.Status = WorkspaceMembershipStatus.Left;
        membership.JoinedAt = null;
        var activeMcpGrants = await db.McpPermissionGrants
            .Where(grant => grant.UserId == childId &&
                            grant.WorkspaceId == membership.WorkspaceId &&
                            grant.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var grant in activeMcpGrants)
            grant.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkspaceSummaryDto>> GetMineAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await EnsurePersonalWorkspaceAsync(userId, cancellationToken);

        return await db.WorkspaceMemberships
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.Status == WorkspaceMembershipStatus.Active)
            .OrderBy(item => item.Workspace.Kind)
            .ThenBy(item => item.Workspace.Name)
            .Select(item => new WorkspaceSummaryDto(
                item.Workspace.Id,
                item.Workspace.Name,
                item.Workspace.Kind,
                item.Workspace.OrganizationId,
                item.RoleGrants.Select(grant => grant.Role).ToList(),
                item.JoinedAt))
            .ToListAsync(cancellationToken);
    }

    public Task<WorkspaceSummaryDto?> GetMembershipAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        db.WorkspaceMemberships
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId &&
                           item.UserId == userId &&
                           item.Status == WorkspaceMembershipStatus.Active)
            .Select(item => new WorkspaceSummaryDto(
                item.Workspace.Id,
                item.Workspace.Name,
                item.Workspace.Kind,
                item.Workspace.OrganizationId,
                item.RoleGrants.Select(grant => grant.Role).ToList(),
                item.JoinedAt))
            .FirstOrDefaultAsync(cancellationToken);

    private async Task EnsureMembershipAsync(
        Workspace workspace,
        Guid userId,
        IReadOnlyCollection<WorkspaceRole> roles,
        CancellationToken cancellationToken)
    {
        var userExists = await db.AppUsers.AnyAsync(user => user.Id == userId, cancellationToken);
        if (!userExists)
            throw new InvalidOperationException("Cannot add an unknown user to a workspace.");

        var membership = await db.WorkspaceMemberships
            .Include(item => item.RoleGrants)
            .FirstOrDefaultAsync(
                item => item.WorkspaceId == workspace.Id && item.UserId == userId,
                cancellationToken);
        if (membership is null)
        {
            membership = new WorkspaceMembership
            {
                WorkspaceId = workspace.Id,
                UserId = userId,
                Status = WorkspaceMembershipStatus.Active,
                JoinedAt = DateTime.UtcNow
            };
            db.WorkspaceMemberships.Add(membership);
        }
        else
        {
            membership.Status = WorkspaceMembershipStatus.Active;
            membership.JoinedAt ??= DateTime.UtcNow;
        }

        foreach (var role in roles.Distinct())
        {
            if (membership.RoleGrants.Any(grant => grant.Role == role))
                continue;

            membership.RoleGrants.Add(new WorkspaceRoleGrant { Role = role });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record WorkspaceSummaryDto(
    Guid Id,
    string Name,
    WorkspaceKind Kind,
    Guid? OrganizationId,
    IReadOnlyList<WorkspaceRole> Roles,
    DateTime? JoinedAt);
