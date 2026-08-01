using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GPOE26.Harness.Contract;

namespace GPOE26.Harness.Conformance;

/// <summary>
/// L'étage conformité : ce qu'un harness doit faire, sans exception et sans part de
/// jugement. Chaque test ici est déterministe — aucun n'appelle de modèle, aucun ne
/// dépend d'une formulation.
///
/// C'est la distinction qui rend le match honnête : on ne peut pas faire échouer une
/// implémentation sur ce qu'un modèle a répondu, mais on peut exiger qu'elle respecte sa
/// parole sur les outils, les droits et la forme du flux.
/// </summary>
[Collection(HarnessCollection.Name)]
public class ConformanceTests(HarnessTarget target)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Guid Eleve = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AutreEleve = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Parent = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Cours = Guid.Parse("44444444-4444-4444-4444-444444444444");

    // ── Les outils exposés suivent la table, exactement ───────────────────────

    [Theory]
    [InlineData(SessionKind.Lecture)]
    [InlineData(SessionKind.Exercice)]
    [InlineData(SessionKind.Code)]
    [InlineData(SessionKind.Suivi)]
    public Task Les_outils_exposes_correspondent_a_la_table(SessionKind kind) =>
        Checks.ToolsMatchPolicy(target, kind);

    [Fact]
    public async Task Une_session_de_suivi_n_expose_aucun_outil()
    {
        var client = target.AsStudent(Eleve);

        var descriptors = await client.GetFromJsonAsync<List<ToolDescriptor>>(
            $"outils?kind={(int)SessionKind.Suivi}", Json);

        Assert.Empty(descriptors!);
    }

    [Fact]
    public async Task Chaque_outil_declare_un_schema_de_parametres_exploitable()
    {
        var client = target.AsStudent(Eleve);

        var descriptors = await client.GetFromJsonAsync<List<ToolDescriptor>>(
            $"outils?kind={(int)SessionKind.Code}", Json);

        Assert.All(descriptors!, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Description));

            // Le schéma part tel quel à l'API du modèle : s'il n'est pas du JSON, l'appel
            // échoue au premier tour, en production.
            var schema = JsonDocument.Parse(d.Parameters);
            Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        });
    }

    // ── Les droits ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Un_endpoint_du_harness_refuse_un_appel_sans_jeton()
    {
        var response = await target.Anonymous().GetAsync($"outils?kind={(int)SessionKind.Lecture}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Un_eleve_ne_peut_pas_ouvrir_de_session_sur_un_autre_eleve()
    {
        var response = await target.AsStudent(Eleve).PostAsJsonAsync(
            "sessions", new OpenSessionRequest(Cours, SessionKind.Lecture, AutreEleve), Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Ce parent est bien parent — d'un autre enfant. Un rôle valide n'est pas un droit d'accès.</summary>
    [Fact]
    public Task Un_parent_ne_peut_pas_consulter_l_enfant_d_un_autre() =>
        Checks.ParentCannotViewOtherChild(target);

    [Fact]
    public async Task Un_parent_consulte_l_enfant_qui_lui_est_rattache()
    {
        var response = await target.AsParent(Parent, Eleve).GetAsync($"suivi/{Eleve}/resume");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Un_parent_ne_peut_pas_ouvrir_de_session_d_etude()
    {
        // Un parent consulte, il n'étudie pas : lui laisser ouvrir une session de lecture
        // au nom de son enfant lui donnerait le répétiteur de l'enfant.
        var response = await target.AsParent(Parent, Eleve).PostAsJsonAsync(
            "sessions", new OpenSessionRequest(Cours, SessionKind.Lecture, Eleve), Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Le test de fuite ──────────────────────────────────────────────────────

    /// <summary>
    /// La garantie centrale de la phase 2b : le parent voit où en est son enfant, jamais
    /// ce qu'il a écrit au répétiteur. Si l'enfant sait que ses mots remontent, il cesse
    /// d'écrire « je n'ai rien compris », et l'outil perd ce qui le rend utile.
    ///
    /// Vérifié sur le JSON, pas sur l'interface : ce qui n'existe pas dans l'API ne peut
    /// pas fuir dans un écran, mais l'inverse n'est pas vrai.
    /// </summary>
    [Fact]
    public Task Le_suivi_ne_renvoie_aucun_contenu_d_echange() =>
        Checks.NoMessageContentInFollowUp(target);

    // ── La forme du flux ──────────────────────────────────────────────────────

    [Fact]
    public Task Un_tour_se_termine_toujours_par_un_evenement_terminal() =>
        Checks.TurnEndsWithTerminal(target);

    [Fact]
    public async Task Le_flux_se_clot_par_le_marqueur_DONE()
    {
        var client = target.AsStudent(Eleve);
        var session = await TurnReader.OpenAsync(client, Cours, SessionKind.Lecture);

        var transcript = await TurnReader.PlayAsync(client, session.Id, "Une question.");

        Assert.True(transcript.SawDoneMarker, "Le flux doit se clore par le marqueur [DONE].");
    }

    [Fact]
    public async Task Aucun_evenement_ne_suit_l_evenement_terminal()
    {
        var client = target.AsStudent(Eleve);
        var session = await TurnReader.OpenAsync(client, Cours, SessionKind.Lecture);

        var transcript = await TurnReader.PlayAsync(client, session.Id, "Une question.");

        var terminalAt = transcript.Events.ToList().FindIndex(e => e.IsTerminal);
        Assert.True(terminalAt >= 0);
        Assert.Equal(transcript.Events.Count - 1, terminalAt);
    }

    [Fact]
    public async Task Tous_les_evenements_ont_un_type_connu()
    {
        var client = target.AsStudent(Eleve);
        var session = await TurnReader.OpenAsync(client, Cours, SessionKind.Lecture);

        var transcript = await TurnReader.PlayAsync(client, session.Id, "Une question.");

        Assert.All(transcript.Events, e => Assert.Contains(e.Type, HarnessEventType.All));
    }

    /// <summary>
    /// Un outil ne peut être appelé que s'il est exposé. C'est la différence entre une
    /// politique appliquée et une politique affichée.
    /// </summary>
    [Fact]
    public async Task Aucun_outil_hors_table_n_est_appele()
    {
        var client = target.AsStudent(Eleve);

        foreach (var kind in (SessionKind[])[SessionKind.Lecture, SessionKind.Exercice, SessionKind.Code])
        {
            var session = await TurnReader.OpenAsync(client, Cours, kind);
            var transcript = await TurnReader.PlayAsync(client, session.Id, "Aide-moi.");

            Assert.All(transcript.ToolsCalled, tool =>
                Assert.True(ToolPolicy.Allows(kind, tool),
                    $"L'outil « {tool} » a été appelé dans une session {kind}, où la table ne l'autorise pas."));
        }
    }

    [Fact]
    public Task Un_echec_d_outil_produit_une_erreur_et_non_un_flux_suspendu() =>
        Checks.ToolFailureSurfacesAsError(target);

    // ── Les sessions ──────────────────────────────────────────────────────────

    [Fact]
    public Task Une_session_reprise_rend_son_historique() =>
        Checks.ResumedSessionHasHistory(target);

    [Fact]
    public async Task Une_session_porte_les_outils_de_sa_nature()
    {
        var client = target.AsStudent(Eleve);
        var session = await TurnReader.OpenAsync(client, Cours, SessionKind.Code);

        Assert.Equal(ToolPolicy.For(SessionKind.Code).OrderBy(t => t), session.Tools.OrderBy(t => t));
    }

    [Fact]
    public async Task La_compaction_date_la_session_sans_la_vider()
    {
        var client = target.AsStudent(Eleve);
        var session = await TurnReader.OpenAsync(client, Cours, SessionKind.Lecture);

        await TurnReader.PlayAsync(client, session.Id, "Une question.");
        await TurnReader.PlayAsync(client, session.Id, "Une autre.");

        var response = await client.PostAsync($"sessions/{session.Id}/compacter", content: null);
        response.EnsureSuccessStatusCode();

        var compacted = await response.Content.ReadFromJsonAsync<SessionDto>(Json);
        Assert.NotNull(compacted!.CompactedAt);

        var history = await client.GetFromJsonAsync<SessionHistoryDto>($"sessions/{session.Id}", Json);
        Assert.NotEmpty(history!.Turns);
    }

    [Fact]
    public async Task Une_session_inconnue_renvoie_404_et_non_une_erreur_interne()
    {
        var response = await target.AsStudent(Eleve).GetAsync($"sessions/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Le rendu du cours et la synchronisation ───────────────────────────────

    [Fact]
    public async Task Le_cours_rendu_porte_son_sommaire_et_des_ancres_qui_existent()
    {
        var rendered = await target.AsStudent(Eleve)
            .GetFromJsonAsync<RenderedCourseDto>($"cours/{Cours}/rendu", Json);

        Assert.NotNull(rendered);
        Assert.False(string.IsNullOrWhiteSpace(rendered!.Html));

        // Une entrée de sommaire dont l'ancre n'est pas dans le HTML donne un lien mort :
        // l'élève clique et rien ne bouge.
        Assert.All(rendered.Outline, entry =>
            Assert.Contains($"id=\"{entry.Anchor}\"", rendered.Html));
    }

    [Fact]
    public async Task La_synchronisation_accepte_un_lot_et_dit_ce_qu_elle_a_retenu()
    {
        var batch = new SyncBatch(
            [new ActivitySignal(Cours, StudyActivityKind.Lecture, DateTime.UtcNow.AddHours(-8))],
            [new OfflineStepResult(Cours, Guid.NewGuid(), 8, 10, DateTime.UtcNow.AddHours(-8), SelfAssessed: true)]);

        var response = await target.AsStudent(Eleve).PostAsJsonAsync("sync", batch, Json);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SyncResult>(Json);

        Assert.Equal(1, result!.ActivitiesAccepted);
        Assert.Equal(1, result.ResultsAccepted);
    }
}
