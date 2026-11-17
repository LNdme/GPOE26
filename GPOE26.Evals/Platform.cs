using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GPOE26.Evals;

/// <summary>Réglages lus dans les variables d'environnement (Evals__…) : adresses des services et comptes d'essai.</summary>
public record EvalSettings(string UserUrl, string CoursUrl, string ChatUrl,
    Account? Student, Account? Teacher, Account? Editor, string? TutorModel)
{
    public static EvalSettings From(Microsoft.Extensions.Configuration.IConfiguration c)
    {
        Account? Read(string who) =>
            c[$"Evals:{who}Email"] is { Length: > 0 } email && c[$"Evals:{who}Password"] is { Length: > 0 } password
                ? new Account(email, password) : null;
        var teacher = Read("Teacher");
        return new EvalSettings(
            (c["Evals:UserUrl"] ?? "http://localhost:5197").TrimEnd('/'),
            (c["Evals:CoursUrl"] ?? "http://localhost:5188").TrimEnd('/'),
            (c["Evals:ChatUrl"] ?? "http://localhost:5212").TrimEnd('/'),
            Read("Student"), teacher, Read("Editor") ?? teacher, c["Evals:TutorModel"]);
    }
}

public record Account(string Email, string Password);

/// <summary>Le service a refusé ou n'a pas pu répondre : le cas n'est pas noté.</summary>
public class NotPlayedException(string reason, bool infrastructure) : Exception(reason)
{
    /// <summary>Vrai : panne (modèle, réseau) ; faux : refus attendu (droit, quota, compte absent).</summary>
    public bool Infrastructure { get; } = infrastructure;
}

