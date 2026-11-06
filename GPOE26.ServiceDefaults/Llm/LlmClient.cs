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

    /// <summary>
    /// Envoie la conversation et renvoie la réponse du premier modèle qui répond.
    /// Les modèles de vision sont utilisés dès qu'un message contient une image.
    /// </summary>
    public async Task<string> CompleteAsync(IReadOnlyList<LlmMessage> messages, int maxTokens = 4000,
        double temperature = 0.4, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new LlmUnavailableException("Aucune clé LLM n'est configurée (LLM__ApiKey).");

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
                var reply = await CallAsync(model, messages, maxTokens, temperature, attempt.Token);
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

    private async Task<string?> CallAsync(string model, IReadOnlyList<LlmMessage> messages, int maxTokens,
        double temperature, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens,
            ["temperature"] = temperature,
            ["messages"] = new JsonArray(messages.Select(ToJson).ToArray())
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        // En-têtes recommandés par OpenRouter (ignorés par les autres fournisseurs)
        request.Headers.TryAddWithoutValidation("X-Title", "GPOE26");

        using var response = await http.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode} {Truncate(json)}", null, response.StatusCode);

        using var doc = JsonDocument.Parse(json);
        // OpenRouter peut renvoyer 200 avec un objet "error" (fournisseur saturé)
        if (doc.RootElement.TryGetProperty("error", out var error))
            throw new HttpRequestException(Truncate(error.ToString()), null, HttpStatusCode.BadGateway);

        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
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
