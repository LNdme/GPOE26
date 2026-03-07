using System.Net.Http.Headers;

namespace GPOE26.Web.Services;

/// <summary>
/// DelegatingHandler that injects the JWT Bearer token into outgoing HTTP requests.
/// Registered on named HttpClients for protected APIs (cours, chat, quiz).
/// </summary>
public class JwtDelegatingHandler : DelegatingHandler
{
    private readonly AuthTokenProvider _tokenProvider;

    public JwtDelegatingHandler(AuthTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = _tokenProvider.Token;
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
