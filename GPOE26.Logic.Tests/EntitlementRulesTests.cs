using User.Model;
using User.Service;

namespace GPOE26.Logic.Tests;

/// <summary>Abonnements qui se chevauchent (docs/PLAN-ETABLISSEMENT-2.md, lot 11, décision P5).</summary>
public class EntitlementRulesTests
{
    private static readonly DateTime June = new(2027, 6, 30, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime July = new(2027, 7, 31, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime December = new(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    private static EntitlementSource Src(EntitlementSourceKind kind, DateTime until, string? label = null,
        SubscriptionPlan plan = SubscriptionPlan.StudentPremium) => new(kind, plan, until, label);

    [Fact]
    public void Sans_etablissement_pas_de_chevauchement()
    {
        Assert.Null(EntitlementRules.Overlap([Src(EntitlementSourceKind.Personal, June)]));
        Assert.Null(EntitlementRules.Overlap([]));
    }

    [Fact]
    public void Etablissement_seul_pas_de_chevauchement() =>
        Assert.Null(EntitlementRules.Overlap([Src(EntitlementSourceKind.School, July, "Lycée")]));

    [Fact]
    public void Abonnement_personnel_et_etablissement_se_chevauchent()
    {
        var overlap = EntitlementRules.Overlap([Src(EntitlementSourceKind.Personal, December), Src(EntitlementSourceKind.School, July, "Lycée de démonstration")]);

        Assert.Equal(new CoverageOverlap("Lycée de démonstration", July, EntitlementSourceKind.Personal, December, null), overlap);
    }

    [Fact]
    public void Formule_famille_nomme_le_parent_payeur()
    {
        var overlap = EntitlementRules.Overlap([Src(EntitlementSourceKind.Family, June, "Maman", SubscriptionPlan.Family), Src(EntitlementSourceKind.Teacher, July, "Élèves de M. Ndi")]);

        Assert.Equal(("Élèves de M. Ndi", EntitlementSourceKind.Family, "Maman"), (overlap!.CoveredBy, overlap.PaidKind, overlap.PaidBy));
    }

    [Fact]
    public void Un_essai_n_est_pas_paye() =>
        Assert.Null(EntitlementRules.Overlap([Src(EntitlementSourceKind.Trial, June), Src(EntitlementSourceKind.School, July, "Lycée")]));

    [Fact]
    public void Les_dates_les_plus_lointaines_sont_retenues()
    {
        var overlap = EntitlementRules.Overlap([
            Src(EntitlementSourceKind.School, December, "Ancien lycée"), Src(EntitlementSourceKind.School, July, "Lycée"),
            Src(EntitlementSourceKind.Personal, December), Src(EntitlementSourceKind.Family, June, "Papa", SubscriptionPlan.Family)]);

        Assert.Equal(("Lycée", July, EntitlementSourceKind.Family, June), (overlap!.CoveredBy, overlap.CoveredUntil, overlap.PaidKind, overlap.PaidUntil));
    }

    [Fact]
    public void Un_etablissement_sans_nom_reste_lisible() =>
        Assert.Equal("votre établissement", EntitlementRules.Overlap([Src(EntitlementSourceKind.School, July), Src(EntitlementSourceKind.Personal, June)])!.CoveredBy);

    [Theory]
    [InlineData(EntitlementSourceKind.Trial, "trial")]
    [InlineData(EntitlementSourceKind.Personal, "subscription")]
    [InlineData(EntitlementSourceKind.Family, "key")]
    [InlineData(EntitlementSourceKind.School, "space")]
    [InlineData(EntitlementSourceKind.Teacher, "space")]
    public void L_ancienne_source_est_gardee(EntitlementSourceKind kind, string expected) =>
        Assert.Equal(expected, EntitlementRules.LegacySource(kind));
}
