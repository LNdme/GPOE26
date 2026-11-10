using Chat.Model;
using Chat.Service;
using Cours.Service;
using Microsoft.Extensions.Configuration;
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
    public void Retire_les_caracteres_nuls_refuses_par_postgresql() =>
        Assert.Equal("## Titre\n\nLa loi\td'Ohm\r\n", PdfMarkdownBuilder.RemoveControlChars("## Ti\0tre\n\nLa\u0001 loi\td'Ohm\0\r\n"));

    [Fact]
    public void Garde_un_texte_propre_tel_quel()
    {
        const string text = "Énergie $E = mc^2$ — schéma ci-dessous\n";
        Assert.Same(text, PdfMarkdownBuilder.RemoveControlChars(text));
    }

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

public class PdfFormulaBuilderTests
{
    private const string Roman = "Times-Roman";
    private const string Italic = "Times-Italic";
    private const string Symbol = "Symbol";

    private static PdfGlyph G(string text, double left, double baseline = 100, double size = 12, string font = Italic) =>
        new(text, left, left + size * 0.5, baseline, size, font);

    [Fact]
    public void Reconnait_un_indice() =>
        Assert.Equal(@"E_{\mathrm{TH}}", PdfFormulaBuilder.ToLatex([G("E", 0), G("T", 6.5, 97, 7, Roman), G("H", 10.5, 97, 7, Roman)], []));

    [Fact]
    public void Construit_une_fraction()
    {
        PdfGlyph[] glyphs = [G("i", 0), G("=", 8, font: Symbol), G("d", 22, 108), G("q", 28, 108), G("d", 22, 92), G("t", 28, 92)];
        Assert.Equal(@"i=\frac{dq}{dt}", PdfFormulaBuilder.ToLatex(glyphs, [new PdfBar(21, 35, 103)]));
    }

    [Fact]
    public void Garde_ce_qui_est_entre_deux_fractions()
    {
        PdfGlyph[] glyphs = [G("a", 10, 108), G("b", 10, 92), G("=", 22, 100, font: Symbol), G("c", 36, 108), G("d", 36, 92)];
        Assert.Equal(@"\frac{a}{b}=\frac{c}{d}", PdfFormulaBuilder.ToLatex(glyphs, [new PdfBar(9, 18, 103), new PdfBar(35, 43, 103)]));
    }

    [Fact]
    public void Place_le_numero_d_equation_a_droite() =>
        Assert.Equal(@"p=ui \qquad\text{(I-4)}", PdfFormulaBuilder.WithEquationTag(@"p=ui\ (\mathrm{I}-4)"));

    [Fact]
    public void Construit_une_fraction_de_fractions()
    {
        PdfGlyph[] glyphs = [G("a", 22, 120), G("b", 22, 108), G("c", 22, 92)];
        var latex = PdfFormulaBuilder.ToLatex(glyphs, [new PdfBar(15, 40, 103), new PdfBar(20, 30, 115)]);
        Assert.Equal(@"\frac{\frac{a}{b}}{c}", latex);
    }

    [Fact]
    public void Traduit_les_symboles_grecs_et_operateurs() =>
        Assert.Equal(@"\beta \cdot i", PdfFormulaBuilder.ToLatex([G("β", 0, font: Symbol), G("⋅", 8, font: Symbol), G("i", 16)], []));

    [Fact]
    public void Dans_une_phrase_seule_l_expression_passe_en_formule()
    {
        PdfGlyph[] line =
        [
            G("o", 0, font: Roman), G("n", 6, font: Roman),
            G("U", 18), G("=", 28, font: Symbol), G("0", 38, font: Roman),
            G("s", 50, font: Roman), G("o", 56, font: Roman), G("i", 62, font: Roman), G("t", 68, font: Roman),
        ];
        Assert.Equal("on $U=0$ soit", PdfFormulaBuilder.InlineLine(line));
    }

    [Fact]
    public void L_italique_d_une_phrase_reste_du_texte()
    {
        PdfGlyph[] line = [G("c", 0), G("o", 6), G("u", 12), G("r", 18), G("a", 24), G("n", 30), G("t", 36), G("d", 50), G("e", 56)];
        Assert.DoesNotContain("$", PdfFormulaBuilder.InlineLine(line));
    }

    [Fact]
    public void Regroupe_indice_et_exposant_d_une_meme_lettre() =>
        Assert.Equal(@"u_{\mathrm{C}}^{2}", PdfFormulaBuilder.ToLatex([G("u", 0), G("C", 7, 97, 7, Roman), G("2", 12, 106, 7, Roman)], []));

