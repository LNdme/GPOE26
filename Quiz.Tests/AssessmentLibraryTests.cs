using Quiz.Model;
using Quiz.Service;

namespace Quiz.Tests;

/// <summary>Bibliothèque d'établissement côté Quiz (docs/PLAN-ETABLISSEMENT-2.md, lot 8).</summary>
public class AssessmentLibraryTests
{
    private readonly Guid _author = Guid.NewGuid();
    private readonly Guid _colleague = Guid.NewGuid();
    private readonly Guid _school = Guid.NewGuid();

    private LibrarySpaceRef Space(params Guid[] teachers) =>
        new(_school, "Lycée", true, teachers.Select(t => new LibraryAuthorRef(t, "prof")).ToList());

    private Assessment Deposited()
    {
        var a = new Assessment
        {
            Title = "Devoir sur les suites",
            TeacherId = _author,
            Kind = AssessmentKind.GradedExam,
            Status = AssessmentStatus.Published,
            Subject = "Mathématiques",
            CourseId = Guid.NewGuid(),
            ClassroomIds = [Guid.NewGuid()],
            LibraryOrganizationIds = [_school],
            OpensAt = DateTime.UtcNow,
            DueAt = DateTime.UtcNow.AddDays(3),
            PublishedAt = DateTime.UtcNow,
            DurationMinutes = 60,
            ScaleTo = 20,
        };
        a.Questions =
        [
            new() { AssessmentId = a.Id, Order = 1, Type = QuestionType.SingleChoice, Text = "u1 = ?", Points = 2,
                Options = ["1", "2"], CorrectOptions = [1], CourseId = a.CourseId, SectionId = Guid.NewGuid() },
            new() { AssessmentId = a.Id, Order = 0, Type = QuestionType.Numeric, Text = "Limite ?", NumericAnswer = 0, Tolerance = 0.1 },
        ];
        return a;
    }

    [Fact]
    public void Un_depot_disparait_quand_son_auteur_quitte_l_etablissement()
    {
        Assert.True(AssessmentLibrary.IsVisibleIn(Deposited(), Space(_author)));
        Assert.False(AssessmentLibrary.IsVisibleIn(Deposited(), Space(_colleague)));
    }

    [Fact]
    public void La_copie_est_un_brouillon_au_copieur_sans_classe_ni_date_ni_lien_de_cours()
    {
        var source = Deposited();

        var copy = AssessmentLibrary.Copy(source, _colleague);

        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal(_colleague, copy.TeacherId);
        Assert.Equal(AssessmentStatus.Draft, copy.Status);
        Assert.Empty(copy.ClassroomIds);
        Assert.Empty(copy.LibraryOrganizationIds);
        Assert.Null(copy.OpensAt);
        Assert.Null(copy.DueAt);
        Assert.Null(copy.PublishedAt);
        Assert.Null(copy.CourseId);
        Assert.Equal(source.Id, copy.SourceAssessmentId);
        Assert.Equal((source.Kind, source.DurationMinutes, source.ScaleTo), (copy.Kind, copy.DurationMinutes, copy.ScaleTo));
    }

    [Fact]
    public void Les_questions_sont_recopiees_et_independantes()
    {
        var source = Deposited();

        var copy = AssessmentLibrary.Copy(source, _colleague);

        Assert.Equal(["Limite ?", "u1 = ?"], copy.Questions.Select(q => q.Text));
        Assert.All(copy.Questions, q => Assert.Equal(copy.Id, q.AssessmentId));
        Assert.All(copy.Questions, q => Assert.Null(q.CourseId));
        Assert.All(copy.Questions, q => Assert.Null(q.SectionId));
        Assert.Equal(source.MaxScore, copy.MaxScore);

        // Modifier la copie ne touche pas l'original (P2)
        copy.Questions[1].Options.Add("3");
        copy.Questions[1].CorrectOptions[0] = 0;
        var original = source.Questions.Single(q => q.Text == "u1 = ?");
        Assert.Equal(["1", "2"], original.Options);
        Assert.Equal([1], original.CorrectOptions);
    }
}
