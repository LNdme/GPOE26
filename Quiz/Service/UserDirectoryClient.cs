using System.Net;
using System.Net.Http.Headers;

namespace Quiz.Service;

public record ClassroomRef(Guid Id, string Name, string? InviteCode, Guid TeacherId);
public record MemberRef(Guid Id, string Username, string? Level, DateTime? LastLoginAt);
public record ChildRef(Guid ChildId, string Username, string Status);

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

    /// <summary>Élèves d'une classe dont l'utilisateur est l'enseignant (null sinon).</summary>
    public Task<List<MemberRef>?> MembersAsync(Guid classroomId) =>
        GetAsync<List<MemberRef>>($"/classrooms/{classroomId}/members");

    /// <summary>Le parent connecté est-il rattaché (lien accepté) à cet élève ?</summary>
    public async Task<ChildRef?> ActiveChildAsync(Guid childId)
    {
        var children = await GetAsync<List<ChildRef>>("/parents/children") ?? new();
        return children.FirstOrDefault(c => c.ChildId == childId && c.Status == "Active");
    }

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
