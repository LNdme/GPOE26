using Chat.Service;

namespace GPOE26.Logic.Tests;

/// <summary>Données partagées : qui voit quoi des notions fragiles, et contexte du tuteur (docs/PLAN-ETABLISSEMENT-2.md, lot 12).</summary>
public class InsightRulesTests
{
    [Theory]
    [InlineData("Teacher", false, InsightView.Named)]
    [InlineData("Teacher", true, InsightView.Named)]      // gestionnaire qui enseigne aussi dans la classe
    [InlineData("Manager", true, InsightView.Anonymous)]
    [InlineData(null, true, InsightView.Anonymous)]
    [InlineData("Student", false, InsightView.None)]
    [InlineData(null, false, InsightView.None)]
    public void Chacun_voit_ce_que_prevoit_le_tableau_P6(string? myRole, bool isManager, InsightView expected) =>
        Assert.Equal(expected, InsightRules.ViewFor(myRole, isManager));

    [Fact]
    public void Les_notions_sont_decoupees_et_nettoyees() =>
        Assert.Equal(["dérivée d'un produit", "signe d'une fonction", "limites"],
            InsightRules.Notions("dérivée d'un produit ; signe d'une fonction,\n- limites."));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ;  , ")]
    public void Rien_a_decouper(string? text) => Assert.Empty(InsightRules.Notions(text));

    [Fact]
    public void Une_notion_repetee_chez_un_eleve_ne_compte_qu_une_fois() =>
        Assert.Equal(["Limites"], InsightRules.Notions("Limites ; limites ; LIMITES"));

    [Fact]
    public void Les_notions_les_plus_frequentes_d_abord_sans_tenir_compte_des_accents()
    {
        var top = InsightRules.TopNotions(["dérivée d'un produit ; limites", "Derivee d'un produit", "limites ; asymptotes", "dérivée d'un produit"]);

        Assert.Equal([new NotionCount("Dérivée d'un produit", 3), new NotionCount("Limites", 2), new NotionCount("Asymptotes", 1)], top);
    }

    [Fact]
    public void Le_resume_est_limite() =>
        Assert.Equal(2, InsightRules.TopNotions(["a1 ; b1 ; c1 ; d1"], take: 2).Count);

    private static readonly TimeZoneInfo Douala = TimeZoneInfo.CreateCustomTimeZone("UTC+1", TimeSpan.FromHours(1), "UTC+1", "UTC+1");
    private static readonly DateTime Now = new(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);

    private static StudentAssessmentRef A(string title, string status, int dueInDays, int attempts = 0, decimal? score = null, string kind = "GradedExam", string? subject = null) =>
        new(Guid.NewGuid(), title, status, kind, subject, Now.AddDays(dueInDays), attempts, false, score);

    [Fact]
    public void Le_tuteur_connait_les_epreuves_a_venir_et_les_derniers_resultats()
    {
        var lines = InsightRules.AssessmentLines([
            A("Devoir suites", "Published", 3, subject: "Mathématiques"),
            A("Épreuve blanche", "Published", 10, kind: "MockExam"),
            A("Trop loin", "Published", 30),
            A("Déjà rendu", "Published", 2, attempts: 1),
            A("Interro", "Closed", -5, attempts: 1, score: 14.5m),
        ], Now, Douala);

        Assert.Equal([
            "Épreuves à venir : Devoir suites (Mathématiques), épreuve notée pour le samedi 14 novembre ; Épreuve blanche, épreuve blanche pour le samedi 21 novembre",
            "Derniers résultats : Interro : 14,5/20"
        ], lines);
    }

    [Fact]
    public void Sans_epreuve_rien_n_est_ajoute() => Assert.Empty(InsightRules.AssessmentLines([], Now, Douala));

    [Fact]
    public void La_classe_cite_son_etablissement_une_fois()
    {
        Assert.Equal("Classe : Terminale C, Club maths (Lycée de démonstration)",
            InsightRules.ClassLine([("Terminale C", "Lycée de démonstration"), ("Club maths", "Lycée de démonstration")]));
        Assert.Equal("Classe : Groupe du soir", InsightRules.ClassLine([("Groupe du soir", null)]));
        Assert.Null(InsightRules.ClassLine([]));
    }
}
