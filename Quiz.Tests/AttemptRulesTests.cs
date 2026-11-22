using Quiz.Model;
using Quiz.Service;

namespace Quiz.Tests;

/// <summary>A8 : l'heure limite d'une tentative est imposée par le serveur.</summary>
public class AttemptRulesTests
{
    private static readonly DateTime Start = new(2026, 11, 11, 8, 0, 0, DateTimeKind.Utc);

    private static Assessment Exam(DateTime? due = null, int? minutes = null, AssessmentStatus status = AssessmentStatus.Published) =>
        new() { Title = "Devoir", DueAt = due, DurationMinutes = minutes, Status = status };

    [Fact]
    public void L_heure_limite_est_la_plus_proche_de_l_echeance_et_de_la_duree()
    {
        Assert.Equal(Start.AddMinutes(60), AttemptRules.Deadline(Exam(due: Start.AddHours(3), minutes: 60), Start));
        Assert.Equal(Start.AddMinutes(30), AttemptRules.Deadline(Exam(due: Start.AddMinutes(30), minutes: 60), Start));
        Assert.Null(AttemptRules.Deadline(Exam(), Start));
    }

    [Fact]
    public void La_marge_de_deux_minutes_est_respectee()
    {
        var exam = Exam(minutes: 60);
        Assert.False(AttemptRules.IsOver(exam, Start, Start.AddMinutes(61)));
        Assert.True(AttemptRules.IsOver(exam, Start, Start.AddMinutes(62)));
    }

    [Fact]
    public void Sans_echeance_ni_duree_une_tentative_n_expire_pas() =>
        Assert.False(AttemptRules.IsOver(Exam(), Start, Start.AddYears(1)));

    [Fact]
    public void Une_evaluation_cloturee_n_accepte_plus_rien() =>
        Assert.True(AttemptRules.IsOver(Exam(status: AssessmentStatus.Closed), Start, Start));

    [Theory]
    [InlineData(61, false)]
    [InlineData(62, false)]
    [InlineData(63, true)]
    public void Une_copie_rendue_apres_la_marge_est_en_retard(int minutesAfterStart, bool late) =>
        Assert.Equal(late, AttemptRules.IsLate(Exam(minutes: 60), Start, Start.AddMinutes(minutesAfterStart)));
}
