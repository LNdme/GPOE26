using Microsoft.EntityFrameworkCore;
using User.Data;
using User.Model;

namespace User.Service;

/// <summary>Droit d'accès Premium d'un utilisateur, quelle qu'en soit l'origine.</summary>
public record Entitlement(bool IsPremium, SubscriptionPlan? Plan, DateTime? Until, string? Source, List<EntitledFeature>? Features = null);

/// <summary>Fonctionnalité accessible. Limit : quota mensuel, vide si illimité.</summary>
public record EntitledFeature(string Code, int? Limit);

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
public class SubscriptionService(UserContext db, IConfiguration config, PlanCatalogClient catalog, SpaceService spaces)
{
    public int TrialDays => config.GetValue("Subscriptions:TrialDays", 14);
    public int FamilySeats => config.GetValue("Subscriptions:FamilySeats", 3);

    /// <summary>Paiement simulé : uniquement en développement, tant qu'aucun moyen de paiement réel n'est branché.</summary>
    public bool SimulatePayments => config.GetValue("Subscriptions:SimulatePayments", false);

    public static string PlanCode(SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.StudentPremium => "student-premium",
        SubscriptionPlan.Family => "family",
        SubscriptionPlan.Teacher => "teacher",
        _ => "organization-license"
    };

    /// <summary>
    /// Une fonctionnalité réservée à un rôle n'est pas accordée aux autres, même si la formule la contient :
    /// la licence d'établissement, partagée par enseignants et élèves, donnait l'assistant enseignant aux élèves.
    /// Les fonctions d'apprentissage (tuteur, quiz, épreuves blanches…) restent ouvertes aux enseignants : ils s'en
    /// servent pour essayer ce que voient leurs élèves.
    /// </summary>
    public static bool FeatureAllowedFor(string code, UserRole role) => code switch
    {
        "teacher-agent" or "ai-grading" => role == UserRole.Teacher,
        "family-view" => role == UserRole.Parent,
        _ => true
    };

    /// <summary>
    /// Fonctionnalités cumulées : formule gratuite + abonnements en cours, filtrées par rôle (null : sans filtre).
    /// Un accès illimité l'emporte sur un quota.
    /// </summary>
    public static List<EntitledFeature> MergeFeatures(IEnumerable<PlanRef> catalog, IEnumerable<SubscriptionPlan> current, UserRole? role = null)
    {
        var codes = current.Select(PlanCode).Append("free").ToHashSet();
        return catalog.Where(p => codes.Contains(p.Code))
            .SelectMany(p => p.Features)
            .Where(f => role is not { } r || FeatureAllowedFor(f.Code, r))
            .GroupBy(f => f.Code)
            .Select(g => new EntitledFeature(g.Key, g.Any(f => f.Limit is null) ? null : g.Max(f => f.Limit)))
            .ToList();
    }

    /// <summary>Quel rôle peut activer une clé de cette formule ?</summary>
    public static bool CanRedeem(SubscriptionPlan plan, UserRole role) => plan switch
    {
        SubscriptionPlan.StudentPremium => role == UserRole.Student,
        SubscriptionPlan.Teacher => role == UserRole.Teacher,
        SubscriptionPlan.Family => role == UserRole.Student,
        SubscriptionPlan.OrganizationLicense => role is UserRole.Teacher or UserRole.Student,
        _ => false
    };

    /// <summary>Quel rôle peut souscrire cette formule (paiement simulé) ?</summary>
    public static bool CanPurchase(SubscriptionPlan plan, UserRole role) => (plan, role) switch
    {
        (SubscriptionPlan.StudentPremium, UserRole.Student) => true,
        (SubscriptionPlan.Family, UserRole.Parent) => true,
        (SubscriptionPlan.Teacher, UserRole.Teacher) => true,
        (SubscriptionPlan.OrganizationLicense, UserRole.Teacher) => true,
        _ => false
    };

    public async Task<Entitlement> GetEntitlementAsync(Guid userId)
    {
        var now = DateTime.UtcNow;
        var active = new[] { SubscriptionStatus.Active, SubscriptionStatus.Trial };

        var own = await db.Subscriptions
            .Where(s => s.OwnerUserId == userId
                        && (s.Plan == SubscriptionPlan.StudentPremium || s.Plan == SubscriptionPlan.Teacher || s.Plan == SubscriptionPlan.Family)
                        && active.Contains(s.Status) && s.EndsAt > now)
            .ToListAsync();

        var viaKey = await db.KeyRedemptions
            .Where(r => r.UserId == userId && !r.Key.IsRevoked)
            .Select(r => r.Key.Subscription)
            .Where(s => active.Contains(s.Status) && s.EndsAt > now)
            .ToListAsync();

        var role = await db.AppUsers.Where(u => u.Id == userId).Select(u => (UserRole?)u.Role).FirstOrDefaultAsync();
        var features = MergeFeatures(await catalog.GetAsync(), own.Concat(viaKey).Select(s => s.Plan), role);
        var best = own.Concat(viaKey).MaxBy(s => s.EndsAt);
        if (best is null) return new Entitlement(false, null, null, null, features);

        var source = own.Contains(best)
            ? (best.Status == SubscriptionStatus.Trial ? "trial" : "subscription")
            : "key";
        return new Entitlement(true, best.Plan, best.EndsAt, source, features);
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

        if (!CanRedeem(key.Subscription.Plan, role)) return (RedeemError.WrongRole, key);
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

        // Une licence d'organisation rattache l'utilisateur à sa structure : enseignant de l'espace,
        // ou élève de sa liste (sans classe tant que le responsable ne l'a pas placé)
        if (key.Subscription.OrganizationId is { } orgId)
        {
            user.OrganizationId = orgId;
            if (user.Role == UserRole.Teacher)
                await spaces.EnsureMemberAsync(orgId, user.Id, OrganizationRole.Teacher);
            else if (user.Role == UserRole.Student)
                await spaces.EnsureStudentAsync(orgId, user, classroomId: null);
        }

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
    public async Task<SubscriptionKey?> ActivateAsync(Subscription sub, int? months = null, bool alwaysIssueKey = false)
    {
        var now = DateTime.UtcNow;
        sub.Status = SubscriptionStatus.Active;
        sub.StartsAt = now;
        sub.EndsAt = now.AddMonths(months ?? (sub.Period == BillingPeriod.Yearly ? 12 : 1));

        SubscriptionKey? key = null;
        if ((alwaysIssueKey || sub.Plan is SubscriptionPlan.Family or SubscriptionPlan.OrganizationLicense)
            && !await db.SubscriptionKeys.AnyAsync(k => k.SubscriptionId == sub.Id))
        {
            var prefix = sub.Plan switch
            {
                SubscriptionPlan.StudentPremium => "ELE",
                SubscriptionPlan.Teacher => "ENS",
                _ => "FAM"
            };
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
