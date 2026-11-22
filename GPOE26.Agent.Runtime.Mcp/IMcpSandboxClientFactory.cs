using GPOE26.Agent.Abstractions;
using ModelContextProtocol.Client;

namespace GPOE26.Agent.Runtime.Mcp;

/// <summary>
/// Creates MCP clients whose server processes are isolated by an OS or hypervisor boundary.
/// Implementations must not launch the configured executable directly on the application host.
/// </summary>
public interface IMcpSandboxClientFactory
{
    Task<McpClient?> CreateClientAsync(
        McpServerProcessOptions options,
        CancellationToken cancellationToken = default);
}
