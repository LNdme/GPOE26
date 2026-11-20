namespace User.Model;

public sealed class McpPermissionGrantRecord
{
    public Guid UserId { get; set; }

    public Guid WorkspaceId { get; set; }

    public required string ExtensionId { get; set; }

    public string[] CapabilityIds { get; set; } = [];

    public string[] PermissionIds { get; set; } = [];

    public Guid ApprovedByUserId { get; set; }

    public DateTime ApprovedAt { get; set; }

    public DateTime? RevokedAt { get; set; }
}

public sealed class McpProcessConfiguration
{
    public Guid UserId { get; set; }

    public Guid WorkspaceId { get; set; }

    public required string ExtensionId { get; set; }

    public required string ProtectedOptions { get; set; }

    public bool IsVerified { get; set; }

    public bool IsSandboxed { get; set; }

    public DateTime UpdatedAt { get; set; }
}
