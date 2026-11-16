using System.Text.Json;
using Chat.Endpoints;

namespace GPOE26.Logic.Tests;

/// <summary>Agent du site (administration) : ce que le modèle reçoit du contenu du site.</summary>
public class SiteAgentTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private static readonly DateTime Now = new(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Les_brouillons_et_les_articles_sans_image_sont_signales()
    {
        var news = Json("""
            {"total":2,"items":[
              {"title":"Bienvenue","category":"Annonce","isPublished":true,"imageUrl":"/img.png","excerpt":"Ouverture","updatedAt":"2026-11-09T10:00:00Z"},
              {"title":"Portes ouvertes","category":"Général","isPublished":false,"imageUrl":null,"content":"Venez nombreux","updatedAt":"2026-11-15T10:00:00Z"}
            ]}
            """);

        var ctx = SiteAgentPrompt.BuildContext(news, Json("[]"), Json("[]"), Json("[]"), Json("""{"name":"LT"}"""), Now);

        Assert.Contains("## Actualités (2)", ctx.Text);
        Assert.Contains("[publié] « Bienvenue »", ctx.Text);
        Assert.Contains("[brouillon] « Portes ouvertes »", ctx.Text);
        Assert.Contains("sans image", ctx.Text);
        Assert.Empty(ctx.Missing);
    }

    [Fact]
    public void Un_evenement_passe_est_distingue_d_un_evenement_a_venir()
    {
        var events = Json("""
            [{"title":"Rentrée","startDate":"2026-09-01T08:00:00Z","type":"Général","location":"Cour","isPublic":true,"description":"x"},
             {"title":"JPO","startDate":"2026-11-24T08:00:00Z","type":"JPOE","location":null,"isPublic":true,"description":"y"}]
            """);

        var ctx = SiteAgentPrompt.BuildContext(Json("""{"items":[]}"""), events, Json("[]"), Json("[]"), null, Now);

        Assert.Contains("« Rentrée » le 2026-09-01 (passé", ctx.Text);
        Assert.Contains("« JPO » le 2026-11-24 (à venir", ctx.Text);
        Assert.Contains("lieu non précisé", ctx.Text);
    }

    [Fact]
    public void Une_partie_illisible_est_annoncee_au_lieu_d_etre_inventee()
    {
        var ctx = SiteAgentPrompt.BuildContext(null, null, Json("[]"), Json("[]"), null, Now);

        Assert.Contains("actualités", ctx.Missing);
        Assert.Contains("événements", ctx.Missing);
        Assert.DoesNotContain("## Actualités", ctx.Text);
    }

    [Fact]
    public void Les_longs_textes_sont_raccourcis()
    {
        var longText = new string('a', 1000);
        var activities = Json($$"""[{"name":"Club","category":"Science","isActive":false,"description":"{{longText}}"}]""");

        var ctx = SiteAgentPrompt.BuildContext(Json("""{"items":[]}"""), Json("[]"), activities, Json("[]"), null, Now);

        Assert.Contains("suspendue", ctx.Text);
        Assert.DoesNotContain(longText, ctx.Text);
        Assert.Contains("…", ctx.Text);
    }

    // ── Actions proposées par l'agent ────────────────────────────────────────────────────────

    private static SiteContext Ctx() => SiteAgentPrompt.BuildContext(
        Json("""{"items":[{"id":"11111111-1111-1111-1111-111111111111","title":"Bienvenue","isPublished":true}]}"""),
        Json("""[{"id":"22222222-2222-2222-2222-222222222222","title":"JPO","startDate":"2026-11-24T08:00:00Z"}]"""),
        Json("[]"), Json("""[{"id":7,"preName":"Jean","name":"DONGMO","role":"Proviseur"}]"""), null, Now);

    [Fact]
    public void Une_creation_valide_devient_une_carte_a_confirmer_et_sort_du_texte()
    {
        var reply = "Voici l'article proposé.\n```action\n{\"type\":\"news.create\",\"title\":\"Portes ouvertes\",\"content\":\"Venez !\",\"publish\":false}\n```\nConfirmez si cela vous convient.";

        var (text, actions, rejected) = SiteAgentActions.Extract(reply, Ctx());

        var action = Assert.Single(actions);
        Assert.Equal("news.create", action.Type);
        Assert.Equal("Créer l'article « Portes ouvertes » (brouillon)", action.Summary);
        Assert.False(action.Destructive);
        Assert.Empty(rejected);
        Assert.DoesNotContain("```", text);
        Assert.Contains("Confirmez", text);
    }

    [Fact]
    public void Un_identifiant_invente_est_ecarte()
    {
        var reply = "```action\n{\"type\":\"news.delete\",\"id\":\"99999999-9999-9999-9999-999999999999\"}\n```";

        var (_, actions, rejected) = SiteAgentActions.Extract(reply, Ctx());

        Assert.Empty(actions);
        Assert.Contains("n'existe pas", Assert.Single(rejected));
    }

    [Fact]
    public void Une_suppression_connue_est_marquee_destructive_avec_le_vrai_titre()
    {
        var (_, actions, _) = SiteAgentActions.Extract(
            "```action\n{\"type\":\"event.delete\",\"id\":\"22222222-2222-2222-2222-222222222222\"}\n```", Ctx());

        var action = Assert.Single(actions);
        Assert.True(action.Destructive);
        Assert.Equal("Supprimer définitivement l'événement « JPO »", action.Summary);
    }

    [Fact]
    public void Un_membre_d_equipe_se_vise_par_son_identifiant_entier()
    {
        var (_, actions, _) = SiteAgentActions.Extract(
            "```action\n{\"type\":\"team.update\",\"id\":7,\"role\":\"Proviseur adjoint\"}\n```", Ctx());

        var action = Assert.Single(actions);
        Assert.Equal("Modifier Jean DONGMO : fonction", action.Summary);
        Assert.Equal("7", action.Data["id"].GetString());
    }

    [Theory]
    [InlineData("{\"type\":\"news.create\",\"title\":\"Sans contenu\"}", "content")]
    [InlineData("{\"type\":\"event.create\",\"title\":\"X\",\"startDate\":\"demain\"}", "date")]
    [InlineData("{\"type\":\"contact.update\",\"email\":\"x@y.cm\"}", "inconnue")]
    [InlineData("{\"type\":\"news.update\",\"id\":\"11111111-1111-1111-1111-111111111111\"}", "aucun changement")]
    [InlineData("pas du json", "illisible")]
    public void Une_proposition_incomplete_ou_inconnue_est_refusee(string json, string reason)
    {
        var (action, error) = SiteAgentActions.Validate(json, Ctx());
        Assert.Null(action);
        Assert.Contains(reason, error);
    }

    [Fact]
    public void Les_champs_non_prevus_ne_passent_pas()
    {
        var (action, _) = SiteAgentActions.Validate(
            "{\"type\":\"news.create\",\"title\":\"T\",\"content\":\"C\",\"isPublished\":true,\"slug\":\"x\"}", Ctx());
        Assert.NotNull(action);
        Assert.False(action!.Data.ContainsKey("isPublished"));
        Assert.False(action.Data.ContainsKey("slug"));
    }
}
