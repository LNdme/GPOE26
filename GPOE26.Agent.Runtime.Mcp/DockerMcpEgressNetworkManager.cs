using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GPOE26.Agent.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GPOE26.Agent.Runtime.Mcp;

public interface IMcpDockerEgressNetworkManager
{
    Task EnsureNetworkAsync(
        McpServerProcessOptions options,
        CancellationToken cancellationToken = default);
}

public sealed class DockerMcpEgressNetworkManager(
    IOptions<McpDockerSandboxOptions> sandboxOptions,
    ILogger<DockerMcpEgressNetworkManager> logger) : IMcpDockerEgressNetworkManager
{
    private const string ManagedNetworkLabel = "gpoe.mcp.sandbox";

    public async Task EnsureNetworkAsync(
        McpServerProcessOptions options,
        CancellationToken cancellationToken = default)
    {
        var networkName = GetNetworkName(options);
        var proxyContainerName = sandboxOptions.Value.EgressProxyContainerName;
        var inspection = await RunDockerAsync(
            ["network", "inspect", "--format", "{{json .}}", networkName],
            cancellationToken);

        if (inspection.ExitCode != 0)
        {
            var creation = await RunDockerAsync(
                [
                    "network", "create",
                    "--driver", "bridge",
                    "--internal",
                    "--label", $"{ManagedNetworkLabel}=true",
                    networkName
                ],
                cancellationToken);

            if (creation.ExitCode != 0)
            {
                inspection = await RunDockerAsync(
                    ["network", "inspect", "--format", "{{json .}}", networkName],
                    cancellationToken);
                if (inspection.ExitCode != 0)
                    throw new InvalidOperationException(
                        $"Could not create or inspect the isolated MCP network '{networkName}': {creation.StandardError.Trim()}");
            }
            else
            {
                inspection = await RunDockerAsync(
                    ["network", "inspect", "--format", "{{json .}}", networkName],
                    cancellationToken);
                if (inspection.ExitCode != 0)
                    throw new InvalidOperationException(
                        $"Created MCP network '{networkName}' but could not inspect it: {inspection.StandardError.Trim()}");
            }
        }

        if (!TryReadManagedNetwork(inspection.StandardOutput, networkName, proxyContainerName, out var proxyConnected))
            throw new InvalidOperationException(
                $"Docker network '{networkName}' is not an isolated GPOE26 MCP network.");

        if (proxyConnected)
            return;

        var connection = await RunDockerAsync(
            [
                "network", "connect",
                "--alias", "gpoe-mcp-egress-proxy",
                networkName,
                proxyContainerName
            ],
            cancellationToken);
        if (connection.ExitCode == 0)
            return;

        inspection = await RunDockerAsync(
            ["network", "inspect", "--format", "{{json .}}", networkName],
            cancellationToken);
        if (inspection.ExitCode == 0 &&
            TryReadManagedNetwork(inspection.StandardOutput, networkName, proxyContainerName, out proxyConnected) &&
            proxyConnected)
            return;

        logger.LogError(
            "Could not connect the MCP egress proxy {ProxyContainer} to network {Network}: {Failure}",
            proxyContainerName,
            networkName,
            connection.StandardError.Trim());
        throw new InvalidOperationException(
            $"Could not connect the MCP egress proxy to isolated network '{networkName}': {connection.StandardError.Trim()}");
    }

    internal static string GetNetworkName(McpServerProcessOptions options)
    {
        var identity = $"{options.UserId:N}|{options.WorkspaceId:N}|{options.ExtensionId}";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return $"gpoe-mcp-{digest[..32].ToLowerInvariant()}";
    }

    private async Task<DockerCommandResult> RunDockerAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = sandboxOptions.Value.DockerCommand,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("The Docker CLI could not be started.");

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        return new DockerCommandResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static bool TryReadManagedNetwork(
        string json,
        string expectedName,
        string proxyContainerName,
        out bool proxyConnected)
    {
        proxyConnected = false;
        using var document = JsonDocument.Parse(json);
        var network = document.RootElement;
        if (!network.TryGetProperty("Name", out var name) ||
            !string.Equals(name.GetString(), expectedName, StringComparison.Ordinal) ||
            !network.TryGetProperty("Driver", out var driver) ||
            !string.Equals(driver.GetString(), "bridge", StringComparison.Ordinal) ||
            !network.TryGetProperty("Internal", out var internalNetwork) ||
            internalNetwork.ValueKind != JsonValueKind.True ||
            !network.TryGetProperty("Labels", out var labels) ||
            labels.ValueKind != JsonValueKind.Object ||
            !labels.TryGetProperty(ManagedNetworkLabel, out var label) ||
            !string.Equals(label.GetString(), "true", StringComparison.Ordinal))
            return false;

        if (network.TryGetProperty("Containers", out var containers) &&
            containers.ValueKind == JsonValueKind.Object)
        {
            proxyConnected = containers.EnumerateObject().Any(container =>
                container.Value.TryGetProperty("Name", out var containerName) &&
                string.Equals(containerName.GetString(), proxyContainerName, StringComparison.Ordinal));
        }

        return true;
    }

    private sealed record DockerCommandResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
