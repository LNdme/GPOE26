using Chat.Service;

namespace GPOE26.Logic.Tests;

/// <summary>D1 : le profil rempli à l'inscription arrive enfin aux agents.</summary>
public class LearnerProfileTests
{
    private static readonly DateOnly Today = new(2026, 11, 10);

    private static LearnerProfileRef Student(string? subSystem = "Francophone", string? language = "fr", DateOnly? exam = null) =>
        new("Student", "Terminale C", null, "Sciences", language, subSystem, "Baccalauréat", exam, ["Mathématiques", "Physique"], 45,
            null, null, null, "Secondary");

    [Fact]
    public void Le_profil_complet_est_transmis()
    {
        var lines = AgentPrompts.LearnerLines(Student(exam: new DateOnly(2027, 6, 12)), Today);
        Assert.Contains("Classe ou niveau : Terminale C, sous-système francophone", lines);
        Assert.Contains("Filière : Sciences", lines);
        Assert.Contains("Matières à renforcer : Mathématiques, Physique", lines);
        Assert.Contains("Temps d'étude quotidien visé : 45 min", lines);
        Assert.Contains(lines, l => l.StartsWith("Examen visé : Baccalauréat, le 12 juin 2027"));
    }

    [Fact]
    public void A_l_approche_de_l_examen_le_tuteur_passe_en_revisions() =>
        Assert.Contains(AgentPrompts.LearnerLines(Student(exam: Today.AddDays(20)), Today), l => l.Contains("dans 20 jour(s) : période de révisions"));

    [Theory]
    [InlineData("Anglophone", "fr", true)]
    [InlineData("Francophone", "en", true)]
    [InlineData("Francophone", "fr", false)]
    [InlineData(null, null, false)]
    public void Un_apprenant_anglophone_recoit_des_reponses_en_anglais(string? subSystem, string? language, bool english) =>
        Assert.Equal(english, AgentPrompts.LanguageRule(Student(subSystem, language)).StartsWith("Réponds en anglais"));

    [Fact]
    public void Sans_profil_rien_n_est_invente() => Assert.Empty(AgentPrompts.LearnerLines(null, Today));

    [Fact]
    public void L_assistant_connait_le_cadre_de_l_enseignant()
    {
        var teacher = new LearnerProfileRef("Teacher", null, "Physique", null, "fr", null, null, null, null, null,
            ["Physique", "Chimie"], "Institution", "Centre de formation Akwa", null);
        var lines = AgentPrompts.TeacherLines(teacher);
        Assert.Contains("Matières enseignées : Physique, Chimie", lines);
        Assert.Contains("Forme dans une institution (entreprise, centre de formation…) : Centre de formation Akwa", lines);
    }
}

/// <summary>D2 : un long cours n'est plus coupé sans prévenir.</summary>
public class CourseSelectionTests
{
    private static string LongCourse()
    {
        var filler = string.Join(" ", Enumerable.Repeat("Développement détaillé avec exemples et remarques.", 400));
        return $"## Cinématique\n{filler}\n\n## Dynamique\n{filler}\n\n## Chute libre\nUn corps en chute libre : h = ½ g t², v = g t.\n{filler}\n\n## Énergie\n{filler}";
    }

    [Fact]
    public void Un_cours_court_est_donne_en_entier()
    {
        var excerpt = AgentPrompts.SelectCourse("## A\ntexte", "question", 1000);
        Assert.Equal("## A\ntexte", excerpt.Text);
        Assert.Null(excerpt.Note);
    }

    [Fact]
    public void Un_long_cours_garde_le_plan_et_la_partie_utile()
    {
        var course = LongCourse();
        var excerpt = AgentPrompts.SelectCourse(course, "Comment calculer la hauteur d'une chute libre ?", 50_000);

        Assert.True(course.Length > 50_000);
        Assert.True(excerpt.Text.Length <= 50_000);
        Assert.Contains("Plan complet du cours :\n- Cinématique\n- Dynamique\n- Chute libre\n- Énergie", excerpt.Text);
        Assert.Contains("h = ½ g t²", excerpt.Text);
        Assert.NotNull(excerpt.Note);
        Assert.Contains("passage(s) sur", excerpt.Note);
    }

    [Fact]
    public void Si_chaque_partie_est_trop_longue_le_debut_de_la_plus_utile_est_donne()
    {
        // Chaque partie dépasse à elle seule la place disponible : avant, la première (Cinématique) était donnée
        var huge = string.Join(" ", Enumerable.Repeat("Développement détaillé.", 3000));
        var course = $"## Cinématique\n{huge}\n\n## Chute libre\nh = ½ g t² et v = g t.\n{huge}\n\n## Énergie\n{huge}";
        var excerpt = AgentPrompts.SelectCourse(course, "Comment calculer la hauteur d'une chute libre ?", 40_000);

        Assert.True(excerpt.Text.Length <= 40_000);
        Assert.Contains("## Chute libre\nh = ½ g t²", excerpt.Text);
        Assert.DoesNotContain("## Cinématique\n", excerpt.Text);
    }

    [Fact]
    public void Un_long_texte_sans_titre_est_coupe_avec_une_note()
    {
        var excerpt = AgentPrompts.SelectCourse(new string('x', 2000), "question", 1000);
        Assert.StartsWith("### Cours\n" + new string('x', 500), excerpt.Text);
        Assert.True(excerpt.Text.Length <= 1000);
        Assert.Contains("1 passage(s) sur 2", excerpt.Note);
    }

    [Fact]
    public void Les_mots_courants_et_les_accents_sont_ignores() =>
        Assert.Equal(new HashSet<string> { "calculer", "hauteur", "chute", "libre", "energie" },
            AgentPrompts.Significant("Comment calculer la hauteur ? Chute libre, énergie, de, la"));
}

/// <summary>D5, D8 : mémoire fiable et consolidée par lots.</summary>
public class MemoryBatchTests
{
    [Fact]
    public void Des_notions_fragiles_vides_effacent_les_anciennes()
    {
        var cleared = MemoryRules.Parse("""{"add":[],"update":[],"forget":[],"summary":"s","weakPoints":""}""", new HashSet<Guid>());
        Assert.Equal("", cleared!.WeakPoints);

        var unknown = MemoryRules.Parse("""{"add":[],"summary":"s"}""", new HashSet<Guid>());
        Assert.Null(unknown!.WeakPoints);
    }

    [Theory]
    [InlineData(0, 60, false)]   // rien de nouveau
    [InlineData(1, 2, false)]    // une question, discussion encore active
    [InlineData(1, 10, true)]    // une question, discussion au repos depuis 10 min
    [InlineData(4, 0, true)]     // un lot complet
    public void La_memoire_relit_par_lots_ou_au_repos(int pending, int minutesSinceLast, bool due)
    {
        var now = new DateTime(2026, 11, 10, 20, 0, 0, DateTimeKind.Utc);
        Assert.Equal(due, ReflectionPolicy.IsDue(pending, now.AddMinutes(-minutesSinceLast), now, batchSize: 4, quiet: TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void Les_echanges_sont_reconstitues_question_par_reponse()
    {
        var exchanges = ReflectionPolicy.Pair(
        [
            ("assistant", "réponse sans question (ignorée)", false),
            ("user", "Q1", false), ("assistant", "R1", false),
            ("user", "Q2", true), ("assistant", "R2", false),
            ("user", "Q3 sans réponse", false)
        ]);
        Assert.Equal([new ReflectionExchange("Q1", "R1"), new ReflectionExchange("Q2\n[photo de copie jointe]", "R2")], exchanges);
    }
}
