namespace GPOE26.Web.Services;

/// <summary>
/// Lightweight token store that holds the current JWT token.
/// This breaks the circular dependency: AuthService → ApiClient → AuthService.
/// AuthService sets the token, JwtDelegatingHandler reads it.
/// Registered as Scoped (one per Blazor Server circuit = one per user).
/// </summary>
public class AuthTokenProvider
{
    public string? Token { get; set; }
}
