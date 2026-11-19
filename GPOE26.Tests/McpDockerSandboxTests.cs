using System.Diagnostics;
using GPOE26.Agent.Abstractions;
using GPOE26.Agent.Runtime.Mcp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GPOE26.Tests;

public sealed class McpDockerSandboxTests
{
    [Fact]
    public void DockerLaunchUsesHardenedDefaultsAndDoesNotPutSecretsInArguments()
    {
        var image = "registry.example/mcp@sha256:" + new string('a', 64);
        var options = ProcessOptions(image, new Dictionary<string, string?>
        {
            ["RESEARCH_API_KEY"] = "not-for-process-arguments"
        });
        var policy = new McpDockerImagePolicy
        {
            ImageReference = image,
            Command = ["node", "server.js"],
            AllowedEnvironmentVariables = ["RESEARCH_API_KEY"]
        };

        var succeeded = DockerMcpSandboxCommandBuilder.TryBuild(
            options,
            policy,
            "http://gpoe-mcp-egress-proxy:3128",
            out var launch,
            out var failure);

        Assert.True(succeeded, failure);
        Assert.NotNull(launch);
        Assert.Contains("--read-only", launch.Arguments);
        Assert.Contains("--cap-drop=ALL", launch.Arguments);
        Assert.Contains("--security-opt=no-new-privileges", launch.Arguments);
        Assert.Contains("--network", launch.Arguments);
        Assert.Contains("none", launch.Arguments);
        Assert.Contains("--pull=never", launch.Arguments);
        Assert.DoesNotContain("not-for-process-arguments", launch.Arguments);
        Assert.Equal("not-for-process-arguments", launch.EnvironmentVariables["RESEARCH_API_KEY"]);
    }

    [Fact]
    public void DockerLaunchRejectsMutableImageTags()
    {
        var mutableImage = "registry.example/mcp:latest";
        var options = ProcessOptions(mutableImage, new Dictionary<string, string?>());
        var policy = new McpDockerImagePolicy { ImageReference = mutableImage };

        var succeeded = DockerMcpSandboxCommandBuilder.TryBuild(
            options,
            policy,
            "http://gpoe-mcp-egress-proxy:3128",
            out _,
            out _);

        Assert.False(succeeded);
    }

    [Fact]
    public void DockerLaunchRejectsEnvironmentVariablesNotAllowedForTheImage()
    {
        var image = "registry.example/mcp@sha256:" + new string('a', 64);
        var options = ProcessOptions(image, new Dictionary<string, string?>
        {
            ["UNAPPROVED_SECRET"] = "value"
        });
        var policy = new McpDockerImagePolicy { ImageReference = image };

        var succeeded = DockerMcpSandboxCommandBuilder.TryBuild(
            options,
            policy,
            "http://gpoe-mcp-egress-proxy:3128",
            out _,
            out _);

        Assert.False(succeeded);
    }

    [Fact]
    public void DockerLaunchUsesOnlyTheInternalEgressNetworkAndForcesProxyVariables()
    {
        var image = "registry.example/mcp@sha256:" + new string('a', 64);
        var options = ProcessOptions(image, new Dictionary<string, string?>());
        var policy = new McpDockerImagePolicy
        {
            ImageReference = image,
            NetworkMode = "egress"
        };
        var otherOptions = ProcessOptions(image, new Dictionary<string, string?>());

        var succeeded = DockerMcpSandboxCommandBuilder.TryBuild(
            options,
            policy,
            "http://gpoe-mcp-egress-proxy:3128",
            out var launch,
            out var failure);

        Assert.True(succeeded, failure);
        Assert.NotNull(launch);
        var launchArguments = launch.Arguments.ToList();
        var networkName = launchArguments[
            launchArguments.IndexOf("--network") + 1];
        Assert.StartsWith("gpoe-mcp-", networkName);
        Assert.Equal(
            DockerMcpEgressNetworkManager.GetNetworkName(options),
            networkName);
        Assert.NotEqual(
            networkName,
            DockerMcpEgressNetworkManager.GetNetworkName(otherOptions));
        Assert.Equal("http://gpoe-mcp-egress-proxy:3128", launch.EnvironmentVariables["HTTP_PROXY"]);
        Assert.Equal("http://gpoe-mcp-egress-proxy:3128", launch.EnvironmentVariables["HTTPS_PROXY"]);
        Assert.Equal(string.Empty, launch.EnvironmentVariables["NO_PROXY"]);
    }

    [Theory]
    [InlineData("bridge")]
    [InlineData("host")]
    public void DockerLaunchRejectsUnrestrictedNetworkModes(string networkMode)
    {
        var image = "registry.example/mcp@sha256:" + new string('a', 64);
        var options = ProcessOptions(image, new Dictionary<string, string?>());
        var policy = new McpDockerImagePolicy
        {
            ImageReference = image,
            NetworkMode = networkMode
        };

        Assert.False(DockerMcpSandboxCommandBuilder.TryBuild(
            options,
            policy,
            "http://gpoe-mcp-egress-proxy:3128",
            out _,
            out _));
    }

