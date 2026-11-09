using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Chat.Service;

public record CourseSectionRef(string? Content, int Order);
public record CourseRef(Guid Id, string Title, string Subject, string? Description, string? ExtractedText, List<CourseSectionRef>? Sections)
{
    /// <summary>Texte du cours donné au tuteur (sections, sinon texte extrait du PDF), borné pour tenir dans le contexte.</summary>
    public string Text(int max = 40_000)
    {
        var text = Sections is { Count: > 0 }
            ? string.Join("\n\n", Sections.OrderBy(s => s.Order).Select(s => s.Content))
            : ExtractedText ?? "";
        if (string.IsNullOrWhiteSpace(text)) text = Description ?? "";
        return text.Length > max ? text[..max] + "\n[…]" : text;
    }
}

/// <summary>
/// Services Cours et User, appelés avec le JWT de l'élève : le tuteur ne voit que les cours
/// auxquels l'élève a accès, et l'abonnement est vérifié par le service qui le gère.
/// </summary>
public class PlatformClients(IHttpClientFactory factory, IHttpContextAccessor context)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CourseRef?> CourseAsync(Guid courseId, CancellationToken ct)
    {
        using var response = await SendAsync("cours", $"/cours/{courseId}", ct);
        return response.StatusCode == HttpStatusCode.OK
            ? await response.Content.ReadFromJsonAsync<CourseRef>(Json, ct)
            : null;
    }

    /// <summary>
    /// Premium d'après le JWT ; s'il dit non, on vérifie auprès du service User
    /// (une clé activée après la connexion ne figure pas encore dans le jeton).
    /// </summary>
    public async Task<bool> IsPremiumAsync(CancellationToken ct)
    {
        if (context.HttpContext?.User.FindFirst("premium")?.Value == "true") return true;
        try
        {
            using var response = await SendAsync("user", "/auth/me", ct);
            if (!response.IsSuccessStatusCode) return false;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("isPremium", out var p) && p.ValueKind == JsonValueKind.True;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    /// <summary>La formule de l'utilisateur inclut-elle cette fonctionnalité ? (lu au service User, source des droits)</summary>
    public async Task<bool> HasFeatureAsync(string code, CancellationToken ct)
    {
        try
        {
            using var response = await SendAsync("user", "/subscriptions/me", ct);
            if (!response.IsSuccessStatusCode) return false;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.GetProperty("entitlement").TryGetProperty("features", out var features)
                && features.ValueKind == JsonValueKind.Array
                && features.EnumerateArray().Any(f => f.GetProperty("code").GetString() == code);
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string client, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        var auth = context.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(auth)) request.Headers.Authorization = AuthenticationHeaderValue.Parse(auth);
        return await factory.CreateClient(client).SendAsync(request, ct);
    }
}
