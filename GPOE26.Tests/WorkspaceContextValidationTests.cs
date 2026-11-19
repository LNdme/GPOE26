using System.Net;
using GPOE26.Workspaces.Abstractions;
using Microsoft.AspNetCore.Http;

namespace GPOE26.Tests;

public sealed class WorkspaceContextValidationTests
{
    [Fact]
    public async Task ValidateAsyncForwardsBearerTokenAndAcceptsCurrentContext()
    {
        string? forwardedAuthorization = null;
        using var http = new HttpClient(new StubHandler(request =>
        {
            forwardedAuthorization = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }))
        {
            BaseAddress = new Uri("http://user.test")
        };
        var accessor = CreateAccessor("Bearer signed-token");
        var validator = new UserApiWorkspaceContextValidator(http, accessor);

        var result = await validator.ValidateAsync(CancellationToken.None);

        Assert.Equal(WorkspaceContextValidationResult.Current, result);
        Assert.Equal("Bearer signed-token", forwardedAuthorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ValidateAsyncRejectsRevokedOrMismatchedContext(HttpStatusCode statusCode)
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(statusCode)))
        {
            BaseAddress = new Uri("http://user.test")
        };
        var validator = new UserApiWorkspaceContextValidator(http, CreateAccessor("Bearer signed-token"));

        var result = await validator.ValidateAsync(CancellationToken.None);

        Assert.Equal(WorkspaceContextValidationResult.Invalid, result);
    }

    [Fact]
    public async Task ValidateAsyncFailsClosedWhenUserServiceIsUnavailable()
    {
        using var http = new HttpClient(new StubHandler(_ => throw new HttpRequestException()))
        {
            BaseAddress = new Uri("http://user.test")
        };
        var validator = new UserApiWorkspaceContextValidator(http, CreateAccessor("Bearer signed-token"));

        var result = await validator.ValidateAsync(CancellationToken.None);

        Assert.Equal(WorkspaceContextValidationResult.Unavailable, result);
    }

    private static IHttpContextAccessor CreateAccessor(string authorization)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = authorization;
        return new HttpContextAccessor { HttpContext = context };
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
