using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GPOE26.Workspaces.Abstractions;

public enum WorkspaceContextValidationResult
{
    Current,
    Invalid,
    Unavailable
}

public interface IWorkspaceContextValidator
{
    Task<WorkspaceContextValidationResult> ValidateAsync(CancellationToken cancellationToken);
}

public sealed class UserApiWorkspaceContextValidator(
    HttpClient http,
    IHttpContextAccessor httpContextAccessor) : IWorkspaceContextValidator
{
    public async Task<WorkspaceContextValidationResult> ValidateAsync(CancellationToken cancellationToken)
    {
        var authorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!AuthenticationHeaderValue.TryParse(authorization, out var authorizationHeader) ||
            !string.Equals(authorizationHeader.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceContextValidationResult.Invalid;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "workspaces/context");
        request.Headers.Authorization = authorizationHeader;

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return WorkspaceContextValidationResult.Unavailable;
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
                return WorkspaceContextValidationResult.Current;

            return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound
                ? WorkspaceContextValidationResult.Invalid
                : WorkspaceContextValidationResult.Unavailable;
        }
    }
}

public static class WorkspaceContextValidationServiceExtensions
{
    public static IServiceCollection AddWorkspaceContextValidation(
        this IServiceCollection services,
        Uri userServiceAddress)
    {
        services.AddHttpContextAccessor();
        services.AddHttpClient<IWorkspaceContextValidator, UserApiWorkspaceContextValidator>(client =>
        {
            client.BaseAddress = userServiceAddress;
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        return services;
    }
}