    [Fact]
    public void Une_fraction_imbriquee_n_est_pas_ecrite_sans_certitude() =>
        Assert.False(PdfFormulaBuilder.IsReliable([G("a", 22, 120), G("b", 22, 108), G("c", 22, 92)], [new PdfBar(15, 40, 103), new PdfBar(20, 30, 115)]));

    [Fact]
    public void Une_somme_a_bornes_n_est_pas_ecrite_sans_certitude() =>
        Assert.False(PdfFormulaBuilder.IsReliable([G("∑", 0, font: Symbol), G("i", 2, 90, 7), G("G", 12)], []));

    [Fact]
    public void Une_fraction_simple_est_ecrite() =>
        Assert.True(PdfFormulaBuilder.IsReliable([G("a", 22, 108), G("b", 22, 92)], [new PdfBar(20, 30, 103)]));

    [Fact]
    public void Reporte_la_fleche_de_vecteur_sur_sa_lettre() =>
        Assert.Equal(@"\vec{E}", PdfFormulaBuilder.ToLatex([G("E", 0), G("r", 1, 110, 10, "KPSHBO+MTExtra")], []));

    [Fact]
    public void Des_points_de_suspension_ne_sont_pas_une_formule()
    {
        PdfGlyph[] line = [G("E", 0), G(".", 14, font: Roman), G(".", 18, font: Roman), G(".", 22, font: Roman)];
        Assert.Equal("$E$ ...", PdfFormulaBuilder.InlineLine(line));
    }

    [Fact]
    public void Le_point_final_reste_hors_de_la_formule()
    {
        PdfGlyph[] line = [G("i", 0), G(".", 4, font: Roman)];
        Assert.Equal("$i$.", PdfFormulaBuilder.InlineLine(line));
    }
}

public class PdfLayoutTests
{
    [Fact]
    public void Deux_colonnes_se_lisent_l_une_apres_l_autre()
    {
        (double, double, double)[] boxes = [(50, 280, 0.2), (320, 540, 0.1), (50, 280, 0.5), (320, 540, 0.6)];
        Assert.Equal([0, 2, 1, 3], PdfLayout.Order(595, boxes));
    }

    [Fact]
    public void Un_titre_sur_toute_la_largeur_separe_les_sections()
    {
        (double, double, double)[] boxes = [(100, 500, 0.05), (50, 280, 0.2), (320, 540, 0.2), (50, 280, 0.3), (320, 540, 0.3)];
        Assert.Equal([0, 1, 3, 2, 4], PdfLayout.Order(595, boxes));
    }

    [Fact]
    public void Le_numero_d_equation_rejoint_son_equation()
    {
        var blocks = new[]
        {
            new PdfBlock(1, 0.3, 11, false, "p", "$$ p=ui $$", 1, Left: 100, Right: 150),
            new PdfBlock(1, 0.295, 11, false, "(I-2)", "(I-2)", 1, Left: 200, Right: 230),
        };
        Assert.Equal(@"$$ p=ui \qquad\text{(I-2)} $$", PdfMarkdownBuilder.Build(blocks, [], 1));
    }

    [Fact]
    public void Un_titre_numerote_en_gras_peut_finir_par_un_point() =>
        Assert.Equal(3, PdfMarkdownBuilder.HeadingLevel(new PdfBlock(1, 0.3, 11, true, "I.1.a. Définition.", "x", 1), 11));

    [Fact]
    public void Un_tiret_de_liste_rejoint_son_paragraphe()
    {
        var blocks = new[]
        {
            new PdfBlock(1, 0.3, 11, false, "-", "-", 1, Left: 50, Right: 54),
            new PdfBlock(1, 0.3, 11, false, "Un conducteur parfait.", "Un conducteur parfait.", 2, Left: 70, Right: 280),
        };
        Assert.Equal("- Un conducteur parfait.", PdfMarkdownBuilder.Build(blocks, [], 1));
    }
}

public class MarkdownRendererTests
{
    [Fact]
    public void Une_formule_seule_sur_sa_ligne_est_centree() =>
        Assert.Contains(@"<div class=""math"">", GPOE26.UI.Services.MarkdownRenderer.Render("avant\n\n$$ a=b $$\n\naprès").Html);

    [Fact]
    public void Une_formule_dans_une_phrase_reste_en_ligne() =>
        Assert.DoesNotContain(@"<div class=""math"">", GPOE26.UI.Services.MarkdownRenderer.Render("on a $x$ et $$ y $$ ici").Html);
}

