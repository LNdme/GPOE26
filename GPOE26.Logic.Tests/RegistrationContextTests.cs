using User.Model;

namespace GPOE26.Logic.Tests;

/// <summary>Lot B5 : l'inscription enregistre le pays, le fuseau du navigateur et, pour un apprenant, tranche et objectif.</summary>
public class RegistrationContextTests
{
    private static AppUser New(UserRole role, EducationStage? stage = null) =>
        new() { Username = "x", PasswordHash = "x", Role = role, Stage = stage };

    [Fact]
    public void Eleve_au_mexique_avec_le_fuseau_du_navigateur()
    {
        var user = New(UserRole.Student, EducationStage.Secondary);
        LearningContextRules.ApplyAtRegistration(user, "mx", "America/Monterrey", null, LevelBand.LowerSecondary, null);

        Assert.Equal("MX", user.Country);
        Assert.Equal("America/Monterrey", user.TimeZone);
        Assert.Equal(LevelBand.LowerSecondary, user.LevelBand);
        Assert.Equal(EducationStage.Secondary, user.Stage);
        Assert.False(LearningContextRules.UsesCameroonSubSystems(user));
    }

    [Fact]
    public void Une_valeur_invalide_est_ignoree_sans_faire_echouer_l_inscription()
    {
        var user = New(UserRole.Student);
        LearningContextRules.ApplyAtRegistration(user, "Mexique", "Mars/Olympus", new string('x', 101), null, null);
        Assert.Null(user.Country);
        Assert.Null(user.TimeZone);
        Assert.Null(user.EducationSystem);

        // Sans fuseau du navigateur, celui du pays est déduit à la lecture
        LearningContextRules.ApplyAtRegistration(user, "FR", null, null, null, null);
        Assert.Equal("Europe/Paris", LearningContextRules.Resolve(user).TimeZone);
    }

    [Fact]
    public void Adulte_qui_apprend_seul()
    {
        var user = New(UserRole.Student, EducationStage.Secondary);   // le serveur met « secondaire » par défaut à un élève
        LearningContextRules.ApplyAtRegistration(user, "ZZ", "Europe/Madrid", null, LevelBand.Adult, LearningGoal.Skill);

        Assert.Null(user.Stage);
        Assert.Equal(LevelBand.Adult, user.LevelBand);
        Assert.Equal(LearningGoal.Skill, user.LearningGoal);
        Assert.Equal("ZZ", LearningContextRules.Resolve(user).Country);
    }

    [Fact]
    public void Parent_et_enseignant_n_ont_ni_tranche_ni_objectif()
    {
        var parent = New(UserRole.Parent);
        LearningContextRules.ApplyAtRegistration(parent, "SN", "Africa/Dakar", "Système sénégalais", LevelBand.Primary, LearningGoal.Exam);
        Assert.Equal("SN", parent.Country);
        Assert.Equal("Africa/Dakar", parent.TimeZone);
        Assert.Null(parent.EducationSystem);
        Assert.Null(parent.LevelBand);
        Assert.Null(parent.LearningGoal);

        var teacher = New(UserRole.Teacher);
        LearningContextRules.ApplyAtRegistration(teacher, "FR", null, "Éducation nationale", LevelBand.Higher, LearningGoal.Exam);
        Assert.Equal("Éducation nationale", teacher.EducationSystem);
        Assert.Null(teacher.LevelBand);
        Assert.Null(teacher.LearningGoal);
    }

    [Theory]
    [InlineData(null, true)]   // comptes d'avant B5 : parcours camerounais inchangé
    [InlineData("CM", true)]
    [InlineData("FR", false)]
    [InlineData("ZZ", false)]
    public void Sous_systemes_camerounais_seulement_au_cameroun(string? country, bool expected)
    {
        var teacher = New(UserRole.Teacher);
        teacher.Country = country;
        Assert.Equal(expected, LearningContextRules.UsesCameroonSubSystems(teacher));
    }
}
