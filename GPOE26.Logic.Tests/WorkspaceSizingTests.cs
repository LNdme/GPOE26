using GPOE26.UI.Components.Workspace;

namespace GPOE26.Logic.Tests;

/// <summary>Règles de taille des panneaux de l'espace d'étude : gabarit de grille, borne au glissement, minimum.</summary>
public class WorkspaceSizingTests
{
    private static readonly string[] Two = ["lecture", "tuteur"];

    [Fact]
    public void Les_panneaux_demarrent_ouverts_a_parts_egales()
    {
        var state = WorkspaceSizing.CreateDefault(Two);
        Assert.True(state.Panels.Values.All(p => p.Open && !p.Collapsed));
        Assert.Equal("1fr 6px 1fr", WorkspaceSizing.BuildGridTemplate(state, Two));
    }

    [Fact]
    public void Le_panneau_reduit_devient_un_rail_fixe_et_la_poignee_reste_entre_les_deux()
    {
        var state = WorkspaceSizing.CreateDefault(Two);
        state.Panels["tuteur"] = state.Panels["tuteur"] with { Collapsed = true };
        Assert.Equal("1fr 52px", WorkspaceSizing.BuildGridTemplate(state, Two));
    }

    [Fact]
    public void Le_rail_en_tete_ne_garde_pas_de_poignee_fantome()
    {
        var state = WorkspaceSizing.CreateDefault(Two);
        state.Panels["lecture"] = state.Panels["lecture"] with { Collapsed = true };
        Assert.Equal("52px 1fr", WorkspaceSizing.BuildGridTemplate(state, Two));
    }

    [Fact]
    public void Un_panneau_agrandi_prend_le_plein_pouvoir()
    {
        var state = WorkspaceSizing.CreateDefault(Two);
        state.MaximizedId = "tuteur";
        Assert.Equal("1fr", WorkspaceSizing.BuildGridTemplate(state, Two));
    }

    [Fact]
    public void Un_panneau_ferme_disparait_de_la_grille_et_de_l_agrandissement()
    {
        var state = WorkspaceSizing.CreateDefault(Two);
        state.Panels["lecture"] = state.Panels["lecture"] with { Open = false };
        state.MaximizedId = "lecture";
        state = WorkspaceSizing.Reconcile(state, Two);
        Assert.Null(state.MaximizedId);
        Assert.Equal("1fr", WorkspaceSizing.BuildGridTemplate(state, Two));
    }

    [Fact]
    public void La_disposition_enregistree_est_alignee_sur_les_panneaux_de_la_page()
    {
        var saved = new WorkspaceState
        {
            MaximizedId = "ancien",
            Panels = new()
            {
                ["ancien"] = new WsPanelState(true, false, 2),
                ["lecture"] = new WsPanelState(true, false, 2),
            },
        };
        var state = WorkspaceSizing.Reconcile(saved, Two);
        // « ancien » est conservé en fantôme (jamais rendu) : la réconciliation est incrémentale
        Assert.Contains("ancien", state.Panels.Keys);
        Assert.Contains("lecture", state.Panels.Keys);
        Assert.Contains("tuteur", state.Panels.Keys);
        Assert.Equal(2.0, state.Panels["lecture"].Part);
        Assert.Equal(1.0, state.Panels["tuteur"].Part);   // ajouté à la part égale
        Assert.Equal("ancien", state.MaximizedId);          // fantôme conservé, sans effet à l'affichage

        var nul = WorkspaceSizing.Reconcile(null, Two);
        Assert.Equal(2, nul.Panels.Count);
    }

    [Fact]
    public void Tout_replie_le_premier_panneau_sert_de_secours_sans_reecrire_l_etat()
    {
        var toutReplie = new WorkspaceState
        {
            Panels = new()
            {
                ["lecture"] = new WsPanelState(true, true, 1),
                ["tuteur"] = new WsPanelState(true, true, 1),
            },
        };
        // L'état persisté reste tel quel : la grille affiche le premier ouvert comme déplié
        var state = WorkspaceSizing.Reconcile(toutReplie, Two);
        Assert.True(state.Panels.Values.All(p => p.Collapsed));
        Assert.Equal("1fr 52px", WorkspaceSizing.BuildGridTemplate(state, Two));
    }

    [Fact]
    public void Les_pixels_deviennent_des_parts_fr_sans_panneau_etrangle()
    {
        // 900 px et 100 px : le petit panneau remonte au minimum, l'autre cède
        var parts = WorkspaceSizing.PartsFromPixels([900, 100]);
        Assert.True(parts[1] >= WorkspaceSizing.MinPart, $"part obtenue : {parts[1]}");
        Assert.Equal(2.0, parts.Sum(), 6);

        var egaux = WorkspaceSizing.PartsFromPixels([500, 500]);
        Assert.Equal([1.0, 1.0], egaux);

        Assert.Empty(WorkspaceSizing.PartsFromPixels([]));
        Assert.Equal([1.0, 1.0], WorkspaceSizing.PartsFromPixels([0, 0]));
    }

