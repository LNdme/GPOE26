using Chat.Service;
using GPOE26.ServiceDefaults.Llm;

namespace GPOE26.Logic.Tests;

/// <summary>H12 : le tuteur reçoit les bons passages d'un long cours ; H11 : réglages et modèle enregistré.</summary>
public class CoursePassageTests
{
    private static string Paragraphs(string word, int count) =>
        string.Join("\n\n", Enumerable.Range(0, count).Select(i => $"{word} {i} " + new string('a', 300)));

    [Fact]
    public void Une_longue_partie_est_coupee_entre_deux_paragraphes()
    {
        var passages = AgentPrompts.Passages($"## Cinématique\n{Paragraphs("Vitesse", 12)}\n\n## Énergie\nCourt.");

        Assert.Equal(["Cinématique", "Cinématique (suite)", "Cinématique (suite)", "Énergie"], passages.Select(p => p.Title));
        Assert.All(passages, p => Assert.True(p.Content.Length <= AgentPrompts.PassageChars));
        Assert.Equal(Enumerable.Range(0, passages.Count), passages.Select(p => p.Position));
        // Aucun paragraphe n'est coupé en deux
        Assert.All(passages.Skip(1).Take(2), p => Assert.StartsWith("Vitesse", p.Content));
    }

    [Fact]
    public void Un_paragraphe_demesure_est_coupe_net()
    {
        var passages = AgentPrompts.Passages(new string('x', 4000));
        Assert.Equal(3, passages.Count);
        Assert.Equal(4000, passages.Sum(p => p.Content.Length));
    }

    [Fact]
    public void Le_sens_trouve_le_bon_passage_meme_sans_mot_commun()
    {
        // La question ne partage aucun mot avec le passage « Chute libre », mais son vecteur en est proche
        var passages = new List<AgentPrompts.CoursePassage>
        {
            new(0, "Cinématique", "Mouvement rectiligne uniforme.", [1, 0, 0]),
            new(1, "Chute libre", "Un corps lâché sans vitesse initiale.", [0, 1, 0]),
            new(2, "Énergie", "Énergie cinétique et potentielle.", [0, 0, 1]),
        };
        var ranks = AgentPrompts.Rank(passages, "Combien de temps met une pomme pour tomber ?", [0.1f, 0.95f, 0.05f]);
        Assert.Equal(1, ranks.MaxBy(r => r.Value).Key);
    }

    [Fact]
    public void Un_seul_mot_commun_ne_l_emporte_pas_sur_le_sens()
    {
        // « temps » est dans le passage de cinématique, mais la question parle d'une chute (vérifié sur la pile de test)
        var passages = new List<AgentPrompts.CoursePassage>
        {
            new(0, "Cinématique", "Le temps mis par un mobile dépend de sa vitesse.", [0, 3, 0]),
            new(1, "Chute libre", "Un corps lâché tombe sous l'effet de la pesanteur.", [3, 0, 0]),
        };
        var ranks = AgentPrompts.Rank(passages, "Combien de temps met une pomme pour tomber ?", [2, 1, 0]);
        Assert.True(ranks[1] > ranks[0], $"chute {ranks[1]:F3}, cinématique {ranks[0]:F3}");
    }

    [Fact]
    public void Sans_vecteurs_les_mots_et_les_titres_decident()
    {
        var passages = new List<AgentPrompts.CoursePassage>
        {
            new(0, "Cinématique", "La hauteur d'un immeuble."),
            new(1, "Chute libre", "Un corps lâché."),
        };
        var ranks = AgentPrompts.Rank(passages, "Calculer la hauteur d'une chute libre", null);
        Assert.Equal(1, ranks.MaxBy(r => r.Value).Key);   // deux mots du titre comptent plus qu'un mot du texte
    }

    [Fact]
    public void Les_passages_choisis_sont_remis_dans_l_ordre_sous_leur_titre()
    {
        var passages = AgentPrompts.Passages($"## Cinématique\n{Paragraphs("Vitesse", 30)}\n\n## Chute libre\nh = ½ g t².\n\n{Paragraphs("Chute", 30)}\n\n## Énergie\n{Paragraphs("Travail", 30)}");
        var excerpt = AgentPrompts.SelectPassages(passages, "Comment calculer la hauteur d'une chute libre ?", null, 6_000);

        Assert.True(excerpt.Text.Length <= 6_000);
        Assert.StartsWith("Plan complet du cours :\n- Cinématique\n- Chute libre\n- Énergie\n\n", excerpt.Text);
        Assert.Contains("## Chute libre\nh = ½ g t²", excerpt.Text);
        Assert.Contains("### Chute libre (suite)\n", excerpt.Text);
        Assert.DoesNotContain("Travail", excerpt.Text);
        Assert.Contains("passage(s) sur", excerpt.Note);
    }

    [Fact]
    public void Des_vecteurs_de_taille_differente_sont_ignores()
    {
        var passages = new List<AgentPrompts.CoursePassage> { new(0, "A", "texte", [1, 0]) };
        Assert.Equal(0, AgentPrompts.Rank(passages, "question", [1, 0, 0])[0]);
    }

    [Theory]
    [InlineData("Mathématiques", 0.2)]
    [InlineData("Physique-Chimie", 0.2)]
    [InlineData("PCT", 0.2)]
    [InlineData("Further Mathematics", 0.2)]
    [InlineData("Histoire-Géographie", 0.4)]
    [InlineData("Français", 0.4)]
    [InlineData(null, 0.4)]
    public void Les_matieres_de_calcul_ont_une_temperature_basse(string? subject, double expected) =>
        Assert.Equal(expected, AgentPrompts.TemperatureFor(subject));

    [Fact]
    public void L_empreinte_change_avec_le_texte_du_cours()
    {
        Assert.Equal(CourseIndex.Hash("## A\ntexte"), CourseIndex.Hash("## A\ntexte"));
        Assert.NotEqual(CourseIndex.Hash("## A\ntexte"), CourseIndex.Hash("## A\ntexte modifié"));
    }

    [Fact]
    public void La_similarite_est_bornee() =>
        Assert.Equal(1, AgentPrompts.Cosine([2, 0], [5, 0]), 6);
}
