using Quiz.Model;
using Quiz.Service;

namespace Quiz.Tests;

public class GraderTests
{
    private static AssessmentQuestion Q(QuestionType type, Action<AssessmentQuestion>? setup = null)
    {
        var q = new AssessmentQuestion { Type = type, Text = "Question", Points = 2 };
        setup?.Invoke(q);
        return q;
    }

    private static AnswerInput A(AssessmentQuestion q, List<int>? options = null, bool? tf = null, string? text = null, string? file = null) =>
        new(q.Id, options, tf, text, file);

    // ── Choix ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SingleChoice_bonne_option_donne_tous_les_points()
    {
        var q = Q(QuestionType.SingleChoice, q => { q.Options = ["24", "48", "96"]; q.CorrectOptions = [1]; });
        Assert.Equal(new GradeResult(true, 2, false), Grader.Grade(q, A(q, [1])));
        Assert.Equal(new GradeResult(false, 0, false), Grader.Grade(q, A(q, [0])));
    }

    [Fact]
    public void MultipleChoice_tout_ou_rien()
    {
        var q = Q(QuestionType.MultipleChoice, q => { q.Options = ["a", "b", "c", "d"]; q.CorrectOptions = [0, 2]; });
        Assert.True(Grader.Grade(q, A(q, [2, 0])).IsCorrect);
        Assert.True(Grader.Grade(q, A(q, [0, 2, 2])).IsCorrect);   // doublon ignoré
        Assert.False(Grader.Grade(q, A(q, [0])).IsCorrect);         // incomplet
        Assert.False(Grader.Grade(q, A(q, [0, 1, 2])).IsCorrect);   // une option fausse en trop
    }

    [Fact]
    public void Sans_reponse_vaut_zero()
    {
        var q = Q(QuestionType.SingleChoice, q => { q.Options = ["a", "b"]; q.CorrectOptions = [0]; });
        Assert.Equal(new GradeResult(false, 0, false), Grader.Grade(q, null));
        Assert.Equal(new GradeResult(false, 0, false), Grader.Grade(q, A(q, [])));
    }

    [Fact]
    public void VraiFaux()
    {
        var q = Q(QuestionType.TrueFalse, q => q.TrueFalseAnswer = false);
        Assert.True(Grader.Grade(q, A(q, tf: false)).IsCorrect);
        Assert.False(Grader.Grade(q, A(q, tf: true)).IsCorrect);
        Assert.False(Grader.Grade(q, A(q)).IsCorrect);
    }

    // ── Réponse courte ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("3")]
    [InlineData(" Trois ")]
    [InlineData("TROIS.")]
    [InlineData("trois")]
    public void ReponseCourte_ignore_casse_espaces_et_ponctuation(string answer)
    {
        var q = Q(QuestionType.ShortAnswer, q => q.AcceptedAnswers = ["3", "trois"]);
        Assert.True(Grader.Grade(q, A(q, text: answer)).IsCorrect);
    }

    [Fact]
    public void ReponseCourte_ignore_les_accents()
    {
        var q = Q(QuestionType.ShortAnswer, q => q.AcceptedAnswers = ["décroissante"]);
        Assert.True(Grader.Grade(q, A(q, text: "Decroissante")).IsCorrect);
        Assert.True(Grader.Grade(q, A(q, text: "  DÉCROISSANTE ")).IsCorrect);
        Assert.False(Grader.Grade(q, A(q, text: "croissante")).IsCorrect);
    }

    // ── Numérique ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1023", true)]
    [InlineData("1 023", true)]
    [InlineData("1 023,0", true)]
    [InlineData("1023.4", false)]
    [InlineData("mille", false)]
    [InlineData("", false)]
    public void Numerique_sans_tolerance(string answer, bool expected)
    {
        var q = Q(QuestionType.Numeric, q => q.NumericAnswer = 1023);
        Assert.Equal(expected, Grader.Grade(q, A(q, text: answer)).IsCorrect);
    }

    [Theory]
    [InlineData("9,8", true)]
    [InlineData("9.78 m/s²", true)]
    [InlineData("9.87", false)]
    public void Numerique_avec_tolerance_et_unite(string answer, bool expected)
    {
        var q = Q(QuestionType.Numeric, q => { q.NumericAnswer = 9.81; q.Tolerance = 0.05; q.Unit = "m/s²"; });
        Assert.Equal(expected, Grader.Grade(q, A(q, text: answer)).IsCorrect);
    }

