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

public record StoredSession(
    string Token,
    DateTime ExpiresAt,
    UserProfile Profile,
    WorkspaceSummary? ActiveWorkspace = null);

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
    private TaskCompletionSource? _workspaceResolution;

    // Renouvellement du jeton : un seul à la fois, même si plusieurs appels d'API partent ensemble
    private Task? _renewal;
    private DateTime _receivedAt;
    private DateTime _renewalRetryAt;

    public string? Token { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public UserProfile? Profile { get; private set; }
    public WorkspaceSummary? ActiveWorkspace { get; private set; }

    public bool IsAuthenticated => Token is not null && DateTime.UtcNow < ExpiresAt;
    public UserRole? Role => IsAuthenticated ? Profile?.Role : null;
    public bool IsPremium => Profile?.IsPremium == true;
    public bool CanTeach => HasWorkspaceRole(WorkspaceRole.Instructor, WorkspaceRole.Admin);
    public bool CanLearn => HasWorkspaceRole(WorkspaceRole.Learner);
    public bool CanManageCourses => HasWorkspaceRole(
        WorkspaceRole.Owner,
        WorkspaceRole.Admin,
        WorkspaceRole.Guardian,
        WorkspaceRole.Instructor);
    public bool IsFamilyGuardian =>
        ActiveWorkspace?.Kind == WorkspaceKind.Family &&
        HasWorkspaceRole(WorkspaceRole.Guardian);

    public bool HasWorkspaceRole(params WorkspaceRole[] roles) =>
        ActiveWorkspace?.Roles.Any(activeRole => roles.Contains(activeRole)) == true;

    public Task WaitForWorkspaceResolutionAsync() =>
        _workspaceResolution?.Task ?? Task.CompletedTask;

    public void CompleteWorkspaceResolution()
    {
        _workspaceResolution?.TrySetResult();
        _workspaceResolution = null;
    }

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
                Apply(saved.Token, saved.ExpiresAt, saved.Profile, saved.ActiveWorkspace, notify: true);
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
        Apply(auth.Token, auth.ExpiresAt, auth.Profile, auth.ActiveWorkspace, notify: true);
        await store.SaveAsync(new StoredSession(auth.Token, auth.ExpiresAt, auth.Profile, auth.ActiveWorkspace));
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

            // Le serveur garde l'espace de travail du jeton renouvelé (tant que la personne en est membre)
            var workspace = auth.ActiveWorkspace ?? ActiveWorkspace;
            Apply(auth.Token, auth.ExpiresAt, auth.Profile, workspace, notify: true);
            try { await store.SaveAsync(new StoredSession(auth.Token, auth.ExpiresAt, auth.Profile, workspace)); }
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
        Apply(Token, ExpiresAt, profile, ActiveWorkspace, notify: true);
        await store.SaveAsync(new StoredSession(Token, ExpiresAt, profile, ActiveWorkspace));
    }

    public async Task SignOutAsync()
    {
        Token = null;
        Profile = null;
        ActiveWorkspace = null;
        ExpiresAt = DateTime.MinValue;
        CompleteWorkspaceResolution();
        await SafeClearAsync();
        Changed?.Invoke();
    }

    public async Task ClearActiveWorkspaceAsync()
    {
        if (Token is null || Profile is null) return;

        Apply(Token, ExpiresAt, Profile, null, notify: true);
        try
        {
            await store.SaveAsync(new StoredSession(Token, ExpiresAt, Profile, null));
        }
        finally
        {
            CompleteWorkspaceResolution();
        }
    }

    /// <summary>Le serveur a refusé le jeton (401) : on considère la session terminée.</summary>
    public Task ExpireAsync() => SignOutAsync();

    private void Apply(
        string token,
        DateTime expiresAt,
        UserProfile profile,
        WorkspaceSummary? activeWorkspace,
        bool notify)
    {
        if (token != Token)
        {
            _receivedAt = DateTime.UtcNow;
            _renewalRetryAt = DateTime.MinValue;
        }
        Token = token;
        ExpiresAt = expiresAt;
        Profile = profile;
        ActiveWorkspace = activeWorkspace;
        if (activeWorkspace is null)
        {
            if (_workspaceResolution is null || _workspaceResolution.Task.IsCompleted)
                _workspaceResolution = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        else
            CompleteWorkspaceResolution();
        if (notify) Changed?.Invoke();
    }

    private async Task SafeClearAsync()
    {
        try { await store.ClearAsync(); } catch { /* rien à nettoyer */ }
    }
}
