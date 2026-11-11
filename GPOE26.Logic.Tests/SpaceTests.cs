using User.Service;

namespace GPOE26.Logic.Tests;

/// <summary>Règles des espaces d'établissement (docs/PLAN-ETABLISSEMENT.md, lot 1).</summary>
public class SpaceRulesTests
{
    [Theory]
    [InlineData(2026, 11, 11, "2026-2027")]
    [InlineData(2026, 8, 1, "2026-2027")]   // rentrée : nouvelle année
    [InlineData(2026, 7, 31, "2025-2026")]  // dernier jour de l'année précédente
    [InlineData(2027, 1, 15, "2026-2027")]
    public void L_annee_scolaire_va_d_aout_a_juillet(int year, int month, int day, string expected)
    {
        var (label, startsOn, endsOn) = SpaceRules.SchoolYearOf(new DateOnly(year, month, day));

        Assert.Equal(expected, label);
        Assert.Equal(1, startsOn.Day);
        Assert.Equal(8, startsOn.Month);
        Assert.Equal(new DateOnly(startsOn.Year + 1, 7, 31), endsOn);
        Assert.InRange(new DateOnly(year, month, day), startsOn, endsOn);
    }

    [Theory]
    [InlineData(2026, 7, "2026-0007")]
    [InlineData(2026, 9999, "2026-9999")]
    [InlineData(2026, 12345, "2026-12345")] // jamais tronqué : le matricule reste unique
    public void Le_matricule_genere_est_annee_tiret_numero(int year, int number, string expected) =>
        Assert.Equal(expected, SpaceRules.Matricule(year, number));

    [Fact]
    public void Des_numeros_differents_donnent_des_matricules_differents()
    {
        var matricules = Enumerable.Range(1, 20_000).Select(n => SpaceRules.Matricule(2026, n)).ToList();
        Assert.Equal(matricules.Count, matricules.Distinct().Count());
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    [InlineData(" Mathématiques ", "Mathématiques")]
    public void La_matiere_vide_veut_dire_toutes_matieres(string? subject, string expected) =>
        Assert.Equal(expected, ClassroomService.SubjectKey(subject));

    [Fact]
    public void La_matiere_est_coupee_a_80_caracteres() =>
        Assert.Equal(80, ClassroomService.SubjectKey(new string('x', 120)).Length);

    [Fact]
    public void Une_annee_saisie_donne_ses_dates()
    {
        Assert.True(SpaceRules.TryParseSchoolYear(" 2027-2028 ", out var year));
        Assert.Equal("2027-2028", year.Label);
        Assert.Equal(new DateOnly(2027, 8, 1), year.StartsOn);
        Assert.Equal(new DateOnly(2028, 7, 31), year.EndsOn);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2027")]
    [InlineData("2027-2029")]   // pas deux années qui se suivent
    [InlineData("2028-2027")]
    [InlineData("27-28")]
    [InlineData("1899-1900")]
    [InlineData("2027-2028-2029")]
    public void Une_annee_mal_ecrite_est_refusee(string? label) =>
        Assert.False(SpaceRules.TryParseSchoolYear(label, out _));
}

/// <summary>Plusieurs enseignants par classe : Cours ne laisse publier que ceux qui y enseignent (lot 2).</summary>
public class MultiTeacherSharingTests
{
    private readonly Guid _me = Guid.NewGuid();
    private readonly Guid _main = Guid.NewGuid();

    [Fact]
    public void Un_co_enseignant_peut_publier_dans_la_classe()
    {
        var classroom = new Cours.Service.ClassroomRef(Guid.NewGuid(), _main, "Teacher");
        Assert.Equal([classroom.Id], Cours.Service.UserDirectoryClient.OwnedBy([classroom], _me));
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("Manager")]   // le gestionnaire voit la classe, mais n'y enseigne pas
    public void Un_eleve_ou_un_gestionnaire_ne_publie_pas(string role)
    {
        var classroom = new Cours.Service.ClassroomRef(Guid.NewGuid(), _me, role);
        Assert.Empty(Cours.Service.UserDirectoryClient.OwnedBy([classroom], _me));
    }

    [Fact]
    public void Sans_MyRole_le_professeur_principal_reste_le_proprietaire()
    {
        var classroom = new Cours.Service.ClassroomRef(Guid.NewGuid(), _me);
        Assert.Equal([classroom.Id], Cours.Service.UserDirectoryClient.OwnedBy([classroom], _me));
        Assert.Empty(Cours.Service.UserDirectoryClient.OwnedBy([classroom], _main));
    }
}
