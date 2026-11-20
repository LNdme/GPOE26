using System.Globalization;
using System.Text;
using GPOE26.Agent.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace GPOE26.Agent.Runtime.Mcp;

public sealed class DockerMcpSandboxClientFactory(
    IOptions<McpDockerSandboxOptions> sandboxOptions,
    IMcpDockerEgressNetworkManager egressNetworkManager,
    ILogger<DockerMcpSandboxClientFactory> logger,
    ILoggerFactory loggerFactory) : IMcpSandboxClientFactory
{
    public async Task<McpClient?> CreateClientAsync(
        McpServerProcessOptions options,
        CancellationToken cancellationToken = default)
    {
        var policy = sandboxOptions.Value.AllowedImages.FirstOrDefault(candidate =>
            string.Equals(candidate.ImageReference, options.ImageReference, StringComparison.Ordinal));

        if (policy is null)
        {
            logger.LogWarning(
                "Refused MCP sandbox launch for extension {ExtensionId}: the OCI image is not allow-listed",
                options.ExtensionId);
            return null;
        }

        if (!DockerMcpSandboxCommandBuilder.TryBuild(
                options,
                policy,
                sandboxOptions.Value.EgressProxyUrl,
                out var launch,
                out var failure))
        {
            logger.LogWarning(
                "Refused MCP sandbox launch for extension {ExtensionId}: {Failure}",
                options.ExtensionId,
                failure);
            return null;
        }

        if (policy.NetworkMode == "egress")
        {
            await egressNetworkManager.EnsureNetworkAsync(options, cancellationToken);
        }

        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        foreach (var (name, value) in launch!.EnvironmentVariables)
            environment[name] = value;

        var transport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Name = "docker-mcp-sandbox",
                Command = sandboxOptions.Value.DockerCommand,
                Arguments = launch.Arguments.ToList(),
                InheritEnvironmentVariables = false,
                EnvironmentVariables = environment
            },
            loggerFactory);

        return await McpClient.CreateAsync(
            transport,
            new McpClientOptions(),
            loggerFactory,
            cancellationToken);
    }
}

internal sealed record DockerMcpSandboxLaunchPlan(
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> EnvironmentVariables);

internal static class DockerMcpSandboxCommandBuilder
{
    private const int MaxArguments = 64;
    private const int MaxArgumentBytes = 32 * 1024;
    private const int MaxEnvironmentVariables = 32;
    private const int MaxEnvironmentValueBytes = 8192;
    private const int MaxEnvironmentBytes = 32 * 1024;

