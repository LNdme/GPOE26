using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chat.Agents;
using Chat.Service;
using GPOE26.ServiceDefaults.Llm;
using static Chat.Endpoints.EndpointHelpers;

namespace Chat.Endpoints;

/// <summary>
/// Agent du site de l'établissement, ouvert dans l'administration (administrateurs et éditeurs du site).
/// Il lit le contenu du site avec le jeton de la personne, répond, rédige, et peut PROPOSER des modifications.
/// Il n'écrit jamais lui-même : chaque proposition est vérifiée ici (type connu, champs obligatoires,
/// identifiant réellement présent sur le site), puis affichée comme une carte que la personne confirme.
/// C'est le site qui exécute l'action confirmée, avec le jeton de la personne, par les API habituelles.
/// La conversation vit dans la page (pas de stockage) : l'historique récent est renvoyé à chaque question.
/// </summary>
public static class SiteAgentEndpoints
{
    public const string Feature = "site-agent";
    private const int MaxHistory = 12;
    private const int MaxMessageLength = 3000;

    public static void MapSiteAgentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/site-agent").WithTags("Agent du site").RequireAuthorization()
            .AddEndpointFilter(async (context, next) =>
            {
                var user = context.HttpContext.User;
                if (!CanEditSite(user))
                    return Error(403, "L'agent du site est réservé aux administrateurs et aux éditeurs du site.");
                return await next(context);
            });

        group.MapPost("/ask", async (SiteAgentAskRequest req, ClaimsPrincipal principal, PlatformClients platform,
            LlmClient llm, AiQuota quota, CancellationToken ct) =>
        {
            var message = req.Message?.Trim() ?? "";
            if (message.Length is 0 or > MaxMessageLength)
                return Error(400, "Écrivez votre demande (3 000 caractères au maximum).");

            var userId = UserId(principal);
            // Outil de travail de l'établissement : pas de formule à vérifier, seulement le plafond quotidien d'IA
            if ((await quota.CheckAsync(userId, Feature, ct, requireFeature: false)).Refusal is { } refusal)
                return Error(429, refusal);

            var context = await SiteContextAsync(platform, ct);
            var system = SiteAgentPrompt.System(principal.FindFirstValue("username"), IsAdmin(principal), context);

            var history = (req.History ?? [])
                .Where(m => m.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(m.Content))
                .TakeLast(MaxHistory)
                .Select(m => new LlmMessage(m.Role, m.Content.Length > 6000 ? m.Content[..6000] : m.Content));

            try
            {
                var raw = await llm.CompleteAsync([new LlmMessage("system", system), .. history, new LlmMessage("user", message)], ct: ct);
                await quota.RecordAsync(userId, Feature, ct);
                var (text, actions, rejected) = SiteAgentActions.Extract(raw, context);
                return Results.Ok(new SiteAgentReply(text, actions, rejected, context.Missing));
            }
            catch (LlmUnavailableException ex)
            {
                return ex is LlmNotConfiguredException
                    ? Error(503, "L'agent n'est pas activé sur ce serveur : aucune clé d'IA n'est configurée.")
                    : Error(502, "L'agent ne répond pas pour le moment. Réessayez dans un instant.");
            }
        })
        .WithSummary("Poser une question à l'agent du site ; il peut proposer des modifications à confirmer");
    }

    /// <summary>Contenu du site lu avec le jeton de la personne : un administrateur ou un éditeur voit aussi les brouillons.</summary>
    private static async Task<SiteContext> SiteContextAsync(PlatformClients platform, CancellationToken ct)
    {
        var news = platform.GetJsonAsync<JsonDocument>("site", "/api/news?pageSize=40", ct, useCache: false);
        var events = platform.GetJsonAsync<JsonDocument>("site", "/api/events", ct, useCache: false);
        var activities = platform.GetJsonAsync<JsonDocument>("site", "/api/activities?includeInactive=true", ct, useCache: false);
        var team = platform.GetJsonAsync<JsonDocument>("site", "/api/hierarchy", ct, useCache: false);
        var contact = platform.GetJsonAsync<JsonDocument>("site", "/api/contact", ct, useCache: false);
        await Task.WhenAll(news, events, activities, team, contact);

        return SiteAgentPrompt.BuildContext(
            (await news)?.RootElement, (await events)?.RootElement, (await activities)?.RootElement,
            (await team)?.RootElement, (await contact)?.RootElement, DateTime.UtcNow);
    }

    private static bool IsAdmin(ClaimsPrincipal user) =>
        user.FindFirstValue("admin") == "true";   // droit à part du rôle (un administrateur reste enseignant)

    private static bool CanEditSite(ClaimsPrincipal user) =>
        IsAdmin(user) || user.FindFirstValue("site_editor") == "true";
}