    // ── Réponse ouverte ───────────────────────────────────────────────────────

    [Fact]
    public void ReponseOuverte_attend_l_enseignant()
    {
        var q = Q(QuestionType.OpenEnded, q => q.Criteria = "Factorisation (2 pts)");
        Assert.Equal(new GradeResult(null, null, true), Grader.Grade(q, A(q, text: "vₙ₊₁ = 0,5 vₙ")));
        Assert.Equal(new GradeResult(null, null, true), Grader.Grade(q, A(q, file: "/uploads/copie.jpg")));
    }

    [Fact]
    public void ReponseOuverte_vide_vaut_zero_sans_correction_manuelle()
    {
        var q = Q(QuestionType.OpenEnded);
        Assert.Equal(new GradeResult(false, 0, false), Grader.Grade(q, A(q, text: "   ")));
        Assert.Equal(new GradeResult(false, 0, false), Grader.Grade(q, null));
    }

    // ── Validation avant publication ──────────────────────────────────────────

    [Fact]
    public void Validation_signale_les_questions_incompletes()
    {
        var a = new Assessment
        {
            Title = "Épreuve",
            Questions =
            [
                Q(QuestionType.SingleChoice, q => { q.Order = 0; q.Options = ["a", "b"]; q.CorrectOptions = [0, 1]; }),
                Q(QuestionType.TrueFalse, q => q.Order = 1),
                Q(QuestionType.ShortAnswer, q => q.Order = 2),
                Q(QuestionType.Numeric, q => q.Order = 3),
                Q(QuestionType.MultipleChoice, q => { q.Order = 4; q.Options = ["a", ""]; q.CorrectOptions = [5]; }),
            ]
        };

        var errors = Grader.ValidateForPublish(a);

        Assert.Contains(errors, e => e.Contains("Aucune classe"));
        Assert.Contains(errors, e => e.StartsWith("Question 1") && e.Contains("exactement une"));
        Assert.Contains(errors, e => e.StartsWith("Question 2"));
        Assert.Contains(errors, e => e.StartsWith("Question 3"));
        Assert.Contains(errors, e => e.StartsWith("Question 4"));
        Assert.Contains(errors, e => e.StartsWith("Question 5") && e.Contains("deux options"));
        Assert.Contains(errors, e => e.StartsWith("Question 5") && e.Contains("inexistante"));
    }

    [Fact]
    public void Validation_accepte_une_evaluation_complete()
    {
        var a = new Assessment
        {
            Title = "Épreuve",
            ClassroomIds = [Guid.NewGuid()],
            Questions = [Q(QuestionType.TrueFalse, q => q.TrueFalseAnswer = true)]
        };
        Assert.Empty(Grader.ValidateForPublish(a));
    }

    // ── Mélange ───────────────────────────────────────────────────────────────

    [Fact]
    public void Melange_stable_pour_une_meme_tentative()
    {
        var items = Enumerable.Range(0, 10).ToList();
        var seed = Guid.NewGuid();
        var first = Grader.StableShuffle(items, seed);
        Assert.Equal(first, Grader.StableShuffle(items, seed));
        Assert.Equal(items, first.OrderBy(i => i));
    }

    // ── Proposition de l'IA ───────────────────────────────────────────────────

    [Fact]
    public void Suggestion_IA_bornee_et_arrondie_au_quart()
    {
        var s = AiGradingService.Parse("Voici : {\"points\": 4.9, \"feedback\": \"Bonne démarche.\"} merci", 6);
        Assert.Equal(5m, s.Points);
        Assert.Equal("Bonne démarche.", s.Feedback);

        Assert.Equal(6m, AiGradingService.Parse("{\"points\": 25, \"feedback\": \"\"}", 6).Points);
        Assert.Equal(0m, AiGradingService.Parse("{\"points\": -3, \"feedback\": \"\"}", 6).Points);
        Assert.Throws<FormatException>(() => AiGradingService.Parse("pas de json", 6));
    }
}
