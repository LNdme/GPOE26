using Chat.Data;
using GPOE26.ServiceDefaults.Llm;
using Microsoft.EntityFrameworkCore;

namespace Chat.Service;

/// <summary>Fonctions d'IA du service Chat, et leur nom affiché dans les messages de refus.</summary>
public static class AiFeatures
{
    public const string Tutor = "tutor-ai";
    public const string TeacherAgent = "teacher-agent";
    /// <summary>Brouillon de cours : ouvert à tous les comptes, limité seulement par le plafond quotidien.</summary>
    public const string CourseDraft = "course-draft";

    public static string Label(string feature) => feature switch
    {
        Tutor => "Le tuteur IA",
        TeacherAgent => "L'assistant enseignant",
        CourseDraft => "La rédaction de cours par l'IA",
        _ => "Cette fonction d'IA"
    };
}

/// <summary>Résultat du contrôle : Refusal non nul = demande refusée (403), avec le message à afficher.</summary>
public record QuotaCheck(string? Refusal, int Status = StatusCodes.Status403Forbidden);

/// <summary>
/// Contrôle et comptage des demandes d'IA (table AiUsage de chatdb). Le contrôle se fait avant l'appel au modèle,
/// le comptage après une réponse : une panne du modèle ne consomme pas le quota.
/// </summary>
public class AiQuota(TutorContext db, PlatformClients platform, IConfiguration config)
{
    /// <param name="requireFeature">Faux pour une fonction ouverte à tous (brouillon de cours) : seul le plafond quotidien s'applique.</param>
    public async Task<QuotaCheck> CheckAsync(Guid userId, string feature, CancellationToken ct, bool requireFeature = true)
    {
        int? monthly = null;
        if (requireFeature)
        {
            var grant = await platform.FeatureAsync(feature, ct);
            if (grant is null) return new QuotaCheck(AiFeatures.Label(feature) + " n'est pas inclus dans votre formule.");
            monthly = grant.Limit;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var month = AiQuotaRules.MonthStart(today);
        var usedThisMonth = monthly is null ? 0 : await db.AiUsage
            .Where(u => u.UserId == userId && u.Feature == feature && u.Day >= month)
            .SumAsync(u => u.Count, ct);
        var usedToday = await db.AiUsage.Where(u => u.UserId == userId && u.Day == today).SumAsync(u => u.Count, ct);

        return new QuotaCheck(AiQuotaRules.Refusal(monthly, usedThisMonth, AiQuotaRules.DailyLimit(config), usedToday, AiFeatures.Label(feature)));
    }

    /// <summary>Compte une demande servie (une ligne par utilisateur, fonction et jour, incrémentée sans conflit).</summary>
    public Task RecordAsync(Guid userId, string feature, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ""AiUsage"" (""UserId"", ""Feature"", ""Day"", ""Count"") VALUES ({userId}, {feature}, {today}, 1)
            ON CONFLICT (""UserId"", ""Feature"", ""Day"") DO UPDATE SET ""Count"" = ""AiUsage"".""Count"" + 1", ct);
    }
}
