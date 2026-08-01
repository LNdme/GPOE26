using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GPOE26.ServiceDefaults;

/// <summary>
/// La porte d'accès enseignant, séparée de <see cref="StudyIdentity.CanViewStudent"/>.
///
/// Pourquoi séparée : un enseignant suit cent cinquante élèves, qu'aucun jeton ne peut
/// porter. Répondre pour lui exige donc d'interroger le service User — et rendre
/// <c>CanViewStudent</c> asynchrone contaminerait onze appels dans quatre services, dont
/// un stub délibérément sans dépendances. On ajoute plutôt une porte, employée là où
/// l'accès enseignant est réellement offert.
///
/// Ce que cela coûte, et qu'il faut assumer :
///
///  · **Le service Cours dépend de User à l'exécution** pour le suivi enseignant. Aucun
///    jeton ne portant cent cinquante identifiants, il n'y a pas d'alternative.
///  · **Un annuaire muet refuse l'accès.** Une autorisation qui s'ouvre quand elle ne
///    peut pas vérifier n'est pas une autorisation.
///  · **La révocation est différée** de la durée du cache : un élève sorti d'une classe
///    reste visible cinq minutes.
/// </summary>
public sealed class StudentDirectory(
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    IMemoryCache cache,
    ILogger<StudentDirectory> logger)
{
    public const string HttpClientName = "user-directory";

    /// <summary>
    /// Cinq minutes. Assez pour qu'une consultation de classe ne coûte qu'un appel, assez
    /// court pour qu'un élève retiré d'une classe cesse rapidement d'être visible.
    /// </summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// L'appelant peut-il consulter cet élève ?
    ///
    /// Soi-même ou son parent : réponse immédiate, sans réseau. Enseignant : une requête,
    /// mise en cache, qui ramène l'union de ses effectifs — pas un appel par élève.
    /// </summary>
    public async Task<bool> CanViewAsync(
        ClaimsPrincipal principal, Guid studentId, CancellationToken ct = default)
    {
        // Le cas courant d'abord, et il ne touche jamais le réseau.
        if (principal.CanViewStudent(studentId)) return true;
        if (!principal.IsTeacher()) return false;

        var roster = await RosterAsync(principal, ct);
        return roster.Contains(studentId);
    }

    /// <summary>
    /// Tous les élèves des classes de cet enseignant.
    ///
    /// Sert à la fois à l'autorisation et à la vue de classe : le service Cours ne connaît
    /// pas les classes, il a besoin de la liste pour agréger quoi que ce soit.
    /// </summary>
    public async Task<IReadOnlySet<Guid>> RosterAsync(
        ClaimsPrincipal principal, CancellationToken ct = default)
    {
        if (!principal.IsTeacher()) return new HashSet<Guid>();

        var teacherId = principal.GetUserId();
        if (teacherId is null) return new HashSet<Guid>();

        var key = $"effectif:{teacherId}";
        if (cache.TryGetValue(key, out IReadOnlySet<Guid>? cached) && cached is not null)
            return cached;

        var students = await FetchAsync(principal, ct);

        // On ne met en cache qu'un succès. Mémoriser un échec ferait refuser l'accès
        // pendant cinq minutes après une coupure d'une seconde.
        if (students is null) return new HashSet<Guid>();

        cache.Set(key, students, CacheDuration);
        return students;
    }

    private async Task<IReadOnlySet<Guid>?> FetchAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);

            // Le jeton de l'appelant est relayé tel quel : c'est le service User qui
            // décide quels élèves lui appartiennent, à partir de sa propre identité. Un
            // appel sous une identité de service permettrait de demander l'effectif de
            // n'importe quel enseignant.
            //
            // Le jeton brut n'est pas dans les claims — il faut le reprendre dans
            // l'en-tête de la requête entrante, comme le fait déjà CoursClient.
            var authorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();

            if (string.IsNullOrWhiteSpace(authorization)
                || !AuthenticationHeaderValue.TryParse(authorization, out var header))
            {
                logger.LogWarning("Aucun jeton à relayer : effectif enseignant non résolu");
                return null;
            }

            client.DefaultRequestHeaders.Authorization = header;

            var students = await client.GetFromJsonAsync<List<Guid>>("/auth/classes/mes-eleves", ct);
            return students is null ? null : new HashSet<Guid>(students);
        }
        catch (Exception ex)
        {
            // On refuse. Une autorisation qui s'ouvre quand l'annuaire ne répond pas
            // n'est pas une autorisation.
            logger.LogWarning(ex, "Effectif enseignant illisible : accès refusé par précaution");
            return null;
        }
    }
}

public static class StudentDirectoryExtensions
{
    /// <summary>
    /// Enregistre l'annuaire et son client HTTP vers le service User.
    ///
    /// <paramref name="userServiceUri"/> passe par la découverte de services d'Aspire
    /// (« https+http://user ») dans les services qui la connaissent.
    /// </summary>
    public static IServiceCollection AddStudentDirectory(
        this IServiceCollection services, string userServiceUri = "https+http://user")
    {
        services.AddMemoryCache();
        services.AddHttpContextAccessor();

        services.AddHttpClient(StudentDirectory.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(userServiceUri);
            // Court : une autorisation qui met dix secondes à se décider bloque la page.
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        services.AddScoped<StudentDirectory>();
        return services;
    }
}
