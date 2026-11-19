using Microsoft.Extensions.Logging;

namespace GPOE26.Tests;

public class WebTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task GetWebResourceRootReturnsOkStatusCode()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.GPOE26_AppHost>(cancellationToken);
        // The smoke test does not call the LLM, but Aspire requires the secret parameter to be resolved.
        appHost.Configuration["Parameters:llm-apikey"] = "integration-test-key";
        appHost.Configuration["Parameters:mcp-chat-service-key"] =
            "integration-test-mcp-service-key-32-bytes-minimum";
        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            // Override the logging filters from the app's configuration
            logging.AddFilter(appHost.Environment.ApplicationName, LogLevel.Debug);
            logging.AddFilter("Aspire.", LogLevel.Debug);
            // To output logs to the xUnit.net ITestOutputHelper, consider adding a package from https://www.nuget.org/packages?q=xunit+logging
        });
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        // Act
        var httpClient = app.CreateHttpClient("webfrontend");
        await app.ResourceNotifications.WaitForResourceHealthyAsync("webfrontend", cancellationToken)
            .WaitAsync(DefaultTimeout, cancellationToken);
        var response = await httpClient.GetAsync("/", cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LearningUiMcpPagesAndCoreServicesBecomeHealthy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.GPOE26_AppHost>(cancellationToken);
        appHost.Configuration["Parameters:llm-apikey"] = "integration-test-key";
        appHost.Configuration["Parameters:mcp-chat-service-key"] =
            "integration-test-mcp-service-key-32-bytes-minimum";

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        foreach (var resourceName in new[] { "user", "chat", "webfrontend" })
        {
            await app.ResourceNotifications.WaitForResourceHealthyAsync(resourceName, cancellationToken)
                .WaitAsync(DefaultTimeout, cancellationToken);
        }

        var webClient = app.CreateHttpClient("webfrontend");
        foreach (var path in new[] { "/", "/login", "/app/mcp", "/app/admin/mcp" })
        {
            using var response = await webClient.GetAsync(path, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var userClient = app.CreateHttpClient("user");
        using var mcpMembersResponse = await userClient.GetAsync(
            "/workspaces/00000000-0000-0000-0000-000000000000/mcp/members",
            cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, mcpMembersResponse.StatusCode);

        var chatClient = app.CreateHttpClient("chat");
        using var mcpCatalogResponse = await chatClient.GetAsync("/tutor/mcp/extensions", cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, mcpCatalogResponse.StatusCode);
    }
}
