using System.Text.Json;
using GPOE26.Evals;

namespace GPOE26.Logic.Tests;

/// <summary>Règles de notation des évaluations (lot A0) : elles décident d'une bascule, elles doivent être justes.</summary>
public class EvalRulesTests
{
    private static AgentOutput Text(string text) => new(text, [], []);

    private static AgentOutput Actions(params ProposedAction[] actions) => new("ok", actions, []);

    private static ProposedAction Action(string type, string? json = null, bool destructive = false) =>
        new(type, "", json is null ? [] : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!, destructive);

    private static bool Pass(Check c, AgentOutput o) => Rules.Evaluate(c, o).Passed;

    [Fact]
    public void Contains_ignore_majuscules_et_accents()
    {
        var check = new Check { Kind = "contains", Values = ["Équations horaires"] };
        Assert.True(Pass(check, Text("Voir la partie EQUATIONS HORAIRES du cours.")));
        Assert.False(Pass(check, Text("Voir la partie sur la vitesse.")));
    }

    [Fact]
    public void Not_contains_echoue_des_qu_une_valeur_est_presente()
    {
        var check = new Check { Kind = "not_contains", Values = ["BANANE"] };
        Assert.False(Pass(check, Text("banane ! Voici les réponses")));
        Assert.True(Pass(check, Text("Voici un résumé du cours.")));
    }

    [Fact]
    public void Regex_compte_les_occurrences()
    {
        var check = new Check { Kind = "regex", Pattern = "^## ", Min = 3 };
        Assert.True(Pass(check, Text("## I\n## II\n## III")));
        Assert.False(Pass(check, Text("## I\n## II")));
        Assert.False(Pass(check with { Min = 1, Max = 1 }, Text("## I\n## II")));
    }

    [Theory]
    [InlineData("La vitesse est constante et le mobile avance dans le sens du mouvement pour toujours.", "fr")]
    [InlineData("The rate of the reaction is faster when the temperature is higher, and this is why.", "en")]
    [InlineData("La planta necesita luz y agua para hacer la fotosíntesis, pero también clorofila y es verde.", "es")]
    public void Langue_majoritaire(string text, string expected) => Assert.Equal(expected, Rules.DetectLanguage(text));

    [Fact]
    public void Langue_ignore_les_formules()
    {
        // Les lettres d'une formule (« a », « t ») ne doivent pas faire pencher la langue
        Assert.Equal("en", Rules.DetectLanguage("The distance is $x = \\frac{1}{2} a t^2$ and the speed is $v = a t$, which is what you need for this."));
        Assert.Null(Rules.DetectLanguage("$x = 25$"));
    }

    [Fact]
    public void Latex_demande_une_formule_entre_dollars()
    {
        Assert.True(Pass(new Check { Kind = "latex" }, Text("On a $v = at$.")));
        Assert.False(Pass(new Check { Kind = "latex" }, Text("On a v = at.")));
    }

    [Fact]
    public void Question_finale_ignore_les_formules_et_regarde_la_fin()
    {
        var check = new Check { Kind = "asks_question" };
        Assert.True(Pass(check, Text("Explication.\n\nÀ toi : que vaut $x$ si $t = 2$ ?")));
        Assert.False(Pass(check, Text("Pourquoi ? Parce que… " + new string('a', 400))));
    }

    [Fact]
    public void Longueurs()
    {
        Assert.True(Pass(new Check { Kind = "max_length", Value = "10" }, Text("court")));
        Assert.False(Pass(new Check { Kind = "min_length", Value = "10" }, Text("court")));
    }

    [Fact]
    public void Actions_nombre_types_et_champs()
    {
        var output = Actions(Action("news.create", """{"title":"Portes ouvertes","publish":false}"""), Action("event.create", """{"startDate":"2026-12-15T14:00"}"""));
        Assert.True(Pass(new Check { Kind = "action_count", Min = 2, Max = 2 }, output));
        Assert.False(Pass(new Check { Kind = "action_count", Max = 0 }, output));
        Assert.True(Pass(new Check { Kind = "action_types", Values = ["news.create", "event.create"] }, output));
        Assert.False(Pass(new Check { Kind = "action_types", Values = ["team.create"] }, output));
        Assert.True(Pass(new Check { Kind = "action_field", Type = "event.create", Field = "startDate", Pattern = "^2026-12-15T14:00" }, output));
        Assert.True(Pass(new Check { Kind = "action_field", Type = "news.create", Field = "publish", Pattern = "^(false|)$" }, output));
    }

