using GPOE26.UI.Services;
using Band = GPOE26.UI.Models.LevelBand;
using Goal = GPOE26.UI.Models.LearningGoal;
using Stage = GPOE26.UI.Models.EducationStage;

namespace GPOE26.Logic.Tests;

/// <summary>
/// Inscription par objectif (docs/PLAN-PUBLICS.md, lot P2) : un seul parcours pour toute personne qui apprend,
/// les questions d'examen seulement pour qui en prépare un.
/// </summary>
public class OnboardingRulesTests
{
    [Theory]
    [InlineData(OnboardingRules.Secondary, Stage.Secondary)]
    [InlineData(OnboardingRules.Higher, Stage.Higher)]
    public void Le_niveau_donne_le_cycle(string level, Stage expected) =>
        Assert.Equal(expected, OnboardingRules.Stage(level));

    [Fact]
    public void Un_adulte_n_a_pas_de_cycle_scolaire() =>
        Assert.Null(OnboardingRules.Stage(OnboardingRules.Adult));

    [Theory]
    [InlineData("Exam", true)]
    [InlineData("Skill", false)]
    [InlineData("Curiosity", false)]
    [InlineData(null, false)]
    public void Seul_l_objectif_examen_fait_demander_l_examen(string? goal, bool expected) =>
        Assert.Equal(expected, OnboardingRules.AsksExam(goal));

    [Fact]
    public void L_examen_choisi_est_garde_pour_qui_en_prepare_un() =>
        Assert.Equal("Baccalauréat", OnboardingRules.ExamTarget("Exam", " Baccalauréat "));

    [Fact]
    public void Pas_d_examen_cette_annee_n_est_pas_un_examen() =>
        Assert.Null(OnboardingRules.ExamTarget("Exam", "Pas d'examen cette année"));

    [Fact]
    public void Un_examen_reste_d_un_objectif_abandonne_n_est_pas_envoye() =>
        // La personne avait choisi « examen » puis « curiosité » : l'examen saisi entre-temps ne doit pas partir
        Assert.Null(OnboardingRules.ExamTarget("Curiosity", "Baccalauréat"));

    [Theory]
    [InlineData(OnboardingRules.Adult, Band.Adult)]
    [InlineData(OnboardingRules.Higher, Band.Higher)]
    public void La_tranche_de_niveau_suit_le_niveau(string level, Band expected) =>
        Assert.Equal(expected, OnboardingRules.Band(level, chosenAtSchool: Band.UpperSecondary));

    [Fact]
    public void Au_secondaire_la_tranche_est_celle_choisie_selon_le_pays() =>
        Assert.Equal(Band.LowerSecondary, OnboardingRules.Band(OnboardingRules.Secondary, Band.LowerSecondary));

    [Fact]
    public void L_objectif_se_lit_depuis_sa_cle() =>
        Assert.Equal(Goal.Skill, OnboardingRules.Goal("Skill"));

    [Fact]
    public void Changer_de_niveau_efface_les_reponses_qui_en_dependent()
    {
        // Une classe de lycée ne doit pas rester enregistrée pour une personne passée à « vie active »
        foreach (var key in new[] { "classe", "serie", "exam", "cycle", "filiere", "examtxt", "themes", "temps" })
            Assert.Contains(key, OnboardingRules.LevelAnswers);
    }
}