    [Fact]
    public void DockerLaunchRejectsInvalidEgressProxyUrls()
    {
        var image = "registry.example/mcp@sha256:" + new string('a', 64);
        var options = ProcessOptions(image, new Dictionary<string, string?>());
        var policy = new McpDockerImagePolicy
        {
            ImageReference = image,
            NetworkMode = "egress"
        };

        Assert.False(DockerMcpSandboxCommandBuilder.TryBuild(
            options,
            policy,
            "http://user:password@gpoe-mcp-egress-proxy:3128/",
            out _,
            out _));
    }

    [Fact]
    public async Task DockerFactoryRefusesImagesThatAreNotAllowListed()
    {
        var factory = new DockerMcpSandboxClientFactory(
            Options.Create(new McpDockerSandboxOptions { DockerCommand = "missing-docker" }),
            new NoOpEgressNetworkManager(),
            NullLogger<DockerMcpSandboxClientFactory>.Instance,
            NullLoggerFactory.Instance);

        var client = await factory.CreateClientAsync(ProcessOptions(
            "registry.example/mcp@sha256:" + new string('a', 64),
            new Dictionary<string, string?>()));

        Assert.Null(client);
    }

    [Fact]
    public async Task DockerFactoryStartsTheLocalTestServerWhenEnabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("GPOE_RUN_DOCKER_MCP_TEST"),
                "1",
                StringComparison.Ordinal))
            return;

        var image = "gpoe/mcp-test@sha256:a54841ec3108c3fec04d59c986ffffaf077497f94c56d84679acef3ddf99370d";
        var policy = new McpDockerImagePolicy
        {
            ImageReference = image,
            Command = [],
            NetworkMode = "none",
            MemoryLimitMegabytes = 256,
            CpuLimitMilliCores = 1000,
            ProcessLimit = 64
        };
        var factory = new DockerMcpSandboxClientFactory(
            Options.Create(new McpDockerSandboxOptions
            {
                DockerCommand = "docker",
                AllowedImages = [policy]
            }),
            new NoOpEgressNetworkManager(),
            NullLogger<DockerMcpSandboxClientFactory>.Instance,
            NullLoggerFactory.Instance);
        await using var client = await factory.CreateClientAsync(ProcessOptions(
            image,
            new Dictionary<string, string?>()));

        Assert.NotNull(client);
        var tools = await client.ListToolsAsync();
        var testRead = tools.Single(tool => tool.Name == "test_read");
        Assert.True(testRead.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.True(testRead.ProtocolTool.Annotations?.DestructiveHint == false);
        var call = await client.CallToolAsync(
            "test_read",
            new Dictionary<string, object?>
            {
                ["value"] = "sandbox-ok"
            });
        Assert.True(call.IsError is not true);
        Assert.NotNull(call.Content);
    }

    [Fact]
    public async Task DockerFactoryStartsTheLocalTestServerOnItsIsolatedEgressNetworkWhenEnabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("GPOE_RUN_DOCKER_MCP_TEST"),
                "1",
                StringComparison.Ordinal))
            return;

        var image = "gpoe/mcp-test@sha256:a54841ec3108c3fec04d59c986ffffaf077497f94c56d84679acef3ddf99370d";
        var options = ProcessOptions(image, new Dictionary<string, string?>());
        var policy = new McpDockerImagePolicy
        {
            ImageReference = image,
            NetworkMode = "egress",
            MemoryLimitMegabytes = 256,
            CpuLimitMilliCores = 1000,
            ProcessLimit = 64
        };
        var sandboxOptions = Options.Create(new McpDockerSandboxOptions
        {
            DockerCommand = "docker",
            EgressProxyUrl = "http://gpoe-mcp-egress-proxy:3128",
            EgressProxyContainerName = "gpoe-mcp-egress-proxy",
            AllowedImages = [policy]
        });
        var factory = new DockerMcpSandboxClientFactory(
            sandboxOptions,
            new DockerMcpEgressNetworkManager(
                sandboxOptions,
                NullLogger<DockerMcpEgressNetworkManager>.Instance),
            NullLogger<DockerMcpSandboxClientFactory>.Instance,
            NullLoggerFactory.Instance);
        var networkName = DockerMcpEgressNetworkManager.GetNetworkName(options);

        try
        {
            await using var client = await factory.CreateClientAsync(options);

            Assert.NotNull(client);
            var call = await client.CallToolAsync(
                "test_read",
                new Dictionary<string, object?> { ["value"] = "egress-sandbox-ok" });
            Assert.True(call.IsError is not true);
        }
        finally
        {
            var inspection = await RunDockerAsync(
                ["network", "inspect", networkName]);
            if (inspection.ExitCode == 0)
            {
                var disconnect = await RunDockerAsync(
                    ["network", "disconnect", "--force", networkName, "gpoe-mcp-egress-proxy"]);
                Assert.Equal(0, disconnect.ExitCode);

                var removal = await RunDockerAsync(["network", "rm", networkName]);
                Assert.Equal(0, removal.ExitCode);
            }
        }
    }

    private static McpServerProcessOptions ProcessOptions(
        string image,
        IReadOnlyDictionary<string, string?> environmentVariables) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "research-mcp",
            image,
            [],
            environmentVariables);

    private sealed class NoOpEgressNetworkManager : IMcpDockerEgressNetworkManager
    {
        public Task EnsureNetworkAsync(
            McpServerProcessOptions options,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private static async Task<DockerCommandResult> RunDockerAsync(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start());
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new DockerCommandResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private sealed record DockerCommandResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
