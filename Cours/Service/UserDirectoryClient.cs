using System.Net.Http.Headers;

namespace Cours.Service;

/// <summary>
/// Classe renvoyée par le service User. TeacherId : son professeur principal ; MyRole : place de l'utilisateur
/// dans la classe (« Teacher », « Manager », « Student ») — une classe peut avoir plusieurs enseignants.
/// </summary>
public record ClassroomRef(Guid Id, Guid TeacherId, string? MyRole = null);

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

    private async Task<List<ClassroomRef>> MyClassroomsAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/classrooms/mine");
        var auth = context.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(auth))
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(auth);

        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ClassroomRef>>() ?? new();
    }
}
