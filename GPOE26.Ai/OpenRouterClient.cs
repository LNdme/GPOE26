using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GPOE26.Ai;

/// <summary>
/// Client de la passerelle OpenRouter (API compatible OpenAI).
///
/// Trois usages, une seule clé :
///   • complétion texte      → <see cref="CompleteAsync"/> / <see cref="StreamAsync"/>
///   • complétion vision     → <see cref="CompleteAsync"/> avec <see cref="AgentRequest.Images"/>
///   • vectorisation         → <see cref="EmbedAsync"/>
///
/// Le modèle employé dépend de l'agent appelant (<see cref="AgentKind"/>) et se règle
/// entièrement par configuration.
/// </summary>
public sealed class OpenRouterClient(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenRouterOptions> options,
    ILogger<OpenRouterClient> logger)
{
    public const string HttpClientName = "openrouter";

    private readonly OpenRouterOptions _options = options.Value;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public OpenRouterOptions Options => _options;

    // ── Complétion ────────────────────────────────────────────────────────────────

    public async Task<string> CompleteAsync(AgentRequest request, CancellationToken ct = default)
    {
        var model = _options.ModelFor(request.Kind);
        var body = BuildChatBody(request, model, stream: false);

        using var http = CreateClient();
        using var response = await http.PostAsJsonAsync("chat/completions", body, SerializerOptions, ct);

        var payload = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess(response.StatusCode, payload, request.Kind, model);

        using var document = JsonDocument.Parse(payload);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        logger.LogInformation("OpenRouter {Agent} via {Model} : {Length} caractères",
            request.Kind, model, content.Length);

        return content;
    }

    /// <summary>
    /// Complétion en flux : renvoie les fragments de texte au fil de leur génération.
    /// Utilisé par l'endpoint SSE du répétiteur, pour que l'élève voie la réponse
    /// s'écrire au lieu d'attendre la fin du pipeline.
    /// </summary>
    public async IAsyncEnumerable<string> StreamAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = _options.ModelFor(request.Kind);
        var body = BuildChatBody(request, model, stream: true);

        using var http = CreateClient();
        using var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(body, options: SerializerOptions),
        };

        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            EnsureSuccess(response.StatusCode, error, request.Kind, model);
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;

            // OpenRouter envoie des commentaires SSE (": OPENROUTER PROCESSING") pour
            // maintenir la connexion pendant les files d'attente : à ignorer.
            if (line.StartsWith(':')) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var data = line[5..].Trim();
            if (data is "[DONE]") yield break;

            string? delta = null;
            try
            {
                using var document = JsonDocument.Parse(data);
                if (document.RootElement.TryGetProperty("choices", out var choices)
                    && choices.GetArrayLength() > 0
                    && choices[0].TryGetProperty("delta", out var deltaElement)
                    && deltaElement.TryGetProperty("content", out var contentElement))
                {
                    delta = contentElement.GetString();
                }
            }
            catch (JsonException)
            {
                // Fragment SSE tronqué : on l'ignore plutôt que d'interrompre le flux.
                continue;
            }

            if (!string.IsNullOrEmpty(delta)) yield return delta;
        }
    }

    // ── Vectorisation ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Vectorise un lot de textes. L'ordre des vecteurs renvoyés suit celui des entrées
    /// (le champ <c>index</c> de la réponse est utilisé pour le garantir).
    /// </summary>
    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken ct = default)
    {
        if (inputs.Count == 0) return [];

        var model = _options.ModelFor(AgentKind.Embedding);
        var body = new { model, input = inputs };

        using var http = CreateClient();
        using var response = await http.PostAsJsonAsync("embeddings", body, SerializerOptions, ct);

        var payload = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess(response.StatusCode, payload, AgentKind.Embedding, model);

        using var document = JsonDocument.Parse(payload);
        var data = document.RootElement.GetProperty("data");

        var slots = new Dictionary<int, float[]>(inputs.Count);
        foreach (var item in data.EnumerateArray())
        {
            var index = item.TryGetProperty("index", out var indexElement) ? indexElement.GetInt32() : slots.Count;
            var embedding = item.GetProperty("embedding");

            var vector = new float[embedding.GetArrayLength()];
            var i = 0;
            foreach (var component in embedding.EnumerateArray())
                vector[i++] = component.GetSingle();

            slots[index] = vector;
        }

        var vectors = new float[inputs.Count][];
        for (var i = 0; i < inputs.Count; i++)
        {
            if (!slots.TryGetValue(i, out var vector))
                throw new OpenRouterException(
                    $"Le modèle d'embedding {model} n'a pas renvoyé de vecteur pour l'entrée {i}.");

            if (vector.Length != _options.EmbeddingDimensions)
                throw new OpenRouterException(
                    $"Le modèle {model} produit des vecteurs de dimension {vector.Length}, " +
                    $"alors que le schéma attend {_options.EmbeddingDimensions}. " +
                    "Alignez OpenRouter:EmbeddingDimensions et régénérez la migration EF.");

            vectors[i] = vector;
        }

        return vectors;
    }

    // ── Synthèse vocale ───────────────────────────────────────────────────────────

    /// <summary>
    /// Lit un texte à voix haute et renvoie l'audio MP3.
    ///
    /// Contrairement aux autres appels, la réponse est **binaire** : l'endpoint renvoie
    /// le flux audio directement, pas du JSON. Une erreur, elle, revient bien en JSON —
    /// d'où la vérification du code HTTP avant de lire les octets.
    /// </summary>
    public async Task<byte[]> SynthesizeSpeechAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        // Garde-fou de coût : le TTS se facture au caractère.
        if (text.Length > _options.MaxSpeechCharacters)
            text = text[.._options.MaxSpeechCharacters];

        var model = _options.ModelFor(AgentKind.Voix);

        var body = new
        {
            model,
            input = text,
            voice = _options.Voice,
            response_format = "mp3",
        };

        using var http = CreateClient();
        using var response = await http.PostAsJsonAsync("audio/speech", body, SerializerOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Synthèse vocale ({Model}) → HTTP {Status} : {Payload}",
                model, (int)response.StatusCode, Truncate(error));

            throw new OpenRouterException(
                $"La synthèse vocale a échoué ({(int)response.StatusCode}) : {Truncate(error)}");
        }

        var audio = await response.Content.ReadAsByteArrayAsync(ct);

        logger.LogInformation("Synthèse vocale via {Model} : {Characters} caractères → {Bytes} octets",
            model, text.Length, audio.Length);

        return audio;
    }

    // ── Catalogue ─────────────────────────────────────────────────────────────────

    /// <summary>Liste les modèles disponibles, pour permettre de les choisir depuis l'application.</summary>
    public async Task<IReadOnlyList<AgentModelInfo>> ListModelsAsync(CancellationToken ct = default)
    {
        using var http = CreateClient();
        using var response = await http.GetAsync("models", ct);

        var payload = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new OpenRouterException($"OpenRouter GET /models a répondu {(int)response.StatusCode} : {Truncate(payload)}");

        using var document = JsonDocument.Parse(payload);

        var models = new List<AgentModelInfo>();
        foreach (var item in document.RootElement.GetProperty("data").EnumerateArray())
        {
            models.Add(new AgentModelInfo(
                item.GetProperty("id").GetString() ?? string.Empty,
                item.TryGetProperty("name", out var name) ? name.GetString() : null,
                item.TryGetProperty("description", out var description) ? description.GetString() : null,
                item.TryGetProperty("context_length", out var context) && context.ValueKind == JsonValueKind.Number
                    ? context.GetInt32()
                    : null));
        }

        return models;
    }

    // ── Interne ───────────────────────────────────────────────────────────────────

    private HttpClient CreateClient()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new OpenRouterException(
                "OpenRouter:ApiKey n'est pas configurée. Renseignez la variable d'environnement " +
                "OpenRouter__ApiKey (injectée par l'AppHost) — la clé ne doit pas être écrite dans appsettings.json.");

        var http = httpClientFactory.CreateClient(HttpClientName);

        // BaseAddress doit se terminer par '/' pour que les chemins relatifs
        // ("chat/completions") se concatènent au lieu de remplacer le dernier segment.
        http.BaseAddress ??= new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        if (!string.IsNullOrWhiteSpace(_options.Referer))
            http.DefaultRequestHeaders.TryAddWithoutValidation("HTTP-Referer", _options.Referer);

        if (!string.IsNullOrWhiteSpace(_options.Title))
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-Title", _options.Title);

        return http;
    }

    private object BuildChatBody(AgentRequest request, string model, bool stream)
    {
        var messages = new List<object>();

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            messages.Add(new { role = "system", content = request.SystemPrompt });

        for (var i = 0; i < request.Turns.Count; i++)
        {
            var turn = request.Turns[i];
            var isLastUserTurn = i == request.Turns.Count - 1 && turn.Role == "user";

            // Les images ne sont jointes qu'au dernier tour utilisateur : le contenu
            // devient alors un tableau de blocs, format attendu par les modèles vision.
            if (isLastUserTurn && request.Images.Count > 0)
            {
                var blocks = new List<object> { new { type = "text", text = turn.Content } };
                foreach (var image in request.Images)
                    blocks.Add(new { type = "image_url", image_url = new { url = image.ToDataUri() } });

                messages.Add(new { role = turn.Role, content = blocks });
            }
            else
            {
                messages.Add(new { role = turn.Role, content = turn.Content });
            }
        }

        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = messages,
            ["max_tokens"] = request.MaxTokens ?? _options.DefaultMaxTokens,
        };

        if (stream) body["stream"] = true;
        if (request.JsonMode) body["response_format"] = new { type = "json_object" };

        return body;
    }

    /// <summary>
    /// OpenRouter signale certaines erreurs (modèle inconnu, quota, filtrage fournisseur)
    /// dans un objet <c>error</c> avec un statut HTTP 200 : vérifier le seul code HTTP
    /// laisserait passer une réponse vide.
    /// </summary>
    private void EnsureSuccess(System.Net.HttpStatusCode status, string payload, AgentKind kind, string model)
    {
        if ((int)status is >= 200 and < 300)
        {
            try
            {
                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.TryGetProperty("error", out var error))
                {
                    var message = error.TryGetProperty("message", out var m) ? m.GetString() : payload;
                    throw new OpenRouterException($"OpenRouter a refusé l'appel {kind} ({model}) : {message}");
                }
            }
            catch (JsonException)
            {
                throw new OpenRouterException(
                    $"Réponse OpenRouter illisible pour {kind} ({model}) : {Truncate(payload)}");
            }

            return;
        }

        logger.LogError("OpenRouter {Agent} ({Model}) → HTTP {Status} : {Payload}",
            kind, model, (int)status, Truncate(payload));

        throw new OpenRouterException(
            $"OpenRouter a répondu {(int)status} pour {kind} ({model}) : {Truncate(payload)}");
    }

    private static string Truncate(string value) =>
        value.Length <= 500 ? value : value[..500] + "…";
}
