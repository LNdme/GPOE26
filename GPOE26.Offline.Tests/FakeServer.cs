using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GPOE26.Offline.Tests;

/// <summary>
/// Faux services Cours / Quiz : réponses programmées par chemin, et un interrupteur « réseau coupé »
/// qui fait échouer toute requête comme une vraie coupure (HttpRequestException).
/// </summary>
public class FakeServer : HttpMessageHandler
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public bool Down { get; set; }
    public List<(string Method, string Path, string? Body)> Requests { get; } = new();
    private readonly Dictionary<string, Func<string?, (HttpStatusCode, object?)>> _routes = new();

    public void On(string method, string path, Func<string?, (HttpStatusCode, object?)> reply) => _routes[$"{method} {path}"] = reply;
    public void On(string method, string path, object reply) => On(method, path, _ => (HttpStatusCode.OK, reply));

    /// <summary>Le service Cours sérialise ses enums en nombres</summary>
    public void OnNumeric(string method, string path, object reply) =>
        On(method, path, _ => (HttpStatusCode.OK, JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(reply, NumericJson))));

    private static readonly JsonSerializerOptions NumericJson = new(JsonSerializerDefaults.Web);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (Down) throw new HttpRequestException("Connexion refusée (réseau coupé)");
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request.Method.Method, path, body));

        if (!_routes.TryGetValue($"{request.Method.Method} {path}", out var reply))
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = Text("{\"message\":\"introuvable\"}") };

        var (status, value) = reply(body);
        return new HttpResponseMessage(status) { Content = Text(JsonSerializer.Serialize(value, Json)) };
    }

    private static StringContent Text(string s) => new(s, Encoding.UTF8, "application/json");
}
