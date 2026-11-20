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
    /// <summary>Après un renouvellement refusé ou impossible, on ne réessaie pas à chaque requête.</summary>
    private static readonly TimeSpan RenewalRetryDelay = TimeSpan.FromMinutes(1);

    // Tâche partagée : la mise en page et la page appellent RestoreAsync en même temps ;
    // chacune doit attendre la même restauration (sinon la seconde croit l'utilisateur déconnecté)
    private Task? _restore;

    // Renouvellement du jeton : un seul à la fois, même si plusieurs appels d'API partent ensemble
    private Task? _renewal;
    private DateTime _receivedAt;
    private DateTime _renewalRetryAt;

    /// <summary>
    /// Délai entre deux relectures du profil pendant la navigation. Les droits (formule, fonctionnalités) changent
    /// côté serveur sans que le navigateur le sache : activation par l'administration, clé, établissement, parent.
    /// </summary>
    public static readonly TimeSpan ProfileMaxAge = TimeSpan.FromMinutes(1);

    // Relecture du profil : une seule à la fois ; MinValue = profil jamais vérifié (session restaurée du navigateur)
    private Task? _profileRefresh;
    private DateTime _profileCheckedAt = DateTime.MinValue;

    public string? Token { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public UserProfile? Profile { get; private set; }

    public bool IsAuthenticated => Token is not null && DateTime.UtcNow < ExpiresAt;
    public UserRole? Role => IsAuthenticated ? Profile?.Role : null;
    public bool IsPremium => Profile?.IsPremium == true;

    /// <summary>La formule de l'utilisateur inclut-elle cette fonctionnalité ?</summary>
    public bool HasFeature(string code) => Profile?.Features?.Any(f => f.Code == code) == true;

    /// <summary>Déclenché à chaque connexion, déconnexion ou mise à jour du profil.</summary>
    public event Action? Changed;

    /// <summary>
    /// Le jeton est encore valide mais entamé : il est temps d'en demander un neuf.
    /// Tant que l'utilisateur est actif, la session ne tombe donc plus au milieu d'une épreuve.
    /// </summary>
    public bool ShouldRenew => IsAuthenticated && DueForRenewal(_receivedAt, ExpiresAt, DateTime.UtcNow) && DateTime.UtcNow >= _renewalRetryAt;

    /// <summary>Vrai une fois le premier tiers de la durée de vie du jeton écoulé (20 min pour un jeton d'une heure).</summary>
    public static bool DueForRenewal(DateTime receivedAt, DateTime expiresAt, DateTime now)
    {
        var lifetime = expiresAt - receivedAt;
        return lifetime > TimeSpan.Zero && now < expiresAt && now - receivedAt >= lifetime / 3;
    }

    public Task RestoreAsync() => _restore ??= RestoreCoreAsync();

    private async Task RestoreCoreAsync()
    {
        try
        {
            var saved = await store.LoadAsync();
            if (saved is not null && saved.ExpiresAt > DateTime.UtcNow)
                {
                Apply(saved.Token, saved.ExpiresAt, saved.Profile, notify: true);
                _profileCheckedAt = DateTime.MinValue;   // copie du navigateur : à relire au service User
            }
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

    /// <summary>
    /// Échange le jeton courant contre un neuf. <paramref name="request"/> appelle le service des comptes
    /// et renvoie null s'il refuse. Un échec ne déconnecte pas : le jeton courant reste valable jusqu'à son terme.
    /// </summary>
    public Task RenewAsync(Func<string, Task<AuthResponse?>> request) => _renewal ??= RenewCoreAsync(request);

    private async Task RenewCoreAsync(Func<string, Task<AuthResponse?>> request)
    {
        try
        {
            var token = Token;
            if (token is null) return;

            var auth = await request(token);
            // Déconnexion ou autre connexion pendant l'appel : on ne ressuscite pas l'ancienne session
            if (auth is null || Token != token)
            {
                _renewalRetryAt = DateTime.UtcNow + RenewalRetryDelay;
                return;
            }

            Apply(auth.Token, auth.ExpiresAt, auth.Profile, notify: true);
            try { await store.SaveAsync(new StoredSession(auth.Token, auth.ExpiresAt, auth.Profile)); }
            catch { /* navigateur injoignable : le jeton neuf sert déjà en mémoire, il sera enregistré au prochain renouvellement */ }
        }
        catch
        {
            _renewalRetryAt = DateTime.UtcNow + RenewalRetryDelay;
        }
        finally
        {
            _renewal = null;
        }
    }

    /// <summary>Met à jour le profil (après activation d'une clé, par exemple) sans changer de jeton.</summary>
    public async Task UpdateProfileAsync(UserProfile profile)
    {
        if (Token is null) return;
        Apply(Token, ExpiresAt, profile, notify: true);
        await store.SaveAsync(new StoredSession(Token, ExpiresAt, profile));
    }

    /// <summary>
    /// Le profil gardé dans le navigateur date de la connexion : un abonnement activé ensuite (par l'administration,
    /// une clé, un établissement ou un parent) n'y figurait pas, et le tuteur restait verrouillé alors que la page
    /// Abonnement affichait « actif ». Relit le profil au service User si sa dernière vérification date de plus de
    /// <see cref="ProfileMaxAge"/> (toujours pour une session restaurée). <paramref name="fetch"/> renvoie null en cas
    /// d'échec : le profil connu reste alors en place.
    /// </summary>
    public Task RefreshProfileAsync(Func<Task<UserProfile?>> fetch) =>
        !IsAuthenticated || DateTime.UtcNow - _profileCheckedAt < ProfileMaxAge
            ? Task.CompletedTask
            : _profileRefresh ??= RefreshProfileCoreAsync(fetch);

    private async Task RefreshProfileCoreAsync(Func<Task<UserProfile?>> fetch)
    {
        try
        {
            var token = Token;
            var profile = await fetch();
            _profileCheckedAt = DateTime.UtcNow;
            // Déconnexion ou autre connexion pendant l'appel : ce profil n'est plus celui de la session
            if (profile is null || token is null || Token != token) return;

            Apply(token, ExpiresAt, profile, notify: true);
            try { await store.SaveAsync(new StoredSession(token, ExpiresAt, profile)); }
            catch { /* navigateur injoignable : le profil neuf sert déjà en mémoire */ }
        }
        catch
        {
            // Service injoignable (application hors connexion) : on garde le profil connu et on réessaiera plus tard
            _profileCheckedAt = DateTime.UtcNow;
        }
        finally
        {
            _profileRefresh = null;
        }
    }

    public async Task SignOutAsync()
    {
        Token = null;
        Profile = null;
        ExpiresAt = DateTime.MinValue;
        _profileCheckedAt = DateTime.MinValue;
        await SafeClearAsync();
        Changed?.Invoke();
    }

    /// <summary>Le serveur a refusé le jeton (401) : on considère la session terminée.</summary>
    public Task ExpireAsync() => SignOutAsync();

    private void Apply(string token, DateTime expiresAt, UserProfile profile, bool notify)
    {
        if (token != Token)
        {
            _receivedAt = DateTime.UtcNow;
            _renewalRetryAt = DateTime.MinValue;
        }
        Token = token;
        ExpiresAt = expiresAt;
        Profile = profile;
        _profileCheckedAt = DateTime.UtcNow;   // profil tout juste reçu du service User (la restauration le remet à relire)
        if (notify) Changed?.Invoke();
    }

    private async Task SafeClearAsync()
    {
        try { await store.ClearAsync(); } catch { /* rien à nettoyer */ }
    }
}
