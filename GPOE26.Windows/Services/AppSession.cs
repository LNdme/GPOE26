using GPOE26.UI.Models;
using GPOE26.UI.Services;

namespace GPOE26.Desktop.Services;

/// <summary>
/// Qui utilise l'application : la session en ligne (<see cref="SessionState"/> de GPOE26.UI, jeton renouvelé),
/// ou, à défaut, le dernier élève connecté sur ce poste en lecture seule hors connexion.
/// </summary>
public sealed class AppSession(SessionState session, DesktopSessionStore store)
{
    private UserProfile? _offlineProfile;

    public SessionState Online => session;

    /// <summary>Profil affiché : celui de la session, ou celui gardé pour la lecture hors connexion.</summary>
    public UserProfile? Profile => session.IsAuthenticated ? session.Profile : _offlineProfile;

    public Guid? UserId => Profile?.Id;

    /// <summary>Lecture hors connexion : pas de jeton valide, seule la copie locale est lue (rien n'est envoyé).</summary>
    public bool IsReadOnly => !session.IsAuthenticated && _offlineProfile is not null;

    public bool IsSignedIn => session.IsAuthenticated || _offlineProfile is not null;

    public event Action? Changed;

    /// <summary>Au démarrage : session encore valide, sinon rien (l'écran de connexion propose le mode hors connexion).</summary>
    public async Task StartAsync()
    {
        session.Changed += () => Changed?.Invoke();
        await session.RestoreAsync();
    }

    /// <summary>Profil du dernier élève connecté, s'il y en a un : l'écran de connexion propose de le rouvrir hors connexion.</summary>
    public Task<UserProfile?> OfflineCandidateAsync() => store.OfflineProfileAsync();

    public async Task SignInAsync(AuthResponse auth)
    {
        _offlineProfile = null;
        await session.SignInAsync(auth);
    }

    public async Task<bool> OpenOfflineAsync()
    {
        _offlineProfile = await store.OfflineProfileAsync();
        Changed?.Invoke();
        return _offlineProfile is not null;
    }

    /// <summary>Déconnexion voulue : on oublie aussi le profil hors connexion (la copie locale reste chiffrée par compte… et effaçable).</summary>
    public async Task SignOutAsync()
    {
        _offlineProfile = null;
        await session.SignOutAsync();
        await store.ForgetAsync();
        Changed?.Invoke();
    }
}
