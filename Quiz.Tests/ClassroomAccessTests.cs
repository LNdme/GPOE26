using System.Text.Json;
using Quiz.Service;

namespace Quiz.Tests;

/// <summary>Plusieurs enseignants par classe (docs/PLAN-ETABLISSEMENT.md, lot 2) : qui peut viser une classe ?</summary>
public class ClassroomAccessTests
{
    private readonly Guid _me = Guid.NewGuid();
    private readonly Guid _main = Guid.NewGuid();

    [Fact]
    public void Un_co_enseignant_peut_viser_la_classe()
    {
        var classroom = new ClassroomRef(Guid.NewGuid(), "Tle C", null, _main, "Teacher");
        Assert.Equal([classroom.Id], ClassroomRef.TaughtBy([classroom], _me));
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("Manager")]
    public void Un_eleve_ou_un_gestionnaire_ne_vise_pas_la_classe(string role)
    {
        var classroom = new ClassroomRef(Guid.NewGuid(), "Tle C", null, _me, role);
        Assert.Empty(ClassroomRef.TaughtBy([classroom], _me));
    }

    [Fact]
    public void Sans_MyRole_le_professeur_principal_reste_le_seul()
    {
        var classroom = new ClassroomRef(Guid.NewGuid(), "Tle C", "ABC-DEF", _me);
        Assert.Equal([classroom.Id], ClassroomRef.TaughtBy([classroom], _me));
        Assert.Empty(ClassroomRef.TaughtBy([classroom], _main));
    }

    [Fact]
    public void Le_role_envoye_en_texte_par_le_service_User_est_lu()
    {
        // Le service User écrit ses énumérations en texte (JsonStringEnumConverter)
        var json = $$"""[{"id":"{{Guid.NewGuid()}}","name":"Tle C","inviteCode":null,"teacherId":"{{_main}}","myRole":"Teacher","mySubjects":["Physique"]}]""";
        var classes = JsonSerializer.Deserialize<List<ClassroomRef>>(json, JsonDefaults.Options)!;
        Assert.Single(ClassroomRef.TaughtBy(classes, _me));
    }

    [Fact]
    public void Le_gestionnaire_voit_toutes_les_evaluations_un_enseignant_les_siennes()
    {
        Assert.True(ClassroomRef.SeesAllAssessments(new ClassroomRef(Guid.NewGuid(), "Tle C", null, _main, "Manager", IsManager: true)));
        // Enseignant qui gère aussi l'établissement : son rôle affiché reste « Teacher », mais il voit tout
        Assert.True(ClassroomRef.SeesAllAssessments(new ClassroomRef(Guid.NewGuid(), "Tle C", null, _main, "Teacher", IsManager: true)));
        Assert.False(ClassroomRef.SeesAllAssessments(new ClassroomRef(Guid.NewGuid(), "Tle C", null, _main, "Teacher")));
        Assert.False(ClassroomRef.SeesAllAssessments(null));
    }

    [Fact]
    public void Le_detail_de_classe_du_service_User_dit_s_il_gere()
    {
        var json = $$"""{"id":"{{Guid.NewGuid()}}","name":"Tle C","inviteCode":null,"teacherId":"{{_main}}","myRole":"Teacher","isManager":true}""";
        var classroom = JsonSerializer.Deserialize<ClassroomRef>(json, JsonDefaults.Options);
        Assert.True(ClassroomRef.SeesAllAssessments(classroom));
    }
}
