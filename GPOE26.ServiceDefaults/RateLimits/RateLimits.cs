using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GPOE26.ServiceDefaults.RateLimits;

/// <summary>Ce qui identifie l'appelant pour une règle.</summary>
public enum RateKey
{
    /// <summary>Adresse e-mail saisie (connexion : personne n'est encore authentifié).</summary>
    Email,

    /// <summary>Compte authentifié (identifiant du jeton).</summary>
    User
}

/// <summary>
/// Une limite : au plus <paramref name="Permits"/> appels (ou échecs) par <paramref name="Window"/> et par appelant.
/// </summary>
/// <param name="FailuresOnly">Seules les réponses refusées comptent (mot de passe faux, code inconnu) : un utilisateur
/// qui réussit n'est jamais freiné.</param>
public sealed record RateRule(string Name, string Method, Regex Path, RateKey Key, int Permits, TimeSpan Window,
    bool FailuresOnly, string Message)
{
    public bool Matches(HttpRequest request) =>
        HttpMethods.Equals(request.Method, Method) && Path.IsMatch(request.Path.Value ?? "");

    /// <summary>Réponses qui comptent comme un échec : demande refusée par le service, pas une panne.</summary>
    public static bool IsFailure(int status) => status is 400 or 401 or 403 or 404 or 409 or 422;
}

/// <summary>
/// Limites de débit de la plateforme (audit A9). Les appels arrivent aux services depuis le serveur du site :
/// limiter par adresse IP mettrait tous les élèves dans le même compteur. On compte donc par compte authentifié,
/// et par adresse e-mail pour la connexion.
/// </summary>
public static class RateRules
{
    private static Regex P(string pattern) => new($"^{pattern}/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Essais de mot de passe : 5 échecs par compte en 15 minutes.</summary>
    public static readonly RateRule Login = new("connexion", "POST", P("/auth/login"), RateKey.Email, 5, TimeSpan.FromMinutes(15),
        FailuresOnly: true, "Trop d'essais de connexion pour ce compte. Réessayez {0}, ou demandez de l'aide à votre établissement.");

    /// <summary>Codes de classe, de rattachement parent et clés d'abonnement : 10 codes refusés en 15 minutes.</summary>
    public static readonly RateRule Codes = new("codes", "POST", P("/(classrooms/join|parents/link|subscriptions/redeem)"), RateKey.User,
        10, TimeSpan.FromMinutes(15), FailuresOnly: true, "Trop de codes refusés. Vérifiez le code auprès de la personne qui vous l'a donné, puis réessayez {0}.");

    /// <summary>
    /// Appels à l'IA : 20 par minute et par compte. Protège des rafales (double clic, script) ; le quota du jour
    /// de chaque formule reste vérifié par le service.
    /// </summary>
    public static readonly RateRule Ai = new("ia", "POST", P("(" + string.Join("|",
            "/tutor/courses/[^/]+/messages(/stream)?",
            "/tutor/images",
            "/teacher-agent/(compose|ask|classes/[^/]+/insights)",
            "/assessments/[^/]+/submissions/[^/]+/answers/[^/]+/ai-(suggestion|feedback)",
            "/api/quiz/(generate|problems)",
            "/chat/draft",
            "/cours/(import-link|import-photos|[^/]+/restructure|[^/]+/upload)") + ")"),
        RateKey.User, 20, TimeSpan.FromMinutes(1),
        FailuresOnly: false, "Trop de demandes à l'IA en peu de temps. Réessayez {0}.");

    public static readonly IReadOnlyList<RateRule> All = [Login, Codes, Ai];

    /// <summary>« dans 12 minutes », « dans une minute », « dans quelques secondes ».</summary>
    public static string In(TimeSpan wait) =>
        wait.TotalSeconds < 45 ? "dans quelques secondes"
        : wait.TotalMinutes < 1.5 ? "dans une minute"
        : $"dans {Math.Ceiling(wait.TotalMinutes)} minutes";
}

/// <summary>Compteurs en mémoire, par règle et par appelant, sur une fenêtre fixe qui commence au premier appel compté.</summary>
public sealed class RateLimitStore(TimeProvider time)
{
    private sealed class Counter
    {
        public DateTimeOffset Start;
        public int Count;
    }

    private readonly ConcurrentDictionary<string, Counter> _counters = new();
    private const int PruneAbove = 20_000;

