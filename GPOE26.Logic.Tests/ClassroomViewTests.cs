using User.Model;
using User.Service;

namespace GPOE26.Logic.Tests;

/// <summary>GET /classrooms/mine?as=… suit l'appartenance réelle, plus le rôle du compte (docs/PLAN-APPRENANT-AU-CENTRE.md, lot J1).</summary>
public class ClassroomViewTests
{
    [Theory]
    [InlineData(null, ClassroomView.All)]
    [InlineData("", ClassroomView.All)]
    [InlineData("all", ClassroomView.All)]
    [InlineData("learner", ClassroomView.Learner)]
    [InlineData("Teacher", ClassroomView.Teacher)]
    [InlineData(" LEARNER ", ClassroomView.Learner)]
    public void Les_valeurs_connues_sont_lues(string? value, ClassroomView expected)
    {
        Assert.True(ClassroomViewRules.TryParse(value, out var view));
        Assert.Equal(expected, view);
    }

    [Theory]
    [InlineData("student")]
    [InlineData("parent")]
    [InlineData("1")]
    public void Une_valeur_inconnue_est_refusee(string value) =>
        Assert.False(ClassroomViewRules.TryParse(value, out _));

    [Fact]
    public void La_vue_apprenant_ne_renvoie_jamais_les_classes_enseignees()
    {
        // Sinon Quiz ferait composer à un enseignant les évaluations de sa propre classe
        Assert.False(ClassroomViewRules.WantsTaught(ClassroomView.Learner));
        Assert.True(ClassroomViewRules.WantsJoined(ClassroomView.Learner));
        Assert.False(ClassroomViewRules.WantsJoined(ClassroomView.Teacher));
        Assert.True(ClassroomViewRules.WantsTaught(ClassroomView.All) && ClassroomViewRules.WantsJoined(ClassroomView.All));
    }

    [Theory]
    [InlineData(ClassroomView.All, true, ClassroomRole.Teacher)]     // enseignée et rejointe : l'enseignement l'emporte
    [InlineData(ClassroomView.All, false, ClassroomRole.Student)]
    [InlineData(ClassroomView.Teacher, true, ClassroomRole.Teacher)]
    [InlineData(ClassroomView.Learner, true, ClassroomRole.Student)] // vue apprenant : on y reste apprenant
    [InlineData(ClassroomView.Learner, false, ClassroomRole.Student)]
    public void La_place_suit_la_vue_demandee(ClassroomView view, bool teaches, ClassroomRole expected) =>
        Assert.Equal(expected, ClassroomViewRules.RoleOf(view, teaches));
}
