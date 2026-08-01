using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using GPOE26.Harness.Contract;

namespace GPOE26.Harness.Conformance;

/// <summary>Ce qu'un tour a produit : ses évènements, dans l'ordre, et ce qu'il a coûté en temps.</summary>
/// <param name="FirstTokenAfter">
/// Délai avant le premier fragment de réponse. C'est ce que l'élève ressent comme
/// « l'attente » — bien plus que la durée totale, pendant laquelle il lit déjà.
/// </param>
public record Transcript(
    IReadOnlyList<HarnessEvent> Events,
    TimeSpan Total,
    TimeSpan? FirstTokenAfter,
    bool SawDoneMarker
)
{
    public IEnumerable<string> ToolsCalled =>
        Events.Where(e => e.Type == HarnessEventType.ToolCall && e.Tool is not null).Select(e => e.Tool!);

    public HarnessEvent? Terminal => Events.LastOrDefault(e => e.IsTerminal);

    public string Text => string.Concat(Events.Where(e => e.Type == HarnessEventType.Token).Select(e => e.Token));

    public int Iterations => Events.Where(e => e.Iteration is { } i).Select(e => e.Iteration!.Value).DefaultIfEmpty(0).Max();
}

public static class TurnReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Joue un tour et lit son flux jusqu'au bout.
    ///
    /// Un délai maximal est imposé : une implémentation qui n'émet jamais d'évènement
    /// terminal doit faire échouer le test, pas suspendre la suite. C'est exactement le
    /// défaut que reproduit le sabotage <c>NoTerminalEvent</c>.
    /// </summary>
    public static async Task<Transcript> PlayAsync(
        HttpClient client, Guid sessionId, string message, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(2));
        var ct = cts.Token;

        var events = new List<HarnessEvent>();
        var watch = Stopwatch.StartNew();
        TimeSpan? firstToken = null;
        var sawDone = false;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"sessions/{sessionId}/tour")
        {
            Content = JsonContent.Create(new TurnRequest(message), options: Json),
        };

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0 || line.StartsWith(':')) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var payload = line[5..].Trim();
            if (payload == "[DONE]") { sawDone = true; break; }

            HarnessEvent? evt;
            try
            {
                evt = JsonSerializer.Deserialize<HarnessEvent>(payload, Json);
            }
            catch (JsonException)
            {
                // Un fragment illisible est un manquement au contrat : on le garde tel
                // quel pour que le test l'affiche, plutôt que de l'ignorer en silence.
                evt = new HarnessEvent("illisible", Error: payload);
            }

            if (evt is null) continue;

            if (firstToken is null && evt.Type == HarnessEventType.Token)
                firstToken = watch.Elapsed;

            events.Add(evt);
        }

        watch.Stop();
        return new Transcript(events, watch.Elapsed, firstToken, sawDone);
    }

    /// <summary>Ouvre une session et renvoie son identifiant.</summary>
    public static async Task<SessionDto> OpenAsync(
        HttpClient client, Guid courseId, SessionKind kind, Guid? studentId = null)
    {
        var response = await client.PostAsJsonAsync("sessions", new OpenSessionRequest(courseId, kind, studentId), Json);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<SessionDto>(Json))!;
    }
}
