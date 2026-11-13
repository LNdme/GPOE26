using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;

namespace User.Service;

public record PlanFeatureRef(string Code, string Label, int? Limit);
public record PlanRef(string Code, string Name, int Seats, List<PlanFeatureRef> Features);

/// <summary>
/// Catalogue des formules, géré par GPOE26.ApiService. Conservé 5 minutes ;
/// si le service ne répond plus, la dernière version connue continue de servir.
/// </summary>
public class PlanCatalogClient(HttpClient http, IMemoryCache cache, ILogger<PlanCatalogClient> logger)
{
    private const string FreshKey = "plans:fresh";
    private const string LastKnownKey = "plans:last";

    public async Task<IReadOnlyList<PlanRef>> GetAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(FreshKey, out IReadOnlyList<PlanRef>? fresh) && fresh is not null)
            return fresh;

        try
        {
            var plans = await http.GetFromJsonAsync<List<PlanRef>>("/api/plans", ct) ?? [];
            cache.Set<IReadOnlyList<PlanRef>>(FreshKey, plans, TimeSpan.FromMinutes(5));
            cache.Set<IReadOnlyList<PlanRef>>(LastKnownKey, plans);
            return plans;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Catalogue des formules indisponible");
            return cache.TryGetValue(LastKnownKey, out IReadOnlyList<PlanRef>? last) && last is not null ? last : [];
        }
    }
}
