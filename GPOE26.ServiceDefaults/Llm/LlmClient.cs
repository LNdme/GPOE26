using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GPOE26.ServiceDefaults.Llm;

/// <summary>Image jointe à un message (photo d'une copie, d'un exercice…).</summary>
public record LlmImage(string ContentType, byte[] Data)
{
    public string DataUrl => $"data:{ContentType};base64,{Convert.ToBase64String(Data)}";
}

/// <summary>Message envoyé au modèle. Role : "system", "user" ou "assistant".</summary>
public record LlmMessage(string Role, string Content, IReadOnlyList<LlmImage>? Images = null);

public class LlmUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Aucun fournisseur d'IA n'est configuré : réessayer ne servira à rien.</summary>
public class LlmNotConfiguredException() : LlmUnavailableException("Aucune clé LLM n'est configurée (LLM__ApiKey).");

/// <summary>
/// Client d'un LLM compatible OpenAI (OpenRouter, DeepSeek, OpenAI, Ollama…).
/// Configuration (section "LLM") :
///   BaseUrl       ex. https://openrouter.ai/api/v1
///   ApiKey        clé (secret : variable d'environnement LLM__ApiKey)
///   Models        modèles texte, séparés par des virgules, essayés dans l'ordre (repli si l'un est saturé)
///   VisionModels  modèles capables de lire une image (sinon : Models)
/// Les anciens réglages "LLM:Model" restent acceptés (un seul modèle).
/// </summary>
public class LlmClient(HttpClient http, IConfiguration config, ILogger<LlmClient> logger)
{
    private string BaseUrl => (config["LLM:BaseUrl"] ?? "https://openrouter.ai/api/v1").TrimEnd('/');
    private string? ApiKey => config["LLM:ApiKey"];

