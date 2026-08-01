using GPOE26.Harness.Contract;
using GPOE26.Harness.Stub;

namespace GPOE26.Harness.Conformance;

/// <summary>
/// Les tests du juge : ils ne vérifient pas le harness, ils vérifient <b>la suite</b>.
///
/// Une suite verte a deux explications possibles — le service est correct, ou la suite ne
/// vérifie rien. Rien dans un rapport de tests ne permet de les distinguer. Le seul moyen
/// est de casser le service exprès et d'exiger l'échec.
///
/// Chaque test ici sabote le stub d'une façon précise, rejoue <b>la vérification de
/// l'étage conformité elle-même</b> — pas une variante écrite pour l'occasion — et exige
/// qu'elle tombe. Si l'un de ces tests devient vert, c'est le garde-fou correspondant qui
/// a cessé de garder quoi que ce soit.
///
/// Ils ne tournent que contre le stub en mémoire : on ne saborde pas un service distant.
/// </summary>
[Collection(HarnessCollection.Name)]
public class JudgeTests(HarnessTarget target)
{
    /// <summary>
    /// Applique un sabotage le temps d'une vérification, et exige qu'elle échoue.
    ///
    /// La variable d'environnement est globale au processus, d'où la collection sans
    /// parallélisme : deux sabotages simultanés produiraient des échecs aléatoires,
    /// c'est-à-dire le genre de test qu'on finit par désactiver.
    /// </summary>
    private static async Task MustFail(Sabotage sabotage, Func<Task> check)
    {
        if (Environment.GetEnvironmentVariable(HarnessTarget.TargetVariable) is { Length: > 0 })
            return; // cible distante : le sabotage n'est pas à notre main

        var previous = Environment.GetEnvironmentVariable(SabotageSettings.EnvironmentVariable);
        Environment.SetEnvironmentVariable(SabotageSettings.EnvironmentVariable, sabotage.ToString());

        try
        {
            var failed = false;
            try
            {
                await check();
            }
            catch (Exception)
            {
                failed = true;
            }

            Assert.True(failed,
                $"Le sabotage « {sabotage} » n'a pas fait tomber la vérification. " +
                "Le garde-fou correspondant ne garde rien.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(SabotageSettings.EnvironmentVariable, previous);
        }
    }

    [Fact]
    public Task Un_outil_expose_en_trop_fait_tomber_la_suite() =>
        MustFail(Sabotage.ExtraTool, () => Checks.ToolsMatchPolicy(target, SessionKind.Lecture));

    /// <summary>
    /// Le sabotage le plus important de tous : il vise la promesse centrale de la
    /// phase 2b. Si ce test passe au vert, le parent peut lire les échanges de son enfant
    /// et personne ne s'en apercevra.
    /// </summary>
    [Fact]
    public Task Une_fuite_de_message_dans_le_suivi_fait_tomber_la_suite() =>
        MustFail(Sabotage.LeakMessage, () => Checks.NoMessageContentInFollowUp(target));

    [Fact]
    public Task Un_flux_sans_evenement_terminal_fait_tomber_la_suite() =>
        MustFail(Sabotage.NoTerminalEvent, () => Checks.TurnEndsWithTerminal(target));

    [Fact]
    public Task Une_autorisation_ignoree_fait_tomber_la_suite() =>
        MustFail(Sabotage.SkipAuthorization, () => Checks.ParentCannotViewOtherChild(target));

    [Fact]
    public Task Un_historique_perdu_fait_tomber_la_suite() =>
        MustFail(Sabotage.ForgetHistory, () => Checks.ResumedSessionHasHistory(target));

    [Fact]
    public Task Un_echec_d_outil_avale_fait_tomber_la_suite() =>
        MustFail(Sabotage.SwallowToolFailure, () => Checks.ToolFailureSurfacesAsError(target));

    /// <summary>
    /// Le contrôle du contrôle : sans sabotage, les mêmes vérifications passent. Sinon
    /// les tests ci-dessus prouveraient seulement qu'elles échouent toujours.
    /// </summary>
    [Fact]
    public async Task Sans_sabotage_les_memes_verifications_passent()
    {
        Assert.Equal(Sabotage.None, SabotageSettings.Current);

        await Checks.ToolsMatchPolicy(target, SessionKind.Lecture);
        await Checks.NoMessageContentInFollowUp(target);
        await Checks.TurnEndsWithTerminal(target);
        await Checks.ParentCannotViewOtherChild(target);
        await Checks.ResumedSessionHasHistory(target);
        await Checks.ToolFailureSurfacesAsError(target);
    }

    /// <summary>Un nom de sabotage mal orthographié doit être refusé, pas ignoré.</summary>
    [Fact]
    public void Un_sabotage_inconnu_est_refuse_bruyamment()
    {
        var previous = Environment.GetEnvironmentVariable(SabotageSettings.EnvironmentVariable);
        Environment.SetEnvironmentVariable(SabotageSettings.EnvironmentVariable, "nimporte-quoi");

        try
        {
            Assert.Throws<InvalidOperationException>(() => SabotageSettings.Current);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SabotageSettings.EnvironmentVariable, previous);
        }
    }
}