public record SiteAgentMessage(string Role, string Content);
public record SiteAgentAskRequest(string? Message, List<SiteAgentMessage>? History);

/// <param name="Actions">Modifications proposées, vérifiées, à confirmer une par une dans la page.</param>
/// <param name="Rejected">Propositions écartées (identifiant inconnu, champ manquant) : expliquées sous la réponse.</param>
/// <param name="Missing">Parties du site que l'agent n'a pas pu lire (service indisponible).</param>
public record SiteAgentReply(string Reply, IReadOnlyList<SiteAction> Actions, IReadOnlyList<string> Rejected, IReadOnlyList<string> Missing);

/// <summary>Une modification proposée : Type (ex. « news.create »), Summary rédigé par le serveur, Data validées.</summary>
public record SiteAction(string Type, string Summary, Dictionary<string, JsonElement> Data, bool Destructive);

/// <param name="Ids">Identifiants présents sur le site, par sorte (news, event, activity, team) : une action ne vise que ceux-là.</param>
/// <param name="Titles">Titre lisible de chaque identifiant, pour les résumés des cartes.</param>
public record SiteContext(string Text, IReadOnlyList<string> Missing,
    IReadOnlyDictionary<string, HashSet<string>>? Ids = null, IReadOnlyDictionary<string, string>? Titles = null);

/// <summary>Mise en texte du contenu du site pour le modèle (fonctions pures, testées).</summary>
public static class SiteAgentPrompt
{
    private const int MaxExcerpt = 220;

    /// <summary>Consigne de l'agent du site : Chat/Agents/Prompts/site.md, avec la personne et le contenu actuel du site.</summary>
    public static string System(string? username, bool isAdmin, SiteContext context) => PromptLibrary.Render("site", new Dictionary<string, string>
    {
        ["person"] = string.IsNullOrWhiteSpace(username) ? "la personne" : username,
        ["right"] = isAdmin ? "administrateur" : "éditeur du site",
        ["context"] = context.Text
    });

    public static SiteContext BuildContext(JsonElement? news, JsonElement? events, JsonElement? activities,
        JsonElement? team, JsonElement? contact, DateTime nowUtc)
    {
        var sb = new StringBuilder();
        var missing = new List<string>();
        var ids = new Dictionary<string, HashSet<string>>
        {
            ["news"] = new(StringComparer.OrdinalIgnoreCase), ["event"] = new(StringComparer.OrdinalIgnoreCase),
            ["activity"] = new(StringComparer.OrdinalIgnoreCase), ["team"] = new(StringComparer.OrdinalIgnoreCase),
        };
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        sb.AppendLine($"Date du jour : {nowUtc:yyyy-MM-dd}");

        if (contact is { ValueKind: JsonValueKind.Object } c)
            sb.AppendLine($"\n## Coordonnées\n{Str(c, "name")} — {Str(c, "address")}, {Str(c, "city")} — {Str(c, "email")} — {Str(c, "phone")}");
        else if (contact is null) missing.Add("coordonnées");

        var newsItems = news is { ValueKind: JsonValueKind.Object } n && n.TryGetProperty("items", out var items) ? items : (JsonElement?)null;
        if (newsItems is { ValueKind: JsonValueKind.Array } list)
        {
            sb.AppendLine($"\n## Actualités ({list.GetArrayLength()})");
            foreach (var a in list.EnumerateArray())
            {
                Remember(ids, titles, "news", Id(a), $"l'article « {Str(a, "title")} »");
                sb.AppendLine($"- (id: {Id(a)}) [{(Bool(a, "isPublished") ? "publié" : "brouillon")}] « {Str(a, "title")} » ({Str(a, "category")}, " +
                              $"modifié le {Date(a, "updatedAt")}{(string.IsNullOrWhiteSpace(Str(a, "imageUrl")) ? ", sans image" : "")}) : " +
                              Excerpt(Str(a, "excerpt") is { Length: > 0 } ex ? ex : Str(a, "content")));
            }
        }
        else missing.Add("actualités");

        if (events is { ValueKind: JsonValueKind.Array } ev)
        {
            sb.AppendLine($"\n## Événements ({ev.GetArrayLength()})");
            foreach (var e in ev.EnumerateArray())
            {
                Remember(ids, titles, "event", Id(e), $"l'événement « {Str(e, "title")} »");
                var start = DateTime.TryParse(Str(e, "startDate"), out var d) ? d : (DateTime?)null;
                var when = start is { } s ? (s.ToUniversalTime() < nowUtc ? "passé" : "à venir") : "date inconnue";
                sb.AppendLine($"- (id: {Id(e)}) « {Str(e, "title")} » le {Date(e, "startDate")} ({when}, {Str(e, "type")}, " +
                              $"{(string.IsNullOrWhiteSpace(Str(e, "location")) ? "lieu non précisé" : Str(e, "location"))}" +
                              $"{(Bool(e, "isPublic") ? "" : ", interne")}) : {Excerpt(Str(e, "description"))}");
            }
        }
        else missing.Add("événements");

        if (activities is { ValueKind: JsonValueKind.Array } act)
        {
            sb.AppendLine($"\n## Activités ({act.GetArrayLength()})");
            foreach (var a in act.EnumerateArray())
            {
                Remember(ids, titles, "activity", Id(a), $"l'activité « {Str(a, "name")} »");
                sb.AppendLine($"- (id: {Id(a)}) « {Str(a, "name")} » ({Str(a, "category")}{(Bool(a, "isActive") ? "" : ", suspendue")}, " +
                              $"horaires : {Or(Str(a, "schedule"), "non précisés")}, responsable : {Or(Str(a, "responsibleTeacher"), "non précisé")}) : " +
                              Excerpt(Str(a, "description")));
            }
        }
        else missing.Add("activités");

        if (team is { ValueKind: JsonValueKind.Array } t)
        {
            sb.AppendLine($"\n## Équipe ({t.GetArrayLength()})");
            foreach (var m in t.EnumerateArray())
            {
                Remember(ids, titles, "team", Id(m), $"{Str(m, "preName")} {Str(m, "name")}".Trim());
                sb.AppendLine($"- (id: {Id(m)}) {Str(m, "preName")} {Str(m, "name")} — {Str(m, "role")}{(Str(m, "department") is { Length: > 0 } dep ? $" ({dep})" : "")}");
            }
        }
        else missing.Add("équipe");

        return new SiteContext(sb.ToString(), missing, ids, titles);
    }