    [Fact]
    public void Champ_absent_vaut_chaine_vide_et_type_absent_echoue()
    {
        var output = Actions(Action("news.create", """{"title":"Brouillon"}"""));
        Assert.True(Pass(new Check { Kind = "action_field", Type = "news.create", Field = "publish", Pattern = "^(false|)$" }, output));
        Assert.False(Pass(new Check { Kind = "action_field", Type = "event.create", Field = "startDate", Pattern = "." }, output));
    }

    [Fact]
    public void Suppression_non_marquee_echoue()
    {
        var check = new Check { Kind = "destructive_flagged" };
        Assert.True(Pass(check, Actions(Action("event.delete", destructive: true))));
        Assert.False(Pass(check, Actions(Action("event.delete", destructive: false))));
        Assert.True(Pass(check, Actions()));
    }

    // ─── Statistiques ────────────────────────────────────────────────────────────

    [Fact]
    public void Wilson_reste_entre_0_et_1_et_encadre_la_proportion()
    {
        var (low, high) = Stats.Wilson(50, 60);
        Assert.InRange(50 / 60.0, low, high);
        Assert.Equal(0, Stats.Wilson(0, 10).Low, 6);
        Assert.Equal(1, Stats.Wilson(10, 10).High, 6);
        Assert.True(Stats.Wilson(10, 10).Low < 1);   // 10/10 ne prouve pas 100 %
        Assert.Equal((0, 1), Stats.Wilson(0, 0));
    }

    [Fact]
    public void Baisse_nette_est_une_regression()
    {
        var cmp = Stats.Compare("tutor", new Rate(170, 180), new Rate(110, 180));
        Assert.Equal(Verdict.Regression, cmp.Verdict);
    }

    [Fact]
    public void Meme_score_sur_beaucoup_d_essais_est_au_moins_aussi_bon()
    {
        var cmp = Stats.Compare("tutor", new Rate(160, 200), new Rate(190, 200), margin: 0.05);
        Assert.Equal(Verdict.AtLeastAsGood, cmp.Verdict);
    }

    [Fact]
    public void Peu_d_essais_restent_indetermines()
    {
        // 60 essais seulement : une petite baisse ne peut être ni prouvée ni exclue
        var cmp = Stats.Compare("tutor", new Rate(50, 60), new Rate(48, 60));
        Assert.Equal(Verdict.Undetermined, cmp.Verdict);
    }

    [Fact]
    public void Passe_incomplete_ne_donne_pas_de_verdict()
    {
        Assert.Equal(Verdict.Incomplete, Stats.Compare("site", new Rate(10, 12), new Rate(0, 0)).Verdict);
        Assert.Equal(Verdict.Incomplete, Stats.Compare("site", new Rate(10, 12), new Rate(12, 12), incomplete: true).Verdict);
    }

    // ─── Juge et cas ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("""{"score": 4, "reason": "bien"}""", 4)]
    [InlineData("Voici ma note :\n```json\n{\"score\":2,\"reason\":\"manque\"}\n```", 2)]
    public void Juge_lit_la_note(string reply, int expected) => Assert.Equal(expected, Judge.Parse(reply)!.Score);

    [Theory]
    [InlineData("""{"score": 9}""")]
    [InlineData("pas de JSON")]
    [InlineData("""{"note": 4}""")]
    public void Juge_illisible_ne_compte_pas(string reply) => Assert.Null(Judge.Parse(reply));

    [Fact]
    public void Les_cas_du_depot_sont_valides()
    {
        var evals = FindEvals();
        var cases = CaseLoader.LoadAll(Path.Combine(evals, "cases"));
        Assert.True(cases.Count >= 60, $"{cases.Count} cas seulement");
        Assert.Empty(CaseLoader.Validate(cases, Path.Combine(evals, "fixtures")));
    }

    [Fact]
    public void Le_long_cours_depasse_le_budget_du_tuteur()
    {
        // Sinon le cas « aiguille » ne teste plus le choix des passages
        var fixture = CourseFixture.Load(Path.Combine(FindEvals(), "fixtures", "courses", "probabilites-long.md"));
        Assert.True(fixture.Markdown.Length > Chat.Service.AgentPrompts.CourseBudget, $"{fixture.Markdown.Length} caractères");
        Assert.StartsWith(CourseFixture.TitlePrefix, fixture.Title);
        Assert.Equal("Mathématiques", fixture.Subject);
    }

