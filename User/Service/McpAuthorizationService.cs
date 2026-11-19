using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using GPOE26.Agent.Abstractions;
using GPOE26.Workspaces.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using User.Data;
using User.Helper;
using User.Model;

namespace User.Service;

public enum McpAuthorizationOutcome
{
    Success,
    Unauthorized,
    Forbidden,
    TargetNotMember,
    GrantNotFound,
    InvalidRequest
}

public sealed record McpManageableMember(
    Guid UserId,
    string Username,
    IReadOnlyList<WorkspaceRole> Roles);

public sealed record McpGrantSummary(
    string ExtensionId,
    IReadOnlyList<string> CapabilityIds,
    IReadOnlyList<string> PermissionIds,
    DateTime ApprovedAt);

public sealed record McpProcessConfigurationSummary(
    Guid UserId,
    Guid WorkspaceId,
    string ExtensionId,
    bool IsVerified,
    bool IsSandboxed,
    DateTime UpdatedAt);

public enum McpProcessConfigurationOutcome
{
    Success,
    InvalidRequest,
    TargetNotMember,
    NotFound
}

public sealed class McpAuthorizationService : IMcpPermissionGrantStore, IMcpServerProcessOptionsStore
{
    private const int MaxExtensionIdLength = 200;
    private const int MaxIdsPerGrant = 100;
    private const int MaxIdLength = 128;
    private readonly IDataProtector _optionsProtector;

    public McpAuthorizationService(UserContext db, IDataProtectionProvider protectionProvider)
    {
        _db = db;
        _optionsProtector = protectionProvider.CreateProtector("GPOE26.McpProcessOptions.v2");
    }

    private readonly UserContext _db;

