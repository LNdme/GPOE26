using GPOE26.UI.Services;

namespace GPOE26.Web.Services;

/// <summary>
/// Jeton courant pour ApiClient (pages historiques), lu dans la session partagée.
/// Registered as Scoped (one per Blazor Server circuit = one per user).
/// </summary>
public class AuthTokenProvider(SessionState session)
{
    public string? Token => session.Token;
}
