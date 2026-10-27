using Microsoft.EntityFrameworkCore;
using User.Data;
using User.Model;

namespace User.Service;

/// <summary>Droit d'accès Premium d'un utilisateur, quelle qu'en soit l'origine.</summary>
public record Entitlement(bool IsPremium, SubscriptionPlan? Plan, DateTime? Until, string? Source)
{
    public static readonly Entitlement Free = new(false, null, null, null);
}

public enum RedeemError
{
    None,
    NotFound,
    Revoked,
    Inactive,
    WrongRole,
    Full
}

/// <summary>
/// Logique métier des abonnements et des clés.
/// Un utilisateur est Premium s'il possède un abonnement individuel en cours
/// (StudentPremium, Teacher, essai) ou s'il a activé une clé dont l'abonnement est en cours.
/// </summary>
public class SubscriptionService(UserContext db, IConfiguration config)
{
    public int TrialDays => config.GetValue("Subscriptions:TrialDays", 14);
    public int FamilySeats => config.GetValue("Subscriptions:FamilySeats", 3);

    public async Task<Entitlement> GetEntitlementAsync(Guid userId)
    {
        var now = DateTime.UtcNow;
        var active = new[] { SubscriptionStatus.Active, SubscriptionStatus.Trial };

        var own = await db.Subscriptions
            .Where(s => s.OwnerUserId == userId
                        && (s.Plan == SubscriptionPlan.StudentPremium || s.Plan == SubscriptionPlan.Teacher)
                        && active.Contains(s.Status) && s.EndsAt > now)
            .OrderByDescending(s => s.EndsAt)
            .FirstOrDefaultAsync();

        var viaKey = await db.KeyRedemptions
            .Where(r => r.UserId == userId && !r.Key.IsRevoked)
            .Select(r => r.Key.Subscription)
            .Where(s => active.Contains(s.Status) && s.EndsAt > now)
            .OrderByDescending(s => s.EndsAt)
            .FirstOrDefaultAsync();

        var best = new[] { own, viaKey }.Where(s => s is not null).MaxBy(s => s!.EndsAt);
        if (best is null) return Entitlement.Free;

        var source = best == own
            ? (best.Status == SubscriptionStatus.Trial ? "trial" : "subscription")
            : "key";
        return new Entitlement(true, best.Plan, best.EndsAt, source);
    }

    /// <summary>
    /// Vérifie qu'une clé peut être activée par ce rôle, sans rien modifier.
    /// Utilisé à l'inscription pour refuser la création du compte si la clé est invalide.
    /// </summary>
    public async Task<(RedeemError Error, SubscriptionKey? Key)> CheckKeyAsync(string code, UserRole role)
    {
        var normalized = CodeGenerator.Normalize(code);
        var key = await db.SubscriptionKeys
            .Include(k => k.Subscription)
            .FirstOrDefaultAsync(k => k.Code == normalized);

        if (key is null) return (RedeemError.NotFound, null);
        if (key.IsRevoked) return (RedeemError.Revoked, key);
        if (!key.Subscription.IsCurrent(DateTime.UtcNow)) return (RedeemError.Inactive, key);

        var allowed = key.Subscription.Plan switch
        {
            SubscriptionPlan.Family => role == UserRole.Student,
            SubscriptionPlan.OrganizationLicense => role is UserRole.Teacher or UserRole.Student,
            _ => false
        };
        if (!allowed) return (RedeemError.WrongRole, key);
        if (key.UsedCount >= key.MaxUses) return (RedeemError.Full, key);

        return (RedeemError.None, key);
    }