    public async Task<McpProcessConfigurationOutcome> ConfigureProcessOptionsAsync(
        Guid targetUserId,
        Guid workspaceId,
        string extensionId,
        string imageReference,
        IReadOnlyList<string>? arguments,
        IReadOnlyDictionary<string, string?>? environmentVariables,
        CancellationToken cancellationToken = default)
    {
        arguments ??= [];
        environmentVariables ??= new Dictionary<string, string?>();
        if (targetUserId == Guid.Empty || workspaceId == Guid.Empty ||
            !await _db.WorkspaceMemberships.AnyAsync(
                membership =>
                    membership.UserId == targetUserId &&
                    membership.WorkspaceId == workspaceId &&
                    membership.Status == WorkspaceMembershipStatus.Active,
                cancellationToken))
            return targetUserId == Guid.Empty || workspaceId == Guid.Empty
                ? McpProcessConfigurationOutcome.InvalidRequest
                : McpProcessConfigurationOutcome.TargetNotMember;

        var options = new McpServerProcessOptions(
            targetUserId,
            workspaceId,
            extensionId,
            imageReference,
            arguments.ToArray(),
            environmentVariables
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        if (!TryValidateProcessOptions(options))
            return McpProcessConfigurationOutcome.InvalidRequest;

        await StoreUnverifiedProcessOptionsAsync(options, cancellationToken);
        return McpProcessConfigurationOutcome.Success;
    }

    public async Task<McpProcessConfigurationOutcome> VerifyProcessOptionsAsync(
        Guid targetUserId,
        Guid workspaceId,
        string extensionId,
        CancellationToken cancellationToken = default)
    {
        if (targetUserId == Guid.Empty || workspaceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(extensionId) ||
            extensionId.Trim().Length > MaxExtensionIdLength)
            return McpProcessConfigurationOutcome.InvalidRequest;

        var normalizedExtension = extensionId.Trim();
        var configuration = await _db.McpProcessConfigurations.FirstOrDefaultAsync(
            item =>
                item.UserId == targetUserId &&
                item.WorkspaceId == workspaceId &&
                item.ExtensionId == normalizedExtension,
            cancellationToken);
        if (configuration is null)
            return McpProcessConfigurationOutcome.NotFound;

        McpServerProcessOptions? options;
        try
        {
            options = JsonSerializer.Deserialize<McpServerProcessOptions>(
                _optionsProtector.Unprotect(configuration.ProtectedOptions));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return McpProcessConfigurationOutcome.InvalidRequest;
        }

        if (options is null || !TryValidateProcessOptions(options))
        {
            configuration.IsVerified = false;
            configuration.IsSandboxed = false;
            configuration.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return McpProcessConfigurationOutcome.InvalidRequest;
        }

        configuration.IsVerified = true;
        configuration.IsSandboxed = true;
        configuration.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return McpProcessConfigurationOutcome.Success;
    }

    public async Task<McpProcessConfigurationOutcome> RevokeProcessOptionsAsync(
        Guid targetUserId,
        Guid workspaceId,
        string extensionId,
        CancellationToken cancellationToken = default)
    {
        if (targetUserId == Guid.Empty || workspaceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(extensionId) ||
            extensionId.Trim().Length > MaxExtensionIdLength)
            return McpProcessConfigurationOutcome.InvalidRequest;

        var configuration = await _db.McpProcessConfigurations.FirstOrDefaultAsync(
            item =>
                item.UserId == targetUserId &&
                item.WorkspaceId == workspaceId &&
                item.ExtensionId == extensionId.Trim(),
            cancellationToken);
        if (configuration is null)
            return McpProcessConfigurationOutcome.NotFound;

        _db.McpProcessConfigurations.Remove(configuration);
        await _db.SaveChangesAsync(cancellationToken);
        return McpProcessConfigurationOutcome.Success;
    }

    public async Task<IReadOnlyList<McpProcessConfigurationSummary>> ListProcessConfigurationsAsync(
        CancellationToken cancellationToken = default) =>
        await _db.McpProcessConfigurations
            .AsNoTracking()
            .OrderBy(configuration => configuration.WorkspaceId)
            .ThenBy(configuration => configuration.ExtensionId)
            .Select(configuration => new McpProcessConfigurationSummary(
                configuration.UserId,
                configuration.WorkspaceId,
                configuration.ExtensionId,
                configuration.IsVerified,
                configuration.IsSandboxed,
                configuration.UpdatedAt))
            .ToListAsync(cancellationToken);

    public async Task<(McpAuthorizationOutcome Outcome, IReadOnlyList<McpManageableMember> Members)>
        GetManageableMembersAsync(
            ClaimsPrincipal principal,
            Guid workspaceId,
            string? query,
            CancellationToken cancellationToken = default)
    {
        if (query?.Trim().Length > 100)
            return (McpAuthorizationOutcome.InvalidRequest, []);

        var (outcome, actor, actorId) = await GetAuthorizedActorAsync(
            principal, workspaceId, cancellationToken);
        if (outcome != McpAuthorizationOutcome.Success)
            return (outcome, []);

        var canManageOthers = actor!.Workspace.Kind switch
        {
            WorkspaceKind.Family => actor.RoleGrants.Any(grant => grant.Role == WorkspaceRole.Guardian),
            WorkspaceKind.Organization => actor.RoleGrants.Any(grant => grant.Role == WorkspaceRole.Admin),
            _ => false
        };
        var search = query?.Trim();
        var membersQuery = _db.WorkspaceMemberships
            .AsNoTracking()
            .Include(membership => membership.User)
            .Include(membership => membership.RoleGrants)
            .Where(membership =>
                membership.WorkspaceId == workspaceId &&
                membership.Status == WorkspaceMembershipStatus.Active);
        if (!canManageOthers)
            membersQuery = membersQuery.Where(membership => membership.UserId == actorId);
        if (!string.IsNullOrEmpty(search))
            membersQuery = membersQuery.Where(membership => membership.User.Username.Contains(search));

        var memberships = await membersQuery
            .OrderBy(membership => membership.User.Username)
            .Take(100)
            .ToListAsync(cancellationToken);
        var members = memberships.Select(membership => new McpManageableMember(
                membership.UserId,
                membership.User.Username,
                membership.RoleGrants.Select(grant => grant.Role).ToArray()))
            .ToArray();
        return (McpAuthorizationOutcome.Success, members);
    }

    public async Task<(McpAuthorizationOutcome Outcome, IReadOnlyList<McpGrantSummary> Grants)>
        GetActiveGrantsAsync(
            ClaimsPrincipal principal,
            Guid workspaceId,
            Guid targetUserId,
            CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(
            principal, workspaceId, targetUserId, permitSelfRevocation: true, cancellationToken);
        if (authorization != McpAuthorizationOutcome.Success)
            return (authorization, []);

        var grants = await _db.McpPermissionGrants
            .AsNoTracking()
            .Where(grant =>
                grant.UserId == targetUserId &&
                grant.WorkspaceId == workspaceId &&
                grant.RevokedAt == null)
            .OrderBy(grant => grant.ExtensionId)
            .Select(grant => new McpGrantSummary(
                grant.ExtensionId,
                grant.CapabilityIds,
                grant.PermissionIds,
                grant.ApprovedAt))
            .ToListAsync(cancellationToken);
        return (McpAuthorizationOutcome.Success, grants);
    }

    public async Task<McpPermissionGrant?> GetApprovedGrantAsync(
        Guid userId,
        Guid workspaceId,
        string extensionId,
        CancellationToken cancellationToken = default)
    {
        var grant = await _db.McpPermissionGrants.AsNoTracking().FirstOrDefaultAsync(
            item => item.UserId == userId &&
                    item.WorkspaceId == workspaceId &&
                    item.ExtensionId == extensionId &&
                    item.RevokedAt == null &&
                    _db.WorkspaceMemberships.Any(membership =>
                        membership.UserId == userId &&
                        membership.WorkspaceId == workspaceId &&
                        membership.Status == WorkspaceMembershipStatus.Active),
            cancellationToken);
        return grant is null
            ? null
            : new McpPermissionGrant(
                grant.UserId,
                grant.WorkspaceId,
                grant.ExtensionId,
                grant.CapabilityIds,
                grant.PermissionIds);
    }

    public async Task<McpServerProcessOptions?> GetOptionsAsync(
        Guid userId,
        Guid workspaceId,
        string extensionId,
        CancellationToken cancellationToken = default)
    {
        var stored = await _db.McpProcessConfigurations.AsNoTracking().FirstOrDefaultAsync(
            item => item.UserId == userId &&
                    item.WorkspaceId == workspaceId &&
                    item.ExtensionId == extensionId &&
                    item.IsVerified &&
                    item.IsSandboxed &&
                    _db.WorkspaceMemberships.Any(membership =>
                        membership.UserId == userId &&
                        membership.WorkspaceId == workspaceId &&
                        membership.Status == WorkspaceMembershipStatus.Active),
            cancellationToken);

        // Keep configuration unavailable until an approved image and workspace flow mark it safe.
        if (stored is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<McpServerProcessOptions>(
                _optionsProtector.Unprotect(stored.ProtectedOptions));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }

    public async Task<McpAuthorizationOutcome> ApproveAsync(
        ClaimsPrincipal principal,
        Guid workspaceId,
        Guid targetUserId,
        string extensionId,
        IReadOnlyCollection<string> capabilityIds,
        IReadOnlyCollection<string> permissionIds,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeGrant(extensionId, capabilityIds, permissionIds, out var normalizedExtension,
                out var normalizedCapabilities, out var normalizedPermissions))
            return McpAuthorizationOutcome.InvalidRequest;

        var authorization = await AuthorizeAsync(
            principal, workspaceId, targetUserId, permitSelfRevocation: false, cancellationToken);
        if (authorization == McpAuthorizationOutcome.Success)
        {
            var actorId = principal.GetUserId()!.Value;
            var grant = await _db.McpPermissionGrants.FirstOrDefaultAsync(
                item => item.UserId == targetUserId &&
                        item.WorkspaceId == workspaceId &&
                        item.ExtensionId == normalizedExtension,
                cancellationToken);
            if (grant is null)
            {
                _db.McpPermissionGrants.Add(new McpPermissionGrantRecord
                {
                    UserId = targetUserId,
                    WorkspaceId = workspaceId,
                    ExtensionId = normalizedExtension,
                    CapabilityIds = normalizedCapabilities,
                    PermissionIds = normalizedPermissions,
                    ApprovedByUserId = actorId,
                    ApprovedAt = DateTime.UtcNow
                });
            }
            else
            {
                grant.CapabilityIds = normalizedCapabilities;
                grant.PermissionIds = normalizedPermissions;
                grant.ApprovedByUserId = actorId;
                grant.ApprovedAt = DateTime.UtcNow;
                grant.RevokedAt = null;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        return authorization;
    }

    public async Task<McpAuthorizationOutcome> RevokeAsync(
        ClaimsPrincipal principal,
        Guid workspaceId,
        Guid targetUserId,
        string extensionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(extensionId) || extensionId.Trim().Length > MaxExtensionIdLength)
            return McpAuthorizationOutcome.InvalidRequest;

        var authorization = await AuthorizeAsync(
            principal, workspaceId, targetUserId, permitSelfRevocation: true, cancellationToken);
        if (authorization != McpAuthorizationOutcome.Success)
            return authorization;

        var grant = await _db.McpPermissionGrants.FirstOrDefaultAsync(
            item => item.UserId == targetUserId &&
                    item.WorkspaceId == workspaceId &&
                    item.ExtensionId == extensionId.Trim() &&
                    item.RevokedAt == null,
            cancellationToken);
        if (grant is null)
            return McpAuthorizationOutcome.GrantNotFound;

        grant.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return McpAuthorizationOutcome.Success;
    }

    internal async Task StoreUnverifiedProcessOptionsAsync(
        McpServerProcessOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options.UserId == Guid.Empty ||
            options.WorkspaceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(options.ExtensionId) ||
            options.ExtensionId.Trim().Length > MaxExtensionIdLength)
            throw new ArgumentException("MCP process options require a scoped identity.", nameof(options));

        var payload = JsonSerializer.Serialize(options with
        {
            ExtensionId = options.ExtensionId.Trim()
        });
        var protectedPayload = _optionsProtector.Protect(payload);
        var current = await _db.McpProcessConfigurations.FirstOrDefaultAsync(
            item => item.UserId == options.UserId &&
                    item.WorkspaceId == options.WorkspaceId &&
                    item.ExtensionId == options.ExtensionId.Trim(),
            cancellationToken);
        if (current is null)
        {
            _db.McpProcessConfigurations.Add(new McpProcessConfiguration
            {
                UserId = options.UserId,
                WorkspaceId = options.WorkspaceId,
                ExtensionId = options.ExtensionId.Trim(),
                ProtectedOptions = protectedPayload,
                IsVerified = false,
                IsSandboxed = false,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            current.ProtectedOptions = protectedPayload;
            current.IsVerified = false;
            current.IsSandboxed = false;
            current.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<McpAuthorizationOutcome> AuthorizeAsync(
        ClaimsPrincipal principal,
        Guid workspaceId,
        Guid targetUserId,
        bool permitSelfRevocation,
        CancellationToken cancellationToken)
    {
        var (actorOutcome, actor, actorId) = await GetAuthorizedActorAsync(
            principal, workspaceId, cancellationToken);
        if (actorOutcome != McpAuthorizationOutcome.Success)
            return actorOutcome;

        var target = await _db.WorkspaceMemberships.AnyAsync(
            membership => membership.WorkspaceId == workspaceId &&
                          membership.UserId == targetUserId &&
                          membership.Status == WorkspaceMembershipStatus.Active,
            cancellationToken);
        if (!target)
            return McpAuthorizationOutcome.TargetNotMember;

        var actorRoles = actor!.RoleGrants.Select(grant => grant.Role).ToHashSet();
        var isSelf = actorId == targetUserId;
        var allowed = actor.Workspace.Kind switch
        {
            WorkspaceKind.Personal =>
                actor.Workspace.OwnerUserId == actorId && isSelf,
            WorkspaceKind.Family =>
                actorRoles.Contains(WorkspaceRole.Guardian) ||
                (permitSelfRevocation && isSelf),
            WorkspaceKind.Organization =>
                actorRoles.Contains(WorkspaceRole.Admin) ||
                (permitSelfRevocation && isSelf),
            _ => false
        };
        return allowed ? McpAuthorizationOutcome.Success : McpAuthorizationOutcome.Forbidden;
    }

    private async Task<(McpAuthorizationOutcome Outcome, WorkspaceMembership? Actor, Guid? ActorId)>
        GetAuthorizedActorAsync(
            ClaimsPrincipal principal,
            Guid workspaceId,
            CancellationToken cancellationToken)
    {
        var actorId = principal.GetUserId();
        if (actorId is null ||
            !principal.TryGetWorkspaceContext(out var tokenContext) ||
            tokenContext!.WorkspaceId != workspaceId)
            return (McpAuthorizationOutcome.Unauthorized, null, null);

        var actor = await _db.WorkspaceMemberships
            .Include(membership => membership.Workspace)
            .Include(membership => membership.RoleGrants)
            .FirstOrDefaultAsync(
                membership => membership.WorkspaceId == workspaceId &&
                              membership.UserId == actorId.Value &&
                              membership.Status == WorkspaceMembershipStatus.Active,
                cancellationToken);
        if (actor is null ||
            !string.Equals(actor.Workspace.Kind.ToString(), tokenContext.Kind, StringComparison.Ordinal) ||
            !actor.RoleGrants.Select(grant => grant.Role.ToString())
                .ToHashSet(StringComparer.Ordinal).SetEquals(tokenContext.Roles))
            return (McpAuthorizationOutcome.Unauthorized, null, null);

        return (McpAuthorizationOutcome.Success, actor, actorId.Value);
    }

    private static bool TryNormalizeGrant(
        string extensionId,
        IReadOnlyCollection<string> capabilityIds,
        IReadOnlyCollection<string> permissionIds,
        out string normalizedExtension,
        out string[] normalizedCapabilities,
        out string[] normalizedPermissions)
    {
        normalizedExtension = extensionId?.Trim() ?? string.Empty;
        normalizedCapabilities = [];
        normalizedPermissions = [];
        if (normalizedExtension.Length == 0 || normalizedExtension.Length > MaxExtensionIdLength)
            return false;

        if (!NormalizeIds(capabilityIds, requireAtLeastOne: true, out normalizedCapabilities) ||
            !NormalizeIds(permissionIds, requireAtLeastOne: false, out normalizedPermissions))
            return false;
        return true;
    }

    private static bool NormalizeIds(
        IReadOnlyCollection<string>? ids,
        bool requireAtLeastOne,
        out string[] normalized)
    {
        normalized = [];
        if (ids is null || ids.Count > MaxIdsPerGrant || (requireAtLeastOne && ids.Count == 0))
            return false;

        var values = ids.Select(id => id?.Trim()).ToArray();
        if (values.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > MaxIdLength))
            return false;
        normalized = values
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return !requireAtLeastOne || normalized.Length > 0;
    }

    private static bool TryValidateProcessOptions(McpServerProcessOptions options)
    {
        if (options.UserId == Guid.Empty ||
            options.WorkspaceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(options.ExtensionId) ||
            options.ExtensionId.Trim().Length > MaxExtensionIdLength ||
            !IsDigestPinnedImage(options.ImageReference) ||
            options.Arguments is null ||
            options.EnvironmentVariables is null ||
            options.Arguments.Count > 64 ||
            options.EnvironmentVariables.Count > 32)
            return false;

        var argumentBytes = options.Arguments.Sum(argument =>
            argument is null ? 0 : System.Text.Encoding.UTF8.GetByteCount(argument));
        if (argumentBytes > 32 * 1024)
            return false;

        var environmentBytes = 0;
        foreach (var pair in options.EnvironmentVariables)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) ||
                pair.Key.Length > 128 ||
                pair.Key.Contains('\0') ||
                pair.Key.Contains(' ') ||
                pair.Key.Contains('=') ||
                IsReservedEnvironmentVariable(pair.Key))
                return false;
            if (pair.Value is not null)
            {
                var valueBytes = System.Text.Encoding.UTF8.GetByteCount(pair.Value);
                if (valueBytes > 8192)
                    return false;
                environmentBytes += valueBytes;
            }
        }
        return environmentBytes <= 32 * 1024;
    }

    private static bool IsDigestPinnedImage(string? imageReference)
    {
        if (string.IsNullOrWhiteSpace(imageReference) ||
            imageReference.Length > 512 ||
            imageReference.Contains('\0') ||
            imageReference.IndexOf("@sha256:", StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        var digestSeparator = imageReference.IndexOf("@sha256:", StringComparison.OrdinalIgnoreCase);
        var digest = imageReference[(digestSeparator + "@sha256:".Length)..];
        if (digest.Length != 64)
            return false;
        return digest.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
    }

    private static bool IsReservedEnvironmentVariable(string name) =>
        name.Equals("PATH", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("HOME", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("USERPROFILE", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("APPDATA", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("LOCALAPPDATA", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("SYSTEMROOT", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("WINDIR", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("TEMP", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("TMP", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("COMSPEC", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("HTTP_PROXY", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("HTTPS_PROXY", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("ALL_PROXY", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("NO_PROXY", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("SSL_CERT_FILE", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("SSL_CERT_DIR", StringComparison.OrdinalIgnoreCase);
}