    private static void Remember(Dictionary<string, HashSet<string>> ids, Dictionary<string, string> titles, string kind, string id, string title)
    {
        if (id.Length == 0) return;
        ids[kind].Add(id);
        titles[$"{kind}:{id}"] = title;
    }

    // Les identifiants sont des Guid (contenu) ou des entiers (équipe)
    private static string Id(JsonElement e) =>
        e.TryGetProperty("id", out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            _ => ""
        } : "";

    internal static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string Date(JsonElement e, string name) =>
        DateTime.TryParse(Str(e, name), out var d) ? d.ToString("yyyy-MM-dd") : "?";

    private static string Or(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string Excerpt(string text)
    {
        var flat = string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= MaxExcerpt ? flat : flat[..MaxExcerpt] + "…";
    }
}

/// <summary>
/// Extraction et vérification des actions proposées par le modèle (fonctions pures, testées).
/// Tout ce qui n'est pas reconnu, incomplet ou vise un identifiant absent du site est écarté avec une explication.
/// </summary>
public static partial class SiteAgentActions
{
    public const int MaxActions = 10;

    private record Spec(string Kind, string Verb, string[] Required, string[] Allowed, bool NeedsId, bool Destructive = false);

    private static readonly Dictionary<string, Spec> Specs = new()
    {
        ["news.create"] = new("news", "create", ["title", "content"], ["title", "content", "excerpt", "category", "publish"], false),
        ["news.update"] = new("news", "update", [], ["title", "content", "excerpt", "category"], true),
        ["news.publish"] = new("news", "publish", [], [], true),
        ["news.unpublish"] = new("news", "unpublish", [], [], true),
        ["news.delete"] = new("news", "delete", [], [], true, Destructive: true),
        ["event.create"] = new("event", "create", ["title", "startDate"], ["title", "startDate", "endDate", "location", "type", "description", "isPublic"], false),
        ["event.update"] = new("event", "update", [], ["title", "startDate", "endDate", "location", "type", "description", "isPublic"], true),
        ["event.delete"] = new("event", "delete", [], [], true, Destructive: true),
        ["activity.create"] = new("activity", "create", ["name", "category"], ["name", "category", "description", "schedule", "responsibleTeacher"], false),
        ["activity.update"] = new("activity", "update", [], ["name", "category", "description", "schedule", "responsibleTeacher", "isActive"], true),
        ["activity.delete"] = new("activity", "delete", [], [], true, Destructive: true),
        ["team.create"] = new("team", "create", ["name", "role"], ["name", "role", "preName", "department", "email", "description", "specialization"], false),
        ["team.update"] = new("team", "update", [], ["name", "role", "preName", "department", "email", "description", "specialization"], true),
        ["team.delete"] = new("team", "delete", [], [], true, Destructive: true),
    };

