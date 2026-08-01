using GPOE26.Harness.Contract;

namespace GPOE26.Tests;

/// <summary>
/// La table d'exposition des outils.
///
/// Elle se teste ici, sans réseau ni modèle, parce qu'une erreur y est invisible à
/// l'usage : un outil exposé de trop ne provoque aucun message d'erreur — il attend
/// simplement qu'un modèle décide de l'appeler. C'est la même raison qui fait tester
/// <c>StudyIdentity</c> dans <c>StudyRulesTests.cs</c>.
/// </summary>
public class ToolPolicyTests
{
    [Fact]
    public void Une_lecture_ne_donne_pas_acces_aux_fichiers()
    {
        var tools = ToolPolicy.For(SessionKind.Lecture);

        Assert.Contains(ToolNames.ChercherDansLeCours, tools);
        Assert.DoesNotContain(ToolNames.LireFichier, tools);
        Assert.DoesNotContain(ToolNames.EcrireFichier, tools);
        Assert.DoesNotContain(ToolNames.ExecuterTests, tools);
    }

    [Fact]
    public void Un_exercice_redige_ne_donne_pas_acces_aux_fichiers()
    {
        var tools = ToolPolicy.For(SessionKind.Exercice);

        Assert.Contains(ToolNames.GenererExercice, tools);
        Assert.Contains(ToolNames.CorrigerReponse, tools);
        Assert.DoesNotContain(ToolNames.ExecuterTests, tools);
    }

    /// <summary>
    /// Seule la session de code touche à l'espace de travail. C'est la frontière qui
    /// justifie d'avoir une nature de session distincte plutôt qu'un seul mode.
    /// </summary>
    [Fact]
    public void Seule_une_session_de_code_touche_aux_fichiers()
    {
        foreach (var kind in Enum.GetValues<SessionKind>())
        {
            var touchesFiles = ToolPolicy.Allows(kind, ToolNames.ExecuterTests);
            Assert.Equal(kind == SessionKind.Code, touchesFiles);
        }
    }

    /// <summary>
    /// La garantie centrale de la phase 2b, portée jusque dans les outils : un parent
    /// consulte, il n'agit pas. Aucun outil exposé, donc aucun outil appelable — quoi
    /// que le modèle décide.
    /// </summary>
    [Fact]
    public void Une_session_de_suivi_n_expose_aucun_outil()
    {
        Assert.Empty(ToolPolicy.For(SessionKind.Suivi));

        foreach (var tool in AllToolNames())
            Assert.False(ToolPolicy.Allows(SessionKind.Suivi, tool));
    }

    [Fact]
    public void Les_natures_s_emboitent_de_la_plus_pauvre_a_la_plus_riche()
    {
        var lecture = ToolPolicy.For(SessionKind.Lecture);
        var exercice = ToolPolicy.For(SessionKind.Exercice);
        var code = ToolPolicy.For(SessionKind.Code);

        Assert.All(lecture, t => Assert.Contains(t, exercice));
        Assert.All(exercice, t => Assert.Contains(t, code));

        Assert.True(exercice.Count > lecture.Count);
        Assert.True(code.Count > exercice.Count);
    }

    [Fact]
    public void Aucune_nature_n_expose_un_outil_inconnu()
    {
        var known = AllToolNames().ToHashSet();

        foreach (var kind in Enum.GetValues<SessionKind>())
            Assert.All(ToolPolicy.For(kind), t => Assert.Contains(t, known));
    }

    [Fact]
    public void Aucune_nature_n_expose_deux_fois_le_meme_outil()
    {
        foreach (var kind in Enum.GetValues<SessionKind>())
        {
            var tools = ToolPolicy.For(kind);
            Assert.Equal(tools.Count, tools.Distinct().Count());
        }
    }

    // ── Qui peut ouvrir quoi ─────────────────────────────────────────────────

    [Theory]
    [InlineData("Parent")]
    [InlineData("Teacher")]
    [InlineData("parent")]
    public void Un_parent_ou_un_enseignant_n_ouvre_qu_une_session_de_suivi(string role)
    {
        Assert.True(ToolPolicy.CanOpen(role, SessionKind.Suivi));

        Assert.False(ToolPolicy.CanOpen(role, SessionKind.Lecture));
        Assert.False(ToolPolicy.CanOpen(role, SessionKind.Exercice));
        Assert.False(ToolPolicy.CanOpen(role, SessionKind.Code));
    }

    /// <summary>
    /// L'inverse compte autant : un élève n'ouvre pas de session de suivi. Sinon il
    /// disposerait, sur lui-même, d'une surface pensée pour un tiers.
    /// </summary>
    [Fact]
    public void Un_eleve_ouvre_tout_sauf_le_suivi()
    {
        Assert.True(ToolPolicy.CanOpen("Student", SessionKind.Lecture));
        Assert.True(ToolPolicy.CanOpen("Student", SessionKind.Exercice));
        Assert.True(ToolPolicy.CanOpen("Student", SessionKind.Code));

        Assert.False(ToolPolicy.CanOpen("Student", SessionKind.Suivi));
    }

    [Fact]
    public void Un_role_absent_est_traite_comme_un_eleve_pas_comme_un_parent()
    {
        // Un jeton sans claim de rôle ne doit pas obtenir la surface de suivi par défaut :
        // le suivi se mérite par un rôle explicite.
        Assert.False(ToolPolicy.CanOpen(null, SessionKind.Suivi));
        Assert.True(ToolPolicy.CanOpen(null, SessionKind.Lecture));
    }

    private static IEnumerable<string> AllToolNames() =>
        typeof(ToolNames)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);
}

/// <summary>
/// Le schéma d'évènements. Deux choses s'y vérifient sans démarrer quoi que ce soit :
/// qu'un tour se termine toujours, et que le contrat reste compatible avec le client Web.
/// </summary>
public class HarnessEventTests
{
    [Fact]
    public void Un_tour_ne_se_termine_que_sur_done_ou_error()
    {
        Assert.True(HarnessEvent.OfDone("réponse").IsTerminal);
        Assert.True(HarnessEvent.OfError("échec").IsTerminal);

        Assert.False(HarnessEvent.OfStep("recherche…").IsTerminal);
        Assert.False(HarnessEvent.OfToken("mot").IsTerminal);
        Assert.False(HarnessEvent.OfToolCall("outil", "{}", 1).IsTerminal);
        Assert.False(HarnessEvent.OfToolResult("outil", "ok", 1).IsTerminal);
    }

    /// <summary>
    /// Le client Web filtre sur `evt.Type` par un switch sans branche par défaut : un
    /// évènement d'un type qu'il ignore le laisse indifférent. C'est ce qui permet
    /// d'ajouter `tool_call` et `tool_result` sans toucher au Web. Ce test fige les sept
    /// premiers champs, dont dépend cette compatibilité.
    /// </summary>
    [Fact]
    public void Les_sept_premiers_champs_restent_ceux_que_le_Web_deserialise()
    {
        var expected = new[] { "Type", "Step", "Token", "Reply", "Citations", "Intent", "Error" };

        var actual = typeof(HarnessEvent)
            .GetProperties()
            .Select(p => p.Name)
            .Take(expected.Length)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Tous_les_types_d_evenement_sont_declares()
    {
        Assert.Equal(6, HarnessEventType.All.Count);
        Assert.Contains(HarnessEventType.ToolCall, HarnessEventType.All);
        Assert.Contains(HarnessEventType.ToolResult, HarnessEventType.All);
    }
}
