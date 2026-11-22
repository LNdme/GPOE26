using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<GpoeMcpTestTools>();

await builder.Build().RunAsync();

[McpServerToolType]
public sealed class GpoeMcpTestTools
{
    [McpServerTool(
        Name = "test_read",
        Title = "Lecture locale de test",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [System.ComponentModel.Description("Retourne une valeur locale de test sans réseau ni accès disque.")]
    public static object TestRead(
        [System.ComponentModel.Description("Valeur à retourner.")]
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("The value is required.", nameof(value));
        if (value.Length > 1024)
            throw new ArgumentException("The value is too long.", nameof(value));

        return new { status = "ok", value };
    }
}
