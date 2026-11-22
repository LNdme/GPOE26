using Quiz.Service;

namespace Quiz.Tests;

/// <summary>Brouillon d'une copie : enregistré pendant la composition, relu à la reprise de la tentative.</summary>
public class AnswerDraftsTests
{
    [Fact]
    public void Le_brouillon_relu_est_celui_qui_a_ete_enregistre()
    {
        var q1 = Guid.NewGuid();
        var q2 = Guid.NewGuid();
        var json = AnswerDrafts.Serialize(
        [
            new AnswerInput(q1, SelectedOptions: [2, 0]),
            new AnswerInput(q2, TrueFalse: false, Text: "v = 9,8 × t")
        ]);

        var restored = AnswerDrafts.Parse(json);

        Assert.Equal(2, restored.Count);
        Assert.Equal([2, 0], restored.Single(a => a.QuestionId == q1).SelectedOptions);
        Assert.False(restored.Single(a => a.QuestionId == q2).TrueFalse);
        Assert.Equal("v = 9,8 × t", restored.Single(a => a.QuestionId == q2).Text);
    }

    [Fact]
    public void Une_question_repetee_garde_sa_derniere_reponse()
    {
        var q = Guid.NewGuid();
        var draft = AnswerDrafts.Normalize([new AnswerInput(q, Text: "ancienne"), new AnswerInput(q, Text: "nouvelle")]);
        Assert.Equal("nouvelle", Assert.Single(draft).Text);
    }

    [Fact]
    public void Un_texte_demesure_est_tronque()
    {
        var draft = AnswerDrafts.Normalize([new AnswerInput(Guid.NewGuid(), Text: new string('a', AnswerDrafts.MaxTextLength + 500))]);
        Assert.Equal(AnswerDrafts.MaxTextLength, Assert.Single(draft).Text!.Length);
    }

    [Fact]
    public void Le_nombre_de_reponses_est_borne()
    {
        var many = Enumerable.Range(0, AnswerDrafts.MaxAnswers + 50).Select(_ => new AnswerInput(Guid.NewGuid(), Text: "x"));
        Assert.Equal(AnswerDrafts.MaxAnswers, AnswerDrafts.Normalize(many).Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ceci n'est pas du JSON")]
    [InlineData("{\"pas\":\"une liste\"}")]
    public void Un_brouillon_absent_ou_illisible_donne_une_copie_vide(string? json) =>
        Assert.Empty(AnswerDrafts.Parse(json));

    [Fact]
    public void Une_reponse_sans_question_est_ignoree() =>
        Assert.Empty(AnswerDrafts.Normalize([new AnswerInput(Guid.Empty, Text: "x")]));
}