    [Fact]
    public void Le_clavier_deplace_la_frontiere_sans_ecraser_un_panneau()
    {
        var (left, right) = WorkspaceSizing.ResizePair(1, 1, 5);
        Assert.True(left >= WorkspaceSizing.MinPart);
        Assert.Equal(2.0, left + right, 6);

        var (l2, r2) = WorkspaceSizing.ResizePair(1, 1, -5);
        Assert.Equal(2.0, l2 + r2, 6);
        Assert.True(r2 >= WorkspaceSizing.MinPart);
    }

    [Fact]
    public void Le_double_clic_egalise_la_paire_et_le_pourcentage_la_mesure()
    {
        Assert.Equal((1.5, 1.5), WorkspaceSizing.EqualPair(3));
        Assert.Equal(50, WorkspaceSizing.PairPercent(1, 1));
        Assert.Equal(75, WorkspaceSizing.PairPercent(3, 1));
    }

    [Fact]
    public void Le_sommaire_demarre_en_rail_a_la_premiere_visite_puis_garde_le_choix_de_l_eleve()
    {
        var ids = new[] { "sommaire", "lecture", "tuteur" };
        var rail = new Dictionary<string, bool> { ["sommaire"] = true };

        var frais = WorkspaceSizing.Reconcile(null, ids, rail);
        Assert.True(frais.Panels["sommaire"].Collapsed);
        Assert.Equal("52px 1fr 6px 1fr", WorkspaceSizing.BuildGridTemplate(frais, ids));

        // L'élève déplie le sommaire : son choix prime sur le rail d'origine
        var choisi = new WorkspaceState
        {
            MaximizedId = frais.MaximizedId,
            Panels = new(frais.Panels) { ["sommaire"] = frais.Panels["sommaire"] with { Collapsed = false } },
        };
        var restaure = WorkspaceSizing.Reconcile(choisi, ids, rail);
        Assert.False(restaure.Panels["sommaire"].Collapsed);

        // Panneau inconnu ajouté plus tard (ex. future épreuve) : rail d'office si demandé
        var ajoute = WorkspaceSizing.Reconcile(frais, ids.Concat(["preuve"]).ToList(), new Dictionary<string, bool> { ["sommaire"] = true, ["preuve"] = true });
        Assert.True(ajoute.Panels["preuve"].Collapsed);
    }

    [Fact]
    public void La_disposition_sauvegardee_survit_avant_que_les_panneaux_ne_soient_connus()
    {
        var saved = new WorkspaceState
        {
            MaximizedId = null,
            Panels = new() { ["tuteur"] = new WsPanelState(false, false, 1) },
        };
        var state = WorkspaceSizing.Reconcile(saved, []);
        Assert.False(state.Panels["tuteur"].Open);
        Assert.Single(state.Panels);
    }

    private sealed class MemoryWorkspaceStore : IWorkspaceStore
    {
        public WorkspaceState? Saved;
        public Task<WorkspaceState?> LoadAsync() => Task.FromResult(Saved);
        public Task SaveAsync(WorkspaceState state) { Saved = state; return Task.CompletedTask; }
        public Task ClearAsync() { Saved = null; return Task.CompletedTask; }
    }

    [Fact]
    public async Task La_premiere_visite_demarre_le_sommaire_en_rail_une_fois_tout_enregistre()
    {
        var ctx = new WorkspaceContext(new MemoryWorkspaceStore());
        await ctx.LoadAsync();
        ctx.Register("sommaire", "Sommaire", "menu", startCollapsed: true);
        ctx.Register("lecture", "Lecture", "book");
        ctx.Register("tuteur", "Tuteur IA", "spark");

        Assert.True(ctx.State.Panels["sommaire"].Collapsed);
        Assert.Equal(["lecture", "tuteur"], ctx.ExpandedIds);
        Assert.Equal("52px 1fr 6px 1fr", WorkspaceSizing.BuildGridTemplate(ctx.State, ctx.AllIds));
    }

    [Fact]
    public void Le_json_de_la_disposition_se_relit_comme_enregistre()
    {
        const string json = """{"MaximizedId":null,"Panels":{"sommaire":{"Open":true,"Collapsed":false,"Part":1},"lecture":{"Open":true,"Collapsed":false,"Part":1},"tuteur":{"Open":false,"Collapsed":false,"Part":1}}}""";
        var state = System.Text.Json.JsonSerializer.Deserialize<WorkspaceState>(json);
        Assert.NotNull(state);
        Assert.False(state!.Panels["tuteur"].Open);
        Assert.Equal(1, state.Panels["lecture"].Part);
    }

    [Fact]
    public void La_grille_garde_le_point_decimal_meme_en_francais()
    {
        // Avec une virgule (« 1,42fr ») le navigateur rejette toute la règle : le panneau redimensionné
        // reprenait sa taille initiale au relâchement de la poignée.
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
        try
        {
            var state = new WorkspaceState
            {
                Panels = new()
                {
                    ["assistant"] = new WsPanelState(Part: 1),
                    ["redaction"] = new WsPanelState(Part: 1.4222),
                    ["apercu"] = new WsPanelState(Part: 0.5778),
                },
            };
            Assert.Equal("1fr 6px 1.4222fr 6px 0.5778fr",
                WorkspaceSizing.BuildGridTemplate(state, ["assistant", "redaction", "apercu"]));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }
}
