using System.Security.Claims;
using GPOE26.UI.Models;
using GPOE26.UI.Services;
using Microsoft.Extensions.Configuration;
using User.Model;
using User.Service;

namespace GPOE26.Logic.Tests;

/// <summary>Renouvellement de la session : elle suit l'activité au lieu de tomber 60 minutes après la connexion.</summary>
public class SessionRenewalTests
{
    private sealed class MemoryStore : ISessionStore
    {
        public StoredSession? Saved;
        public Task<StoredSession?> LoadAsync() => Task.FromResult(Saved);
        public Task SaveAsync(StoredSession session) { Saved = session; return Task.CompletedTask; }
        public Task ClearAsync() { Saved = null; return Task.CompletedTask; }
    }

    private static AuthResponse Auth(string token, int minutes = 60) => new(token, DateTime.UtcNow.AddMinutes(minutes), null!);

    [Theory]
    [InlineData(0, false)]    // jeton neuf
    [InlineData(19, false)]
    [InlineData(20, true)]    // un tiers de la durée de vie écoulé
    [InlineData(59, true)]
    [InlineData(60, false)]   // expiré : il n'y a plus rien à renouveler
    public void Le_jeton_d_une_heure_se_renouvelle_apres_vingt_minutes(int minutesElapsed, bool expected)
    {
        var received = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, SessionState.DueForRenewal(received, received.AddMinutes(60), received.AddMinutes(minutesElapsed)));
    }

    [Fact]
    public async Task Un_jeton_neuf_n_est_pas_renouvele()
    {
        var session = new SessionState(new MemoryStore());
        await session.SignInAsync(Auth("jeton-1"));
        Assert.False(session.ShouldRenew);
    }

    [Fact]
    public async Task Le_renouvellement_remplace_le_jeton_et_l_enregistre()
    {
        var store = new MemoryStore();
        var session = new SessionState(store);
        await session.SignInAsync(Auth("jeton-1"));

        await session.RenewAsync(current => Task.FromResult<AuthResponse?>(Auth(current + "-renouvelé")));

        Assert.Equal("jeton-1-renouvelé", session.Token);
        Assert.Equal("jeton-1-renouvelé", store.Saved!.Token);
        Assert.True(session.IsAuthenticated);
    }

    [Fact]
    public async Task Un_renouvellement_refuse_ne_deconnecte_pas()
    {
        var session = new SessionState(new MemoryStore());
        await session.SignInAsync(Auth("jeton-1"));

        await session.RenewAsync(_ => Task.FromResult<AuthResponse?>(null));
        await session.RenewAsync(_ => throw new HttpRequestException("service injoignable"));

        Assert.Equal("jeton-1", session.Token);
        Assert.True(session.IsAuthenticated);
    }

    [Fact]
    public async Task Plusieurs_appels_simultanes_ne_demandent_qu_un_seul_jeton()
    {
        var session = new SessionState(new MemoryStore());
        await session.SignInAsync(Auth("jeton-1"));
        var gate = new TaskCompletionSource<AuthResponse?>();
        var calls = 0;
        Task<AuthResponse?> Request(string _) { calls++; return gate.Task; }

        var first = session.RenewAsync(Request);
        var second = session.RenewAsync(Request);
        gate.SetResult(Auth("jeton-2"));
        await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
        Assert.Equal("jeton-2", session.Token);
    }

    [Fact]
    public async Task Une_deconnexion_pendant_le_renouvellement_reste_une_deconnexion()
    {
        var session = new SessionState(new MemoryStore());
        await session.SignInAsync(Auth("jeton-1"));
        var gate = new TaskCompletionSource<AuthResponse?>();

        var renewal = session.RenewAsync(_ => gate.Task);
        await session.SignOutAsync();
        gate.SetResult(Auth("jeton-2"));
        await renewal;

        Assert.Null(session.Token);
        Assert.False(session.IsAuthenticated);
    }
}

/// <summary>Côté service des comptes : le jeton garde la date de la vraie connexion, qui borne la session.</summary>
public class SessionLimitTests
{
    private static readonly TimeSpan TwelveHours = TimeSpan.FromHours(12);

    [Theory]
    [InlineData(0, true)]
    [InlineData(11, true)]
    [InlineData(12, false)]
    [InlineData(48, false)]
    public void Une_session_se_prolonge_jusqu_a_douze_heures(int hoursSinceSignIn, bool expected)
    {
        var now = new DateTime(2026, 11, 10, 20, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, Jwtservice.CanRenew(now.AddHours(-hoursSinceSignIn), now, TwelveHours));
    }

    [Fact]
    public void Une_date_de_connexion_dans_le_futur_est_refusee()
    {
        var now = new DateTime(2026, 11, 10, 20, 0, 0, DateTimeKind.Utc);
        Assert.False(Jwtservice.CanRenew(now.AddMinutes(5), now, TwelveHours));
    }

    [Fact]
    public void Le_jeton_renouvele_garde_la_date_de_la_connexion_d_origine()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "une-cle-secrete-de-plus-de-trente-deux-caracteres",
            ["Jwt:Issuer"] = "GPOE2026",
            ["Jwt:Audience"] = "GPOE2026Users"
        }).Build();
        var jwt = new Jwtservice(config);
        var user = new AppUser { Email = "eleve@exemple.cm", Username = "Élève", PasswordHash = "x" };
        var signedInAt = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);

        var (token, _) = jwt.GenerateToken(user, new User.Service.Entitlement(false, null, null, null), signedInAt);

        var claims = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token).Claims;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        Assert.Equal(signedInAt, Jwtservice.SignedInAt(principal));
    }

    [Fact]
    public void Un_ancien_jeton_sans_date_de_connexion_est_reconnu() =>
        Assert.Null(Jwtservice.SignedInAt(new ClaimsPrincipal(new ClaimsIdentity())));
}
