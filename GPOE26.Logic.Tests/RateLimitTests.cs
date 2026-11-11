using System.Text;
using GPOE26.ServiceDefaults.RateLimits;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GPOE26.Logic.Tests;

/// <summary>A9 : limites de débit sur la connexion, les codes et l'IA.</summary>
public class RateLimitTests
{
    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly DateTimeOffset T0 = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("POST", "/auth/login", "connexion")]
    [InlineData("POST", "/classrooms/join", "codes")]
    [InlineData("POST", "/parents/link", "codes")]
    [InlineData("POST", "/subscriptions/redeem", "codes")]
    [InlineData("POST", "/access-codes/redeem", "codes")]
    [InlineData("POST", "/tutor/courses/8f0e2c55-1d1a-4a0e-9a59-2b0c8d1f3e11/messages/stream", "ia")]
    [InlineData("POST", "/teacher-agent/ask", "ia")]
    [InlineData("POST", "/api/Quiz/generate", "ia")]
    [InlineData("POST", "/Chat/draft", "ia")]
    [InlineData("POST", "/cours/import-link", "ia")]
    [InlineData("POST", "/assessments/a/submissions/b/answers/c/ai-feedback", "ia")]
    [InlineData("GET", "/auth/login", null)]
    [InlineData("POST", "/auth/register", null)]
    [InlineData("PUT", "/assessments/a/attempts/b/draft", null)]
    [InlineData("POST", "/tutor/courses/x/messages/autre", null)]
    public void Chaque_route_a_la_bonne_regle(string method, string path, string? rule)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        Assert.Equal(rule, RateRules.All.FirstOrDefault(r => r.Matches(context.Request))?.Name);
    }

    [Fact]
    public void Le_compteur_bloque_puis_libere_a_la_fin_de_la_fenetre()
    {
        var time = new ManualTime(T0);
        var store = new RateLimitStore(time);
        for (var i = 0; i < 5; i++)
        {
            Assert.Null(store.Blocked(RateRules.Login, "eleve@x.cm"));
            store.Hit(RateRules.Login, "eleve@x.cm");
        }

        time.Now = T0.AddMinutes(5);
        Assert.Equal(TimeSpan.FromMinutes(10), store.Blocked(RateRules.Login, "eleve@x.cm"));
        Assert.Null(store.Blocked(RateRules.Login, "autre@x.cm"));   // un compte ne bloque pas les autres
        Assert.Null(store.Blocked(RateRules.Codes, "eleve@x.cm"));   // ni les autres règles

        time.Now = T0.AddMinutes(15);
        Assert.Null(store.Blocked(RateRules.Login, "eleve@x.cm"));
    }

    [Theory]
    [InlineData(20, "dans quelques secondes")]
    [InlineData(70, "dans une minute")]
    [InlineData(600, "dans 10 minutes")]
    public void Le_delai_se_lit_simplement(int seconds, string expected) =>
        Assert.Equal(expected, RateRules.In(TimeSpan.FromSeconds(seconds)));

    private static async Task<HttpContext> LoginAsync(RateLimitMiddleware middleware, string email)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/auth/login";
        var body = Encoding.UTF8.GetBytes($$"""{"Email":"{{email}}","password":"faux"}""");
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);
        return context;
    }

    private static RateLimitMiddleware Middleware(RateLimitStore store, int status, Action<string>? seenBody = null) =>
        new(async ctx =>
        {
            // L'endpoint relit le corps normalement après le middleware
            seenBody?.Invoke(await new StreamReader(ctx.Request.Body).ReadToEndAsync());
            ctx.Response.StatusCode = status;
        }, store, new ConfigurationBuilder().Build(), NullLogger<RateLimitMiddleware>.Instance);

    [Fact]
    public async Task Cinq_mots_de_passe_faux_bloquent_le_compte_quelle_que_soit_la_casse()
    {
        var store = new RateLimitStore(new ManualTime(T0));
        string? seen = null;
        var failing = Middleware(store, 401, b => seen = b);
        for (var i = 0; i < 5; i++)
            Assert.Equal(401, (await LoginAsync(failing, i % 2 == 0 ? "Eleve@X.cm" : " eleve@x.cm")).Response.StatusCode);
        Assert.Contains("\"password\"", seen);

        var blocked = await LoginAsync(Middleware(store, 200), "eleve@x.cm");
        Assert.Equal(429, blocked.Response.StatusCode);
        Assert.Equal("900", blocked.Response.Headers.RetryAfter.ToString());
        blocked.Response.Body.Position = 0;
        Assert.Contains("Trop d'essais de connexion", await new StreamReader(blocked.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task Les_connexions_reussies_ne_comptent_pas()
    {
        var store = new RateLimitStore(new ManualTime(T0));
        var ok = Middleware(store, 200);
        for (var i = 0; i < 20; i++)
            Assert.Equal(200, (await LoginAsync(ok, "prof@x.cm")).Response.StatusCode);
    }

    [Fact]
    public async Task La_limite_peut_etre_desactivee_pour_une_pile_de_test()
    {
        var store = new RateLimitStore(new ManualTime(T0));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimits:Enabled"] = "false" }).Build();
        var failing = new RateLimitMiddleware(ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; }, store, config,
            NullLogger<RateLimitMiddleware>.Instance);
        for (var i = 0; i < 8; i++)
            Assert.Equal(401, (await LoginAsync(failing, "eleve@x.cm")).Response.StatusCode);
    }
}