/// <summary>Appels aux services, avec le jeton du compte d'essai ; mêmes routes que l'interface.</summary>
public partial class Platform(EvalSettings settings) : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly Dictionary<string, string> _tokens = [];
    private readonly Dictionary<string, Guid> _courses = [];

    private record LoginResponse(string Token);
    private record CourseSummary(Guid Id, string Title, string? Description);
    private record ImageUploaded(Guid Id);
    private record TutorMessage(string Content, string? Model);
    private record TutorAnswer(TutorMessage Answer);
    private record ComposeAnswer(string Markdown);
    private record SiteReply(string Reply, List<ProposedAction> Actions, List<string> Rejected);

    public async Task<string> TokenAsync(Account? account, string who, CancellationToken ct)
    {
        if (account is null)
            throw new NotPlayedException($"compte {who} non configuré (Evals__{who}Email / Evals__{who}Password)", false);
        if (_tokens.TryGetValue(account.Email, out var cached)) return cached;
        using var response = await _http.PostAsJsonAsync($"{settings.UserUrl}/auth/login", new { email = account.Email, password = account.Password }, ct);
        if (!response.IsSuccessStatusCode)
            throw new NotPlayedException($"connexion {who} refusée ({(int)response.StatusCode})", false);
        var token = (await response.Content.ReadFromJsonAsync<LoginResponse>(CaseLoader.Json, ct))!.Token;
        return _tokens[account.Email] = token;
    }

    // ─── Cours d'essai ───────────────────────────────────────────────────────────

    /// <summary>
    /// Cours d'essai du compte élève, créé à la première passe puis réutilisé ; recréé si le fichier a changé
    /// (son empreinte est dans la description). Le contenu tient dans une section Markdown, comme dans l'éditeur.
    /// </summary>
    public async Task<Guid> CourseAsync(string key, string fixturesDir, CancellationToken ct)
    {
        if (_courses.TryGetValue(key, out var known)) return known;
        var token = await TokenAsync(settings.Student, "Student", ct);
        var fixture = CourseFixture.Load(Path.Combine(fixturesDir, "courses", key + ".md"));
        var marker = $"eval-fixture:{key}:{fixture.Hash}";

        var mine = await SendAsync<List<CourseSummary>>(HttpMethod.Get, $"{settings.CoursUrl}/cours", token, null, ct) ?? [];
        foreach (var stale in mine.Where(c => c.Description?.Contains($"eval-fixture:{key}:") == true && c.Description != marker))
            await SendAsync<JsonElement?>(HttpMethod.Delete, $"{settings.CoursUrl}/cours/{stale.Id}", token, null, ct);
        if (mine.FirstOrDefault(c => c.Description == marker) is { } existing) return _courses[key] = existing.Id;

        var created = await SendAsync<CourseSummary>(HttpMethod.Post, $"{settings.CoursUrl}/cours", token, new
        {
            title = fixture.Title,
            subject = fixture.Subject,
            description = marker,
            sections = new[] { new { type = 1, content = fixture.Markdown, order = 0, level = 0 } }
        }, ct);
        return _courses[key] = created!.Id;
    }

    // ─── Agents ──────────────────────────────────────────────────────────────────

    public async Task<AgentOutput> AskTutorAsync(EvalCase c, string fixturesDir, CancellationToken ct)
    {
        var token = await TokenAsync(settings.Student, "Student", ct);
        var courseId = await CourseAsync(c.Course!, fixturesDir, ct);
        var url = $"{settings.ChatUrl}/tutor/courses/{courseId}";
        // Chaque cas part d'une discussion vide : les cas précédents ne doivent pas servir d'historique
        await SendAsync<JsonElement?>(HttpMethod.Delete, url, token, null, ct);
        try
        {
            foreach (var turn in c.Turns)
                await SendAsync<TutorAnswer>(HttpMethod.Post, url + "/messages", token, new { message = turn, model = settings.TutorModel }, ct);

            Guid? imageId = c.Image is null ? null : await UploadImageAsync(Path.Combine(fixturesDir, "images", c.Image), token, ct);
            var reply = await SendAsync<TutorAnswer>(HttpMethod.Post, url + "/messages", token,
                new { message = c.Message ?? "", imageId, model = settings.TutorModel }, ct);
            return new AgentOutput(reply!.Answer.Content, [], [], reply.Answer.Model);
        }
        finally
        {
            await SendAsync<JsonElement?>(HttpMethod.Delete, url, token, null, CancellationToken.None);
        }
    }

    public async Task<AgentOutput> ComposeAsync(EvalCase c, CancellationToken ct)
    {
        var token = await TokenAsync(settings.Teacher, "Teacher", ct);
        var reply = await SendAsync<ComposeAnswer>(HttpMethod.Post, $"{settings.ChatUrl}/teacher-agent/compose", token, c.Compose, ct);
        return new AgentOutput(reply!.Markdown, [], []);
    }

    /// <summary>L'agent du site propose ; rien n'est exécuté ici (l'exécution se fait dans le navigateur, après confirmation).</summary>
    public async Task<AgentOutput> AskSiteAsync(EvalCase c, CancellationToken ct)
    {
        var token = await TokenAsync(settings.Editor, "Editor", ct);
        var reply = await SendAsync<SiteReply>(HttpMethod.Post, $"{settings.ChatUrl}/site-agent/ask", token,
            new { message = c.Message, history = c.History }, ct);
        return new AgentOutput(reply!.Reply, reply.Actions, reply.Rejected);
    }

    private async Task<Guid> UploadImageAsync(string path, string token, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(path, ct));
        file.Headers.ContentType = new MediaTypeHeaderValue(path.EndsWith(".png") ? "image/png" : "image/jpeg");
        form.Add(file, "file", Path.GetFileName(path));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.ChatUrl}/tutor/images") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, ct);
        await ThrowIfRefusedAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<ImageUploaded>(CaseLoader.Json, ct))!.Id;
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string url, string token, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body, options: CaseLoader.Json);
        HttpResponseMessage response;
        try { response = await _http.SendAsync(request, ct); }
        catch (HttpRequestException ex) { throw new NotPlayedException($"service injoignable : {ex.Message}", true); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new NotPlayedException("délai dépassé (5 min)", true); }

        using (response)
        {
            if (method == HttpMethod.Delete && response.StatusCode == HttpStatusCode.NotFound) return default;
            await ThrowIfRefusedAsync(response, ct);
            if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0) return default;
            return await response.Content.ReadFromJsonAsync<T>(CaseLoader.Json, ct);
        }
    }

    /// <summary>403, 429 : refus (droit, formule, quota) ; 502, 503, 5xx : panne. Le message du service est gardé.</summary>
    private static async Task ThrowIfRefusedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        var message = MessageField().Match(body) is { Success: true } m ? Regex.Unescape(m.Groups[1].Value) : body.Length > 200 ? body[..200] : body;
        var code = (int)response.StatusCode;
        throw new NotPlayedException($"{code} {response.RequestMessage?.RequestUri?.AbsolutePath} : {message}", code >= 500);
    }

    [GeneratedRegex("\"message\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"")] private static partial Regex MessageField();

    public void Dispose() => _http.Dispose();
}

/// <summary>
/// Cours d'essai : un fichier Markdown dont la première ligne est
/// &lt;!-- matière: Physique --&gt; et dont le premier titre # est le titre du cours.
/// </summary>
public record CourseFixture(string Title, string Subject, string Markdown, string Hash)
{
    public const string TitlePrefix = "[Évaluation] ";

    public static CourseFixture Load(string path)
    {
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        var subject = Regex.Match(text, @"<!--\s*mati[eè]re\s*:\s*(.+?)\s*-->").Groups[1].Value;
        var title = Regex.Match(text, @"^#\s+(.+)$", RegexOptions.Multiline).Groups[1].Value.Trim();
        if (subject.Length == 0 || title.Length == 0)
            throw new InvalidDataException($"{Path.GetFileName(path)} : il faut <!-- matière: … --> et un titre « # … ».");
        var markdown = Regex.Replace(text, @"<!--.*?-->\n?", "", RegexOptions.Singleline).Trim();
        // Le titre est celui du cours : on ne le répète pas dans le corps
        markdown = Regex.Replace(markdown, @"^#\s+.+\n+", "");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(markdown + subject + title)))[..10].ToLowerInvariant();
        return new CourseFixture(TitlePrefix + title, subject, markdown, hash);
    }
}
