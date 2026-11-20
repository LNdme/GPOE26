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

    /// <summary>
    /// Espace de travail d'un jeton neuf (connexion, inscription, activation par code) : celui de la famille pour un
    /// parent, celui de son établissement pour un membre (ajouté au besoin), sinon son espace personnel.
    /// </summary>
    public async Task<WorkspaceSummaryDto> DefaultForAsync(AppUser user, CancellationToken cancellationToken = default)
    {
        var personal = await EnsurePersonalWorkspaceAsync(user.Id, cancellationToken);
        if (user.Role == UserRole.Parent)
        {
            await EnsureFamilyWorkspaceAsync(user.Id, cancellationToken: cancellationToken);
            return (await GetMineAsync(user.Id, cancellationToken)).Single(workspace => workspace.Kind == WorkspaceKind.Family);
        }

        await SyncSpaceMembershipsAsync(user, cancellationToken);
        var mine = await GetMineAsync(user.Id, cancellationToken);
        if (user.OrganizationId is { } organizationId && await IsSchoolAsync(organizationId, cancellationToken))
        {
            await EnsureOrganizationMembershipAsync(user, organizationId, cancellationToken);
            return (await GetMineAsync(user.Id, cancellationToken))
                .FirstOrDefault(workspace => workspace.OrganizationId == organizationId) ?? personal;
        }
        // Élève inscrit sur la liste d'un seul établissement : son espace de travail
        var schools = mine.Where(workspace => workspace.Kind == WorkspaceKind.Organization).ToList();
        return user.Role == UserRole.Student && schools.Count == 1 ? schools[0] : personal;
    }

    /// <summary>Au renouvellement de la session : l'espace du jeton actuel si la personne en est toujours membre.</summary>
    public async Task<WorkspaceSummaryDto> CurrentOrDefaultAsync(AppUser user, Guid? currentWorkspaceId,
        CancellationToken cancellationToken = default)
    {
        await SyncSpaceMembershipsAsync(user, cancellationToken);
        return currentWorkspaceId is { } id && await GetMembershipAsync(id, user.Id, cancellationToken) is { } current
            ? current
            : await DefaultForAsync(user, cancellationToken);
    }

    /// <summary>
    /// Pont avec les espaces d'établissement (docs/PLAN-ETABLISSEMENT.md) : la personne rejoint l'espace de travail
    /// de chaque établissement où elle figure. Responsable et gestionnaires : Admin ; enseignants : Instructor ;
    /// élèves activés sur la liste : Learner. Appelé à chaque jeton neuf (connexion, renouvellement).
    /// Les rôles ne sont qu'ajoutés : un retrait dans l'établissement ne retire pas l'adhésion à l'espace de travail
    /// (l'établissement reste vérifié par ses propres règles, en base, sur ses routes).
    /// </summary>
    public async Task SyncSpaceMembershipsAsync(AppUser user, CancellationToken cancellationToken = default)
    {
        var schools = db.Organizations.Where(o => o.Type != OrganizationType.Independent).Select(o => o.Id);
        var members = await db.OrganizationMembers
            .Where(m => m.UserId == user.Id && m.Status == MemberStatus.Active && schools.Contains(m.OrganizationId))
            .Select(m => new { m.OrganizationId, m.Role })
            .ToListAsync(cancellationToken);
        var studentOf = await db.OrganizationStudents
            .Where(s => s.UserId == user.Id && s.Status == StudentStatus.Active && schools.Contains(s.OrganizationId))
            .Select(s => s.OrganizationId)
            .ToListAsync(cancellationToken);

        var roles = new Dictionary<Guid, HashSet<WorkspaceRole>>();
        foreach (var member in members)
            Roles(member.OrganizationId).Add(member.Role == OrganizationRole.Teacher ? WorkspaceRole.Instructor : WorkspaceRole.Admin);
        foreach (var organizationId in studentOf)
            Roles(organizationId).Add(WorkspaceRole.Learner);

        foreach (var (organizationId, granted) in roles)
            await EnsureMembershipAsync(await EnsureOrganizationWorkspaceAsync(organizationId, cancellationToken), user.Id,
                granted, cancellationToken);

        HashSet<WorkspaceRole> Roles(Guid organizationId) =>
            roles.TryGetValue(organizationId, out var set) ? set : roles[organizationId] = [];
    }

    /// <summary>
    /// Espace de travail d'une classe créée dans un espace d'établissement : celui de l'établissement, ou, pour un
    /// espace d'enseignant indépendant, l'espace personnel de son responsable.
    /// </summary>
    public async Task<Guid> WorkspaceIdForSpaceAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        if (await IsSchoolAsync(organizationId, cancellationToken))
            return (await EnsureOrganizationWorkspaceAsync(organizationId, cancellationToken)).Id;
        var owner = await db.OrganizationMembers
            .Where(m => m.OrganizationId == organizationId && m.Role == OrganizationRole.Owner && m.Status == MemberStatus.Active)
            .Select(m => (Guid?)m.UserId)
            .FirstOrDefaultAsync(cancellationToken);
        return owner is { } ownerId
            ? (await EnsurePersonalWorkspaceAsync(ownerId, cancellationToken)).Id
            : (await EnsureOrganizationWorkspaceAsync(organizationId, cancellationToken)).Id;
    }

    private Task<bool> IsSchoolAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.Organizations.AnyAsync(o => o.Id == organizationId && o.Type != OrganizationType.Independent, cancellationToken);

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