    [GeneratedRegex(@"```\s*action\s*\n(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ActionBlock();

    /// <summary>Retire les blocs d'action du texte et renvoie les actions valides, et les raisons des refus.</summary>
    public static (string Text, List<SiteAction> Actions, List<string> Rejected) Extract(string reply, SiteContext context)
    {
        var actions = new List<SiteAction>();
        var rejected = new List<string>();

        foreach (Match m in ActionBlock().Matches(reply ?? ""))
        {
            if (actions.Count >= MaxActions) { rejected.Add($"Plus de {MaxActions} actions proposées : les suivantes sont ignorées."); break; }
            var (action, error) = Validate(m.Groups[1].Value, context);
            if (action is not null) actions.Add(action);
            else if (error is not null) rejected.Add(error);
        }

        var text = ActionBlock().Replace(reply ?? "", "").Trim();
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return (text, actions, rejected);
    }

    public static (SiteAction? Action, string? Error) Validate(string json, SiteContext context)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(json.Trim()).RootElement; }
        catch (JsonException) { return (null, "Une proposition illisible a été écartée."); }
        if (root.ValueKind != JsonValueKind.Object) return (null, "Une proposition illisible a été écartée.");

        var type = SiteAgentPrompt.Str(root, "type").Trim().ToLowerInvariant();
        if (!Specs.TryGetValue(type, out var spec)) return (null, $"Action « {type} » inconnue : écartée.");

        var data = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in root.EnumerateObject())
            if (spec.Allowed.Contains(p.Name, StringComparer.OrdinalIgnoreCase) && p.Value.ValueKind != JsonValueKind.Null)
                data[p.Name] = p.Value.Clone();

        string? id = null;
        if (spec.NeedsId)
        {
            id = root.TryGetProperty("id", out var v) ? v.ValueKind == JsonValueKind.Number ? v.GetRawText() : v.GetString() : null;
            if (string.IsNullOrWhiteSpace(id) || context.Ids?.TryGetValue(spec.Kind, out var known) != true || !known!.Contains(id))
                return (null, $"Proposition « {type} » écartée : l'élément visé n'existe pas sur le site.");
            data["id"] = JsonDocument.Parse(JsonSerializer.Serialize(id)).RootElement.Clone();
        }

        foreach (var field in spec.Required)
            if (!data.TryGetValue(field, out var value) || value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
                return (null, $"Proposition « {type} » écartée : le champ « {field} » manque.");

        if (spec.Verb == "update" && data.Count <= 1)
            return (null, $"Proposition « {type} » écartée : aucun changement indiqué.");

        foreach (var dateField in new[] { "startDate", "endDate" })
            if (data.TryGetValue(dateField, out var d) && !(d.ValueKind == JsonValueKind.String && DateTime.TryParse(d.GetString(), out _)))
                return (null, $"Proposition « {type} » écartée : la date « {dateField} » est invalide.");

        foreach (var (key, value) in data)
            if (value.ValueKind == JsonValueKind.String && value.GetString()!.Length > 20000)
                return (null, $"Proposition « {type} » écartée : le champ « {key} » est trop long.");

        return (new SiteAction(type, Summary(spec, data, id, context), data, spec.Destructive), null);
    }

    /// <summary>Résumé lisible rédigé par le serveur (et non par le modèle) : c'est ce que la personne confirme.</summary>
    private static string Summary(Spec spec, Dictionary<string, JsonElement> data, string? id, SiteContext context)
    {
        string Field(string name) => data.TryGetValue(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.GetRawText() : "";
        var target = id is not null && context.Titles?.TryGetValue($"{spec.Kind}:{id}", out var t) == true ? t : "l'élément";
        var changes = string.Join(", ", data.Keys.Where(k => k != "id").Select(Label));

        return (spec.Kind, spec.Verb) switch
        {
            ("news", "create") => $"Créer l'article « {Field("title")} »{(Field("publish") == "true" ? " et le publier" : " (brouillon)")}",
            ("event", "create") => $"Créer l'événement « {Field("title")} » le {Field("startDate").Replace('T', ' ')}",
            ("activity", "create") => $"Créer l'activité « {Field("name")} » ({Field("category")})",
            ("team", "create") => $"Ajouter {Field("preName")} {Field("name")} ({Field("role")}) à l'équipe".Replace("  ", " "),
            (_, "update") => $"Modifier {target} : {changes}",
            (_, "publish") => $"Publier {target}",
            (_, "unpublish") => $"Repasser {target} en brouillon",
            (_, "delete") => $"Supprimer définitivement {target}",
            _ => $"{spec.Verb} {target}"
        };
    }

    private static string Label(string field) => field switch
    {
        "title" => "titre", "content" => "contenu", "excerpt" => "résumé", "category" => "catégorie",
        "startDate" => "début", "endDate" => "fin", "location" => "lieu", "type" => "type", "description" => "description",
        "isPublic" => "visibilité", "name" => "nom", "schedule" => "horaires", "responsibleTeacher" => "responsable",
        "isActive" => "statut", "role" => "fonction", "preName" => "prénom", "department" => "service", "email" => "e-mail",
        "specialization" => "spécialité", _ => field
    };
}
