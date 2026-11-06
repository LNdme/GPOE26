using GPOE26.UI.Models;

namespace GPOE26.UI.Services;

/// <summary>
/// Stockage persistant du jeton, fourni par l'hôte :
/// localStorage protégé côté Web, stockage sécurisé du système côté application Windows.
/// </summary>
public interface ISessionStore
{
    Task<StoredSession?> LoadAsync();
    Task SaveAsync(StoredSession session);
    Task ClearAsync();
}

public record StoredSession(string Token, DateTime ExpiresAt, UserProfile Profile);

/// <summary>
/// Session de l'utilisateur connecté (une par circuit Blazor / par fenêtre de l'application).
/// Restaurée au démarrage depuis ISessionStore pour survivre à un rechargement de page.
/// </summary>
public class SessionState(ISessionStore store)
{
    // Tâche partagée : la mise en page et la page appellent RestoreAsync en même temps ;
    // chacune doit attendre la même restauration (sinon la seconde croit l'utilisateur déconnecté)
    private Task? _restore;

    public string? Token { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public UserProfile? Profile { get; private set; }

    public bool IsAuthenticated => Token is not null && DateTime.UtcNow < ExpiresAt;
    public UserRole? Role => IsAuthenticated ? Profile?.Role : null;
    public bool IsPremium => Profile?.IsPremium == true;

    /// <summary>Déclenché à chaque connexion, déconnexion ou mise à jour du profil.</summary>
    public event Action? Changed;

    public Task RestoreAsync() => _restore ??= RestoreCoreAsync();

    private async Task RestoreCoreAsync()
    {
        try
        {
            var saved = await store.LoadAsync();
            if (saved is not null && saved.ExpiresAt > DateTime.UtcNow)
                Apply(saved.Token, saved.ExpiresAt, saved.Profile, notify: true);
            else if (saved is not null)
                await store.ClearAsync();
        }
        catch
        {
            // Stockage illisible (clés de chiffrement renouvelées, données corrompues) : on repart déconnecté
            await SafeClearAsync();
        }
    }

    public async Task SignInAsync(AuthResponse auth)
    {
        Apply(auth.Token, auth.ExpiresAt, auth.Profile, notify: true);
        await store.SaveAsync(new StoredSession(auth.Token, auth.ExpiresAt, auth.Profile));
    }

    /// <summary>Met à jour le profil (après activation d'une clé, par exemple) sans changer de jeton.</summary>
    public async Task UpdateProfileAsync(UserProfile profile)
    {
        if (Token is null) return;
        Apply(Token, ExpiresAt, profile, notify: true);
        await store.SaveAsync(new StoredSession(Token, ExpiresAt, profile));
    }

    public async Task SignOutAsync()
    {
        Token = null;
        Profile = null;
        ExpiresAt = DateTime.MinValue;
        await SafeClearAsync();
        Changed?.Invoke();
    }

    /// <summary>Le serveur a refusé le jeton (401) : on considère la session terminée.</summary>
    public Task ExpireAsync() => SignOutAsync();

    private void Apply(string token, DateTime expiresAt, UserProfile profile, bool notify)
    {
        Token = token;
        ExpiresAt = expiresAt;
        Profile = profile;
        if (notify) Changed?.Invoke();
    }

    private async Task SafeClearAsync()
    {
        try { await store.ClearAsync(); } catch { /* rien à nettoyer */ }
    }
}
