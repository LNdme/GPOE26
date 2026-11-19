using System.Security.Claims;
using GPOE26.Agent.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using User.Data;
using User.Model;
using User.Service;

namespace GPOE26.Tests;

public sealed class McpAuthorizationServiceTests
{
    [Fact]
    public async Task PersonalApprovalIsIsolatedByUserAndWorkspaceAndCanBeRevoked()
    {
        await using var db = CreateContext();
        var ownerId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var otherWorkspaceId = Guid.NewGuid();
        await AddMemberAsync(db, ownerId, workspaceId, WorkspaceKind.Personal,
            WorkspaceRole.Owner, ownerId);
        await AddMemberAsync(db, ownerId, otherWorkspaceId, WorkspaceKind.Personal,
            WorkspaceRole.Owner, ownerId);
        var service = CreateService(db);
        var caller = Principal(ownerId, workspaceId, WorkspaceKind.Personal, WorkspaceRole.Owner);

        Assert.Equal(McpAuthorizationOutcome.Success, await service.ApproveAsync(
            caller, workspaceId, ownerId, "sample", ["read"], ["library.read"]));

        Assert.NotNull(await service.GetApprovedGrantAsync(ownerId, workspaceId, "sample"));
        Assert.Null(await service.GetApprovedGrantAsync(Guid.NewGuid(), workspaceId, "sample"));
        Assert.Null(await service.GetApprovedGrantAsync(ownerId, otherWorkspaceId, "sample"));

        Assert.Equal(McpAuthorizationOutcome.Success,
            await service.RevokeAsync(caller, workspaceId, ownerId, "sample"));
        Assert.Null(await service.GetApprovedGrantAsync(ownerId, workspaceId, "sample"));
    }

    [Fact]
    public async Task FamilyGuardianCanApproveForAnActiveMember()
    {
        await using var db = CreateContext();
        var guardianId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        await AddMemberAsync(db, guardianId, workspaceId, WorkspaceKind.Family,
            WorkspaceRole.Guardian, guardianId);
        await AddMemberAsync(db, childId, workspaceId, WorkspaceKind.Family,
            WorkspaceRole.Learner, guardianId);
        var service = CreateService(db);

        var result = await service.ApproveAsync(
            Principal(guardianId, workspaceId, WorkspaceKind.Family, WorkspaceRole.Guardian),
            workspaceId,
            childId,
            "family-extension",
            ["read"],
            []);

        Assert.Equal(McpAuthorizationOutcome.Success, result);
        Assert.Equal(childId,
            (await service.GetApprovedGrantAsync(childId, workspaceId, "family-extension"))!.UserId);
    }

    [Fact]
    public async Task ManageableMembersAreLimitedToTheActorUnlessFamilyGuardian()
    {
        await using var db = CreateContext();
        var guardianId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        await AddMemberAsync(db, guardianId, workspaceId, WorkspaceKind.Family,
            WorkspaceRole.Guardian, guardianId);
        await AddMemberAsync(db, childId, workspaceId, WorkspaceKind.Family,
            WorkspaceRole.Learner, guardianId);
        var service = CreateService(db);

        var guardianResult = await service.GetManageableMembersAsync(
            Principal(guardianId, workspaceId, WorkspaceKind.Family, WorkspaceRole.Guardian),
            workspaceId,
            null);
        var learnerResult = await service.GetManageableMembersAsync(
            Principal(childId, workspaceId, WorkspaceKind.Family, WorkspaceRole.Learner),
            workspaceId,
            null);

        Assert.Equal(McpAuthorizationOutcome.Success, guardianResult.Outcome);
        Assert.Equal(2, guardianResult.Members.Count);
        Assert.Contains(guardianResult.Members, member => member.UserId == guardianId);
        Assert.Contains(guardianResult.Members, member => member.UserId == childId);
        Assert.Equal(McpAuthorizationOutcome.Success, learnerResult.Outcome);
        Assert.Equal(new[] { childId }, learnerResult.Members.Select(member => member.UserId));
    }

