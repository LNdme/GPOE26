namespace GPOE26.UI.Services;

public enum OfflineItemKind { Course, Assessment }

/// <summary>
/// État hors connexion affiché par les pages. Le Web n'en a pas besoin (OnlineOnlyStatus) ;
/// l'application Windows fournit l'implémentation de GPOE26.Offline (cache SQLite, file d'envoi).
/// </summary>
public interface IOfflineStatus
{
    /// <summary>L'hôte sait travailler hors connexion (sinon les boutons « hors ligne » sont masqués)</summary>
    bool Supported { get; }

    /// <summary>Les services répondent</summary>
    bool Online { get; }

    /// <summary>Copies et photos enregistrées sur l'appareil, en attente d'envoi</summary>
    int Pending { get; }

    /// <summary>Envois refusés par le serveur (tentatives épuisées…) : message pour l'élève</summary>
    IReadOnlyList<string> Problems { get; }

    bool Syncing { get; }
    DateTime? LastSync { get; }

    event Action? Changed;

    Task<bool> IsAvailableOfflineAsync(OfflineItemKind kind, Guid id);

    /// <summary>Télécharge un cours ou une évaluation ; renvoie un message d'erreur, ou null si c'est fait.</summary>
    Task<string?> MakeAvailableOfflineAsync(OfflineItemKind kind, Guid id);

    Task SyncNowAsync();
}

/// <summary>Hôte toujours en ligne (site web) : rien à télécharger, rien à synchroniser.</summary>
public class OnlineOnlyStatus : IOfflineStatus
{
    public bool Supported => false;
    public bool Online => true;
    public int Pending => 0;
    public IReadOnlyList<string> Problems => [];
    public bool Syncing => false;
    public DateTime? LastSync => null;
    public event Action? Changed { add { } remove { } }
    public Task<bool> IsAvailableOfflineAsync(OfflineItemKind kind, Guid id) => Task.FromResult(false);
    public Task<string?> MakeAvailableOfflineAsync(OfflineItemKind kind, Guid id) => Task.FromResult<string?>("Indisponible sur le site web.");
    public Task SyncNowAsync() => Task.CompletedTask;
}
