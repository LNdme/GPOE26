using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GPOE26.UI.Models;

namespace GPOE26.UI.Services;

/// <summary>Résultat d'un appel d'API : la valeur, ou un message d'erreur lisible par l'utilisateur.</summary>
public record ApiResult<T>(T? Value, string? Error, HttpStatusCode Status)
{
    public bool Ok => Error is null;
}

/// <summary>Noms des HttpClient que l'hôte (Web ou application Windows) doit enregistrer.</summary>
public static class ApiClients
{
    public const string User = "user";
    public const string Cours = "cours";
    public const string Quiz = "quiz";
    public const string Chat = "chat";
    public const string Site = "apiservice";
}

/// <summary>
/// Base des clients d'API : ajoute le JWT de la session, désérialise, et transforme
/// les erreurs HTTP en messages (le champ "message" renvoyé par nos services, sinon un message générique).
/// </summary>
public abstract class ApiBase(IHttpClientFactory factory, SessionState session, string clientName, bool stringEnums = true)
{
    private static readonly JsonSerializerOptions StringEnums = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Le service Cours sérialise ses enums en nombres
    private static readonly JsonSerializerOptions NumericEnums = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    protected JsonSerializerOptions Json => stringEnums ? StringEnums : NumericEnums;

    protected Task<ApiResult<T>> GetAsync<T>(string path) => SendAsync<T>(HttpMethod.Get, path, null);
    protected Task<ApiResult<T>> PostAsync<T>(string path, object? body = null) => SendAsync<T>(HttpMethod.Post, path, body ?? new { });
    protected Task<ApiResult<T>> PutAsync<T>(string path, object body) => SendAsync<T>(HttpMethod.Put, path, body);
    protected Task<ApiResult<T>> DeleteAsync<T>(string path) => SendAsync<T>(HttpMethod.Delete, path, null);