    [Fact]
    public async Task ActiveGrantListingUsesTheSameWorkspaceManagementRules()
    {
        await using var db = CreateContext();
        var guardianId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var otherChildId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        await AddMemberAsync(db, guardianId, workspaceId, WorkspaceKind.Family,
            WorkspaceRole.Guardian, guardianId);
        await AddMemberAsync(db, childId, workspaceId, WorkspaceKind.Family,
            WorkspaceRole.Learner, guardianId);
        await AddMemberAsync(db, otherChildId, workspaceId, WorkspaceKind.Family,
            WorkspaceRole.Learner, guardianId);
        var service = CreateService(db);
        var guardian = Principal(guardianId, workspaceId, WorkspaceKind.Family, WorkspaceRole.Guardian);
        Assert.Equal(McpAuthorizationOutcome.Success, await service.ApproveAsync(
            guardian, workspaceId, childId, "family-extension", ["read"], ["library.read"]));

        var visibleToGuardian = await service.GetActiveGrantsAsync(guardian, workspaceId, childId);
        var hiddenFromSibling = await service.GetActiveGrantsAsync(
            Principal(otherChildId, workspaceId, WorkspaceKind.Family, WorkspaceRole.Learner),
            workspaceId,
            childId);

        Assert.Equal(McpAuthorizationOutcome.Success, visibleToGuardian.Outcome);
        Assert.Equal("family-extension", Assert.Single(visibleToGuardian.Grants).ExtensionId);
        Assert.Equal(McpAuthorizationOutcome.Forbidden, hiddenFromSibling.Outcome);
        Assert.Empty(hiddenFromSibling.Grants);
    }

    [Fact]
    public async Task OrganizationApprovalRequiresWorkspaceAdminRoleNotGlobalAdmin()
    {
        await using var db = CreateContext();
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        await AddMemberAsync(db, actorId, workspaceId, WorkspaceKind.Organization,
            WorkspaceRole.Member, null);
        await AddMemberAsync(db, targetId, workspaceId, WorkspaceKind.Organization,
            WorkspaceRole.Learner, null);
        var service = CreateService(db);

        var result = await service.ApproveAsync(
            Principal(actorId, workspaceId, WorkspaceKind.Organization, WorkspaceRole.Member, globalRole: "Admin"),
            workspaceId,
            targetId,
            "org-extension",
            ["read"],
            []);

        Assert.Equal(McpAuthorizationOutcome.Forbidden, result);
        Assert.Null(await service.GetApprovedGrantAsync(targetId, workspaceId, "org-extension"));
    }

    [Fact]
    public async Task OrganizationWorkspaceAdminCanApproveForAnActiveMember()
    {
        await using var db = CreateContext();
        var adminId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        await AddMemberAsync(db, adminId, workspaceId, WorkspaceKind.Organization,
            WorkspaceRole.Admin, null);
        await AddMemberAsync(db, targetId, workspaceId, WorkspaceKind.Organization,
            WorkspaceRole.Learner, null);
        var service = CreateService(db);

        var result = await service.ApproveAsync(
            Principal(adminId, workspaceId, WorkspaceKind.Organization, WorkspaceRole.Admin),
            workspaceId,
            targetId,
            "org-extension",
            ["read"],
            []);

        Assert.Equal(McpAuthorizationOutcome.Success, result);
        Assert.NotNull(await service.GetApprovedGrantAsync(targetId, workspaceId, "org-extension"));
    }

    [Fact]
    public async Task FamilyMemberWithoutGuardianRoleCannotApproveForAnotherMember()
    {
        await using var db = CreateContext();
        var learnerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        await AddMemberAsync(db, learnerId, workspaceId, WorkspaceKind.Family,
            WorkspaceRole.Learner, Guid.NewGuid());
        await AddMemberAsync(db, targetId, workspaceId, WorkspaceKind.Family,
            WorkspaceRole.Learner, Guid.NewGuid());
        var service = CreateService(db);

        var result = await service.ApproveAsync(
            Principal(learnerId, workspaceId, WorkspaceKind.Family, WorkspaceRole.Learner),
            workspaceId,
            targetId,
            "family-extension",
            ["read"],
            []);

        Assert.Equal(McpAuthorizationOutcome.Forbidden, result);
        Assert.Null(await service.GetApprovedGrantAsync(targetId, workspaceId, "family-extension"));
    }

    [Fact]
    public async Task LeavingFamilyWorkspaceRevokesTheMembersGrant()
    {
        await using var db = CreateContext();
        var guardianId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        await AddMemberAsync(db, guardianId, workspaceId, WorkspaceKind.Family,
            WorkspaceRole.Guardian, guardianId);
        await AddMemberAsync(db, childId, workspaceId, WorkspaceKind.Family,
            WorkspaceRole.Learner, guardianId);
        var service = CreateService(db);
        Assert.Equal(McpAuthorizationOutcome.Success, await service.ApproveAsync(
            Principal(guardianId, workspaceId, WorkspaceKind.Family, WorkspaceRole.Guardian),
            workspaceId,
            childId,
            "family-extension",
            ["read"],
            []));

        await new WorkspaceService(db).RemoveFamilyMembershipAsync(guardianId, childId);

        Assert.Null(await service.GetApprovedGrantAsync(childId, workspaceId, "family-extension"));
        Assert.NotNull(await db.McpPermissionGrants.SingleAsync(grant =>
            grant.UserId == childId && grant.WorkspaceId == workspaceId && grant.RevokedAt != null));
    }

