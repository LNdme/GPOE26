using GPOE26.ServiceDefaults.Llm;

namespace GPOE26.Logic.Tests;

/// <summary>Sélecteur de modèle de l'application Windows : le choix de l'élève passe en tête, sans sortir de la liste du serveur.</summary>
public class ModelChoiceTests
{
    private static readonly string[] Models = ["a/premier:free", "b/second:free", "openrouter/free"];

    [Fact]
    public void Sans_choix_l_ordre_du_serveur_est_garde()
    {
        Assert.Equal(Models, LlmClient.PreferFirst(Models, null));
        Assert.Equal(Models, LlmClient.PreferFirst(Models, "  "));
    }

    [Fact]
    public void Le_modele_choisi_passe_en_tete_et_les_autres_restent_en_repli()
    {
        Assert.Equal(["b/second:free", "a/premier:free", "openrouter/free"], LlmClient.PreferFirst(Models, "B/Second:free"));
    }

    [Fact]
    public void Un_modele_hors_liste_est_ignore()
    {
        Assert.Equal(Models, LlmClient.PreferFirst(Models, "anthropic/modele-payant"));
    }
}
