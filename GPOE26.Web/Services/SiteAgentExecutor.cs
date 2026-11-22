using System.Text.Json;
using GPOE26.Web.Models;

namespace GPOE26.Web.Services;

/// <summary>
/// Prévient les pages de l'administration qu'un contenu a changé hors d'elles (action confirmée dans le panneau de l'agent),
/// pour qu'elles se rechargent sans recharger toute la page (la conversation est gardée).
/// Une instance par circuit Blazor.
/// </summary>
public class AdminContentEvents
{
    /// <summary>Sorte de contenu modifiée : news, event, activity ou team.</summary>
    public event Action<string>? Changed;

    public void Notify(string kind) => Changed?.Invoke(kind);
}

/// <summary>
/// Exécute une action proposée par l'agent du site APRÈS confirmation par la personne,
/// avec son jeton et par les mêmes API que les formulaires : l'agent n'a jamais plus de droits qu'elle.
/// </summary>
public class SiteAgentExecutor(ApiClient api, AdminContentEvents events)
{
    public async Task<(bool Ok, string Message)> ExecuteAsync(SiteAgentAction action)
    {
        var d = action.Data;
        var kind = action.Type.Split('.')[0];
        var (ok, message) = action.Type switch
        {
            "news.create" => await NewsCreateAsync(d),
            "news.update" => await NewsUpdateAsync(d, n => Apply(n, d)),
            "news.publish" => await NewsUpdateAsync(d, n => n with { IsPublished = true, PublishedAt = DateTime.UtcNow }),
            "news.unpublish" => await NewsUpdateAsync(d, n => n with { IsPublished = false }),
            "news.delete" => Result(await WithGuid(d, api.DeleteNewsAsync)),

            "event.create" => Result(await api.CreateEventAsync(Apply(new SchoolEvent { Type = "Général", IsPublic = true }, d))),
            "event.update" => await UpdateAsync(d, api.GetEventByIdAsync, (id, e) => api.UpdateEventAsync(id, Apply(e, d))),
            "event.delete" => Result(await WithGuid(d, api.DeleteEventAsync)),

            "activity.create" => Result(await api.CreateActivityAsync(Apply(new SchoolActivity { IsActive = true }, d))),
            "activity.update" => await UpdateAsync(d, api.GetActivityByIdAsync, (id, a) => api.UpdateActivityAsync(id, Apply(a, d))),
            "activity.delete" => Result(await WithGuid(d, api.DeleteActivityAsync)),

            "team.create" => Result(await api.CreateHierarchyAsync(Apply(new Hierarchy(), d))),
            "team.update" => await TeamUpdateAsync(d),
            "team.delete" => int.TryParse(Str(d, "id"), out var memberId)
                ? Result(await api.DeleteHierarchyAsync(memberId))
                : (false, "Membre introuvable."),

            _ => (false, "Action inconnue.")
        };

        if (ok) events.Notify(kind);
        return (ok, ok ? "Fait." : message);
    }

    // ── Actualités ─────────────────────────────────────────────────────────────────────────────

    private async Task<(bool, string)> NewsCreateAsync(Dictionary<string, JsonElement> d)
    {
        var publish = Bool(d, "publish") == true;
        var article = Apply(new NewArticle { Category = "Général", IsPublished = publish }, d);
        if (publish) article.PublishedAt = DateTime.UtcNow;
        return Result(await api.CreateNewsAsync(article));
    }

    private async Task<(bool, string)> NewsUpdateAsync(Dictionary<string, JsonElement> d, Func<NewArticle, NewArticle> change) =>
        await UpdateAsync(d, api.GetNewsByIdAsync, (id, n) => api.UpdateNewsAsync(id, change(n)));

    private static NewArticle Apply(NewArticle n, Dictionary<string, JsonElement> d) => n with
    {
        Title = Str(d, "title") ?? n.Title,
        Content = Str(d, "content") ?? n.Content,
        Excerpt = Str(d, "excerpt") ?? n.Excerpt,
        Category = Str(d, "category") ?? n.Category,
    };

    // ── Événements, activités, équipe ──────────────────────────────────────────────────────────

    private static SchoolEvent Apply(SchoolEvent e, Dictionary<string, JsonElement> d) => e with
    {
        Title = Str(d, "title") ?? e.Title,
        Description = Str(d, "description") ?? e.Description,
        Location = Str(d, "location") ?? e.Location,
        Type = Str(d, "type") ?? e.Type,
        StartDate = Date(d, "startDate") ?? e.StartDate,
        EndDate = Date(d, "endDate") ?? e.EndDate,
        IsPublic = Bool(d, "isPublic") ?? e.IsPublic,
    };

    private static SchoolActivity Apply(SchoolActivity a, Dictionary<string, JsonElement> d) => a with
    {
        Name = Str(d, "name") ?? a.Name,
        Category = Str(d, "category") ?? a.Category,
        Description = Str(d, "description") ?? a.Description,
        Schedule = Str(d, "schedule") ?? a.Schedule,
        ResponsibleTeacher = Str(d, "responsibleTeacher") ?? a.ResponsibleTeacher,
        IsActive = Bool(d, "isActive") ?? a.IsActive,
    };

    private static Hierarchy Apply(Hierarchy m, Dictionary<string, JsonElement> d) => m with
    {
        Name = Str(d, "name") ?? m.Name,
        PreName = Str(d, "preName") ?? m.PreName,
        Role = Str(d, "role") ?? m.Role,
        Department = Str(d, "department") ?? m.Department,
        Email = Str(d, "email") ?? m.Email,
        Description = Str(d, "description") ?? m.Description,
        Specialization = Str(d, "specialization") ?? m.Specialization,
    };

    private async Task<(bool, string)> TeamUpdateAsync(Dictionary<string, JsonElement> d)
    {
        if (!int.TryParse(Str(d, "id"), out var id)) return (false, "Membre introuvable.");
        var member = await api.GetHierarchyByIdAsync(id);
        return member is null ? (false, "Ce membre n'existe plus.") : Result(await api.UpdateHierarchyAsync(id, Apply(member, d)));
    }

    // ── Outils ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Relit l'élément juste avant de le modifier : on change seulement les champs demandés.</summary>
    private static async Task<(bool, string)> UpdateAsync<T>(Dictionary<string, JsonElement> d,
        Func<Guid, Task<T?>> get, Func<Guid, T, Task<bool>> update) where T : class
    {
        if (!Guid.TryParse(Str(d, "id"), out var id)) return (false, "Élément introuvable.");
        var current = await get(id);
        if (current is null) return (false, "Cet élément n'existe plus.");
        return Result(await update(id, current));
    }

    private static Task<bool> WithGuid(Dictionary<string, JsonElement> d, Func<Guid, Task<bool>> call) =>
        Guid.TryParse(Str(d, "id"), out var id) ? call(id) : Task.FromResult(false);

    private static (bool, string) Result(bool ok) =>
        (ok, ok ? "Fait." : "Le serveur a refusé la modification. Vérifiez vos droits ou reconnectez-vous.");

    private static string? Str(Dictionary<string, JsonElement> d, string key) =>
        d.TryGetValue(key, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null
        } : null;

    private static bool? Bool(Dictionary<string, JsonElement> d, string key) =>
        d.TryGetValue(key, out var v) ? v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(v.GetString(), out var b) => b,
            _ => null
        } : null;

    private static DateTime? Date(Dictionary<string, JsonElement> d, string key) =>
        Str(d, key) is { } s && DateTime.TryParse(s, out var date) ? date : null;
}
