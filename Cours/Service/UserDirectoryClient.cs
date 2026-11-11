using System.Net.Http.Headers;

namespace Cours.Service;

/// <summary>
/// Classe renvoyée par le service User. TeacherId : son professeur principal ; MyRole : place de l'utilisateur
/// dans la classe (« Teacher », « Manager », « Student ») — une classe peut avoir plusieurs enseignants.
/// </summary>
public record ClassroomRef(Guid Id, Guid TeacherId, string? MyRole = null);

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

/// <summary>
/// Accès au service User pour le partage des cours : on transmet le JWT de l'utilisateur
/// courant, et le service User ne renvoie que ses classes (enseignant : celles qu'il anime,
/// élève : celles qu'il a rejointes).
/// </summary>
public class UserDirectoryClient(HttpClient http, IHttpContextAccessor context)
{
    /// <summary>Classes de l'utilisateur, animées ou rejointes : sert à savoir quels cours il peut LIRE.</summary>
    public async Task<HashSet<Guid>> MyClassroomIdsAsync() =>
        (await MyClassroomsAsync()).Select(c => c.Id).ToHashSet();

    /// <summary>
    /// Classes où l'utilisateur enseigne : les seules avec lesquelles il peut PARTAGER un cours.
    /// Un élève est membre de sa classe, pas son enseignant : il ne peut rien y publier.
    /// </summary>
    public async Task<HashSet<Guid>> OwnedClassroomIdsAsync(Guid userId) =>
        OwnedBy(await MyClassroomsAsync(), userId);

    /// <summary>Sans MyRole (service User antérieur au lot 2), on retombe sur le professeur principal.</summary>
    public static HashSet<Guid> OwnedBy(IEnumerable<ClassroomRef> classrooms, Guid userId) =>
        classrooms.Where(c => c.MyRole is { } role ? role == "Teacher" : c.TeacherId == userId).Select(c => c.Id).ToHashSet();

    /// <summary>
    /// Bibliothèque d'un établissement, ou null si l'utilisateur n'y est ni enseignant ni gestionnaire
    /// (ou si l'établissement n'existe pas, ou s'il s'agit d'un espace d'enseignant indépendant).
    /// </summary>
    public async Task<LibrarySpaceRef?> LibrarySpaceAsync(Guid organizationId)
    {
        using var request = Authorized(HttpMethod.Get, $"/organizations/{organizationId}/library");
        using var response = await http.SendAsync(request);
        if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound
            or System.Net.HttpStatusCode.Unauthorized)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LibrarySpaceRef>();
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        var auth = context.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(auth))
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(auth);
        return request;
    }

    private async Task<List<ClassroomRef>> MyClassroomsAsync()
    {
        using var request = Authorized(HttpMethod.Get, "/classrooms/mine");

        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ClassroomRef>>() ?? new();
    }
}
