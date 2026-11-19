using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using GPOE26.Agent.Abstractions;
using GPOE26.Workspaces.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace GPOE26.Agent.Runtime.Mcp;

public sealed class ReadOnlyMcpProcessAdapter(
    IAgentExtensionRegistry registry,
    IMcpPermissionGrantStore grants,
    IMcpServerProcessOptionsStore processOptions,
    IWorkspaceContextValidator workspaceValidator,
    IHttpContextAccessor httpContextAccessor,
    ILogger<ReadOnlyMcpProcessAdapter> logger,
    TimeSpan? timeout = null,
    int maxSessions = 32,
    int maxRequestBytes = 256 * 1024,
    int maxResponseBytes = 1024 * 1024,
    IMcpSandboxClientFactory? sandboxClientFactory = null) : IMcpReadOnlyAdapter, IAsyncDisposable
{
    private readonly McpReadOnlyPolicy _policy = new();
    private readonly Dictionary<McpSessionKey, McpServerSession> _sessions = [];
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private readonly TimeSpan _timeout = timeout is null || timeout > TimeSpan.Zero
        ? timeout ?? TimeSpan.FromSeconds(30)
        : throw new ArgumentOutOfRangeException(nameof(timeout));
    private readonly int _maxSessions = maxSessions > 0
        ? maxSessions
        : throw new ArgumentOutOfRangeException(nameof(maxSessions));
    private readonly int _maxRequestBytes = maxRequestBytes > 0
        ? maxRequestBytes
        : throw new ArgumentOutOfRangeException(nameof(maxRequestBytes));
    private readonly int _maxResponseBytes = maxResponseBytes > 0
        ? maxResponseBytes
        : throw new ArgumentOutOfRangeException(nameof(maxResponseBytes));

    public async Task<McpCapabilityListingResult> ListApprovedCapabilitiesAsync(
        ClaimsPrincipal caller,
        string extensionId,
        CancellationToken cancellationToken = default)
    {
        var (identity, failure) = await AuthenticateAsync(caller, extensionId, cancellationToken);
        if (failure is not null)
            return new(false, failure.Error, []);

        extensionId = identity!.ExtensionId;
        var session = await GetSessionAsync(identity, extensionId, cancellationToken);
        if (session is null)
            return new(false, "The MCP server process could not be started.", []);

        var manifest = GetManifest(extensionId);
        var capabilities = manifest!.Capabilities
            .Where(capability => _policy.TryAuthorize(
                manifest,
                identity!.Grant,
                capability.Id,
                out _,
                out _) && session.Tools.ContainsKey(capability.Id))
            .ToArray();
        return new(true, null, capabilities);
    }

    public async Task<McpReadOnlyResult> ReadAsync(
        ClaimsPrincipal caller,
        string extensionId,
        string capabilityId,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        var (identity, failure) = await AuthenticateAsync(caller, extensionId, cancellationToken);
        if (failure is not null)
            return failure;

        extensionId = identity!.ExtensionId;
        var manifest = GetManifest(extensionId);
        if (manifest is null)
            return Denied("The MCP server is not registered.");
        if (!_policy.TryAuthorize(manifest!, identity!.Grant, capabilityId, out _, out var policyError))
        {
            LogCall(identity, extensionId, capabilityId, "denied", started);
            return Denied(policyError ?? "The capability is not approved.");
        }
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            LogCall(identity, extensionId, capabilityId, "invalid_arguments", started);
            return Denied("MCP tool arguments must be a JSON object.");
        }

        var argumentBytes = JsonSerializer.SerializeToUtf8Bytes(arguments);
        if (argumentBytes.Length > _maxRequestBytes)
        {
            LogCall(identity, extensionId, capabilityId, "request_too_large", started);
            return Denied("MCP tool arguments exceed the configured size limit.");
        }

        var session = await GetSessionAsync(identity, extensionId, cancellationToken);
        if (session is null ||
            !session.Tools.TryGetValue(capabilityId, out var tool))
        {
            LogCall(identity, extensionId, capabilityId, "tool_unavailable", started);
            return Denied("The approved capability is not available from the MCP server.");
        }

        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(_timeout);
        try
        {
            var input = JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentBytes)
                ?? new Dictionary<string, object?>();
            var result = await tool.CallAsync(input, cancellationToken: requestTimeout.Token);
            var resultJson = JsonSerializer.SerializeToElement(result);
            if (JsonSerializer.SerializeToUtf8Bytes(resultJson).Length > _maxResponseBytes)
            {
                LogCall(identity, extensionId, capabilityId, "response_too_large", started);
                return new McpReadOnlyResult(true, false, "The MCP result exceeds the configured size limit.", null);
            }

            var succeeded = result.IsError != true;
            LogCall(identity, extensionId, capabilityId, succeeded ? "succeeded" : "tool_error", started);
            return new McpReadOnlyResult(
                true,
                succeeded,
                succeeded ? null : "The MCP tool reported an error.",
                resultJson);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await RemoveSessionAsync(
                new McpSessionKey(identity.UserId, identity.WorkspaceId, extensionId),
                CancellationToken.None);
            LogCall(identity, extensionId, capabilityId, "timed_out", started);
            return new McpReadOnlyResult(true, false, "The MCP read timed out.", null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RemoveSessionAsync(
                new McpSessionKey(identity.UserId, identity.WorkspaceId, extensionId),
                CancellationToken.None);
            logger.LogWarning(
                "MCP call failed for extension {ExtensionId}, capability {CapabilityId}, user {UserId}, workspace {WorkspaceId} ({FailureType})",
                extensionId,
                capabilityId,
                identity.UserId,
                identity.WorkspaceId,
                exception.GetType().Name);
            LogCall(identity, extensionId, capabilityId, "failed", started);
            return new McpReadOnlyResult(true, false, "The MCP server call failed.", null);
        }
    }

    private async Task<(McpCallerIdentity? Identity, McpReadOnlyResult? Failure)> AuthenticateAsync(
        ClaimsPrincipal caller,
        string extensionId,
        CancellationToken cancellationToken)
    {
        var requestCaller = httpContextAccessor.HttpContext?.User;
        if (!TryGetCallerIdentity(caller, out var userId, out var workspaceId) ||
            !TryGetCallerIdentity(requestCaller, out var requestUserId, out var requestWorkspaceId) ||
            userId != requestUserId ||
            workspaceId != requestWorkspaceId)
        {
            logger.LogWarning("Denied MCP access for an unauthenticated or invalid caller");
            return (null, Denied("An authenticated user with an active workspace is required."));
        }
        var manifest = GetManifest(extensionId);
        if (manifest is null)
            return (null, Denied("The MCP server is not registered."));
        extensionId = manifest.Id;

        var validation = await workspaceValidator.ValidateAsync(cancellationToken);
        if (validation != WorkspaceContextValidationResult.Current)
        {
            await RemoveSessionAsync(new McpSessionKey(userId, workspaceId, extensionId), cancellationToken);
            logger.LogWarning(
                "Denied MCP access for user {UserId}, workspace {WorkspaceId}: workspace validation {ValidationResult}",
                userId,
                workspaceId,
                validation);
            return (null, Denied(validation == WorkspaceContextValidationResult.Unavailable
                ? "The active workspace could not be validated."
                : "The active workspace is no longer valid."));
        }

        var grant = await grants.GetApprovedGrantAsync(
            userId,
            workspaceId,
            extensionId,
            cancellationToken);
        if (grant is null ||
            grant.UserId != userId ||
            grant.WorkspaceId != workspaceId ||
            !string.Equals(grant.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase))
        {
            await RemoveSessionAsync(new McpSessionKey(userId, workspaceId, extensionId), cancellationToken);
            logger.LogInformation(
                "Denied MCP access: no matching approved grant for extension {ExtensionId}, user {UserId}, workspace {WorkspaceId}",
                extensionId,
                userId,
                workspaceId);
            return (null, Denied("No explicit permission grant exists for this user and workspace."));
        }

        return (new McpCallerIdentity(userId, workspaceId, extensionId, grant), null);
    }

    private async Task<McpServerSession?> GetSessionAsync(
        McpCallerIdentity identity,
        string extensionId,
        CancellationToken cancellationToken)
    {
        var key = new McpSessionKey(identity.UserId, identity.WorkspaceId, extensionId);
        await _sessionLock.WaitAsync(cancellationToken);
        McpClient? client = null;
        try
        {
            if (_sessions.TryGetValue(key, out var existing))
                return existing;
            if (_sessions.Count >= _maxSessions)
            {
                logger.LogWarning(
                    "MCP process session limit reached for user {UserId}, workspace {WorkspaceId}",
                    identity.UserId,
                    identity.WorkspaceId);
                return null;
            }
            var options = await processOptions.GetOptionsAsync(
                identity.UserId,
                identity.WorkspaceId,
                extensionId,
                cancellationToken);
            if (options is null ||
                options.UserId != identity.UserId ||
                options.WorkspaceId != identity.WorkspaceId ||
                !string.Equals(options.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "MCP process configuration is missing or not scoped to extension {ExtensionId}, user {UserId}, workspace {WorkspaceId}",
                    extensionId,
                    identity.UserId,
                    identity.WorkspaceId);
                return null;
            }
            if (sandboxClientFactory is null)
            {
                logger.LogWarning(
                    "MCP extension {ExtensionId} was not started because no sandbox provider is configured",
                    extensionId);
                return null;
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_timeout);
            client = await sandboxClientFactory.CreateClientAsync(options, timeoutSource.Token);
            if (client is null)
            {
                logger.LogWarning(
                    "MCP sandbox provider refused to start extension {ExtensionId}",
                    extensionId);
                return null;
            }

            var tools = await client.ListToolsAsync(cancellationToken: timeoutSource.Token);
            var manifest = GetManifest(extensionId);
            var toolMap = new Dictionary<string, McpClientTool>(StringComparer.Ordinal);
            foreach (var capability in manifest!.Capabilities)
            {
                var serverTool = tools.FirstOrDefault(tool =>
                    string.Equals(tool.Name, capability.Id, StringComparison.Ordinal));
                if (serverTool is null ||
                    serverTool.ProtocolTool.Annotations?.ReadOnlyHint != true ||
                    serverTool.ProtocolTool.Annotations.DestructiveHint == true ||
                    !MatchesDeclaredSchema(capability, serverTool))
                    continue;
                toolMap.Add(capability.Id, serverTool);
            }

            var session = new McpServerSession(client, toolMap);
            _sessions.Add(key, session);
            client = null;
            logger.LogInformation(
                "Started isolated MCP process session for extension {ExtensionId}, user {UserId}, workspace {WorkspaceId}; {ToolCount} declared tools matched",
                extensionId,
                identity.UserId,
                identity.WorkspaceId,
                toolMap.Count);
            return session;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (client is not null)
                await client.DisposeAsync();
            logger.LogWarning("MCP process initialization timed out for extension {ExtensionId}", extensionId);
            return null;
        }
        catch (OperationCanceledException)
        {
            if (client is not null)
                await client.DisposeAsync();
            throw;
        }
        catch (Exception exception)
        {
            if (client is not null)
                await client.DisposeAsync();
            logger.LogWarning(
                "MCP process initialization failed for extension {ExtensionId} ({FailureType})",
                extensionId,
                exception.GetType().Name);
            return null;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private static bool MatchesDeclaredSchema(AgentExtensionCapability capability, McpClientTool tool)
    {
        if (string.IsNullOrWhiteSpace(capability.InputSchemaJson))
            return false;

        try
        {
            using var declared = JsonDocument.Parse(capability.InputSchemaJson);
            return JsonElement.DeepEquals(declared.RootElement, tool.ProtocolTool.InputSchema);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private AgentExtensionManifest? GetManifest(string extensionId) =>
        registry.TryGet(extensionId, out var manifest) &&
        manifest.Kind == AgentExtensionKind.McpServer
            ? manifest
            : null;

    private static bool TryGetCallerIdentity(
        ClaimsPrincipal? caller,
        out Guid userId,
        out Guid workspaceId)
    {
        userId = default;
        workspaceId = default;
        return caller.Identity?.IsAuthenticated == true &&
               Guid.TryParse(
                   caller.FindFirstValue("sub") ?? caller.FindFirstValue(ClaimTypes.NameIdentifier),
                   out userId) &&
               caller.TryGetWorkspaceContext(out var workspace) &&
               (workspaceId = workspace!.WorkspaceId) != Guid.Empty;
    }

    private async Task RemoveSessionAsync(McpSessionKey key, CancellationToken cancellationToken)
    {
        McpServerSession? session = null;
        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            if (_sessions.Remove(key, out var removed))
                session = removed;
        }
        finally
        {
            _sessionLock.Release();
        }
        if (session is not null)
            await session.DisposeAsync();
    }

    private void LogCall(
        McpCallerIdentity identity,
        string extensionId,
        string capabilityId,
        string outcome,
        long started)
    {
        logger.LogInformation(
            "MCP capability {CapabilityId} on extension {ExtensionId} {Outcome} for user {UserId}, workspace {WorkspaceId} in {ElapsedMs} ms",
            capabilityId,
            extensionId,
            outcome,
            identity.UserId,
            identity.WorkspaceId,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private static McpReadOnlyResult Denied(string error) =>
        new(false, false, error, null);

    public async ValueTask DisposeAsync()
    {
        await _sessionLock.WaitAsync();
        try
        {
            foreach (var session in _sessions.Values)
                await session.DisposeAsync();
            _sessions.Clear();
        }
        finally
        {
            _sessionLock.Release();
            _sessionLock.Dispose();
        }
    }

    private sealed record McpCallerIdentity(
        Guid UserId,
        Guid WorkspaceId,
        string ExtensionId,
        McpPermissionGrant Grant);

    private sealed record McpSessionKey(Guid UserId, Guid WorkspaceId, string ExtensionId);

    private sealed record McpServerSession(
        McpClient Client,
        IReadOnlyDictionary<string, McpClientTool> Tools) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Client.DisposeAsync();
    }
}