    [Fact]
    public async Task ProcessConfigurationIsPersistedEncryptedAndRequiresExplicitVerification()
    {
        await using var db = CreateContext();
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        await AddMemberAsync(db, userId, workspaceId, WorkspaceKind.Personal,
            WorkspaceRole.Owner, userId);
        var service = CreateService(db);
        var image = "registry.example/research-mcp@sha256:" + new string('a', 64);

        var configured = await service.ConfigureProcessOptionsAsync(
            userId,
            workspaceId,
            "research-mcp",
            image,
            [],
            new Dictionary<string, string?> { ["RESEARCH_API_KEY"] = "top-secret" });

        Assert.Equal(McpProcessConfigurationOutcome.Success, configured);
        var stored = await db.McpProcessConfigurations.SingleAsync(
            item => item.UserId == userId &&
                    item.WorkspaceId == workspaceId &&
                    item.ExtensionId == "research-mcp");
        Assert.False(stored.IsVerified);
        Assert.False(stored.IsSandboxed);
        Assert.DoesNotContain("top-secret", stored.ProtectedOptions);
        Assert.Null(await service.GetOptionsAsync(userId, workspaceId, "research-mcp"));

        Assert.Equal(McpProcessConfigurationOutcome.Success,
            await service.VerifyProcessOptionsAsync(userId, workspaceId, "research-mcp"));
        Assert.True((await service.GetOptionsAsync(
            userId, workspaceId, "research-mcp"))!.EnvironmentVariables["RESEARCH_API_KEY"] == "top-secret");

        Assert.Equal(McpProcessConfigurationOutcome.Success,
            await service.RevokeProcessOptionsAsync(userId, workspaceId, "research-mcp"));
        Assert.Empty(await db.McpProcessConfigurations.ToListAsync());
    }

    [Fact]
    public async Task ProcessConfigurationRejectsMutableImagesAndReservedEnvironmentVariables()
    {
        await using var db = CreateContext();
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        await AddMemberAsync(db, userId, workspaceId, WorkspaceKind.Personal,
            WorkspaceRole.Owner, userId);
        var service = CreateService(db);

        Assert.Equal(McpProcessConfigurationOutcome.InvalidRequest,
            await service.ConfigureProcessOptionsAsync(
                userId,
                workspaceId,
                "research-mcp",
                "registry.example/research-mcp:latest",
                [],
                null));
        Assert.Equal(McpProcessConfigurationOutcome.InvalidRequest,
            await service.ConfigureProcessOptionsAsync(
                userId,
                workspaceId,
                "research-mcp",
                "registry.example/research-mcp@sha256:" + new string('b', 64),
                [],
                new Dictionary<string, string?> { ["PATH"] = "/tmp" }));
    }

    private static UserContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<UserContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new UserContext(options);
    }

    private static McpAuthorizationService CreateService(UserContext db)
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        return new McpAuthorizationService(db, services.BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>());
    }

    private static async Task AddMemberAsync(
        UserContext db,
        Guid userId,
        Guid workspaceId,
        WorkspaceKind kind,
        WorkspaceRole role,
        Guid? ownerId)
    {
        if (!await db.AppUsers.AnyAsync(user => user.Id == userId))
            db.AppUsers.Add(new AppUser
            {
                Id = userId,
                Email = $"{userId}@example.test",
                Username = userId.ToString(),
                PasswordHash = "test-hash",
                Role = UserRole.Admin
            });

        if (!await db.Workspaces.AnyAsync(workspace => workspace.Id == workspaceId))
            db.Workspaces.Add(new Workspace
            {
                Id = workspaceId,
                Name = "Test",
                Kind = kind,
                OwnerUserId = ownerId
            });

        var membership = new WorkspaceMembership
        {
            WorkspaceId = workspaceId,
            UserId = userId,
            Status = WorkspaceMembershipStatus.Active
        };
        membership.RoleGrants.Add(new WorkspaceRoleGrant { Role = role });
        db.WorkspaceMemberships.Add(membership);
        await db.SaveChangesAsync();
    }

    private static ClaimsPrincipal Principal(
        Guid userId,
        Guid workspaceId,
        WorkspaceKind kind,
        WorkspaceRole role,
        string? globalRole = null)
    {
        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("workspace_id", workspaceId.ToString()),
            new("workspace_kind", kind.ToString()),
            new("workspace_role", role.ToString())
        };
        if (globalRole is not null)
            claims.Add(new Claim("role", globalRole));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
