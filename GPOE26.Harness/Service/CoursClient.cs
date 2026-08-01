using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace GPOE26.Harness.Service;

/// <summary>Un passage du cours retrouvé par la recherche vectorielle.</summary>
public record CoursePassage(Guid ChunkId, string HeadingPath, string Content, int Order, double Score);

/// <summary>Vue du cours renvoyée par le service Cours.</summary>
public record CourseSnapshot(
    Guid Id, string Title, string Subject, string? Description,
    string? ExtractedText, string? FormattedMarkdown)
{
    public string? Content =>
        !string.IsNullOrWhiteSpace(FormattedMarkdown) ? FormattedMarkdown : ExtractedText;
}

/// <summary>Une étape du parcours, réduite à ce dont un outil a besoin.</summary>
public record JourneyStep(string Title, string Status, int? Score, int? Total, string? HeadingPath);

/// <summary>Le parcours, tel que l'outil `ou_en_est_l_eleve` le rapporte.</summary>
public record Journey(List<JourneyStep> Steps, int PassedCount);

/// <summary>
/// Accès au service Cours.
///
/// Comme dans Chat, le JWT de l'appelant est relayé tel quel : les endpoints de Cours
/// filtrent sur le propriétaire du cours, et c'est ce relais qui garantit qu'un élève ne
/// travaille que ses propres cours. Un appel sous une identité de service contournerait
/// précisément le contrôle qu'on veut conserver.
/// </summary>
public sealed class CoursClient(
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    ILogger<CoursClient> logger)
{
    public const string HttpClientName = "cours";

    public async Task<CourseSnapshot?> GetCourseAsync(Guid courseId, CancellationToken ct = default)
    {
        try
        {
            return await Authorized().GetFromJsonAsync<CourseSnapshot>($"/cours/{courseId}", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cours {CourseId} illisible", courseId);
            return null;
        }
    }

    public async Task<IReadOnlyList<CoursePassage>> SearchAsync(
        Guid courseId, string query, int k = 5, CancellationToken ct = default)
    {
        try
        {
            var response = await Authorized().PostAsJsonAsync(
                $"/cours/{courseId}/search", new { Query = query, K = k }, ct);

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<CoursePassage>>(ct) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Recherche impossible dans le cours {CourseId}", courseId);
            return [];
        }
    }

    public async Task<Journey?> GetJourneyAsync(Guid courseId, CancellationToken ct = default)
    {
        try
        {
            return await Authorized().GetFromJsonAsync<Journey>($"/cours/{courseId}/parcours", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Parcours illisible pour le cours {CourseId}", courseId);
            return null;
        }
    }

    private HttpClient Authorized()
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        var authorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authorization)
            && AuthenticationHeaderValue.TryParse(authorization, out var header))
        {
            client.DefaultRequestHeaders.Authorization = header;
        }

        return client;
    }
}
