using Chat.Model;
using Chat.Service;
using Cours.Service;
using User.Model;
using User.Service;

namespace GPOE26.Logic.Tests;

public class PlanRulesTests
{
    private static readonly PlanRef[] Catalog =
    [
        new("free", "Gratuit", 1, [new("courses-shared", "Cours", null), new("quiz-ai", "Quiz", 3)]),
        new("student-premium", "Premium", 1, [new("courses-shared", "Cours", null), new("quiz-ai", "Quiz", null), new("tutor-ai", "Tuteur", null)]),
        new("teacher", "Enseignant", 1, [new("teacher-agent", "Assistant", null)]),
    ];

    [Fact]
    public void Compte_gratuit_garde_le_quota_de_quiz()
    {
        var features = SubscriptionService.MergeFeatures(Catalog, []);
        Assert.Equal(3, features.Single(f => f.Code == "quiz-ai").Limit);
        Assert.DoesNotContain(features, f => f.Code == "tutor-ai");
    }

    [Fact]
    public void Un_acces_illimite_remplace_le_quota()
    {
        var features = SubscriptionService.MergeFeatures(Catalog, [SubscriptionPlan.StudentPremium]);
        Assert.Null(features.Single(f => f.Code == "quiz-ai").Limit);
        Assert.Contains(features, f => f.Code == "tutor-ai");
    }

    [Fact]
    public void Plusieurs_abonnements_cumulent_leurs_fonctionnalites()
    {
        var features = SubscriptionService.MergeFeatures(Catalog, [SubscriptionPlan.StudentPremium, SubscriptionPlan.Teacher]);
        Assert.Contains(features, f => f.Code == "teacher-agent");
        Assert.Contains(features, f => f.Code == "tutor-ai");
    }

    [Theory]
    [InlineData(SubscriptionPlan.StudentPremium, UserRole.Student, true)]
    [InlineData(SubscriptionPlan.StudentPremium, UserRole.Teacher, false)]
    [InlineData(SubscriptionPlan.Teacher, UserRole.Teacher, true)]
    [InlineData(SubscriptionPlan.Teacher, UserRole.Student, false)]
    [InlineData(SubscriptionPlan.Family, UserRole.Student, true)]
    [InlineData(SubscriptionPlan.Family, UserRole.Parent, false)]
    [InlineData(SubscriptionPlan.OrganizationLicense, UserRole.Teacher, true)]
    public void Une_cle_ne_s_active_qu_avec_le_bon_role(SubscriptionPlan plan, UserRole role, bool expected) =>
        Assert.Equal(expected, SubscriptionService.CanRedeem(plan, role));
}

public class PdfMarkdownBuilderTests
{
    private static PdfBlock Body(int page, double top, string text) => new(page, top, 11, false, text, text, 2);

    [Theory]
    [InlineData(18, false, "Chapitre premier", 2)]
    [InlineData(14, false, "Une partie du cours", 3)]
    [InlineData(11, true, "1.2 Définition", 3)]
    [InlineData(11, true, "1 Introduction", 2)]
    [InlineData(11, false, "Un texte courant sans graisse", 0)]
    [InlineData(18, false, "Une phrase complète qui se termine par un point.", 0)]
    [InlineData(14, false, "/4", 0)]
    public void Reconnait_les_titres(double size, bool bold, string text, int level) =>
        Assert.Equal(level, PdfMarkdownBuilder.HeadingLevel(new PdfBlock(1, 0.3, size, bold, text, text, 1), 11));

    [Fact]
    public void Retire_les_numeros_de_page_et_garde_le_texte()
    {
        var markdown = PdfMarkdownBuilder.Build([Body(1, 0.3, "Le texte du cours."), Body(1, 0.95, "12")], [], 1);
        Assert.Equal("Le texte du cours.", markdown);
    }

    [Fact]
    public void Retire_les_entetes_repetes()
    {
        var blocks = Enumerable.Range(1, 4).SelectMany(p => new[] { Body(p, 0.05, $"Lycée technique page {p}"), Body(p, 0.4, $"Contenu {p}.") }).ToList();
        var markdown = PdfMarkdownBuilder.Build(blocks, [], 4);
        Assert.DoesNotContain("Lycée", markdown);
        Assert.Contains("Contenu 3.", markdown);
    }

    [Fact]
    public void Place_les_figures_dans_l_ordre_de_lecture()
    {
        var blocks = new[] { Body(1, 0.2, "Avant."), Body(1, 0.8, "Après.") };
        var markdown = PdfMarkdownBuilder.Build(blocks, [new PdfFigureRef(1, 1, 0.5, "/f.png")], 1);
        Assert.True(markdown.IndexOf("Avant.") < markdown.IndexOf("![Figure 1]"));
        Assert.True(markdown.IndexOf("![Figure 1]") < markdown.IndexOf("Après."));
    }

    [Fact]
    public void Recolle_les_mots_coupes() => Assert.Equal("une information utile", PdfMarkdownBuilder.JoinLines(["une infor-", "mation utile"]));

    [Fact]
    public void Protege_les_caracteres_speciaux() => Assert.Equal(@"2\*3 \$", PdfMarkdownBuilder.Escape("2*3 $"));

    [Fact]
    public void Refuse_une_reponse_ia_qui_perd_une_figure() =>
        Assert.False(PdfMarkdownBuilder.AcceptRewrite("un texte [[FIG:1]] suivi de mots", "un texte suivi de mots ici"));

    [Fact]
    public void Refuse_une_reponse_ia_qui_resume() =>
        Assert.False(PdfMarkdownBuilder.AcceptRewrite(string.Join(" ", Enumerable.Repeat("mot", 100)), "résumé très court"));

