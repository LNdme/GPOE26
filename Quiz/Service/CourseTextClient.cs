using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace Quiz.Service;

/// <summary>
/// Texte d'un cours, lu au service Cours avec le JWT de l'utilisateur : il n'obtient que les cours auxquels il a accès.
/// Avant, le navigateur envoyait lui-même le texte à transformer en quiz, sans limite de taille et sans lien avec un vrai cours.
/// </summary>
public partial class CourseTextClient(HttpClient http, IHttpContextAccessor context)
{
    /// <summary>Longueur maximale du texte confié au modèle (comme pour le tuteur).</summary>
    public const int MaxLength = 40_000;

    private record SectionRef(string? Content, int Order);
    private record CourseRef(Guid Id, string Title, string? Description, string? ExtractedText, List<SectionRef>? Sections);

    public record CourseText(string Title, string Text);

    /// <summary>Le texte du cours, ou null s'il n'existe pas ou n'est pas accessible. Lève UserServiceUnavailableException si Cours ne répond pas.</summary>
    public async Task<CourseText?> GetAsync(Guid courseId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/cours/{courseId}");
        var auth = context.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(auth)) request.Headers.Authorization = AuthenticationHeaderValue.Parse(auth);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new CoursServiceUnavailableException(ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden) return null;
            if (!response.IsSuccessStatusCode) throw new CoursServiceUnavailableException(new HttpRequestException($"Cours a répondu {(int)response.StatusCode}"));
            var course = await response.Content.ReadFromJsonAsync<CourseRef>(JsonDefaults.Options, ct);
            return course is null ? null : new CourseText(course.Title, Build(course.Sections?.OrderBy(s => s.Order).Select(s => s.Content), course.ExtractedText, course.Description));
        }
    }

    /// <summary>Sections du cours (sinon texte extrait du PDF, sinon description), sans les blocs de laboratoire, borné à MaxLength.</summary>
    public static string Build(IEnumerable<string?>? sections, string? extracted, string? description)
    {
        var parts = sections?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? [];
        var text = parts.Count > 0 ? string.Join("\n\n", parts) : extracted ?? "";
        text = SimulationBlock().Replace(text, "[Laboratoire interactif]");
        if (string.IsNullOrWhiteSpace(text)) text = description ?? "";
        return Limit(text);
    }

    public static string Limit(string text) => text.Length > MaxLength ? text[..MaxLength] + "\n[…]" : text;

    [GeneratedRegex(@"```simulation[\s\S]*?```")]
    private static partial Regex SimulationBlock();
}

/// <summary>Le service Cours ne répond pas : l'appelant renvoie 503.</summary>
public class CoursServiceUnavailableException(Exception inner)
    : Exception("Le service des cours est indisponible.", inner);