public class SimulationCatalogTests
{
    [Fact]
    public void Produit_un_bloc_json_valide_avec_les_accents()
    {
        var type = GPOE26.UI.Services.SimulationCatalog.Find("dc-circuit");
        var draft = GPOE26.UI.Services.SimulationCatalog.NewDraft(type);
        draft.PredictQuestion = "Le courant total ?";
        draft.PredictOptions = "Augmente\nDiminue";
        draft.PredictAnswer = 2;

        var block = GPOE26.UI.Services.SimulationCatalog.ToMarkdown(type, draft);

        Assert.Contains("```simulation", block);
        Assert.Contains("Résistances", block);
        var json = block.Split("```")[1]["simulation".Length..];
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("dc-circuit", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("branches").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("predict").GetProperty("answer").GetInt32());
    }

    [Fact]
    public void Sans_reponses_proposees_il_n_y_a_pas_de_prediction()
    {
        var type = GPOE26.UI.Services.SimulationCatalog.Find("qubit");
        var draft = GPOE26.UI.Services.SimulationCatalog.NewDraft(type);
        draft.PredictQuestion = "Question seule";
        Assert.DoesNotContain("predict", GPOE26.UI.Services.SimulationCatalog.ToMarkdown(type, draft));
    }

    [Fact]
    public void Les_blocs_de_simulation_sortent_du_texte_donne_a_l_ia()
    {
        var course = new Cours.Model.Course { Title = "T", Subject = "S" };
        course.Sections.Add(new Cours.Model.CourseSection { Content = "Avant\n```simulation\n{\"type\":\"qubit\"}\n```\nAprès" });
        course.RebuildExtractedText();
        Assert.DoesNotContain("qubit", course.ExtractedText);
        Assert.Contains("Après", course.ExtractedText);
    }
}

