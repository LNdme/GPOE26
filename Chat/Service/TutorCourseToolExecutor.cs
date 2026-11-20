using System.Security.Claims;
using System.Text;
using System.Text.Json;
using GPOE26.Agent.Abstractions;
using Microsoft.Extensions.Logging;

namespace Chat.Service;

public sealed class TutorCourseToolContext
{
    private Guid? _userId;
    private CourseRef? _course;

    public void AuthorizeCourse(Guid userId, CourseRef course)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("A valid user is required.", nameof(userId));
        if (course.Id == Guid.Empty)
            throw new ArgumentException("A valid course is required.", nameof(course));
        if (_course is not null)
            throw new InvalidOperationException("The current course scope has already been authorized.");

        _userId = userId;
        _course = course;
    }

    internal bool TryGetCourse(Guid userId, out CourseRef? course)
    {
        course = _course;
        return _userId == userId && course is not null;
    }
}

public sealed class TutorCourseToolExecutor(
    TutorCourseToolContext context,
    IHttpContextAccessor httpContextAccessor,
    IMcpReadOnlyAdapter? mcpAdapter = null,
    IAgentExtensionRegistry? extensionRegistry = null,
    ILogger<TutorCourseToolExecutor>? logger = null) : IAgentToolExecutor
{
    private const int MaxMcpToolArgumentsBytes = 256 * 1024;

    private static readonly AgentToolDefinition ReadCurrentCourse = new(
        "cours.read_current",
        "Lit le contenu du cours actuel, déjà autorisé pour l'élève.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    private IReadOnlyList<AgentToolDefinition> _tools = [ReadCurrentCourse];
    private IReadOnlyDictionary<string, McpToolBinding> _mcpBindings =
        new Dictionary<string, McpToolBinding>(StringComparer.Ordinal);
    private bool _mcpToolsPrepared;

    public IReadOnlyList<AgentToolDefinition> Tools => _tools;

    public async Task PrepareMcpToolsAsync(CancellationToken cancellationToken)
    {
        if (_mcpToolsPrepared)
            return;

        _mcpToolsPrepared = true;
        if (mcpAdapter is null || extensionRegistry is null)
            return;

        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            return;

        var bindings = new Dictionary<string, McpToolBinding>(StringComparer.Ordinal);
        foreach (var manifest in extensionRegistry.Manifests.Where(
                     item => item.Kind == AgentExtensionKind.McpServer))
        {
            McpCapabilityListingResult listing;
            try
            {
                listing = await mcpAdapter.ListApprovedCapabilitiesAsync(
                    principal,
                    manifest.Id,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger?.LogWarning(
                    "MCP capability discovery timed out for extension {ExtensionId}; continuing without it",
                    manifest.Id);
                continue;
            }
            catch (HttpRequestException exception)
            {
                logger?.LogWarning(
                    "MCP capability discovery failed for extension {ExtensionId} ({FailureType}); continuing without it",
                    manifest.Id,
                    exception.GetType().Name);
                continue;
            }

            if (!listing.Succeeded)
                continue;

            foreach (var capability in listing.Capabilities)
            {
                var target = $"{manifest.Id}:{capability.Id}";
                bindings.TryAdd(target, new McpToolBinding(manifest.Id, capability));
            }
        }

        if (bindings.Count == 0)
            return;

        _mcpBindings = bindings;
        var approvedCapabilities = string.Join(
            Environment.NewLine,
            bindings.Select(pair => $"{pair.Key}: {pair.Value.Capability.Description}"));
        var mcpTool = new AgentToolDefinition(
            AgentToolNames.McpRead,
            "Lit une capacité MCP en lecture seule explicitement approuvée. " +
            "Cibles autorisées pour cette requête : " + approvedCapabilities,
            BuildMcpInputSchema(bindings.Keys));
        _tools = [ReadCurrentCourse, mcpTool];
    }

    public Task<AgentToolResult> ExecuteAsync(AgentToolCall call, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.Equals(call.Name, AgentToolNames.McpRead, StringComparison.Ordinal))
            return ExecuteMcpReadAsync(call, cancellationToken);

        if (!string.Equals(call.Name, ReadCurrentCourse.Name, StringComparison.Ordinal))
            return Task.FromResult(new AgentToolResult("Outil non autorisé.", IsError: true));

        if (!HasNoArguments(call.ArgumentsJson))
            return Task.FromResult(new AgentToolResult("Cet outil n'accepte aucun argument.", IsError: true));

        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(
                principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier),
                out var userId) ||
            !context.TryGetCourse(userId, out var course))
        {
            return Task.FromResult(new AgentToolResult("Le cours actuel n'est pas autorisé.", IsError: true));
        }

        return Task.FromResult(new AgentToolResult(course!.FullText()));
    }

    private async Task<AgentToolResult> ExecuteMcpReadAsync(
        AgentToolCall call,
        CancellationToken cancellationToken)
    {
        if (mcpAdapter is null ||
            !_mcpBindings.Any() ||
            !TryParseMcpArguments(call.ArgumentsJson, out var target, out var arguments) ||
            !_mcpBindings.TryGetValue(target, out var binding))
        {
            return new AgentToolResult("La capacité MCP n'est pas autorisée pour cette requête.", IsError: true);
        }

        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            return new AgentToolResult("Une identité authentifiée est requise.", IsError: true);

        try
        {
            var result = await mcpAdapter.ReadAsync(
                principal,
                binding.ExtensionId,
                binding.Capability.Id,
                arguments,
                cancellationToken);
            if (!result.Allowed || !result.Succeeded)
                return new AgentToolResult(result.Error ?? "La lecture MCP a été refusée.", IsError: true);

            return new AgentToolResult(result.Data?.GetRawText() ?? "null");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(
                "MCP read timed out for extension {ExtensionId}, capability {CapabilityId}",
                binding.ExtensionId,
                binding.Capability.Id);
            return new AgentToolResult("La lecture MCP a dépassé le délai autorisé.", IsError: true);
        }
        catch (HttpRequestException exception)
        {
            logger?.LogWarning(
                "MCP authorization check failed for extension {ExtensionId}, capability {CapabilityId} ({FailureType})",
                binding.ExtensionId,
                binding.Capability.Id,
                exception.GetType().Name);
            return new AgentToolResult("L'autorisation MCP n'a pas pu être revalidée.", IsError: true);
        }
    }

    private static string BuildMcpInputSchema(IEnumerable<string> targets) =>
        JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["target"] = new
                {
                    type = "string",
                    description = "Capacité approuvée à lire.",
                    @enum = targets.ToArray()
                },
                ["arguments"] = new
                {
                    type = "object",
                    description = "Arguments de la capacité sélectionnée."
                }
            },
            required = new[] { "target", "arguments" },
            additionalProperties = false
        });

    private static bool TryParseMcpArguments(
        string json,
        out string target,
        out JsonElement arguments)
    {
        target = string.Empty;
        arguments = default;
        if (Encoding.UTF8.GetByteCount(json) > MaxMcpToolArgumentsBytes)
            return false;

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Count() != 2 ||
                !root.TryGetProperty("target", out var targetElement) ||
                targetElement.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("arguments", out var argumentsElement) ||
                argumentsElement.ValueKind != JsonValueKind.Object)
                return false;

            target = targetElement.GetString() ?? string.Empty;
            arguments = argumentsElement.Clone();
            return target.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasNoArguments(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   !document.RootElement.EnumerateObject().Any();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record McpToolBinding(string ExtensionId, AgentExtensionCapability Capability);
}
