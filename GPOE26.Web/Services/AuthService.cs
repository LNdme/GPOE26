using System.Text.Json;
using GPOE26.Web.Models;

namespace GPOE26.Web.Services;

/// <summary>
/// Gère l'état de connexion (JWT en mémoire).
/// Délègue les appels HTTP à ApiClient.
/// </summary>
public class AuthService(ApiClient api)
{
    private string? _jwt;
    private UserProfileDto? _profile;
    private DateTime _expiresAt = DateTime.MinValue;

    public event Action? OnAuthChanged;

    public bool IsAuthenticated => _jwt is not null && DateTime.UtcNow < _expiresAt;
    public UserProfileDto? Profile => _profile;
    public string? Token => _jwt;

    // ── Login ─────────────────────────────────────────────────────
    public async Task<(bool ok, string? error)> LoginAsync(string email, string password)
    {
        var auth = await api.LoginAsync(email, password);
        if (auth is null) return (false, "Email ou mot de passe incorrect.");
        SetAuth(auth);
        return (true, null);
    }

    // ── Register ──────────────────────────────────────────────────
    public async Task<(bool ok, string? error)> RegisterAsync(RegisterRequest req)
    {
        var auth = await api.RegisterAsync(req);
        if (auth is null) return (false, "Erreur lors de la création du compte.");
        SetAuth(auth);
        return (true, null);
    }

    // ── Logout ────────────────────────────────────────────────────
    public void Logout()
    {
        _jwt = null; _profile = null; _expiresAt = DateTime.MinValue;
        OnAuthChanged?.Invoke();
    }

    /// <summary>
    /// Injecte le JWT dans les headers d'un HttpClient nommé.
    /// Utilisé par ApiClient pour les endpoints protégés.
    /// </summary>
    public void InjectToken(HttpClient client)
    {
        if (_jwt is not null)
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _jwt);
    }

    private void SetAuth(AuthResponse auth)
    {
        _jwt = auth.Token;
        _profile = auth.Profile;
        _expiresAt = auth.ExpiresAt;
        OnAuthChanged?.Invoke();
    }
}
