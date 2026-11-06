namespace GPOE26.Offline;

/// <summary>Réglages fournis par l'hôte.</summary>
public class OfflineOptions
{
    /// <summary>Dossier des bases SQLite (une par utilisateur). Application Windows : FileSystem.AppDataDirectory.</summary>
    public required string DataDirectory { get; init; }

    /// <summary>Intervalle de vérification du retour du réseau quand l'application est hors connexion</summary>
    public TimeSpan ProbeInterval { get; init; } = TimeSpan.FromSeconds(15);
}
