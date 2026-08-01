using System.Net.Http.Headers;
using System.Net.Http.Json;
using Chat.Model;

namespace Chat.Service;

/// <summary>
/// Accès au service Cours depuis le service Chat.
///
/// C'est ce qui manquait pour que le répétiteur puisse réellement travailler : jusqu'ici
/// le frontend devait joindre le cours entier à chaque message, et ResolveCourseContent
/// renvoyait un texte bouchon quand seul un courseId était fourni. Le Chat va désormais
/// chercher lui-même le cours, et surtout n'en récupère que les passages pertinents via
/// la recherche vectorielle.
/// </summary>
public sealed class CoursClient(
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    ILogger<CoursClient> logger)
{
    public const string HttpClientName = "cours";

    /// <summary>Récupère un cours complet (métadonnées + contenu mis en forme).</summary>
    public async Task<CourseSnapshot?> GetCourseAsync(Guid courseId, CancellationToken ct = default)
    {
        try
        {
            var client = CreateAuthorizedClient();
            return await client.GetFromJsonAsync<CourseSnapshot>($"/cours/{courseId}", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Impossible de récupérer le cours {CourseId}", courseId);
            return null;
        }
    }

    /// <summary>
    /// Recherche sémantique dans un cours. Le service Cours possède le contenu ET son
    /// index vectoriel ; le Chat ne manipule jamais d'embeddings.
    /// </summary>
    public async Task<IReadOnlyList<CoursePassage>> SearchAsync(
        Guid courseId, string query, int k = 5, CancellationToken ct = default)
    {
        try
        {
            var client = CreateAuthorizedClient();
            var response = await client.PostAsJsonAsync(
                $"/cours/{courseId}/search", new { Query = query, K = k }, ct);

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<CoursePassage>>(ct) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Échec de la recherche dans le cours {CourseId}", courseId);
            return [];
        }
    }

    /// <summary>
    /// Progression d'un enfant sur un cours, pour la rédaction du bilan parental.
    ///
    /// Le contrôle d'accès reste chez Cours, qui applique StudyIdentity au jeton relayé :
    /// un parent qui demanderait l'enfant d'un autre reçoit un 403, et le Chat n'a aucune
    /// règle d'autorisation à réimplémenter — donc aucune à faire diverger.
    /// </summary>
    public async Task<ChildCourseDetail?> GetChildCourseAsync(
        Guid studentId, Guid courseId, CancellationToken ct = default)
    {
        try
        {
            var client = CreateAuthorizedClient();
            var response = await client.GetAsync($"/suivi/{studentId}/cours/{courseId}", ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Suivi refusé ou introuvable ({Status}) : élève {StudentId}, cours {CourseId}",
                    (int)response.StatusCode, studentId, courseId);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<ChildCourseDetail>(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Impossible de lire le suivi de l'élève {StudentId} sur le cours {CourseId}",
                studentId, courseId);
            return null;
        }
    }

    /// <summary>
    /// Réémet le JWT de l'élève vers le service Cours.
    ///
    /// Les endpoints /cours sont protégés et filtrent sur le propriétaire : sans ce
    /// relais, un élève pourrait au mieux ne rien lire, au pire lire le cours d'un autre
    /// si l'appel passait sous une identité de service.
    /// </summary>
    private HttpClient CreateAuthorizedClient()
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
