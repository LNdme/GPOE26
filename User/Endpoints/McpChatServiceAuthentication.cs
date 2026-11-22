using GPOE26.Agent.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using User.Helper;

namespace User.Endpoints;

public static class McpChatServiceAuthentication
{
    private const string ServiceKeyConfigurationPath = "Mcp:ChatServiceKey";

    public static RouteGroupBuilder RequireMcpChatService(this RouteGroupBuilder group) =>
        group.AddEndpointFilter(async (context, next) =>
        {
            var expected = context.HttpContext.RequestServices
                .GetRequiredService<IConfiguration>()[ServiceKeyConfigurationPath];
            var request = context.HttpContext.Request;
            var pathAndQuery = $"{request.Path}{request.QueryString}";
            var timestamp = request.Headers[McpChatServiceProtocol.TimestampHeader].ToString();
            var signature = request.Headers[McpChatServiceProtocol.SignatureHeader].ToString();
            var userId = context.HttpContext.User.GetUserId();
            if (!McpChatServiceProtocol.IsValidSignature(
                    expected,
                    request.Method,
                    pathAndQuery,
                    userId ?? Guid.Empty,
                    timestamp,
                    signature,
                    DateTimeOffset.UtcNow))
                return Results.Unauthorized();

            return await next(context);
        });
}
