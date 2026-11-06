using System.Net;
using GPOE26.UI.Services;

namespace GPOE26.Offline;

internal static class Caching
{
    public const string NotDownloaded = "Hors connexion : ce contenu n'a pas encore été téléchargé sur cet appareil.";

    /// <summary>Appel suivi par le moniteur de réseau</summary>
    public static async Task<ApiResult<T>> TrackAsync<T>(this NetworkMonitor net, Task<ApiResult<T>> call)
    {
        var result = await call;
        net.Report(result.NetworkError);
        return result;
    }

    /// <summary>
    /// Lecture : en ligne, la réponse est mise en cache ; hors connexion, la dernière version enregistrée est rendue.
    /// </summary>
    public static async Task<ApiResult<T>> CachedAsync<T>(this OfflineDatabase db, NetworkMonitor net, string kind, string key,
        Func<Task<ApiResult<T>>> call, string offlineMessage = NotDownloaded)
    {
        var result = await net.TrackAsync(call());
        if (result.Ok)
        {
            if (result.Value is not null) await db.SaveAsync(kind, key, result.Value);
            return result;
        }
        if (!result.NetworkError) return result;

        var cached = await db.LoadAsync<T>(kind, key);
        return cached is not null
            ? new ApiResult<T>(cached, null, HttpStatusCode.OK, NetworkError: true)
            : result with { Error = offlineMessage };
    }
}