    /// <summary>Envoi d'une image en multipart/form-data (champ "file").</summary>
    protected Task<ApiResult<T>> UploadAsync<T>(string path, ImageUpload image)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(image.Data);
        file.Headers.ContentType = new MediaTypeHeaderValue(image.ContentType);
        form.Add(file, "file", image.FileName);
        return SendAsync<T>(HttpMethod.Post, path, form);
    }

    /// <summary>Envoi de plusieurs images dans une même requête (champ "files").</summary>
    protected Task<ApiResult<T>> UploadManyAsync<T>(string path, IEnumerable<ImageUpload> images)
    {
        var form = new MultipartFormDataContent();
        foreach (var image in images)
        {
            var file = new ByteArrayContent(image.Data);
            file.Headers.ContentType = new MediaTypeHeaderValue(image.ContentType);
            form.Add(file, "files", image.FileName);
        }
        return SendAsync<T>(HttpMethod.Post, path, form);
    }

    /// <summary>Contenu binaire protégé (photo d'une copie) affiché en data URL : le navigateur n'a pas le jeton.</summary>
    protected async Task<string?> DataUrlAsync(string path)
    {
        try
        {
            await RenewSessionIfDueAsync();
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (session.Token is { } token)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await factory.CreateClient(clientName).SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            var type = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            return $"data:{type};base64,{Convert.ToBase64String(await response.Content.ReadAsByteArrayAsync())}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Avant chaque appel : si le jeton est entamé, on en demande un neuf au service des comptes.
    /// La session suit ainsi l'activité de l'utilisateur au lieu de tomber 60 minutes après la connexion.
    /// </summary>
    protected Task RenewSessionIfDueAsync() =>
        session.ShouldRenew ? session.RenewAsync(RequestRenewalAsync) : Task.CompletedTask;

    private async Task<AuthResponse?> RequestRenewalAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await factory.CreateClient(ApiClients.User).SendAsync(request);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<AuthResponse>(StringEnums) : null;
    }

    protected async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, object? body)
    {
        try
        {
            await RenewSessionIfDueAsync();
            var client = factory.CreateClient(clientName);
            using var request = new HttpRequestMessage(method, path);
            if (session.Token is { } token)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is HttpContent content)
                request.Content = content;
            else if (body is not null)
                request.Content = JsonContent.Create(body, body.GetType(), options: Json);

            using var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                // Opérations sans contenu utile : on les type ApiResult<bool>
                if (typeof(T) == typeof(bool))
                    return new ApiResult<T>((T)(object)true, null, response.StatusCode);
                if (response.StatusCode == HttpStatusCode.NoContent)
                    return new ApiResult<T>(default, null, response.StatusCode);
                // Réponse en texte brut (brouillon de cours rédigé par l'IA, par exemple)
                if (typeof(T) == typeof(string))
                    return new ApiResult<T>((T)(object)await response.Content.ReadAsStringAsync(), null, response.StatusCode);
                var value = await response.Content.ReadFromJsonAsync<T>(Json);
                return new ApiResult<T>(value, null, response.StatusCode);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && session.IsAuthenticated)
                await session.ExpireAsync();

            return new ApiResult<T>(default, await ReadErrorAsync(response), response.StatusCode);
        }
        catch (HttpRequestException)
        {
            return new ApiResult<T>(default, "Service injoignable. Vérifiez votre connexion et réessayez.", HttpStatusCode.ServiceUnavailable);
        }
        catch (TaskCanceledException)
        {
            return new ApiResult<T>(default, "Le service met trop de temps à répondre.", HttpStatusCode.RequestTimeout);
        }
    }

    /// <summary>
    /// Requête dont la réponse arrive en NDJSON, ligne par ligne : {"type":"delta","text":…} transmis à
    /// <paramref name="onDelta"/> au fur et à mesure, puis {"type":"done",…} (le résultat) ou {"type":"error","message":…}.
    /// Une erreur avant le début du flux (400, 403, 404…) se lit comme pour un appel ordinaire.
    /// </summary>
    protected async Task<ApiResult<T>> PostStreamAsync<T>(string path, object body, Func<string, Task> onDelta, CancellationToken ct)
    {
        try
        {
            await RenewSessionIfDueAsync();
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, body.GetType(), options: Json) };
            if (session.Token is { } token)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await factory.CreateClient(clientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized && session.IsAuthenticated)
                    await session.ExpireAsync();
                return new ApiResult<T>(default, await ReadErrorAsync(response), response.StatusCode);
            }

            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                switch (root.TryGetProperty("type", out var type) ? type.GetString() : null)
                {
                    case "delta":
                        if (root.TryGetProperty("text", out var text) && text.GetString() is { Length: > 0 } piece)
                            await onDelta(piece);
                        break;
                    case "done":
                        return new ApiResult<T>(root.Deserialize<T>(Json), null, response.StatusCode);
                    case "error":
                        return new ApiResult<T>(default, root.TryGetProperty("message", out var m) ? m.GetString() : "Une erreur est survenue. Réessayez.",
                            HttpStatusCode.BadGateway);
                }
            }
            return new ApiResult<T>(default, "La réponse a été interrompue. Réessayez.", HttpStatusCode.BadGateway);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new ApiResult<T>(default, "Demande annulée.", HttpStatusCode.RequestTimeout);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException)
        {
            return new ApiResult<T>(default, "La connexion au service a été coupée. Réessayez.", HttpStatusCode.ServiceUnavailable);
        }
        catch (TaskCanceledException)
        {
            return new ApiResult<T>(default, "Le service met trop de temps à répondre.", HttpStatusCode.RequestTimeout);
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
                if (root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Array)
                {
                    var details = e.EnumerateArray().Select(x => x.GetString()).Where(x => x is not null);
                    return string.Join("\n", new[] { message }.Concat(details).Where(x => !string.IsNullOrEmpty(x)));
                }
                if (root.TryGetProperty("errors", out var ve) && ve.ValueKind == JsonValueKind.Object)
                    return string.Join("\n", ve.EnumerateObject().SelectMany(p => p.Value.EnumerateArray().Select(x => x.GetString())));
                if (message is not null) return message;
                if (root.TryGetProperty("title", out var t)) return t.GetString() ?? "";
            }
        }
        catch (JsonException) { }

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Votre session a expiré. Reconnectez-vous.",
            HttpStatusCode.Forbidden => "Vous n'avez pas accès à cette action.",
            HttpStatusCode.NotFound => "Élément introuvable.",
            HttpStatusCode.ServiceUnavailable => "Un service est momentanément indisponible.",
            _ => "Une erreur est survenue. Réessayez."
        };
    }
}