    /// <summary>
    /// Active une clé pour un utilisateur. Idempotent : réactiver une clé déjà activée ne consomme pas de place.
    /// La place est réservée par un UPDATE conditionnel pour éviter de dépasser MaxUses en cas d'activations simultanées.
    /// </summary>
    public async Task<RedeemError> RedeemAsync(AppUser user, string code)
    {
        var (error, key) = await CheckKeyAsync(code, user.Role);
        if (key is not null && await db.KeyRedemptions.AnyAsync(r => r.KeyId == key.Id && r.UserId == user.Id))
            return RedeemError.None;
        if (error != RedeemError.None) return error;

        var reserved = await db.SubscriptionKeys
            .Where(k => k.Id == key!.Id && k.UsedCount < k.MaxUses)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.UsedCount, k => k.UsedCount + 1));
        if (reserved == 0) return RedeemError.Full;

        db.KeyRedemptions.Add(new KeyRedemption { KeyId = key!.Id, UserId = user.Id });

        // Une licence d'organisation rattache l'utilisateur à sa structure
        if (key.Subscription.OrganizationId is { } orgId)
            user.OrganizationId = orgId;

        await db.SaveChangesAsync();
        return RedeemError.None;
    }

    /// <summary>
    /// Attribue à un enfant une place de l'abonnement familial en cours du parent, s'il en reste.
    /// Appelé quand le rattachement parent ↔ enfant devient actif.
    /// </summary>
    public async Task<bool> TryAssignFamilySeatAsync(Guid parentId, AppUser child)
    {
        var now = DateTime.UtcNow;
        var keys = await db.SubscriptionKeys
            .Include(k => k.Subscription)
            .Where(k => k.Subscription.OwnerUserId == parentId
                        && k.Subscription.Plan == SubscriptionPlan.Family
                        && k.Subscription.Status == SubscriptionStatus.Active
                        && k.Subscription.EndsAt > now
                        && !k.IsRevoked && k.UsedCount < k.MaxUses)
            .ToListAsync();

        foreach (var key in keys)
            if (await RedeemAsync(child, key.Code) == RedeemError.None)
                return true;
        return false;
    }

    public async Task<Subscription> StartTrialAsync(Guid userId, SubscriptionPlan plan)
    {
        var now = DateTime.UtcNow;
        var trial = new Subscription
        {
            Plan = plan,
            Status = SubscriptionStatus.Trial,
            OwnerUserId = userId,
            StartsAt = now,
            EndsAt = now.AddDays(TrialDays)
        };
        db.Subscriptions.Add(trial);
        await db.SaveChangesAsync();
        return trial;
    }

    /// <summary>Un essai garde le statut Trial même après sa date de fin : un seul essai par compte.</summary>
    public Task<bool> HasHadTrialAsync(Guid userId) =>
        db.Subscriptions.AnyAsync(s => s.OwnerUserId == userId && s.Status == SubscriptionStatus.Trial);

    /// <summary>
    /// Active un abonnement (paiement confirmé — en attendant la phase 5, par un administrateur)
    /// et génère la clé à partager pour les formules Family et OrganizationLicense.
    /// </summary>
    public async Task<SubscriptionKey?> ActivateAsync(Subscription sub, int? months = null)
    {
        var now = DateTime.UtcNow;
        sub.Status = SubscriptionStatus.Active;
        sub.StartsAt = now;
        sub.EndsAt = now.AddMonths(months ?? (sub.Period == BillingPeriod.Yearly ? 12 : 1));

        SubscriptionKey? key = null;
        if (sub.Plan is SubscriptionPlan.Family or SubscriptionPlan.OrganizationLicense
            && !await db.SubscriptionKeys.AnyAsync(k => k.SubscriptionId == sub.Id))
        {
            var prefix = "FAM";
            if (sub.Plan == SubscriptionPlan.OrganizationLicense)
            {
                var org = await db.Organizations.FindAsync(sub.OrganizationId);
                prefix = org?.Type == OrganizationType.Institution ? "ORG" : "ETB";
            }
            key = new SubscriptionKey
            {
                Code = await UniqueKeyCodeAsync(prefix),
                SubscriptionId = sub.Id,
                MaxUses = sub.Seats
            };
            db.SubscriptionKeys.Add(key);
        }

        await db.SaveChangesAsync();
        return key;
    }

    private async Task<string> UniqueKeyCodeAsync(string prefix)
    {
        while (true)
        {
            var code = CodeGenerator.SubscriptionKey(prefix);
            if (!await db.SubscriptionKeys.AnyAsync(k => k.Code == code)) return code;
        }
    }

    public static string Message(RedeemError error) => error switch
    {
        RedeemError.NotFound => "Clé d'abonnement introuvable.",
        RedeemError.Revoked => "Cette clé a été révoquée.",
        RedeemError.Inactive => "L'abonnement lié à cette clé n'est pas actif.",
        RedeemError.WrongRole => "Cette clé ne peut pas être utilisée avec ce type de compte.",
        RedeemError.Full => "Toutes les places de cette clé sont déjà utilisées.",
        _ => ""
    };
}