    public IReadOnlyList<string> TextModels => Split(config["LLM:Models"] ?? config["LLM:Model"] ?? "openrouter/free");
    public IReadOnlyList<string> VisionModels => config["LLM:VisionModels"] is { Length: > 0 } v ? Split(v) : TextModels;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) || BaseUrl.Contains("localhost");
    private bool IsOpenRouter => BaseUrl.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Envoie la conversation et renvoie la réponse du premier modèle qui répond.
    /// Les modèles de vision sont utilisés dès qu'un message contient une image.
    /// </summary>
    /// <param name="requireComplete">
    /// Une réponse coupée par la limite de longueur compte comme un échec : le modèle suivant est essayé.
    /// Pour les retranscriptions, où une fin manquante serait du contenu perdu.
    /// </param>
    /// <param name="reasoning">
    /// False pour une tâche de recopie (retranscription d'une page) : un modèle « raisonnant » y dépense des milliers
    /// de jetons de réflexion, dépasse le délai ou la limite de longueur, et la page n'est jamais lue.
    /// </param>
    public async Task<string> CompleteAsync(IReadOnlyList<LlmMessage> messages, int maxTokens = 4000,
        double temperature = 0.4, CancellationToken ct = default, bool requireComplete = false, bool reasoning = true)
    {
        if (!IsConfigured)
            throw new LlmNotConfiguredException();

        var vision = messages.Any(m => m.Images is { Count: > 0 });
        var models = vision ? VisionModels : TextModels;
        Exception? last = null;

        foreach (var model in models)
        {
            try
            {
                // Délai par modèle : un fournisseur gratuit saturé ne doit pas bloquer les suivants
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attempt.CancelAfter(TimeSpan.FromSeconds(int.TryParse(config["LLM:AttemptTimeoutSeconds"], out var s) ? s : 90));
                var reply = await CallAsync(model, messages, maxTokens, temperature, requireComplete, reasoning, attempt.Token);
                if (!string.IsNullOrWhiteSpace(reply)) return reply.Trim();
                logger.LogWarning("Réponse vide du modèle {Model}, essai du suivant", model);
                last = new LlmUnavailableException($"Réponse vide de {model}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                logger.LogWarning("Modèle {Model} indisponible : {Error}", model, ex.Message);
                last = ex;
            }
        }

        throw new LlmUnavailableException("Aucun modèle n'a pu répondre. Réessayez dans un instant.", last);
    }

    /// <summary>
    /// Comme <see cref="CompleteAsync"/>, mais la réponse arrive morceau par morceau, dès que le modèle l'écrit.
    /// Le modèle suivant n'est essayé que si le précédent échoue avant d'avoir rien écrit ; une coupure
    /// au milieu de la réponse lève <see cref="LlmUnavailableException"/> (le début déjà reçu reste à l'appelant).
    /// </summary>
    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<LlmMessage> messages, int maxTokens = 4000,
        double temperature = 0.4, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new LlmNotConfiguredException();

        var vision = messages.Any(m => m.Images is { Count: > 0 });
        var models = vision ? VisionModels : TextModels;
        var firstToken = TimeSpan.FromSeconds(int.TryParse(config["LLM:AttemptTimeoutSeconds"], out var s) ? s : 90);
        Exception? last = null;

        foreach (var model in models)
        {
            // Délai jusqu'au premier mot : un fournisseur gratuit saturé ne doit pas bloquer les suivants.
            // Une fois la réponse commencée, elle a jusqu'à quatre minutes pour se terminer.
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attempt.CancelAfter(firstToken);

            HttpResponseMessage? response = null;
            Stream? body = null;
            try
            {
                using var request = BuildRequest(model, messages, maxTokens, temperature, reasoning: true, stream: true);
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attempt.Token);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"{(int)response.StatusCode} {Truncate(await response.Content.ReadAsStringAsync(attempt.Token))}",
                        null, response.StatusCode);
                body = await response.Content.ReadAsStreamAsync(attempt.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                response?.Dispose();
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning("Modèle {Model} indisponible : {Error}", model, ex.Message);
                response?.Dispose();
                last = ex;
                continue;
            }

            using (response)
            await using (body)
            {
                var reader = new StreamReader(body, Encoding.UTF8);
                var thinking = new ThinkingFilter();
                var wrote = false;
                while (true)
                {
                    string? piece;
                    try
                    {
                        var line = await reader.ReadLineAsync(attempt.Token);
                        if (line is null) break;
                        piece = ReadStreamLine(line, out var done);
                        if (done) break;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException or JsonException)
                    {
                        if (wrote)
                            throw new LlmUnavailableException("La réponse a été interrompue. Réessayez dans un instant.", ex);
                        logger.LogWarning("Modèle {Model} interrompu avant de répondre : {Error}", model, ex.Message);
                        last = ex;
                        break;
                    }

                    if (thinking.Visible(piece) is not { Length: > 0 } visible) continue;
                    if (!wrote)
                    {
                        wrote = true;
                        attempt.CancelAfter(TimeSpan.FromMinutes(4));
                    }
                    yield return visible;
                }

                if (wrote) yield break;
                if (last is null)
                    logger.LogWarning("Réponse vide du modèle {Model}, essai du suivant", model);
                last ??= new LlmUnavailableException($"Réponse vide de {model}");
            }
        }

        throw new LlmUnavailableException("Aucun modèle n'a pu répondre. Réessayez dans un instant.", last);
    }

    /// <summary>
    /// Une ligne du flux « server-sent events » d'une API compatible OpenAI : renvoie le texte ajouté (ou null),
    /// et <paramref name="done"/> à la fin du flux. Une erreur du fournisseur dans le flux lève HttpRequestException.
    /// </summary>
    public static string? ReadStreamLine(string line, out bool done)
    {
        done = false;
        if (!line.StartsWith("data:", StringComparison.Ordinal)) return null;   // commentaires « : OPENROUTER PROCESSING », lignes vides
        var data = line[5..].Trim();
        if (data == "[DONE]") { done = true; return null; }
        if (data.Length == 0) return null;

        using var doc = JsonDocument.Parse(data);
        if (doc.RootElement.TryGetProperty("error", out var error))
            throw new HttpRequestException(Truncate(error.ToString()), null, HttpStatusCode.BadGateway);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) return null;
        return choices[0].TryGetProperty("delta", out var delta)
               && delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
            ? content.GetString()
            : null;
    }

    /// <summary>
    /// Au fil du flux, cache la réflexion qu'un modèle « raisonnant » écrit entre &lt;think&gt; et &lt;/think&gt;
    /// au début de sa réponse (l'équivalent de <see cref="StripThinking"/> pour une réponse en morceaux).
    /// </summary>
    public sealed class ThinkingFilter
    {
        private readonly StringBuilder _pending = new();
        private bool _decided, _inside;

        public string? Visible(string? piece)
        {
            if (string.IsNullOrEmpty(piece)) return null;
            if (_decided && !_inside) return piece;

            _pending.Append(piece);
            var text = _pending.ToString();
            if (!_decided)
            {
                var start = text.TrimStart();
                // Pas encore assez de texte pour savoir si la réponse commence par <think>
                if (start.Length < "<think>".Length && "<think>".StartsWith(start, StringComparison.OrdinalIgnoreCase)) return null;
                _decided = true;
                _inside = start.StartsWith("<think>", StringComparison.OrdinalIgnoreCase);
                if (!_inside) { _pending.Clear(); return text; }
            }

            var end = text.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
            if (end < 0) return null;
            _inside = false;
            _pending.Clear();
            var rest = text[(end + "</think>".Length)..].TrimStart();
            return rest.Length > 0 ? rest : null;
        }
    }

    /// <summary>Vecteur de sens d'un texte (réglage LLM:EmbeddingModel). Null si aucun modèle n'est configuré ou en cas d'échec.</summary>
    public async Task<float[]?> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (!IsConfigured || config["LLM:EmbeddingModel"] is not { Length: > 0 } model) return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/embeddings")
            {
                Content = new StringContent(new JsonObject { ["model"] = model, ["input"] = text }.ToJsonString(), Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.GetProperty("data")[0].GetProperty("embedding").EnumerateArray().Select(v => v.GetSingle()).ToArray();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning("Embedding indisponible : {Error}", ex.Message);
            return null;
        }
    }

    private HttpRequestMessage BuildRequest(string model, IReadOnlyList<LlmMessage> messages, int maxTokens,
        double temperature, bool reasoning, bool stream)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens,
            ["temperature"] = temperature,
            ["messages"] = new JsonArray(messages.Select(ToJson).ToArray())
        };
        if (stream)
            body["stream"] = true;
        // Paramètre propre à OpenRouter : d'autres fournisseurs compatibles OpenAI refuseraient un champ inconnu
        if (!reasoning && IsOpenRouter)
            body["reasoning"] = new JsonObject { ["enabled"] = false };

        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        // En-têtes recommandés par OpenRouter (ignorés par les autres fournisseurs)
        request.Headers.TryAddWithoutValidation("X-Title", "GPOE26");
        return request;
    }

    private async Task<string?> CallAsync(string model, IReadOnlyList<LlmMessage> messages, int maxTokens,
        double temperature, bool requireComplete, bool reasoning, CancellationToken ct)
    {
        using var request = BuildRequest(model, messages, maxTokens, temperature, reasoning, stream: false);
        using var response = await http.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode} {Truncate(json)}", null, response.StatusCode);

        using var doc = JsonDocument.Parse(json);
        // OpenRouter peut renvoyer 200 avec un objet "error" (fournisseur saturé)
        if (doc.RootElement.TryGetProperty("error", out var error))
            throw new HttpRequestException(Truncate(error.ToString()), null, HttpStatusCode.BadGateway);

        var choice = doc.RootElement.GetProperty("choices")[0];
        if (requireComplete && choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String && finish.GetString() == "length")
            throw new InvalidOperationException("Réponse coupée par la limite de longueur");

        var message = choice.GetProperty("message");
        var content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        return StripThinking(content);
    }

    private static JsonNode ToJson(LlmMessage m)
    {
        if (m.Images is not { Count: > 0 })
            return new JsonObject { ["role"] = m.Role, ["content"] = m.Content };

        var parts = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = m.Content } };
        foreach (var image in m.Images)
            parts.Add(new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject { ["url"] = image.DataUrl }
            });
        return new JsonObject { ["role"] = m.Role, ["content"] = parts };
    }

    /// <summary>Certains modèles « raisonnants » placent leur réflexion dans la réponse : on ne garde que la réponse.</summary>
    public static string? StripThinking(string? content)
    {
        if (content is null) return null;
        var end = content.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        return end >= 0 ? content[(end + "</think>".Length)..] : content;
    }

    private static IReadOnlyList<string> Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Truncate(string s) => s.Length > 300 ? s[..300] + "…" : s;
}

public static class LlmClientExtensions
{
    /// <summary>Enregistre LlmClient (délai généreux : les modèles gratuits sont lents, le repli est géré par le client).</summary>
    public static IHostApplicationBuilder AddLlmClient(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHttpClient<LlmClient>(client => client.Timeout = TimeSpan.FromMinutes(4));
        return builder;
    }
}
