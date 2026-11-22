namespace GPOE26.Agent.Runtime.Mcp;

public sealed class McpDockerSandboxOptions
{
    public string DockerCommand { get; set; } = "docker";

    public string EgressProxyUrl { get; set; } = "http://gpoe-mcp-egress-proxy:3128";

    public string EgressProxyContainerName { get; set; } = "gpoe-mcp-egress-proxy";

    public List<McpDockerImagePolicy> AllowedImages { get; set; } = [];
}

public sealed class McpDockerImagePolicy
{
    public string ImageReference { get; set; } = string.Empty;

    public List<string> Command { get; set; } = [];

    public string NetworkMode { get; set; } = "none";

    public string? WorkingDirectory { get; set; }

    public List<string> AllowedEnvironmentVariables { get; set; } = [];

    public int MemoryLimitMegabytes { get; set; } = 256;

    public int CpuLimitMilliCores { get; set; } = 1000;

    public int ProcessLimit { get; set; } = 64;
}
