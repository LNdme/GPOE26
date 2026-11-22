using Quiz.Model;
using Quiz.Service;

namespace Quiz.Tests;

/// <summary>Lot J1 (docs/PLAN-APPRENANT-AU-CENTRE.md) : tout compte apprend, mais l'auteur ne compose pas sa propre évaluation.</summary>
public class AssignmentRulesTests
{
    private static readonly Guid Auteur = Guid.NewGuid();
    private static readonly Guid Collegue = Guid.NewGuid();

    [Fact]
    public void Un_enseignant_qui_apprend_compose_les_evaluations_des_autres()
    {
        var deCollegue = new Assessment { TeacherId = Collegue, Title = "Devoir de maths" };
        Assert.Single(AssignmentRules.ForLearner([deCollegue], Auteur));
    }

    [Fact]
    public void L_auteur_ne_compose_jamais_sa_propre_evaluation()
    {
        // Sinon une seule copie bloquerait la modification et la suppression de l'évaluation
        var sienne = new Assessment { TeacherId = Auteur, Title = "Ma propre épreuve" };
        var autre = new Assessment { TeacherId = Collegue, Title = "Épreuve d'un collègue" };

        var visibles = AssignmentRules.ForLearner([sienne, autre], Auteur);

        Assert.Equal(new[] { autre }, visibles);
    }

    [Fact]
    public void Sans_evaluation_la_liste_reste_vide() =>
        Assert.Empty(AssignmentRules.ForLearner([], Auteur));

    // ── Lot J4 : une évaluation compte pour une période passée si elle la chevauche, ou s'il y a une copie ──

    private static readonly DateTime Rentree = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly PeriodRef Trimestre = new(Rentree, Rentree.AddMonths(3));

    private static Assessment Publiee(DateTime publiee, DateTime? echeance = null) => new()
    {
        TeacherId = Collegue, Title = "Devoir", Status = AssessmentStatus.Published, PublishedAt = publiee, DueAt = echeance
    };

    [Fact]
    public void Une_evaluation_publiee_pendant_la_periode_compte() =>
        Assert.True(AssignmentRules.CountsFor(Publiee(Rentree.AddDays(10), Rentree.AddDays(20)), [Trimestre], hasSubmission: false));

    [Fact]
    public void Une_evaluation_publiee_apres_le_depart_est_hors_periode() =>
        Assert.False(AssignmentRules.CountsFor(Publiee(Rentree.AddMonths(4)), [Trimestre], hasSubmission: false));

    [Fact]
    public void Une_evaluation_terminee_avant_l_arrivee_est_hors_periode() =>
        Assert.False(AssignmentRules.CountsFor(Publiee(Rentree.AddMonths(-2), Rentree.AddMonths(-1)), [Trimestre], hasSubmission: false));

    [Fact]
    public void Une_evaluation_encore_ouverte_a_l_arrivee_compte()
    {
        // H2 : un élève arrivé en cours d'année garde les évaluations encore ouvertes (pas de régression)
        var ouverte = Publiee(Rentree.AddMonths(-1), echeance: null);
        Assert.True(AssignmentRules.CountsFor(ouverte, [Trimestre], hasSubmission: false));
    }

    [Fact]
    public void Une_copie_rendue_compte_toujours() =>
        Assert.True(AssignmentRules.CountsFor(Publiee(Rentree.AddMonths(6)), [Trimestre], hasSubmission: true));

    [Fact]
    public void Une_evaluation_cloturee_sans_echeance_se_termine_a_sa_cloture()
    {
        var close = new Assessment
        {
            TeacherId = Collegue, Title = "Quiz", Status = AssessmentStatus.Closed,
            PublishedAt = Rentree.AddMonths(-3), UpdatedAt = Rentree.AddMonths(-2)
        };
        Assert.False(AssignmentRules.CountsFor(close, [Trimestre], hasSubmission: false));
    }

    [Fact]
    public void Deux_periodes_avec_une_absence_entre_elles()
    {
        // A → B → A : l'évaluation publiée pendant l'absence ne compte pas
        var retour = new PeriodRef(Rentree.AddMonths(5), null);
        var pendantAbsence = Publiee(Rentree.AddMonths(4), Rentree.AddMonths(4).AddDays(7));
        Assert.False(AssignmentRules.CountsFor(pendantAbsence, [Trimestre, retour], hasSubmission: false));
        Assert.True(AssignmentRules.CountsFor(Publiee(Rentree.AddMonths(6)), [Trimestre, retour], hasSubmission: false));
    }
}