    private static readonly HashSet<string> ReservedEnvironmentVariables =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "PATH",
            "HOME",
            "USERPROFILE",
            "APPDATA",
            "LOCALAPPDATA",
            "SYSTEMROOT",
            "WINDIR",
            "TEMP",
            "TMP",
            "COMSPEC",
            "HTTP_PROXY",
            "HTTPS_PROXY",
            "ALL_PROXY",
            "NO_PROXY",
            "SSL_CERT_FILE",
            "SSL_CERT_DIR"
        };

    public static bool TryBuild(
        McpServerProcessOptions options,
        McpDockerImagePolicy policy,
        string egressProxyUrl,
        out DockerMcpSandboxLaunchPlan? launch,
        out string failure)
    {
        launch = null;
        failure = "The MCP container policy is invalid.";

        if (options.UserId == Guid.Empty ||
            options.WorkspaceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(options.ExtensionId) ||
            options.ExtensionId.Length > 200 ||
            !IsDigestPinnedImage(options.ImageReference) ||
            !string.Equals(options.ImageReference, policy.ImageReference, StringComparison.Ordinal) ||
            options.Arguments is null ||
            options.EnvironmentVariables is null ||
            !TryGetProxySettings(options, policy, egressProxyUrl, out var proxyEnvironment, out var networkName) ||
            !IsValidPolicy(policy))
        {
            return false;
        }

        if (!TryValidateArguments(policy.Command, out var commandBytes) ||
            !TryValidateArguments(options.Arguments, out var argumentBytes) ||
            commandBytes + argumentBytes > MaxArgumentBytes)
        {
            failure = "MCP container arguments exceed the configured limits.";
            return false;
        }

        if (!TryValidateEnvironment(options.EnvironmentVariables, policy, out var environment, out failure))
            return false;

        foreach (var (name, value) in proxyEnvironment)
            environment.Add(name, value);

        var arguments = new List<string>
        {
            "run",
            "--rm",
            "--interactive",
            "--pull=never",
            "--read-only",
            "--cap-drop=ALL",
            "--security-opt=no-new-privileges",
            "--user=65532:65532",
            "--network",
            networkName,
            "--memory",
            $"{policy.MemoryLimitMegabytes}m",
            "--cpus",
            (policy.CpuLimitMilliCores / 1000m).ToString("0.###", CultureInfo.InvariantCulture),
            "--pids-limit",
            policy.ProcessLimit.ToString(CultureInfo.InvariantCulture),
            "--tmpfs",
            "/tmp:rw,noexec,nosuid,nodev,size=16m"
        };

        if (policy.WorkingDirectory is not null)
        {
            arguments.Add("--workdir");
            arguments.Add(policy.WorkingDirectory);
        }

        foreach (var name in environment.Keys)
        {
            arguments.Add("--env");
            arguments.Add(name);
        }

        arguments.Add(options.ImageReference);
        arguments.AddRange(policy.Command);
        arguments.AddRange(options.Arguments);

        launch = new DockerMcpSandboxLaunchPlan(arguments, environment);
        failure = string.Empty;
        return true;
    }

    private static bool IsValidPolicy(McpDockerImagePolicy policy) =>
        IsDigestPinnedImage(policy.ImageReference) &&
        (policy.NetworkMode is "none" or "egress") &&
        (policy.MemoryLimitMegabytes is >= 64 and <= 2048) &&
        (policy.CpuLimitMilliCores is >= 100 and <= 4000) &&
        (policy.ProcessLimit is >= 16 and <= 256) &&
        (policy.WorkingDirectory is null || IsContainerPath(policy.WorkingDirectory)) &&
        policy.Command is not null &&
        policy.AllowedEnvironmentVariables is not null &&
        policy.AllowedEnvironmentVariables.Count <= MaxEnvironmentVariables &&
        policy.AllowedEnvironmentVariables.All(IsAllowedEnvironmentVariableName);

    private static bool TryGetProxySettings(
        McpServerProcessOptions options,
        McpDockerImagePolicy policy,
        string proxyUrl,
        out Dictionary<string, string> environment,
        out string networkName)
    {
        environment = new Dictionary<string, string>(StringComparer.Ordinal);
        networkName = policy.NetworkMode;
        if (policy.NetworkMode == "none")
            return true;

        if (!Uri.TryCreate(proxyUrl, UriKind.Absolute, out var proxy) ||
            proxy.Scheme != Uri.UriSchemeHttp ||
            !string.Equals(proxy.Host, "gpoe-mcp-egress-proxy", StringComparison.OrdinalIgnoreCase) ||
            proxy.Port != 3128 ||
            proxy.UserInfo.Length != 0 ||
            proxy.Query.Length != 0 ||
            proxy.Fragment.Length != 0 ||
            (proxy.AbsolutePath != "/" && proxy.AbsolutePath.Length != 0))
            return false;

        networkName = DockerMcpEgressNetworkManager.GetNetworkName(options);
        var value = proxy.ToString().TrimEnd('/');
        environment["HTTP_PROXY"] = value;
        environment["HTTPS_PROXY"] = value;
        environment["http_proxy"] = value;
        environment["https_proxy"] = value;
        environment["NO_PROXY"] = string.Empty;
        environment["no_proxy"] = string.Empty;
        return true;
    }

    private static bool TryValidateArguments(IReadOnlyList<string> arguments, out int totalBytes)
    {
        totalBytes = 0;
        if (arguments.Count > MaxArguments)
            return false;

        foreach (var argument in arguments)
        {
            if (argument is null || argument.Contains('\0'))
                return false;

            totalBytes += Encoding.UTF8.GetByteCount(argument);
            if (totalBytes > MaxArgumentBytes)
                return false;
        }

        return true;
    }

    private static bool TryValidateEnvironment(
        IReadOnlyDictionary<string, string?> requested,
        McpDockerImagePolicy policy,
        out Dictionary<string, string> environment,
        out string failure)
    {
        environment = new Dictionary<string, string>(StringComparer.Ordinal);
        failure = "An MCP environment variable is not authorized by the image policy.";
        if (requested.Count > MaxEnvironmentVariables)
        {
            failure = "MCP environment variables exceed the configured limits.";
            return false;
        }

        var allowed = new HashSet<string>(policy.AllowedEnvironmentVariables, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalBytes = 0;
        foreach (var (name, value) in requested)
        {
            if (!IsAllowedEnvironmentVariableName(name) ||
                ReservedEnvironmentVariables.Contains(name) ||
                name.StartsWith("DOCKER_", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("LD_", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("DYLD_", StringComparison.OrdinalIgnoreCase) ||
                !allowed.Contains(name) ||
                !seen.Add(name) ||
                value is null ||
                value.Contains('\0') ||
                value.Contains('\r') ||
                value.Contains('\n'))
            {
                return false;
            }

            var valueBytes = Encoding.UTF8.GetByteCount(value);
            totalBytes += Encoding.UTF8.GetByteCount(name) + valueBytes;
            if (valueBytes > MaxEnvironmentValueBytes || totalBytes > MaxEnvironmentBytes)
            {
                failure = "MCP environment variables exceed the configured limits.";
                return false;
            }

            environment.Add(name, value);
        }

        failure = string.Empty;
        return true;
    }

    private static bool IsAllowedEnvironmentVariableName(string name) =>
        !string.IsNullOrEmpty(name) &&
        (char.IsAsciiLetter(name[0]) || name[0] == '_') &&
        name.Skip(1).All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static bool IsDigestPinnedImage(string? imageReference)
    {
        if (string.IsNullOrWhiteSpace(imageReference))
            return false;

        var digestSeparator = imageReference.LastIndexOf("@sha256:", StringComparison.Ordinal);
        return digestSeparator > 0 &&
               imageReference.Length == digestSeparator + 8 + 64 &&
               imageReference[..digestSeparator].All(character =>
                   char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or '/' or ':') &&
               imageReference[(digestSeparator + 8)..].All(Uri.IsHexDigit);
    }

    private static bool IsContainerPath(string path) =>
        path.Length <= 256 &&
        path.StartsWith("/", StringComparison.Ordinal) &&
        !path.Contains('\0') &&
        !path.Split('/').Any(segment => segment is "." or "..");
}
