using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Chat.Service;

public record CourseSectionRef(string? Content, int Order);
public record CourseRef(Guid Id, string Title, string Subject, string? Description, string? ExtractedText, List<CourseSectionRef>? Sections)
{
    /// <summary>
    /// Texte entier du cours (sections, sinon texte extrait du PDF). S'il est trop long pour le modèle,
    /// AgentPrompts.SelectCourse en garde le plan et les parties utiles à la question.
    /// </summary>
    public string FullText()
    {
        var text = Sections is { Count: > 0 }
            ? string.Join("\n\n", Sections.OrderBy(s => s.Order).Select(s => s.Content))
            : ExtractedText ?? "";
        // Le tuteur n'a pas besoin de la description technique d'un laboratoire
        text = System.Text.RegularExpressions.Regex.Replace(text, @"```simulation[\s\S]*?```", "[Laboratoire interactif dans le cours]");
        return string.IsNullOrWhiteSpace(text) ? Description ?? "" : text;
    }
}

/// <summary>
/// Un service dont dépend la réponse (Cours, User) ne répond pas : l'appelant renvoie 503 avec un message honnête,
/// au lieu de dire à l'élève qu'il n'est « pas abonné » ou que le cours « n'existe pas ».
/// </summary>
public class PlatformUnavailableException(string service, Exception? inner = null)
    : Exception($"Le service {service} ne répond pas pour le moment. Réessaie dans un instant.", inner);

/// <summary>
/// Services Cours, User et Quiz, appelés avec le JWT de l'utilisateur : le tuteur ne voit que les cours
/// auxquels l'élève a accès, et l'abonnement est vérifié par le service qui le gère.
/// </summary>
public class PlatformClients(IHttpClientFactory factory, IHttpContextAccessor context, IMemoryCache cache)
{
    /// <summary>
    /// Les lectures de contexte (classes, progression, évaluations, cours) changent peu d'une question à l'autre :
    /// gardées une minute par utilisateur, elles ne sont plus refaites à chaque message.
    /// </summary>
    public static readonly TimeSpan ContextCacheDuration = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Le cours, s'il existe et que l'utilisateur y a accès ; null sinon. Lève PlatformUnavailableException si Cours ne répond pas.</summary>
    public async Task<CourseRef?> CourseAsync(Guid courseId, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await SendAsync("cours", $"/cours/{courseId}", ct);
        }
        catch (Exception ex) when (IsOutage(ex, ct))
        {
            throw new PlatformUnavailableException("des cours", ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden) return null;
            if (!response.IsSuccessStatusCode) throw new PlatformUnavailableException("des cours");
            return await response.Content.ReadFromJsonAsync<CourseRef>(Json, ct);
        }
    }

    /// <summary>
    /// Lecture JSON avec le JWT de l'appelant, gardée une minute pour cet utilisateur.
    /// Un service absent ne doit jamais empêcher l'agent de répondre : renvoie null.
    /// </summary>
    public async Task<T?> GetJsonAsync<T>(string client, string path, CancellationToken ct) where T : class
    {
        var key = $"{UserKey()}|{client}|{path}|{typeof(T).FullName}";
        if (cache.TryGetValue(key, out T? cached)) return cached;

        try
        {
            using var response = await SendAsync(client, path, ct);
            if (!response.IsSuccessStatusCode) return null;
            var value = await response.Content.ReadFromJsonAsync<T>(Json, ct);
            if (value is not null) cache.Set(key, value, ContextCacheDuration);
            return value;
        }
        catch (Exception ex) when (ex is JsonException || IsOutage(ex, ct))
        {
            return null;
        }
    }

    /// <summary>
    /// La formule de l'utilisateur inclut-elle cette fonctionnalité ? Renvoie la fonctionnalité (et son quota) ou null.
    /// Lu au service User, source des droits, sans cache : une clé activée vaut tout de suite.
    /// Lève PlatformUnavailableException si le service ne répond pas (ce n'est pas un refus).
    /// </summary>
    public async Task<FeatureGrant?> FeatureAsync(string code, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await SendAsync("user", "/subscriptions/me", ct);
        }
        catch (Exception ex) when (IsOutage(ex, ct))
        {
            throw new PlatformUnavailableException("des abonnements", ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return null;
            if (!response.IsSuccessStatusCode) throw new PlatformUnavailableException("des abonnements");
            try
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                return FindFeature(doc.RootElement, code);
            }
            catch (JsonException ex)
            {
                throw new PlatformUnavailableException("des abonnements", ex);
            }
        }
    }

    public async Task<bool> HasFeatureAsync(string code, CancellationToken ct) => await FeatureAsync(code, ct) is not null;

    /// <summary>Lit la fonctionnalité dans la réponse de /subscriptions/me ; une réponse incomplète vaut « non incluse ».</summary>
    public static FeatureGrant? FindFeature(JsonElement subscriptions, string code)
    {
        if (subscriptions.ValueKind != JsonValueKind.Object
            || !subscriptions.TryGetProperty("entitlement", out var entitlement) || entitlement.ValueKind != JsonValueKind.Object
            || !entitlement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var feature in features.EnumerateArray())
        {
            if (feature.ValueKind != JsonValueKind.Object
                || !feature.TryGetProperty("code", out var c) || c.ValueKind != JsonValueKind.String || c.GetString() != code)
                continue;
            int? limit = feature.TryGetProperty("limit", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : null;
            return new FeatureGrant(code, limit);
        }
        return null;
    }

    /// <summary>Panne du service appelé (refus de connexion, délai dépassé), à distinguer d'une annulation par l'appelant.</summary>
    private static bool IsOutage(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested);

    private string UserKey() =>
        context.HttpContext?.User.FindFirstValue("sub") ?? context.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonyme";

    private async Task<HttpResponseMessage> SendAsync(string client, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        var auth = context.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(auth)) request.Headers.Authorization = AuthenticationHeaderValue.Parse(auth);
        return await factory.CreateClient(client).SendAsync(request, ct);
    }
}

/// <summary>Fonctionnalité incluse dans la formule. Limit : quota mensuel, null si illimité.</summary>
public record FeatureGrant(string Code, int? Limit);
