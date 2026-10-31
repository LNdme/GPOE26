using System.Net.Http.Headers;

namespace Cours.Service;

/// <summary>
/// Accès au service User pour le partage des cours : on transmet le JWT de l'utilisateur
/// courant, et le service User ne renvoie que ses classes (enseignant : celles qu'il anime,
/// élève : celles qu'il a rejointes).
/// </summary>
public class UserDirectoryClient(HttpClient http, IHttpContextAccessor context)
{
    private record ClassroomRef(Guid Id);

    public async Task<HashSet<Guid>> MyClassroomIdsAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/classrooms/mine");
        var auth = context.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(auth))
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(auth);

        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var classrooms = await response.Content.ReadFromJsonAsync<List<ClassroomRef>>() ?? new();
        return classrooms.Select(c => c.Id).ToHashSet();
    }
}
