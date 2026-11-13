using System.Text.Json;
using Chat.Service;
using GPOE26.ServiceDefaults.Llm;
using Microsoft.Extensions.Configuration;

namespace GPOE26.Logic.Tests;

/// <summary>Quotas d'IA : quota mensuel de la formule, puis plafond quotidien d'usage raisonnable.</summary>
public class AiQuotaRulesTests
{
    [Fact]
    public void Sans_quota_ni_plafond_atteint_la_demande_passe() =>
        Assert.Null(AiQuotaRules.Refusal(monthlyLimit: null, usedThisMonth: 999, dailyLimit: 150, usedToday: 3, "Le tuteur IA"));

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(10, true)]
    public void Le_quota_mensuel_de_la_formule_est_applique(int used, bool refused) =>
        Assert.Equal(refused, AiQuotaRules.Refusal(3, used, 150, 0, "La génération d'exercices par l'IA") is not null);

    [Fact]
    public void Le_message_cite_le_quota()
    {
        var message = AiQuotaRules.Refusal(3, 3, 150, 0, "La génération d'exercices par l'IA");
        Assert.Contains("3 demandes de ce mois", message);
    }

    [Theory]
    [InlineData(149, false)]
    [InlineData(150, true)]
    public void Le_plafond_quotidien_protege_la_cle(int usedToday, bool refused) =>
        Assert.Equal(refused, AiQuotaRules.Refusal(null, 0, 150, usedToday, "Le tuteur IA") is not null);

    [Fact]
    public void Un_plafond_a_zero_est_desactive() =>
        Assert.Null(AiQuotaRules.Refusal(null, 0, dailyLimit: 0, usedToday: 100_000, "Le tuteur IA"));

    [Theory]
    [InlineData(null, AiQuotaRules.DefaultDailyLimit)]
    [InlineData("40", 40)]
    [InlineData("0", 0)]
    [InlineData("-5", AiQuotaRules.DefaultDailyLimit)]
    [InlineData("beaucoup", AiQuotaRules.DefaultDailyLimit)]
    public void Le_plafond_se_regle_dans_la_configuration(string? value, int expected)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["LLM:DailyLimitPerUser"] = value }).Build();
        Assert.Equal(expected, AiQuotaRules.DailyLimit(config));
    }

    [Fact]
    public void Le_mois_commence_le_premier() =>
        Assert.Equal(new DateOnly(2026, 11, 1), AiQuotaRules.MonthStart(new DateOnly(2026, 11, 10)));
}

/// <summary>Lecture des droits renvoyés par le service User : une réponse incomplète ne fait plus planter le tuteur.</summary>
public class FeatureLookupTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void La_fonction_et_son_quota_sont_lus()
    {
        var grant = PlatformClients.FindFeature(Json("""{"entitlement":{"features":[{"code":"quiz-ai","limit":3},{"code":"tutor-ai","limit":null}]}}"""), "quiz-ai");
        Assert.Equal(new FeatureGrant("quiz-ai", 3), grant);
    }

    [Fact]
    public void Sans_quota_la_fonction_est_illimitee() =>
        Assert.Null(PlatformClients.FindFeature(Json("""{"entitlement":{"features":[{"code":"tutor-ai","limit":null}]}}"""), "tutor-ai")!.Limit);

    [Theory]
    [InlineData("""{"entitlement":{"features":[{"code":"quiz-ai"}]}}""")]
    [InlineData("""{"entitlement":{"features":[]}}""")]
    [InlineData("""{"entitlement":{}}""")]
    [InlineData("""{"autre":true}""")]
    [InlineData("""[]""")]
    [InlineData("""{"entitlement":{"features":[42, {"code":7}]}}""")]
    public void Une_reponse_incomplete_vaut_fonction_absente(string json) =>
        Assert.Null(PlatformClients.FindFeature(Json(json), "tutor-ai"));
}

/// <summary>Suivi de classe : la notion à revoir porte le titre du cours, pas celui d'un devoir.</summary>
public class ClassInsightsTitleTests
{
    [Fact]
    public void Le_titre_du_cours_l_emporte_sur_celui_du_devoir()
    {
        var course = Guid.NewGuid();
        var progress = new ClassProgressRef(Guid.NewGuid(),
            [new ClassAssessmentRef(Guid.NewGuid(), "Devoir n°2", course)],
            [new ClassStudentRef(Guid.NewGuid(), "Awa", DateTime.UtcNow, 8, [new ClassMasteryRef(course, 40)])]);

        var withTitles = ClassInsightsCalculator.Compute(progress, DateTime.UtcNow, new Dictionary<Guid, string> { [course] = "Chute libre" });
        Assert.Equal("Chute libre", Assert.Single(withTitles.WeakCourses).Title);

        var withoutTitles = ClassInsightsCalculator.Compute(progress, DateTime.UtcNow);
        Assert.Equal("Devoir n°2", Assert.Single(withoutTitles.WeakCourses).Title);
    }
}