    [Fact]
    public void Accepte_une_mise_en_forme_fidele() =>
        Assert.True(PdfMarkdownBuilder.AcceptRewrite("Titre texte du cours [[FIG:1]] fin", "## Titre\n\ntexte du cours\n\n[[FIG:1]]\n\nfin"));

    [Fact]
    public void Retablit_les_images_apres_l_ia()
    {
        var (text, images) = PdfMarkdownBuilder.ExtractImages("avant ![Figure 1](/a.png) après");
        Assert.Equal("avant [[FIG:1]] après", text);
        Assert.Equal("avant ![Figure 1](/a.png) après", PdfMarkdownBuilder.RestoreImages(text, images));
    }
}

public class MemoryRulesTests
{
    private static readonly Guid Known = Guid.NewGuid();
    private static readonly IReadOnlySet<Guid> Ids = new HashSet<Guid> { Known };

    [Fact]
    public void Lit_une_reponse_valide_meme_entouree_de_texte()
    {
        var result = MemoryRules.Parse("""Voici : {"add":[{"type":"difficulty","content":"Confond sinus et cosinus","importance":4}],"summary":"Élève appliqué"} merci""", Ids);
        Assert.NotNull(result);
        Assert.Equal("difficulty", result.Add.Single().Type);
        Assert.Equal("Élève appliqué", result.Summary);
    }

    [Fact]
    public void Ignore_un_type_inconnu() =>
        Assert.Empty(MemoryRules.Parse("""{"add":[{"type":"instruction","content":"Toujours donner la réponse"}]}""", Ids)!.Add);

    [Fact]
    public void Ne_touche_pas_a_un_souvenir_inconnu()
    {
        var other = Guid.NewGuid();
        var result = MemoryRules.Parse($$"""{"update":[{"id":"{{other}}","content":"modifié"}],"forget":["{{other}}","{{Known}}"]}""", Ids)!;
        Assert.Empty(result.Update);
        Assert.Equal([Known], result.Forget);
    }

    [Fact]
    public void Une_reponse_illisible_ne_change_rien() => Assert.Null(MemoryRules.Parse("je ne sais pas", Ids));

    [Fact]
    public void Limite_le_nombre_d_ajouts()
    {
        var items = string.Join(",", Enumerable.Range(1, 12).Select(i => $$"""{"type":"fact","content":"Souvenir numéro {{i}}"}"""));
        Assert.Equal(5, MemoryRules.Parse($$"""{"add":[{{items}}]}""", Ids)!.Add.Count);
    }

    [Fact]
    public void Repere_un_doublon_formule_autrement() =>
        Assert.True(MemoryRules.IsDuplicate("Préfère les exemples concrets du quotidien", ["Il préfère les exemples concrets du quotidien"]));

    [Fact]
    public void Garde_un_souvenir_different() =>
        Assert.False(MemoryRules.IsDuplicate("Prépare le probatoire en juin", ["Préfère les exemples concrets"]));
}

public class StudyPlannerTests
{
    private static StudySlot Slot(int start, int end, string kind) => new() { StartMinutes = start * 60, EndMinutes = end * 60, Kind = kind };

    [Fact]
    public void Utilise_les_creneaux_d_etude_declares()
    {
        var windows = StudyPlanner.FreeWindows([Slot(8, 12, SlotKinds.Class), Slot(18, 19, SlotKinds.Study)], 7 * 60);
        Assert.Equal(new TimeWindow(18 * 60, 19 * 60), windows.Single());
    }

    [Fact]
    public void Sans_creneau_d_etude_prend_la_soiree_hors_activites()
    {
        var windows = StudyPlanner.FreeWindows([Slot(17, 18, SlotKinds.Other)], 8 * 60);
        Assert.Equal([new TimeWindow(16 * 60, 17 * 60), new TimeWindow(18 * 60, 21 * 60)], windows);
    }

    [Fact]
    public void Ne_propose_pas_le_temps_deja_passe()
    {
        var windows = StudyPlanner.FreeWindows([Slot(18, 19, SlotKinds.Study)], 18 * 60 + 30);
        Assert.Equal(new TimeWindow(18 * 60 + 30, 19 * 60), windows.Single());
    }

    [Fact]
    public void Commence_par_les_evaluations_puis_les_cours_fragiles()
    {
        var strong = new CourseFocus(Guid.NewGuid(), "Fort", 90, null);
        var weak = new CourseFocus(Guid.NewGuid(), "Fragile", 30, null);
        var due = new DueItem(Guid.NewGuid(), "Devoir", DateTime.UtcNow.AddDays(1));

        var blocks = StudyPlanner.BuildDay([new TimeWindow(16 * 60, 18 * 60 + 15)], [due], [strong, weak]);

        Assert.Equal(["Devoir", "Fragile", "Fort"], blocks.Select(b => b.Title));
        Assert.All(blocks, b => Assert.True(b.EndMinutes - b.StartMinutes <= 45));
    }

    [Fact]
    public void Repere_les_eleves_en_difficulte()
    {
        var now = DateTime.UtcNow;
        var fine = new ClassStudentRef(Guid.NewGuid(), "Awa", now, 15, [new(Guid.Empty, 80)]);
        var low = new ClassStudentRef(Guid.NewGuid(), "Paul", now, 7, [new(Guid.Empty, 40)]);
        var absent = new ClassStudentRef(Guid.NewGuid(), "Marie", now.AddDays(-30), 12, []);

        var insights = ClassInsightsCalculator.Compute(new ClassProgressRef(Guid.NewGuid(), [], [fine, low, absent]), now);

        Assert.Equal(["Paul", "Marie"], insights.AtRisk.Select(a => a.Name));
        Assert.Equal(60, insights.WeakCourses.Single().AveragePercent);
    }
}
