using User.Endpoints;
using User.Model;
using User.Service;

namespace GPOE26.Logic.Tests;

/// <summary>Qui voit quoi (docs/PLAN-APPRENANT-AU-CENTRE.md, §3, lot J3) : règles pures.</summary>
public class VisibilityTests
{
    private static readonly VisibilityOptions Options = new(RetentionMonths: 12);
    private static readonly DateTime Now = new(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void La_personne_voit_tout_toujours() =>
        Assert.Equal(VisibilityRules.AllScopes,
            VisibilityRules.VisibleScopes(ViewerKind.Self, current: false, stillVisible: false, [SharingScope.Progress]));

    [Fact]
    public void Un_membre_actuel_partage_tout_par_defaut() =>
        Assert.Equal(VisibilityRules.AllScopes, VisibilityRules.VisibleScopes(ViewerKind.Teacher, true, true, []));

    [Fact]
    public void Un_ancien_membre_ne_montre_jamais_ses_notions_fragiles()
    {
        // Elles sont réécrites en continu et ne se datent pas (H3)
        var scopes = VisibilityRules.VisibleScopes(ViewerKind.Manager, current: false, stillVisible: true, []);
        Assert.Equal([SharingScope.OfficialResults, SharingScope.Progress], scopes);
    }

    [Fact]
    public void Apres_la_conservation_un_espace_ne_voit_plus_rien() =>
        Assert.Empty(VisibilityRules.VisibleScopes(ViewerKind.Teacher, current: false, stillVisible: false, []));

    [Fact]
    public void Les_resultats_officiels_ne_se_retirent_pas()
    {
        var scopes = VisibilityRules.VisibleScopes(ViewerKind.Manager, true, true,
            [SharingScope.OfficialResults, SharingScope.Progress, SharingScope.WeakPoints]);
        Assert.Equal([SharingScope.OfficialResults], scopes);
        Assert.False(VisibilityRules.CanOptOut(SharingScope.OfficialResults));
        Assert.True(VisibilityRules.CanOptOut(SharingScope.Progress));
    }

    [Theory]
    [InlineData(-1, true)]    // parti il y a un mois : encore visible
    [InlineData(-11, true)]
    [InlineData(-13, false)]  // plus de 12 mois : plus rien
    public void La_conservation_part_de_la_date_de_depart(int monthsAgo, bool visible) =>
        Assert.Equal(visible, VisibilityRules.FormerStillVisible(Now.AddMonths(monthsAgo), Now, Options));

    [Fact]
    public void Un_membre_actuel_est_toujours_visible_et_le_retour_arriere_coupe_les_anciens()
    {
        Assert.True(VisibilityRules.FormerStillVisible(null, Now, Options));
        Assert.False(VisibilityRules.FormerStillVisible(Now.AddMonths(-1), Now, Options with { Enabled = false }));
        Assert.Equal(Now.AddMonths(-1).AddMonths(12), VisibilityRules.VisibleUntil(Now.AddMonths(-1), Options));
        Assert.Null(VisibilityRules.VisibleUntil(null, Options));
    }

    [Fact]
    public void Un_enseignant_arrive_apres_le_depart_ne_voit_pas_l_ancien_eleve()
    {
        var left = Now.AddMonths(-2);
        Assert.True(VisibilityRules.TeacherKnewMember(left.AddMonths(-6), left));
        Assert.False(VisibilityRules.TeacherKnewMember(left.AddDays(1), left));
        Assert.True(VisibilityRules.TeacherKnewMember(Now, memberLeftAt: null));
    }

    [Theory]
    [InlineData(2000, null, false, true)]     // 26 ans
    [InlineData(2010, null, false, false)]    // 16 ans, même sans parent
    [InlineData(2008, null, false, false)]    // peut-être encore 17 ans le 1er janvier : prudence
    [InlineData(null, null, true, false)]     // âge inconnu, parent rattaché : mineure
    [InlineData(null, null, false, true)]     // âge inconnu, sans parent : majeure
    public void La_majorite_est_reputee(int? birthYear, string? schoolBirthDate, bool guardian, bool adult) =>
        Assert.Equal(adult, VisibilityRules.IsDeemedAdult(birthYear,
            schoolBirthDate is null ? null : DateOnly.Parse(schoolBirthDate), guardian, new DateOnly(2026, 11, 22)));

    [Fact]
    public void La_fiche_d_etablissement_compte_quand_l_annee_manque()
    {
        var today = new DateOnly(2026, 11, 22);
        Assert.False(VisibilityRules.IsDeemedAdult(null, new DateOnly(2009, 1, 1), false, today));
        Assert.True(VisibilityRules.IsDeemedAdult(null, new DateOnly(2008, 11, 22), true, today));   // 18 ans aujourd'hui
        Assert.Equal(17, VisibilityRules.AgeOn(new DateOnly(2008, 11, 23), today));
    }

    [Theory]
    [InlineData("progress", SharingScope.Progress)]
    [InlineData("Weak-Points", SharingScope.WeakPoints)]
    [InlineData("official-results", SharingScope.OfficialResults)]
    public void Les_portees_des_routes_sont_lues(string text, SharingScope expected)
    {
        Assert.Equal(expected, VisibilityRules.ParseScope(text));
        Assert.Equal(text.ToLowerInvariant(), VisibilityRules.Name(expected));
    }

    [Fact]
    public void Une_portee_inconnue_est_refusee() => Assert.Null(VisibilityRules.ParseScope("discussions"));
}

/// <summary>Année de naissance facultative dans le profil (lot J3, choix T3).</summary>
public class BirthYearProfileTests
{
    private static AppUser NewUser() => new() { Username = "adulte", PasswordHash = "x", Role = UserRole.Student };

    [Fact]
    public void Une_annee_plausible_est_gardee_et_zero_l_efface()
    {
        var user = NewUser();
        Assert.Null(ProfileRules.Apply(user, new EditProfileRequest(null, BirthYear: 1990)));
        Assert.Equal(1990, user.BirthYear);
        Assert.Null(ProfileRules.Apply(user, new EditProfileRequest(null, BirthYear: 0)));
        Assert.Null(user.BirthYear);
    }

    [Theory]
    [InlineData(1850)]
    [InlineData(3000)]
    public void Une_annee_impossible_est_refusee(int year) =>
        Assert.NotNull(ProfileRules.Apply(NewUser(), new EditProfileRequest(null, BirthYear: year)));
}
