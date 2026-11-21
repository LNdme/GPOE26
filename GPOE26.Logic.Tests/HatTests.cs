using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using User.Model;
using User.Service;
using UiHats = GPOE26.UI.Models.UserHats;
using UiRole = GPOE26.UI.Models.UserRole;

namespace GPOE26.Logic.Tests;

/// <summary>
/// Casquettes (docs/PLAN-PUBLICS.md, lot P3) : toute personne apprend, et peut en plus suivre des enfants,
/// enseigner, gérer un espace ou payer pour d'autres.
/// </summary>
public class HatRulesTests
{
    [Theory]
    [InlineData(UserRole.Student)]
    [InlineData(UserRole.Teacher)]
    [InlineData(UserRole.Parent)]
    public void Toute_personne_apprend(UserRole role) =>
        Assert.Equal(HatRules.Learner, HatRules.Compute(role, new HatRules.Facts())[0]);

    [Fact]
    public void Un_parent_suit_ses_enfants_et_peut_apprendre() =>
        Assert.Equal([HatRules.Learner, HatRules.Guardian], HatRules.Compute(UserRole.Parent, new HatRules.Facts()));

    [Fact]
    public void Un_enseignant_enseigne_et_peut_apprendre() =>
        Assert.Equal([HatRules.Learner, HatRules.Teacher], HatRules.Compute(UserRole.Teacher, new HatRules.Facts()));

    [Fact]
    public void Un_enseignant_rattache_a_un_enfant_le_suit_aussi() =>
        Assert.Contains(HatRules.Guardian, HatRules.Compute(UserRole.Teacher, new HatRules.Facts(FollowsChild: true)));

    [Fact]
    public void Un_responsable_d_espace_porte_la_casquette_de_gestionnaire() =>
        Assert.Contains(HatRules.Manager, HatRules.Compute(UserRole.Teacher, new HatRules.Facts(ManagesSpace: true)));

    [Fact]
    public void Qui_paie_une_formule_Famille_est_payeur() =>
        Assert.Contains(HatRules.Payer, HatRules.Compute(UserRole.Parent, new HatRules.Facts(PaysForFamily: true)));

    [Fact]
    public void Un_eleve_n_enseigne_pas_et_ne_gere_rien() =>
        Assert.Equal([HatRules.Learner], HatRules.Compute(UserRole.Student, new HatRules.Facts()));

    [Theory]
    [InlineData(UserRole.Student, UiRole.Student)]
    [InlineData(UserRole.Teacher, UiRole.Teacher)]
    [InlineData(UserRole.Parent, UiRole.Parent)]
    public void L_interface_deduit_les_memes_casquettes_que_le_serveur_pour_un_ancien_profil(UserRole server, UiRole ui) =>
        // Un profil enregistré avant P3 n'a pas de casquettes : l'interface les déduit du rôle, comme le serveur
        Assert.Equal(HatRules.FromRole(server), UiHats.FromRole(ui));

    [Fact]
    public void Les_noms_des_casquettes_sont_les_memes_des_deux_cotes()
    {
        Assert.Equal(HatRules.Learner, UiHats.Learner);
        Assert.Equal(HatRules.Guardian, UiHats.Guardian);
        Assert.Equal(HatRules.Teacher, UiHats.Teacher);
        Assert.Equal(HatRules.Manager, UiHats.Manager);
        Assert.Equal(HatRules.Payer, UiHats.Payer);
    }

    [Fact]
    public void Le_jeton_porte_une_claim_par_casquette()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "une-cle-secrete-de-plus-de-trente-deux-caracteres",
            ["Jwt:Issuer"] = "GPOE2026",
            ["Jwt:Audience"] = "GPOE2026Users"
        }).Build();
        var user = new AppUser { Email = "parent@exemple.test", Username = "Parent", PasswordHash = "x", Role = UserRole.Parent };

        var (token, _) = new Jwtservice(config).GenerateToken(user, new User.Service.Entitlement(false, null, null, null),
            hats: [HatRules.Learner, HatRules.Guardian, HatRules.Payer]);

        var hats = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.Where(c => c.Type == "hats").Select(c => c.Value);
        Assert.Equal([HatRules.Learner, HatRules.Guardian, HatRules.Payer], hats);
    }

    [Fact]
    public void Sans_casquettes_fournies_le_jeton_les_deduit_du_role()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "une-cle-secrete-de-plus-de-trente-deux-caracteres",
            ["Jwt:Issuer"] = "GPOE2026",
            ["Jwt:Audience"] = "GPOE2026Users"
        }).Build();
        var user = new AppUser { Email = "prof@exemple.test", Username = "Prof", PasswordHash = "x", Role = UserRole.Teacher };

        var (token, _) = new Jwtservice(config).GenerateToken(user, new User.Service.Entitlement(false, null, null, null));

        var hats = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.Where(c => c.Type == "hats").Select(c => c.Value);
        Assert.Equal([HatRules.Learner, HatRules.Teacher], hats);
    }
}
