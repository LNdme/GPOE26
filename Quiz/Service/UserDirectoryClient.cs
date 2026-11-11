using System.Net;
using System.Net.Http.Headers;

namespace Quiz.Service;

/// <summary>
/// Classe renvoyée par le service User. MyRole : place de l'utilisateur dans la classe (« Teacher », « Manager »,
/// « Student ») ; une classe peut avoir plusieurs enseignants, TeacherId n'est que le professeur principal.
/// </summary>
public record ClassroomRef(Guid Id, string Name, string? InviteCode, Guid TeacherId, string? MyRole = null, bool IsManager = false)
{
    /// <summary>
    /// Le suivi d'une classe montre toutes ses évaluations au gestionnaire de l'espace (lot 6), et seulement
    /// les siennes à un enseignant : chacun suit sa matière.
    /// </summary>
    public static bool SeesAllAssessments(ClassroomRef? classroom) => classroom?.IsManager == true;

    /// <summary>
    /// Classes où l'utilisateur enseigne : les seules qu'une évaluation peut viser.
    /// Sans MyRole (service User antérieur au lot 2), on retombe sur le professeur principal.
    /// </summary>
    public static HashSet<Guid> TaughtBy(IEnumerable<ClassroomRef> classrooms, Guid userId) =>
        classrooms.Where(c => c.MyRole is { } role ? role == "Teacher" : c.TeacherId == userId).Select(c => c.Id).ToHashSet();
}
/// <summary>Enseignant actif d'un établissement (service User), auteur possible d'un dépôt en bibliothèque.</summary>
public record LibraryAuthorRef(Guid UserId, string Username);

/// <summary>
/// Bibliothèque d'un établissement vue par l'utilisateur courant (service User) : ses enseignants actifs,
/// et CanManage si l'utilisateur en est responsable ou gestionnaire.
/// </summary>
public record LibrarySpaceRef(Guid Id, string Name, bool CanManage, List<LibraryAuthorRef> Teachers)
{
    public bool IsTeacher(Guid userId) => Teachers.Any(t => t.UserId == userId);
}

public record MemberRef(Guid Id, string Username, string? Level, DateTime? LastLoginAt);
public record ChildRef(Guid ChildId, string Username, string Status);
public record EntitledFeatureRef(string Code, int? Limit);
public record EntitlementRef(List<EntitledFeatureRef>? Features);
public record MySubscriptionsRef(EntitlementRef Entitlement);

/// <summary>Le service User ne répond pas : l'appelant renvoie 503.</summary>
public class UserServiceUnavailableException(Exception inner)
    : Exception("Le service des comptes est indisponible.", inner);

/// <summary>
/// Accès au service User (classes, élèves, rattachements parent ↔ enfant).
/// On transmet le JWT de l'utilisateur courant : le service User applique lui-même
/// ses règles (un enseignant ne voit que ses classes, un parent que ses enfants…).
/// </summary>
public class UserDirectoryClient(HttpClient http, IHttpContextAccessor context)
{
    /// <summary>Enseignant : ses classes (InviteCode renseigné). Élève : les classes qu'il a rejointes.</summary>
    public async Task<List<ClassroomRef>> MyClassroomsAsync() =>
        await GetAsync<List<ClassroomRef>>("/classrooms/mine") ?? new();

    /// <summary>Une classe vue par l'utilisateur (place, gestionnaire ou non) ; null s'il n'y a pas accès.</summary>
    public Task<ClassroomRef?> ClassroomAsync(Guid classroomId) => GetAsync<ClassroomRef>($"/classrooms/{classroomId}");

    /// <summary>Élèves d'une classe, pour ses enseignants et les gestionnaires de son espace (null sinon).</summary>
    public Task<List<MemberRef>?> MembersAsync(Guid classroomId) =>
        GetAsync<List<MemberRef>>($"/classrooms/{classroomId}/members");

    /// <summary>Le parent connecté est-il rattaché (lien accepté) à cet élève ?</summary>
    public async Task<ChildRef?> ActiveChildAsync(Guid childId)
    {
        var children = await GetAsync<List<ChildRef>>("/parents/children") ?? new();
        return children.FirstOrDefault(c => c.ChildId == childId && c.Status == "Active");
    }

    /// <summary>La fonctionnalité si la formule de l'utilisateur l'inclut (avec son quota), sinon null.</summary>
    public async Task<EntitledFeatureRef?> FeatureAsync(string code)
    {
        var mine = await GetAsync<MySubscriptionsRef>("/subscriptions/me");
        return mine?.Entitlement.Features?.FirstOrDefault(f => f.Code == code);
    }

    /// <summary>
    /// Bibliothèque d'un établissement, ou null si l'utilisateur n'y est ni enseignant ni gestionnaire
    /// (ou s'il s'agit d'un espace d'enseignant indépendant).
    /// </summary>
    public Task<LibrarySpaceRef?> LibrarySpaceAsync(Guid organizationId) =>
        GetAsync<LibrarySpaceRef>($"/organizations/{organizationId}/library");

    private async Task<T?> GetAsync<T>(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        var auth = context.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(auth))
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(auth);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new UserServiceUnavailableException(ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                return default;
            if (!response.IsSuccessStatusCode)
                throw new UserServiceUnavailableException(new HttpRequestException($"User a répondu {(int)response.StatusCode}"));
            return await response.Content.ReadFromJsonAsync<T>(JsonDefaults.Options);
        }
    }
}
