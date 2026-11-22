using Microsoft.EntityFrameworkCore;
using User.Data;
using User.DTOs;
using User.Model;

namespace User.Service;

/// <summary>
/// Profil renvoyé à l'interface et jeton signé, calculés au même endroit : droits (formule et sources) et casquettes
/// (lot P3). Les routes d'inscription, de connexion, de renouvellement et /auth/me passent toutes par ici.
/// </summary>
public class ProfileService(UserContext db, SubscriptionService subs, Jwtservice jwt)
{
    /// <summary>Casquettes de la personne, lues en base (liens parent, espaces, formule Famille payée).</summary>
    public async Task<IReadOnlyList<string>> HatsAsync(AppUser user)
    {
        var id = user.Id;
        var followsChild = await db.ParentChildLinks.AnyAsync(l => l.ParentId == id && l.Status == LinkStatus.Active);
        var memberships = await db.OrganizationMembers
            .Where(m => m.UserId == id && m.Status == MemberStatus.Active)
            .Select(m => m.Role)
            .ToListAsync();
        var paysForFamily = await db.Subscriptions.AnyAsync(s => s.OwnerUserId == id && s.Plan == SubscriptionPlan.Family
            && (s.Status == SubscriptionStatus.PendingPayment || ((s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial) && s.EndsAt > DateTime.UtcNow)));

        return HatRules.Compute(user.Role, new HatRules.Facts(
            FollowsChild: followsChild,
            TeachesInSpace: memberships.Contains(OrganizationRole.Teacher),
            ManagesSpace: memberships.Any(r => r is OrganizationRole.Owner or OrganizationRole.Manager),
            PaysForFamily: paysForFamily));
    }

    public async Task<UserProfileDto> ProfileAsync(AppUser user) =>
        new(user, await subs.GetEntitlementAsync(user.Id), await HatsAsync(user));

    /// <summary>
    /// Jeton neuf et profil à jour. <paramref name="signedInAt"/> : date de la connexion d'origine (renouvellement) ;
    /// <paramref name="activeWorkspace"/> : espace de travail actif, dont les claims sont exigés par les services.
    /// </summary>
    public async Task<AuthResponse> AuthAsync(AppUser user, DateTime? signedInAt = null, WorkspaceSummaryDto? activeWorkspace = null)
    {
        var entitlement = await subs.GetEntitlementAsync(user.Id);
        var hats = await HatsAsync(user);
        var (token, expiresAt) = jwt.GenerateToken(user, entitlement, signedInAt, hats, activeWorkspace);
        return new AuthResponse(token, expiresAt, new UserProfileDto(user, entitlement, hats), activeWorkspace);
    }
}