public class SafeWebFetcherTests
{
    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("::ffff:192.168.0.1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("172.32.0.1", true)]
    [InlineData("2606:4700::1111", true)]
    public void Seules_les_adresses_publiques_sont_joignables(string address, bool expected) =>
        Assert.Equal(expected, SafeWebFetcher.IsPublicAddress(System.Net.IPAddress.Parse(address)));

    [Theory]
    [InlineData("http://localhost:5197/auth/me")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[::1]/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/a.pdf")]
    [InlineData("https://user:secret@example.com/")]
    [InlineData("https://example.com:8080/")]
    [InlineData("http://cours.internal/")]
    [InlineData("")]
    public void Refuse_les_liens_dangereux(string url) =>
        Assert.Throws<CourseImportException>(() => SafeWebFetcher.ValidateUrl(url));

    [Fact]
    public void Ajoute_https_quand_il_manque() =>
        Assert.Equal("https", SafeWebFetcher.ValidateUrl("exemple.org/cours").Scheme);

    [Theory]
    [InlineData("https://drive.google.com/file/d/ABC_1-x/view?usp=sharing", "https://drive.google.com/uc?export=download&id=ABC_1-x")]
    [InlineData("https://drive.google.com/open?id=XYZ", "https://drive.google.com/uc?export=download&id=XYZ")]
    [InlineData("https://docs.google.com/document/d/DOC/edit", "https://docs.google.com/document/d/DOC/export?format=pdf")]
    [InlineData("https://docs.google.com/presentation/d/PRES/edit", "https://docs.google.com/presentation/d/PRES/export/pdf")]
    [InlineData("https://www.dropbox.com/s/a/b.pdf?dl=0", "https://www.dropbox.com/s/a/b.pdf?dl=1")]
    [InlineData("https://exemple.org/cours.html", "https://exemple.org/cours.html")]
    public void Vise_le_document_derriere_un_lien_de_partage(string link, string expected) =>
        Assert.Equal(expected, SafeWebFetcher.NormalizeShareLink(new Uri(link)).AbsoluteUri);

    [Fact]
    public void Reconnait_une_image_a_son_contenu()
    {
        Assert.Equal("png", CourseImportService.ImageExtension([0x89, 0x50, 0x4E, 0x47, 1, 2]));
        Assert.Equal("jpg", CourseImportService.ImageExtension([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Null(CourseImportService.ImageExtension(System.Text.Encoding.UTF8.GetBytes("<svg onload='x'>")));
    }

    [Fact]
    public void Fait_un_titre_a_partir_du_nom_de_fichier()
    {
        Assert.Equal("Cours lois electriques", CourseImportService.TitleFromFile("Cours_lois-electriques.pdf"));
        Assert.Equal("Document importé", CourseImportService.TitleFromFile("uc"));
    }
}

public class HtmlCourseExtractorTests
{
    private static readonly Uri Base = new("https://exemple.org/cours/ch1.html");

    [Fact]
    public void Garde_la_structure_du_cours_et_retire_le_reste()
    {
        var page = HtmlCourseExtractor.Extract("""
            <html><head><title>Site</title><style>p{}</style></head><body>
            <nav><a href="/">Accueil</a></nav>
            <main><h1>États quantiques</h1><p>Un <strong>photon</strong> seul.</p>
            <h2>Le qubit</h2><ul><li>Premier</li><li>Second</li></ul>
            <script>alert(1)</script><button>Vérifier</button><canvas></canvas></main>
            </body></html>
            """, Base);

        Assert.Equal("États quantiques", page.Title);
        Assert.Contains("Un **photon** seul.", page.Markdown);
        Assert.Contains("## Le qubit", page.Markdown);
        Assert.Contains("- Premier", page.Markdown);
        Assert.DoesNotContain("Accueil", page.Markdown);
        Assert.DoesNotContain("alert", page.Markdown);
        Assert.DoesNotContain("Vérifier", page.Markdown);
        Assert.DoesNotContain("# États quantiques", page.Markdown);
    }

    [Fact]
    public void Retrouve_le_latex_des_formules_rendues()
    {
        var page = HtmlCourseExtractor.Extract("""
            <main><p>Soit <span class="katex"><math><semantics><annotation encoding="application/x-tex">E=mc^2</annotation></semantics></math><span aria-hidden="true">E=mc2</span></span>.</p></main>
            """, Base);
        Assert.Contains("$E=mc^2$", page.Markdown);
        Assert.DoesNotContain("E=mc2", page.Markdown);
    }

    [Fact]
    public void Laisse_intactes_les_formules_mathjax()
    {
        var page = HtmlCourseExtractor.Extract(@"<main><p>La norme \(|\alpha|^2 + |\beta|^2 = 1\) vaut 1, et a_b reste du texte.</p></main>", Base);
        Assert.Contains(@"\(|\alpha|^2 + |\beta|^2 = 1\)", page.Markdown);
        Assert.Contains(@"a\_b", page.Markdown);
    }

    [Fact]
    public void Resout_les_images_et_ignore_les_pictogrammes()
    {
        var page = HtmlCourseExtractor.Extract("""
            <main><p>Texte.</p><figure><img src="../img/a.png" alt="Schéma"><figcaption>Figure 1</figcaption></figure>
            <img src="/i.png" width="16"><img src="data:image/png;base64,AAA"></main>
            """, Base);
        Assert.Equal("https://exemple.org/img/a.png", Assert.Single(page.Images).Url.AbsoluteUri);
        Assert.Contains("[[IMG:1]]", page.Markdown);
        Assert.Contains("*Figure 1*", page.Markdown);
    }

    [Fact]
    public void Separe_le_numero_du_titre_et_l_orateur_de_sa_replique()
    {
        var markdown = HtmlCourseExtractor.Extract("""
            <main><h2><span class="n">1.1</span>Interférences à un quanton</h2>
            <blockquote><p><b>Einstein</b>« Dieu ne joue pas aux dés. »</p><p><b>Bohr</b>« Albert, arrête. »</p></blockquote>
            <span class="big-num" aria-hidden="true">1</span><p>Un photon <em>seul</em>s'avance.</p></main>
            """, Base).Markdown;
        Assert.Contains("## 1.1 Interférences à un quanton", markdown);
        Assert.Contains("> **Einstein** « Dieu ne joue pas aux dés. »\n>\n> **Bohr** « Albert, arrête. »", markdown);
        Assert.DoesNotContain("\n1\n", "\n" + markdown + "\n");
    }

    [Fact]
    public void Signale_les_simulations_de_la_page_d_origine()
    {
        var markdown = HtmlCourseExtractor.Extract("""
            <main><h2>Interférences</h2><p>Texte.</p>
            <figure class="lab" data-lab="young"><div class="lab-head"><h3>Fentes d’Young, photon par photon</h3></div>
            <div class="lab-panel"><span>N = 0</span><button>Lancer</button></div><figcaption>Poussez D vers 1.</figcaption></figure></main>
            """, Base).Markdown;
        Assert.Contains($"**{LabProposalRules.OriginalLabMarker} Fentes d’Young, photon par photon**", markdown);
        Assert.Contains("*Poussez D vers 1.*", markdown);
        Assert.DoesNotContain("N = 0", markdown);
        Assert.DoesNotContain("### Fentes", markdown);
    }

    [Fact]
    public void Ne_garde_que_l_article_d_une_page_de_wiki()
    {
        var markdown = HtmlCourseExtractor.Extract("""
            <body><main id="content"><h1>Diviseur de courant</h1><ul class="langs"><li>Català</li><li>Dansk</li></ul>
            <div class="mw-parser-output"><h2>Généralités<span class="mw-editsection">[<a href="/e">modifier</a>]</span></h2>
            <p>Un diviseur de courant partage le courant entre plusieurs branches parallèles.<sup class="reference"><a href="#n1">[1]</a></sup></p>
            <p>Le courant se répartit en raison inverse des résistances de chaque branche du montage.</p></div></main></body>
            """, Base).Markdown;
        Assert.DoesNotContain("Català", markdown);
        Assert.DoesNotContain("modifier", markdown);
        Assert.DoesNotContain("&#91;1", markdown);
        Assert.Contains("## Généralités", markdown);
    }

    [Fact]
    public void Un_crochet_echappe_n_est_pas_une_formule()
    {
        Assert.Equal("&#91;1&#93; a\\_b", PdfMarkdownBuilder.Escape("[1] a_b"));
        var html = GPOE26.UI.Services.MarkdownRenderer.Render("Voir &#91;1&#93;, puis \\[modifier\\](https://x.org) | \\[code\\](https://x.org) ici.").Html;
        Assert.Contains("[1]", html);
        Assert.DoesNotContain("math", html);
        Assert.Contains("math", GPOE26.UI.Services.MarkdownRenderer.Render("Soit \\[ x^2 \\] et \\( y \\).").Html);
    }

    [Fact]
    public void Convertit_les_tableaux() =>
        Assert.Contains("| 1 | 2 |", HtmlCourseExtractor.Extract("<main><table><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></table></main>", Base).Markdown);

    [Fact]
    public void Remet_le_schema_a_la_place_de_son_repere()
    {
        var merged = PdfMarkdownBuilder.MergeTranscription("Avant [schéma] après [schéma]", "![p](/a.png)");
        Assert.Contains("![p](/a.png)", merged);
        Assert.DoesNotContain("[schéma]", merged);
        Assert.Equal("Sans schéma", PdfMarkdownBuilder.MergeTranscription("Sans schéma", "![p](/a.png)"));
    }

    [Theory]
    [InlineData("Une inductance L est un dipôle tel que :\n\n[sch", true)]
    [InlineData("Soit $$ x = \\frac{1}{2}", true)]
    [InlineData("\\begin{aligned} a &= b", true)]
    [InlineData("", true)]
    [InlineData("Texte complet [schéma: 10, 20, 80, 40] et $$ x = 1 $$ fin.", false)]
    [InlineData("La fonction est définie sur ]0, 1[", false)]
    public void Repere_une_retranscription_interrompue(string text, bool expected) =>
        Assert.Equal(expected, PdfMarkdownBuilder.LooksTruncated(text));

    [Fact]
    public void Decoupe_chaque_schema_a_sa_position()
    {
        var boxes = new List<SchemaBox>();
        var merged = PdfMarkdownBuilder.MergeTranscription(
            "Pour une résistance : [schéma: 10, 35, 90, 60] puis [Schéma : 5.5; 70; 50; 95 %] et [schéma: 80, 10, 20, 30]",
            "![p](/page.jpg)",
            box => { boxes.Add(box); return $"![s](/s{boxes.Count}.jpg)"; });
        Assert.Equal(new SchemaBox(10, 35, 90, 60), boxes[0]);
        Assert.Equal(new SchemaBox(5.5, 70, 50, 95), boxes[1]);
        Assert.Equal(2, boxes.Count);
        Assert.Contains("![s](/s1.jpg)", merged);
        Assert.Contains("![s](/s2.jpg)", merged);
        Assert.Contains("![p](/page.jpg)", merged);
        Assert.DoesNotContain("schéma", merged, StringComparison.OrdinalIgnoreCase);
    }
}

public class LabProposalRulesTests
{
    private static readonly string[] Headings = ["Le diviseur de courant", "Charge du condensateur"];

    private static LabProposal One(string lab) =>
        Assert.Single(LabProposalRules.Parse($$"""{"labs":[{{lab}}]}""", Headings)!.Labs);

    [Fact]
    public void Accepte_un_labo_valide_et_borne_ses_valeurs()
    {
        var lab = One("""{"section":"le diviseur  de courant","type":"dc-circuit","title":"T","reason":"R","settings":{"topology":"parallel","branches":3,"voltage":999,"R1":1}}""");
        Assert.Equal("Le diviseur de courant", lab.Anchor);
        Assert.Contains("\"voltage\": 48", lab.Block);
        Assert.Contains("\"R1\": 10", lab.Block);
        Assert.Contains("\"branches\": 3", lab.Block);
    }

    [Theory]
    [InlineData("""{"section":"Le diviseur de courant","type":"inconnu","title":"T","reason":"R"}""")]
    [InlineData("""{"section":"Section absente","type":"young","title":"T","reason":"R"}""")]
    [InlineData("""{"section":"Le diviseur de courant","type":"young","reason":"R"}""")]
    [InlineData("""{"section":"Le diviseur de courant","type":"plot","title":"T","reason":"R","settings":{"expr":"constructor(x)"}}""")]
    [InlineData("""{"section":"Le diviseur de courant","type":"plot","title":"T","reason":"R","settings":{"expr":"a*x"}}""")]
    public void Ecarte_ce_qui_n_est_pas_valide(string lab) =>
        Assert.Empty(LabProposalRules.Parse($$"""{"labs":[{{lab}}]}""", Headings)!.Labs);

    [Fact]
    public void Limite_a_quatre_labos_et_un_par_section()
    {
        var headings = Enumerable.Range(1, 6).Select(i => $"S{i}").ToArray();
        var labs = string.Join(",", headings.Concat(["S1"]).Select(h => $$"""{"section":"{{h}}","type":"qubit","title":"T","reason":"R"}"""));
        Assert.Equal(4, LabProposalRules.Parse($$"""{"labs":[{{labs}}]}""", headings)!.Labs.Count);
    }

    [Fact]
    public void Un_cours_non_technique_n_a_aucun_labo() =>
        Assert.Empty(LabProposalRules.Parse("""{"subject":"Histoire","labs":[]}""", Headings)!.Labs);

    [Fact]
    public void Une_reponse_illisible_ne_propose_rien() => Assert.Null(LabProposalRules.Parse("je ne sais pas", Headings));

    [Fact]
    public void Lit_une_reponse_entouree_de_texte_et_de_latex()
    {
        var reply = """
            Voici mon analyse {brouillon}.
            ```json
            {"subject":"Électricité","labs":[{"section":"Le diviseur de courant","type":"dc-circuit","title":"Diviseur","reason":"Vois \frac{R_2}{R_1+R_2} en \alpha action"}]}
            ```
            """;
        var analysis = LabProposalRules.Parse(reply, Headings);
        Assert.Equal("Électricité", analysis!.Subject);
        Assert.Single(analysis.Labs);
    }

    [Fact]
    public void Un_titre_ne_peut_pas_fermer_le_bloc()
    {
        var lab = One("""{"section":"Le diviseur de courant","type":"qubit","title":"a```b","reason":"R"}""");
        Assert.Equal(2, lab.Block.Split("```").Length - 1);
    }

    [Theory]
    [InlineData("a*sin(b*x)", true)]
    [InlineData("exp(-x^2/2)+pi", true)]
    [InlineData("a*(x", false)]
    [InlineData("alert(1)", false)]
    [InlineData("window", false)]
    [InlineData("x;1", false)]
    public void Controle_les_expressions(string expression, bool expected) =>
        Assert.Equal(expected, LabProposalRules.IsSafeExpression(expression, ["a", "b"]));

    [Fact]
    public void Place_le_labo_a_la_fin_de_sa_section()
    {
        var result = LabProposalRules.InsertAfterSection("## A\n\nTexte A.\n\n## B\n\nTexte B.", "A", "\n\n[LABO]\n\n");
        Assert.True(result.IndexOf("Texte A.") < result.IndexOf("[LABO]"));
        Assert.True(result.IndexOf("[LABO]") < result.IndexOf("## B"));
    }

    [Fact]
    public void Garde_les_labos_complets_d_une_reponse_coupee()
    {
        var reply = """
            {"title":"Circuits","subject":"Électricité","labs":[
            {"section":"Le diviseur de courant","type":"dc-circuit","title":"Diviseur","reason":"R"},
            {"section":"Charge du condensateur","type":"transient","title":"Charge","reas
            """;
        var analysis = LabProposalRules.Parse(reply, Headings);
        Assert.Equal("Électricité", analysis!.Subject);
        Assert.Equal("Diviseur", Assert.Single(analysis.Labs).Title);
    }

    [Theory]
    [InlineData(@"{""a"":""\\alpha""}", @"{""a"":""\\alpha""}")]
    [InlineData(@"{""a"":""\alpha""}", @"{""a"":""\\alpha""}")]
    [InlineData(@"{""a"":""\""x\"" \n""}", @"{""a"":""\""x\"" \n""}")]
    public void Repare_seulement_les_echappements_invalides(string json, string expected) =>
        Assert.Equal(expected, LabProposalRules.RepairEscapes(json));

    [Fact]
    public void Remplace_le_repere_de_la_simulation_d_origine()
    {
        var markdown = $"## A\n\nAvant.\n\n**{LabProposalRules.OriginalLabMarker} Fentes**\n\n*Consigne.*\n\nAprès.\n\n## B\n\nTexte B.";
        var result = LabProposalRules.InsertAfterSection(markdown, "A", "\n\n[LABO]\n\n");
        Assert.DoesNotContain(LabProposalRules.OriginalLabMarker, result);
        Assert.True(result.IndexOf("Avant.") < result.IndexOf("[LABO]"));
        Assert.True(result.IndexOf("[LABO]") < result.IndexOf("*Consigne.*"));
        Assert.True(result.IndexOf("*Consigne.*") < result.IndexOf("Après."));
    }

    [Fact]
    public void Une_section_disparue_place_le_labo_a_la_fin() =>
        Assert.EndsWith("[LABO]\n\n", LabProposalRules.InsertAfterSection("## A\n\nTexte.", "Absente", "\n\n[LABO]\n\n"));

    [Fact]
    public void Ne_repropose_pas_un_labo_deja_en_place()
    {
        var markdown = "## Le diviseur de courant\n\nTexte.\n\n```simulation\n{\n  \"type\": \"dc-circuit\"\n}\n```\n\n## Charge du condensateur\n\nTexte.";
        var circuit = "\n\n```simulation\n{\n  \"type\": \"dc-circuit\"\n}\n```\n\n";
        Assert.True(LabProposalRules.AlreadyInSection(markdown, "Le diviseur de courant", circuit));
        Assert.False(LabProposalRules.AlreadyInSection(markdown, "Charge du condensateur", circuit));
        Assert.Contains("(Labo déjà présent ici : dc-circuit)", LabProposalRules.ForModel(markdown));
        Assert.DoesNotContain("```simulation", LabProposalRules.ForModel(markdown));
    }

    [Fact]
    public void Ignore_les_titres_dans_les_blocs_de_code() =>
        Assert.Equal(["A"], LabProposalRules.Headings("## A\n```\n## Pas un titre\n```"));
}

public class AriaTests
{
    [Fact]
    public void Les_etats_aria_sont_ecrits_en_texte()
    {
        Assert.Equal("true", GPOE26.UI.Services.Aria.State(true));
        Assert.Equal("false", GPOE26.UI.Services.Aria.State(false));
        Assert.Equal("step", GPOE26.UI.Services.Aria.Current(true));
        Assert.Null(GPOE26.UI.Services.Aria.Current(false));
    }

    /// <summary>Un booléen lié directement à un état ARIA serait rendu comme un attribut vide : jamais annoncé.</summary>
    [Fact]
    public void Aucun_etat_aria_n_est_lie_a_un_booleen_brut()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "GPOE26.slnx"))) root = root.Parent;
        Assert.NotNull(root);

        var pattern = new System.Text.RegularExpressions.Regex(@"aria-(?:selected|expanded|pressed|checked|current)=""@(?!Aria\.)(?!\([^""]*\?\s*""[^""]*""\s*:)");
        var offenders = new[] { "GPOE26.UI", "GPOE26.Web" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(root!.FullName, project), "*.razor", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, index)))
            .Where(x => pattern.IsMatch(x.line))
            .Select(x => $"{Path.GetFileName(x.file)}:{x.index + 1}")
            .ToList();
        Assert.Empty(offenders);
    }
}

