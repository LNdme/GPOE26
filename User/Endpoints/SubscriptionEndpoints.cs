using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using User.Data;
using User.DTOs;
using User.Helper;
using User.Model;
using User.Service;

namespace User.Endpoints;

public static class SubscriptionEndpoints
{
    public static void MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/subscriptions").WithTags("Abonnements").RequireAuthorization();

        // ── GET /subscriptions/me ─────────────────────────────────────────────────
        // Droit Premium courant + abonnements payés par l'utilisateur (avec la clé familiale à partager).
        group.MapGet("/me", async (ClaimsPrincipal principal, UserContext db, SubscriptionService subs) =>
        {
            var userId = principal.GetUserId()!.Value;
            var owned = await db.Subscriptions
                .Where(s => s.OwnerUserId == userId)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
            var ids = owned.Select(s => s.Id).ToList();
            var keys = await db.SubscriptionKeys.Where(k => ids.Contains(k.SubscriptionId)).ToListAsync();

            return Results.Ok(new MySubscriptionsDto(
                await subs.GetEntitlementAsync(userId),
                owned.Select(s => new SubscriptionDto(s, keys.Where(k => k.SubscriptionId == s.Id))).ToList()));
        })
        .WithSummary("Mon accès Premium et mes abonnements")
        .Produces<MySubscriptionsDto>();

        // ── POST /subscriptions/redeem ────────────────────────────────────────────
        // Clé familiale (élève) ou clé d'établissement / d'institution (enseignant, élève).
        group.MapPost("/redeem", async (RedeemKeyRequest req, ClaimsPrincipal principal, UserContext db, SubscriptionService subs) =>
        {
            var user = await db.CurrentUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            var error = await subs.RedeemAsync(user, req.Code);
            return error switch
            {
                RedeemError.None => Results.Ok(await subs.GetEntitlementAsync(user.Id)),
                RedeemError.NotFound => Results.NotFound(new { message = SubscriptionService.Message(error) }),
                _ => Results.BadRequest(new { message = SubscriptionService.Message(error) })
            };
        })
        .WithSummary("Activer une clé d'abonnement")
        .Produces<Entitlement>()
        .Produces(400)
        .Produces(404);

        // ── POST /subscriptions/checkout ──────────────────────────────────────────
        // Crée l'abonnement en attente de paiement. Le paiement (Mobile Money, carte)
        // sera branché en phase 5 ; d'ici là un administrateur l'active.
        group.MapPost("/checkout", async (CheckoutRequest req, ClaimsPrincipal principal, UserContext db, SubscriptionService subs) =>
        {
            var user = await db.CurrentUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            var allowed = (req.Plan, user.Role) switch
            {
                (SubscriptionPlan.StudentPremium, UserRole.Student) => true,
                (SubscriptionPlan.Family, UserRole.Parent) => true,
                (SubscriptionPlan.Teacher, UserRole.Teacher) => true,
                _ => false
            };
            if (!allowed)
                return Results.BadRequest(new { message = "Cette formule n'est pas disponible pour ce type de compte." });

            var sub = new Subscription
            {
                Plan = req.Plan,
                Period = req.Period,
                OwnerUserId = user.Id,
                Seats = req.Plan == SubscriptionPlan.Family ? subs.FamilySeats : 1
            };
            db.Subscriptions.Add(sub);
            await db.SaveChangesAsync();
            return Results.Accepted($"/subscriptions/me", new SubscriptionDto(sub, []));
        })
        .WithSummary("Souscrire (en attente de paiement)")
        .Produces<SubscriptionDto>(202)
        .Produces(400);

        // ── POST /subscriptions/trial ─────────────────────────────────────────────
        group.MapPost("/trial", async (ClaimsPrincipal principal, UserContext db, SubscriptionService subs) =>
        {
            var userId = principal.GetUserId()!.Value;
            if (await subs.HasHadTrialAsync(userId))
                return Results.Conflict(new { message = "La période d'essai a déjà été utilisée." });

            var trial = await subs.StartTrialAsync(userId, SubscriptionPlan.Teacher);
            return Results.Ok(new SubscriptionDto(trial, []));
        })
        .RequireAuthorization(Policies.Teacher)
        .WithSummary("Démarrer l'essai gratuit enseignant")
        .Produces<SubscriptionDto>()
        .Produces(409);
    }
}
