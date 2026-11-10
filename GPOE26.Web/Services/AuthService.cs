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

    public bool IsParent => string.Equals(_profile?.Role, "Parent", StringComparison.OrdinalIgnoreCase);
    public bool IsStudent => string.Equals(_profile?.Role, "Student", StringComparison.OrdinalIgnoreCase);
    public bool IsTeacher => string.Equals(_profile?.Role, "Teacher", StringComparison.OrdinalIgnoreCase);

    // ── Administration du site ────────────────────────────────────────────────
    //
    // ⚠️ Ces trois propriétés servent à CACHER des boutons, pas à protéger quoi que ce
    // soit. Elles vivent dans le navigateur, sur un profil que le serveur a émis mais
    // que rien n'empêche d'inspecter. Ce qui protège réellement le contenu du site, ce
    // sont les politiques Redaction et Administration de GPOE26.ApiService.
    //
    // Leur seule raison d'être : éviter qu'un utilisateur légitime se heurte à un 403
    // incompréhensible après avoir cliqué sur un bouton qu'on n'aurait pas dû lui
    // montrer.

    public bool IsAdmin => string.Equals(_profile?.Role, "Admin", StringComparison.OrdinalIgnoreCase);

    public bool IsEditor => string.Equals(_profile?.Role, "Editor", StringComparison.OrdinalIgnoreCase);

    /// <summary>Peut publier actualités, événements et activités.</summary>
    public bool CanPublish => IsAdmin || IsEditor;

    /// <summary>
    /// Peut gouverner le site : équipe de direction, coordonnées, tarifs, comptes.
    /// Un rédacteur alimente le site, il ne le gouverne pas.
    /// </summary>
    public bool CanAdminister => IsAdmin;

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
        var (auth, error) = await api.RegisterAsync(req);
        if (auth is null) return (false, error ?? "Erreur lors de la création du compte.");
        SetAuth(auth);
        return (true, null);
    }

    /// <summary>
    /// Remplace le jeton par une version rafraîchie, sans toucher au profil.
    ///
    /// Le rattachement d'un enfant modifie le claim « children » : sans cette bascule,
    /// le parent devrait attendre l'expiration de son jeton — jusqu'à une heure — avant
    /// de voir apparaître l'enfant qu'il vient d'ajouter.
    /// </summary>
    public void ApplyRefreshedToken(string token, DateTime expiresAt)
    {
        if (string.IsNullOrWhiteSpace(token)) return;

        _jwt = token;
        _expiresAt = expiresAt;
        tokenProvider.Token = token;
        OnAuthChanged?.Invoke();
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
