using GPOE26.UI.Models;

namespace GPOE26.UI.Services;

/// <summary>
/// Historique de la personne et partage de ses résultats (docs/PLAN-APPRENANT-AU-CENTRE.md, lots J3 et J5).
/// kind : « organization » ou « guardian » ; scope : SharingScopes.Progress ou SharingScopes.WeakPoints.
/// </summary>
public interface IHistoryApi
{
    Task<ApiResult<LearnerHistory>> HistoryAsync();
    Task<ApiResult<bool>> SetSharingAsync(string kind, Guid id, string scope, bool share);
}

public class HistoryApi(IHttpClientFactory f, SessionState s) : ApiBase(f, s, ApiClients.User), IHistoryApi
{
    public Task<ApiResult<LearnerHistory>> HistoryAsync() => GetAsync<LearnerHistory>("/me/history");

    public Task<ApiResult<bool>> SetSharingAsync(string kind, Guid id, string scope, bool share)
    {
        var path = $"/me/sharing/{kind}/{id}/{RouteName(scope)}";
        return share ? PutAsync<bool>(path, new { }) : DeleteAsync<bool>(path);
    }

    /// <summary>Nom de la portée dans la route : progress, weak-points.</summary>
    public static string RouteName(string scope) => scope switch
    {
        SharingScopes.Progress => "progress",
        SharingScopes.WeakPoints => "weak-points",
        _ => "official-results"
    };
}
