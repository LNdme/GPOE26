using GPOE26.ServiceDefaults.Llm;
using Microsoft.EntityFrameworkCore;
using Quiz.Data;

namespace Quiz.Service;

/// <summary>Fonctions d'IA du service Quiz et leur nom affiché dans les messages de refus.</summary>
public static class AiFeatures
{
    public const string QuizGeneration = "quiz-ai";
    public const string Grading = "ai-grading";
    public const string Feedback = "ai-feedback";

    public static string Label(string feature) => feature switch
    {
        QuizGeneration => "La génération d'exercices par l'IA",
        Grading => "L'aide à la correction",
        Feedback => "L'analyse de ta copie par l'IA",
        _ => "Cette fonction d'IA"
    };
}

/// <summary>Usage de l'IA par utilisateur, fonction et jour (UTC) : quotas mensuels et plafond quotidien.</summary>
public class AiUsage
{
    public Guid UserId { get; set; }
    public string Feature { get; set; } = "";
    public DateOnly Day { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// Contrôle avant l'appel au modèle, comptage après une réponse (une panne ne consomme pas le quota).
/// La lecture d'une copie photographiée pendant une épreuve n'est jamais bloquée ni comptée : l'élève compose.
/// </summary>
public class AiQuota(EvaluationContext db, UserDirectoryClient users, IConfiguration config)
{
    /// <summary>Message de refus, ou null si la demande est permise.</summary>
    public async Task<string?> RefusalAsync(Guid userId, string feature, CancellationToken ct)
    {
        var grant = await users.FeatureAsync(feature);
        if (grant is null) return $"{AiFeatures.Label(feature)} n'est pas incluse dans votre formule.";

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var month = AiQuotaRules.MonthStart(today);
        var usedThisMonth = grant.Limit is null ? 0 : await db.AiUsage
            .Where(u => u.UserId == userId && u.Feature == feature && u.Day >= month)
            .SumAsync(u => u.Count, ct);
        var usedToday = await db.AiUsage.Where(u => u.UserId == userId && u.Day == today).SumAsync(u => u.Count, ct);

        return AiQuotaRules.Refusal(grant.Limit, usedThisMonth, AiQuotaRules.DailyLimit(config), usedToday, AiFeatures.Label(feature));
    }

    public Task RecordAsync(Guid userId, string feature, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ""AiUsage"" (""UserId"", ""Feature"", ""Day"", ""Count"") VALUES ({userId}, {feature}, {today}, 1)
            ON CONFLICT (""UserId"", ""Feature"", ""Day"") DO UPDATE SET ""Count"" = ""AiUsage"".""Count"" + 1", ct);
    }
}
