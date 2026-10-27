using Microsoft.EntityFrameworkCore;
using User.Data;
using User.DTOs;
using User.Model;
using User.Service;

namespace User.Endpoints;

/// <summary>
/// Administration de la plateforme : établissements / institutions, licences,
/// et activation manuelle des abonnements tant que le paiement n'est pas branché.
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").WithTags("Administration").RequireAuthorization(Policies.Admin);

        // ── POST /admin/organizations ─────────────────────────────────────────────
        admin.MapPost("/organizations", async (CreateOrganizationRequest req, UserContext db) =>
        {
            var org = new Organization { Name = req.Name.Trim(), Type = req.Type, City = req.City };
            db.Organizations.Add(org);
            await db.SaveChangesAsync();
            return Results.Created($"/admin/organizations/{org.Id}", new OrganizationDto(org.Id, org.Name, org.Type, org.City, []));
        })
        .WithSummary("Créer un établissement ou une institution")
        .Produces<OrganizationDto>(201);

        // ── GET /admin/organizations ──────────────────────────────────────────────
        admin.MapGet("/organizations", async (UserContext db) =>
        {
            var orgs = await db.Organizations.OrderBy(o => o.Name).ToListAsync();
            var licenses = await db.Subscriptions.Where(s => s.OrganizationId != null).ToListAsync();
            var ids = licenses.Select(l => l.Id).ToList();
            var keys = await db.SubscriptionKeys.Where(k => ids.Contains(k.SubscriptionId)).ToListAsync();

            return Results.Ok(orgs.Select(o => new OrganizationDto(o.Id, o.Name, o.Type, o.City,
                licenses.Where(l => l.OrganizationId == o.Id)
                    .Select(l => new SubscriptionDto(l, keys.Where(k => k.SubscriptionId == l.Id)))
                    .ToList())));
        })
        .WithSummary("Établissements, institutions et leurs licences")
        .Produces<List<OrganizationDto>>();

        // ── POST /admin/organizations/{id}/licenses ───────────────────────────────
        // Licence active immédiatement ; la clé (ETB-… / ORG-…) est à transmettre à la structure.
        admin.MapPost("/organizations/{id:guid}/licenses", async (Guid id, CreateLicenseRequest req, UserContext db, SubscriptionService subs) =>
        {
            if (!await db.Organizations.AnyAsync(o => o.Id == id)) return Results.NotFound();

            var license = new Subscription
            {
                Plan = SubscriptionPlan.OrganizationLicense,
                Period = BillingPeriod.Yearly,
                OrganizationId = id,
                Seats = req.Seats
            };
            db.Subscriptions.Add(license);
            var key = await subs.ActivateAsync(license, req.Months);
            return Results.Created($"/admin/organizations/{id}", new SubscriptionDto(license, key is null ? [] : [key]));
        })
        .WithSummary("Créer une licence et sa clé")
        .Produces<SubscriptionDto>(201)
        .Produces(404);

        // ── POST /admin/subscriptions/{id}/activate ───────────────────────────────
        // Remplacé par le webhook de paiement en phase 5.
        admin.MapPost("/subscriptions/{id:guid}/activate", async (Guid id, ActivateSubscriptionRequest req, UserContext db, SubscriptionService subs) =>
        {
            var sub = await db.Subscriptions.FindAsync(id);
            if (sub is null) return Results.NotFound();
            if (sub.Status != SubscriptionStatus.PendingPayment)
                return Results.Conflict(new { message = "Seul un abonnement en attente de paiement peut être activé." });

            await subs.ActivateAsync(sub, req.Months);
            var keys = await db.SubscriptionKeys.Where(k => k.SubscriptionId == id).ToListAsync();
            return Results.Ok(new SubscriptionDto(sub, keys));
        })
        .WithSummary("Activer un abonnement en attente (paiement reçu)")
        .Produces<SubscriptionDto>()
        .Produces(404)
        .Produces(409);
    }
}
