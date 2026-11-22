namespace User.Model;

public sealed class Workspace
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }

    public WorkspaceKind Kind { get; set; }

    public Guid? OwnerUserId { get; set; }

    public Guid? OrganizationId { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<WorkspaceMembership> Memberships { get; set; } = [];
}

public sealed class WorkspaceMembership
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Workspace Workspace { get; set; } = null!;

    public Guid UserId { get; set; }

    public AppUser User { get; set; } = null!;

    public WorkspaceMembershipStatus Status { get; set; } = WorkspaceMembershipStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? JoinedAt { get; set; } = DateTime.UtcNow;

    public List<WorkspaceRoleGrant> RoleGrants { get; set; } = [];
}

public sealed class WorkspaceRoleGrant
{
    public Guid MembershipId { get; set; }

    public WorkspaceMembership Membership { get; set; } = null!;

    public WorkspaceRole Role { get; set; }

    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
}

public enum WorkspaceKind
{
    Personal,
    Family,
    Organization
}

public enum WorkspaceMembershipStatus
{
    Active,
    Pending,
    Suspended,
    Left
}

public enum WorkspaceRole
{
    Owner,
    Admin,
    Guardian,
    Learner,
    Instructor,
    Member
}