/// <summary>
/// Retranscription des pages photographiées : un modèle « raisonnant » y passait plus de 7 000 jetons de réflexion par page,
/// dépassait le délai et la page restait en photo. La réflexion est coupée pour ces appels, sur OpenRouter seulement.
/// </summary>
public class LlmReasoningTests
{
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public System.Text.Json.Nodes.JsonObject? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = System.Text.Json.Nodes.JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"finish_reason":"stop","message":{"content":"## Titre"}}]}""")
            };
        }
    }

    private static async Task<System.Text.Json.Nodes.JsonObject> SendAsync(string baseUrl, bool reasoning)
    {
        var handler = new CaptureHandler();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["LLM:BaseUrl"] = baseUrl, ["LLM:ApiKey"] = "test", ["LLM:VisionModels"] = "vision-model" })
            .Build();
        var client = new GPOE26.ServiceDefaults.Llm.LlmClient(new HttpClient(handler), config,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GPOE26.ServiceDefaults.Llm.LlmClient>.Instance);

        var reply = await client.CompleteAsync(
            [new GPOE26.ServiceDefaults.Llm.LlmMessage("user", "Retranscris", [new GPOE26.ServiceDefaults.Llm.LlmImage("image/jpeg", [1, 2, 3])])],
            requireComplete: true, reasoning: reasoning, ct: TestContext.Current.CancellationToken);
        Assert.Equal("## Titre", reply);
        return handler.Body!;
    }

    [Fact]
    public async Task Coupe_la_reflexion_pour_une_retranscription_sur_openrouter()
    {
        var body = await SendAsync("https://openrouter.ai/api/v1", reasoning: false);
        Assert.False(body["reasoning"]!["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Laisse_la_reflexion_par_defaut() =>
        Assert.Null((await SendAsync("https://openrouter.ai/api/v1", reasoning: true))["reasoning"]);

    [Fact]
    public async Task N_envoie_pas_le_champ_aux_autres_fournisseurs() =>
        Assert.Null((await SendAsync("http://localhost:11434/v1", reasoning: false))["reasoning"]);
}

public class HomeRedirectTests
{
    // L'administrateur envoyé sur /app voyait l'accueil élève, et l'API lui refusait ces données (403)
    [Fact]
    public void L_administrateur_arrive_sur_la_gestion_du_site_apres_connexion() =>
        Assert.Equal("/admin", GPOE26.UI.Components.AppPage.SafeReturnUrl(null, GPOE26.UI.Models.UserRole.Admin));

    [Theory]
    [InlineData(GPOE26.UI.Models.UserRole.Student)]
    [InlineData(GPOE26.UI.Models.UserRole.Teacher)]
    [InlineData(GPOE26.UI.Models.UserRole.Parent)]
    public void Les_autres_roles_arrivent_dans_leur_espace(GPOE26.UI.Models.UserRole role) =>
        Assert.Equal("/app", GPOE26.UI.Components.AppPage.SafeReturnUrl(null, role));

    [Fact]
    public void La_page_demandee_reste_prioritaire() =>
        Assert.Equal("/app/assistant", GPOE26.UI.Components.AppPage.SafeReturnUrl("/app/assistant", GPOE26.UI.Models.UserRole.Admin));

    [Fact]
    public void Une_adresse_externe_renvoie_a_l_accueil_du_role() =>
        Assert.Equal("/admin", GPOE26.UI.Components.AppPage.SafeReturnUrl("//evil.example", GPOE26.UI.Models.UserRole.Admin));
}

public class ImageFormatTests
{
    private static byte[] Header(params object[] parts) =>
        parts.SelectMany(p => p is string s ? System.Text.Encoding.ASCII.GetBytes(s) : [(byte)(int)p]).ToArray();

    [Fact]
    public void Un_jpeg_est_reconnu_quelle_que_soit_son_extension() =>
        // Les « .jfif » de Windows étaient refusés sur leur extension alors que ce sont des JPEG
        Assert.Equal(".jpg", GPOE26.ApiService.Services.ImageFormat.ExtensionOf(Header(0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, "JFIF")));

    [Theory]
    [InlineData(".png")]
    [InlineData(".gif")]
    [InlineData(".webp")]
    [InlineData(".avif")]
    public void Les_formats_du_web_sont_acceptes(string expected)
    {
        var header = expected switch
        {
            ".png" => Header(0x89, "PNG", 0x0D, 0x0A, 0x1A, 0x0A),
            ".gif" => Header("GIF89a"),
            ".webp" => Header("RIFF", 0, 0, 0, 0, "WEBP"),
            _ => Header(0, 0, 0, 0x20, "ftypavif"),
        };
        Assert.Equal(expected, GPOE26.ApiService.Services.ImageFormat.ExtensionOf(header));
    }

    [Fact]
    public void Une_photo_iphone_est_refusee_avec_une_consigne()
    {
        var heic = Header(0, 0, 0, 0x18, "ftypheic");
        Assert.Null(GPOE26.ApiService.Services.ImageFormat.ExtensionOf(heic));
        Assert.Contains("HEIC", GPOE26.ApiService.Services.ImageFormat.RejectionMessage(heic));
    }

    [Fact]
    public void Un_document_renomme_en_image_est_refuse() =>
        Assert.Null(GPOE26.ApiService.Services.ImageFormat.ExtensionOf(Header("%PDF-1.7 abcd")));

    [Fact]
    public void Un_fichier_vide_est_refuse() =>
        Assert.Null(GPOE26.ApiService.Services.ImageFormat.ExtensionOf([]));
}