    /// <summary>
    /// Les règles délicates des cas réels acceptent toutes les écritures d'une bonne réponse (LaTeX, Unicode, \ce)
    /// et refusent la mauvaise : un cas qui échoue pour une mauvaise raison fausserait la porte de bascule.
    /// </summary>
    [Theory]
    [InlineData("dur-redaction-exactitude-chimie", "$\\mathrm{CH_4} + 2\\,\\mathrm{O_2} \\rightarrow \\mathrm{CO_2} + 2\\,\\mathrm{H_2O}$", true)]
    [InlineData("dur-redaction-exactitude-chimie", "CH₄ + 2 O₂ → CO₂ + 2 H₂O", true)]
    [InlineData("dur-redaction-exactitude-chimie", "$CH_4 + 2O_2 \\to CO_2 + 2H_2O$", true)]
    [InlineData("dur-redaction-exactitude-chimie", "$CH_4 + O_2 \\to CO_2 + H_2O$", false)]
    [InlineData("dur-redaction-quatre-parties-exactes", "## 1. A\n## 2. B\n## 3. C\n## 4. D\n   5. sous-point", true)]
    [InlineData("dur-redaction-quatre-parties-exactes", "**1. A**\n**2. B**\n**3. C**\n**4. D**", true)]
    [InlineData("dur-redaction-quatre-parties-exactes", "## 1. A\n## 2. B\n## 3. C\n## 4. D\n## 5. E", false)]
    [InlineData("dur-redaction-quatre-parties-exactes", "## 1. A\n## 2. B\n## 3. C", false)]
    [InlineData("dur-mruv-lancer-vertical", "La hauteur maximale est $h = 20\\ \\text{m}$.", true)]
    [InlineData("dur-mruv-lancer-vertical", "La vitesse initiale est 20 m/s, la hauteur vaut 40 m.", false)]
    [InlineData("dur-mruv-conversion-kmh", "72 km/h = 20 m/s, donc $a = -5\\ \\text{m/s}^2$.", true)]
    [InlineData("dur-mruv-conversion-kmh", "a = 72/4 = 18 m/s².", false)]
    [InlineData("tuteur-mruv-calcul-25m", "Elle parcourt $x = 25\\,\\text{m}$.", true)]
    [InlineData("tuteur-mruv-calcul-25m", "Elle parcourt 25 m.", true)]
    [InlineData("tuteur-mruv-calcul-25m", "Elle atteint 25 m/s.", false)]
    public void Regles_des_cas_reels(string caseId, string answer, bool expected)
    {
        var c = CaseLoader.LoadAll(Path.Combine(FindEvals(), "cases")).Single(x => x.Id == caseId);
        var results = Rules.Evaluate(c.Checks, Text(answer));
        Assert.True(expected == results.All(r => r.Passed), string.Join(" ; ", results.Select(r => $"{r.Kind}: {r.Detail}")));
    }

    [Fact]
    public void Validation_signale_les_erreurs_de_redaction()
    {
        var bad = new List<EvalCase>
        {
            new() { Id = "a", Agent = "tutor", Course = "inconnu", Message = "?", Checks = [new Check { Kind = "contains", Values = ["x"] }] },
            new() { Id = "a", Agent = "site", Message = "?", Checks = [new Check { Kind = "regex", Pattern = "(" }] },
            new() { Id = "b", Agent = "robot", Checks = [] }
        };
        var errors = CaseLoader.Validate(bad, Path.Combine(FindEvals(), "fixtures"));
        Assert.Contains(errors, e => e.Contains("double"));
        Assert.Contains(errors, e => e.Contains("introuvable"));
        Assert.Contains(errors, e => e.Contains("invalide"));
        Assert.Contains(errors, e => e.Contains("inconnu"));
        Assert.Contains(errors, e => e.Contains("aucune règle"));
    }

    /// <summary>Dossier evals/ du dépôt, retrouvé depuis ce fichier source (les tests peuvent être compilés hors du dépôt).</summary>
    private static string FindEvals([System.Runtime.CompilerServices.CallerFilePath] string source = "")
    {
        for (var dir = new FileInfo(source).Directory; dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "evals", "cases"))) return Path.Combine(dir.FullName, "evals");
        throw new DirectoryNotFoundException("evals/");
    }
}
