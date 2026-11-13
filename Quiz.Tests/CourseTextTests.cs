using Quiz.Service;

namespace Quiz.Tests;

/// <summary>Texte d'un cours confié au modèle pour générer des exercices : lu au service Cours, nettoyé et borné.</summary>
public class CourseTextTests
{
    [Fact]
    public void Les_sections_sont_assemblees_dans_l_ordre() =>
        Assert.Equal("## A\n\n## B", CourseTextClient.Build(["## A", null, "  ", "## B"], "texte du PDF", "description"));

    [Fact]
    public void Sans_section_le_texte_du_pdf_sert() =>
        Assert.Equal("texte du PDF", CourseTextClient.Build([], "texte du PDF", "description"));

    [Fact]
    public void A_defaut_la_description_sert() =>
        Assert.Equal("description", CourseTextClient.Build(null, null, "description"));

    [Fact]
    public void Un_cours_vide_donne_un_texte_vide() =>
        Assert.Equal("", CourseTextClient.Build(null, null, null));

    [Fact]
    public void Le_bloc_technique_d_un_laboratoire_n_est_pas_envoye()
    {
        var text = CourseTextClient.Build(["Avant\n```simulation\n{\"type\":\"pendule\",\"params\":[1,2,3]}\n```\nAprès"], null, null);
        Assert.DoesNotContain("pendule", text);
        Assert.Contains("[Laboratoire interactif]", text);
        Assert.StartsWith("Avant", text);
        Assert.EndsWith("Après", text);
    }

    [Fact]
    public void Un_texte_demesure_est_borne()
    {
        var text = CourseTextClient.Limit(new string('x', CourseTextClient.MaxLength + 10_000));
        Assert.True(text.Length <= CourseTextClient.MaxLength + 5);
        Assert.EndsWith("[…]", text);
    }
}
