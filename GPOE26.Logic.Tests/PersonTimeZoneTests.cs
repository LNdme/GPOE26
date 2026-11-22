using Chat.Service;
using GPOE26.UI.Models;
using GPOE26.UI.Services;

namespace GPOE26.Logic.Tests;

/// <summary>Lot B3 : l'heure de la personne, pas celle du Cameroun ; « Africa/Douala » seulement en dernier recours.</summary>
public class PersonTimeZoneTests
{
    private static readonly DateTime NoonUtc = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);

    // ─── Agents (Chat) ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("America/Mexico_City", "Europe/Paris", "America/Mexico_City")]   // le fuseau de la personne d'abord
    [InlineData(null, "Europe/Paris", "Europe/Paris")]                          // puis celui de la plateforme
    [InlineData("Mars/Olympus", "Europe/Paris", "Europe/Paris")]                // un fuseau inconnu est ignoré
    [InlineData(null, null, "Africa/Douala")]                                   // dernier recours
    [InlineData(" ", "nimporte/quoi", "Africa/Douala")]
    public void Fuseau_des_agents(string? person, string? configured, string expected)
    {
        var zone = AgentContextService.ResolveZone(person, configured);
        Assert.Equal(TimeZoneInfo.FindSystemTimeZoneById(expected).BaseUtcOffset, zone.BaseUtcOffset);
        Assert.Equal(TimeZoneInfo.FindSystemTimeZoneById(expected).GetUtcOffset(NoonUtc), zone.GetUtcOffset(NoonUtc));
    }

    [Fact]
    public void Le_jour_de_l_eleve_depend_de_son_fuseau()
    {
        // 23 h 30 UTC : déjà le lendemain à Douala, encore la veille à Mexico
        var late = new DateTime(2026, 11, 17, 23, 30, 0, DateTimeKind.Utc);
        Assert.Equal(18, TimeZoneInfo.ConvertTimeFromUtc(late, AgentContextService.ResolveZone(null, null)).Day);
        Assert.Equal(17, TimeZoneInfo.ConvertTimeFromUtc(late, AgentContextService.ResolveZone("America/Mexico_City", null)).Day);
    }

    // ─── Interface (AppTime) ──────────────────────────────────────────────────────

    private static UserProfile Profile(string? timeZone) => new(
        Guid.NewGuid(), null, "x", UserRole.Student, null, null, null, "fr", NoonUtc, null, null, null, null, null, null, null, null,
        null, null, false, null, null,
        LearningContext: new LearningContext("MX", "Mexique", null, null, null, null, null, null, GradeScale.Over10, timeZone, []));

    [Fact]
    public void Sans_personne_le_fuseau_de_l_hote()
    {
        Assert.Same(AppTime.Zone, AppTime.Current);
        Assert.Null(AppTime.ZoneOf(null));
        Assert.Null(AppTime.ZoneOf(Profile(null)));
        Assert.Null(AppTime.ZoneOf(Profile("Mars/Olympus")));
    }

    [Fact]
    public void Le_fuseau_de_la_personne_s_applique_puis_se_retire()
    {
        var mexico = AppTime.ZoneOf(Profile("America/Mexico_City"))!;
        using (AppTime.Use(mexico))
        {
            Assert.Equal(6, AppTime.ToLocal(NoonUtc).Hour);   // 12 h UTC = 6 h à Mexico
            Assert.Equal(NoonUtc, AppTime.ToUtc(new DateTime(2026, 11, 17, 6, 0, 0)));
        }
        Assert.Same(AppTime.Zone, AppTime.Current);
    }

    [Fact]
    public void Un_profil_restaure_pendant_l_activite_compte_aussitot()
    {
        UserProfile? profile = null;   // la session n'est pas encore restaurée au début de l'activité
        using (AppTime.Use(() => AppTime.ZoneOf(profile)))
        {
            Assert.Same(AppTime.Zone, AppTime.Current);
            profile = Profile("Europe/Paris");
            Assert.Equal(13, AppTime.ToLocal(NoonUtc).Hour);
        }
    }

    [Fact]
    public async Task Deux_personnes_servies_en_meme_temps_gardent_chacune_leur_fuseau()
    {
        var mexico = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
        var paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        var gate = new TaskCompletionSource();

        async Task<int> Activity(TimeZoneInfo zone)
        {
            using (AppTime.Use(zone))
            {
                await gate.Task;              // les deux activités sont en cours en même temps
                await Task.Yield();
                return AppTime.ToLocal(NoonUtc).Hour;
            }
        }

        var first = Activity(mexico);
        var second = Activity(paris);
        gate.SetResult();
        var hours = await Task.WhenAll(first, second);
        Assert.Equal(new[] { 6, 13 }, hours);
        Assert.Same(AppTime.Zone, AppTime.Current);
    }
}
