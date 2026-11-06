using GPOE26.UI.Services;

namespace GPOE26.Offline;

/// <summary>
/// En ligne ou non, d'après les appels réels aux services : un appel qui n'aboutit pas fait passer hors connexion,
/// puis le service est sondé à intervalle régulier jusqu'à ce qu'il réponde. Unique pour l'application.
/// L'hôte peut aussi appeler ProbeNowAsync (changement de réseau signalé par le système).
/// </summary>
public class NetworkMonitor(IHttpClientFactory factory, OfflineOptions options) : IDisposable
{
    private readonly object _lock = new();
    private Timer? _probe;
    private int _probing;

    public bool Online { get; private set; } = true;

    public event Action<bool>? Changed;

    /// <summary>Résultat d'un appel aux services</summary>
    public void Report(bool networkError)
    {
        if (networkError) SetOnline(false);
        else SetOnline(true);
    }

    public async Task<bool> ProbeNowAsync()
    {
        if (Interlocked.Exchange(ref _probing, 1) == 1) return Online;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            // N'importe quelle réponse HTTP (même 404) prouve que le service est joignable
            using var response = await factory.CreateClient(ApiClients.Quiz).GetAsync("/", timeout.Token);
            SetOnline(true);
        }
        catch (Exception)
        {
            SetOnline(false);
        }
        finally
        {
            Interlocked.Exchange(ref _probing, 0);
        }
        return Online;
    }

    private void SetOnline(bool online)
    {
        lock (_lock)
        {
            if (online == Online) return;
            Online = online;
            if (online)
            {
                _probe?.Dispose();
                _probe = null;
            }
            else
            {
                _probe ??= new Timer(_ => _ = ProbeNowAsync(), null, options.ProbeInterval, options.ProbeInterval);
            }
        }
        Changed?.Invoke(online);
    }

    public void Dispose() => _probe?.Dispose();
}
