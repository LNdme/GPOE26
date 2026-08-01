using System.Text.Json;
using GPOE26.Harness.Contract;
using GPOE26.Harness.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace GPOE26.Tests;

/// <summary>
/// Ce que le harness expose réellement au modèle.
///
/// La suite de conformité pose la même question à <c>GET /outils</c>, mais elle exige un
/// service démarré, une base et une clé d'API. Ici on interroge directement la fabrique
/// de fonctions : aucune dépendance n'est sollicitée par <c>FunctionsFor</c>, et c'est
/// précisément l'endroit où une erreur serait le plus coûteuse.
///
/// Car l'écart possible est silencieux : si <c>AIFunctionFactory.Create</c> nommait les
/// fonctions d'après la méthode C# plutôt que d'après le nom demandé, le modèle recevrait
/// <c>ChercherDansLeCours</c> là où la table dit <c>chercher_dans_le_cours</c>. Rien
/// n'échouerait — l'autorisation comparerait simplement des noms qui ne se rencontrent
/// jamais.
/// </summary>
public class HarnessToolsTests
{
    /// <summary>
    /// Les dépendances ne sont pas sollicitées par la fabrique : la construire ainsi
    /// évite de monter un conteneur pour vérifier une liste de noms.
    /// </summary>
    private static StudyTools Tools() =>
        new(cours: null!, openRouter: null!, speech: null!, logger: NullLogger<StudyTools>.Instance);

    [Theory]
    [InlineData(SessionKind.Lecture)]
    [InlineData(SessionKind.Exercice)]
    [InlineData(SessionKind.Code)]
    [InlineData(SessionKind.Suivi)]
    public void Les_fonctions_exposees_portent_exactement_les_noms_de_la_table(SessionKind kind)
    {
        var exposed = Tools().FunctionsFor(kind)
            .OfType<AIFunction>()
            .Select(f => f.Name)
            .OrderBy(n => n)
            .ToArray();

        var expected = ToolPolicy.For(kind).OrderBy(n => n).ToArray();

        Assert.Equal(expected, exposed);
    }

    [Fact]
    public void Une_session_de_suivi_ne_recoit_aucune_fonction()
    {
        // Un parent consulte : le modèle ne reçoit rien à appeler, donc rien n'est
        // appelable, quoi qu'il décide.
        Assert.Empty(Tools().FunctionsFor(SessionKind.Suivi));
    }

    [Fact]
    public void Chaque_fonction_decrit_ce_qu_elle_fait()
    {
        // La description part telle quelle au modèle : c'est elle qui détermine s'il
        // appelle le bon outil. Une description vide est un outil que personne n'appellera.
        Assert.All(Tools().FunctionsFor(SessionKind.Code).OfType<AIFunction>(), f =>
            Assert.False(string.IsNullOrWhiteSpace(f.Description),
                $"L'outil « {f.Name} » n'a pas de description."));
    }

    [Fact]
    public void Chaque_fonction_publie_un_schema_de_parametres_exploitable()
    {
        Assert.All(Tools().FunctionsFor(SessionKind.Code).OfType<AIFunction>(), f =>
        {
            // Le schéma part à l'API du modèle : s'il n'est pas du JSON d'objet, l'appel
            // échoue au premier tour, en production.
            var schema = JsonDocument.Parse(f.JsonSchema.ToString());
            Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        });
    }

    [Fact]
    public void Aucune_fonction_n_est_exposee_deux_fois()
    {
        foreach (var kind in Enum.GetValues<SessionKind>())
        {
            var names = Tools().FunctionsFor(kind).OfType<AIFunction>().Select(f => f.Name).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
        }
    }

    /// <summary>
    /// La boucle doit avoir une fin. Un modèle qui enchaîne les outils sans conclure
    /// coûte à chaque tour, et l'élève voit défiler des étapes sans jamais de réponse.
    /// </summary>
    [Fact]
    public void La_boucle_a_un_plafond_d_iterations()
    {
        Assert.InRange(GPOE26.Harness.TurnRunner.MaxIterations, 1, 10);
    }
}
