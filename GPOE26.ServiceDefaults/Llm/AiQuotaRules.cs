namespace GPOE26.ServiceDefaults.Llm;

/// <summary>
/// Limites d'usage des fonctions d'IA, communes aux services Chat et Quiz.
///   - quota mensuel de la formule (catalogue des formules, champ Limit ; aucun quota = illimité) ;
///   - plafond quotidien d'usage raisonnable (LLM:DailyLimitPerUser), toutes fonctions d'un service confondues :
///     il protège la clé d'IA contre un compte qui enverrait des milliers de demandes. 0 le désactive.
/// Chaque service compte dans sa propre base (table AiUsage) ; seules les règles sont partagées.
/// </summary>
public static class AiQuotaRules
{
    public const int DefaultDailyLimit = 150;

    /// <summary>Message de refus, ou null si la demande est permise.</summary>
    public static string? Refusal(int? monthlyLimit, int usedThisMonth, int dailyLimit, int usedToday, string featureLabel)
    {
        if (monthlyLimit is { } limit && usedThisMonth >= limit)
            return limit == 0
                ? $"{featureLabel} n'est pas incluse dans votre formule."
                : $"Vous avez utilisé vos {limit} demandes de ce mois pour {featureLabel.ToLowerInvariant()}. Passez à une formule supérieure pour continuer.";

        if (dailyLimit > 0 && usedToday >= dailyLimit)
            return $"Limite quotidienne atteinte ({dailyLimit} demandes à l'IA aujourd'hui). Elle se renouvelle demain.";

        return null;
    }

    public static int DailyLimit(Microsoft.Extensions.Configuration.IConfiguration config) =>
        int.TryParse(config["LLM:DailyLimitPerUser"], out var value) && value >= 0 ? value : DefaultDailyLimit;

    /// <summary>Premier jour du mois de cette date (UTC), pour sommer l'usage du mois.</summary>
    public static DateOnly MonthStart(DateOnly day) => new(day.Year, day.Month, 1);
}