    /// <summary>Temps à attendre si l'appelant a épuisé sa limite ; null sinon.</summary>
    public TimeSpan? Blocked(RateRule rule, string key)
    {
        if (!_counters.TryGetValue(Id(rule, key), out var counter)) return null;
        lock (counter)
        {
            var end = counter.Start + rule.Window;
            var now = time.GetUtcNow();
            return now < end && counter.Count >= rule.Permits ? end - now : null;
        }
    }

    /// <summary>Compte un appel (ou un échec).</summary>
    public void Hit(RateRule rule, string key)
    {
        var now = time.GetUtcNow();
        var counter = _counters.GetOrAdd(Id(rule, key), _ => new Counter { Start = now });
        lock (counter)
        {
            if (now >= counter.Start + rule.Window)
            {
                counter.Start = now;
                counter.Count = 0;
            }
            counter.Count++;
        }
        if (_counters.Count > PruneAbove) Prune(now);
    }

    private void Prune(DateTimeOffset now)
    {
        var longest = RateRules.All.Max(r => r.Window);
        foreach (var (id, counter) in _counters)
            if (now - counter.Start > longest) _counters.TryRemove(id, out _);
    }

    private static string Id(RateRule rule, string key) => rule.Name + ":" + key;
}

/// <summary>
/// Applique <see cref="RateRules"/> avant tout le reste. Placé par un IStartupFilter : aucun Program.cs n'a à l'appeler.
/// Une règle par compte authentifie la requête elle-même (le résultat est mis en cache par ASP.NET Core et resservi
/// à UseAuthentication) : un jeton invalide n'est pas limité ici, il sera refusé plus loin.
/// Désactivable par configuration (<c>RateLimits:Enabled=false</c>), pour une pile de test.
/// </summary>
public sealed class RateLimitMiddleware(RequestDelegate next, RateLimitStore store, IConfiguration config, ILogger<RateLimitMiddleware> logger)
{
    private const int MaxLoginBody = 8 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        var rule = config.GetValue("RateLimits:Enabled", true) ? RateRules.All.FirstOrDefault(r => r.Matches(context.Request)) : null;
        var key = rule is null ? null : rule.Key == RateKey.Email ? await EmailAsync(context.Request) : await UserAsync(context);
        if (rule is null || key is null)
        {
            await next(context);
            return;
        }

        if (store.Blocked(rule, key) is { } wait)
        {
            logger.LogWarning("Limite « {Rule} » atteinte pour {Key}", rule.Name, rule.Key == RateKey.Email ? Mask(key) : key);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
            await context.Response.WriteAsJsonAsync(new { message = string.Format(rule.Message, RateRules.In(wait)) });
            return;
        }

        if (!rule.FailuresOnly) store.Hit(rule, key);
        await next(context);
        if (rule.FailuresOnly && RateRule.IsFailure(context.Response.StatusCode)) store.Hit(rule, key);
    }

    /// <summary>Adresse e-mail du corps JSON de la connexion, relu ensuite normalement par l'endpoint.</summary>
    public static async Task<string?> EmailAsync(HttpRequest request)
    {
        if (request.ContentLength is > MaxLoginBody) return null;
        request.EnableBuffering(MaxLoginBody);
        try
        {
            using var doc = await JsonDocument.ParseAsync(request.Body);
            foreach (var property in doc.RootElement.EnumerateObject())
                if (property.Name.Equals("email", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString()?.Trim().ToLowerInvariant() is { Length: > 0 } email ? email : null;
            return null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
        {
            return null;   // corps illisible : l'endpoint répondra lui-même 400
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    private static async Task<string?> UserAsync(HttpContext context)
    {
        try
        {
            var result = await context.AuthenticateAsync();
            return result.Succeeded
                ? result.Principal.FindFirstValue("sub") ?? result.Principal.FindFirstValue(ClaimTypes.NameIdentifier)
                : null;
        }
        catch (InvalidOperationException)
        {
            return null;   // service sans authentification par défaut
        }
    }

    private static string Mask(string email) =>
        email.IndexOf('@') is > 1 and var at ? email[0] + new string('*', at - 1) + email[at..] : "***";
}

/// <summary>Place <see cref="RateLimitMiddleware"/> en tête du pipeline de chaque service.</summary>
public sealed class RateLimitStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseMiddleware<RateLimitMiddleware>();
        next(app);
    };
}
