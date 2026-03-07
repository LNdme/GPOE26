using System.Text.Json;
using GPOE26.Web.Models;

namespace GPOE26.Web.Services;

/// <summary>
/// Gère l'état de connexion (JWT en mémoire).
/// Délègue les appels HTTP à ApiClient.
/// Synchronise le token vers AuthTokenProvider pour le DelegatingHandler.
/// </summary>
public class AuthService(ApiClient api, AuthTokenProvider tokenProvider)
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
        tokenProvider.Token = null;
        OnAuthChanged?.Invoke();
    }

    private void SetAuth(AuthResponse auth)
    {
        _jwt = auth.Token;
        _profile = auth.Profile;
        _expiresAt = auth.ExpiresAt;
        tokenProvider.Token = _jwt;
        OnAuthChanged?.Invoke();
    }
}
