using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using User.Data;
using User.Helper;
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

        // ── GET /admin/users ──────────────────────────────────────────────────────
        // Comptes à qui donner un accès d'administration ou d'édition du site.
        admin.MapGet("/users", async (UserContext db, string? search, bool staffOnly = false) =>
        {
            var query = db.AppUsers.AsNoTracking();
            if (staffOnly)
                query = query.Where(u => u.Role == UserRole.Admin || u.SiteEditor);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = $"%{search.Trim()}%";
                query = query.Where(u => EF.Functions.ILike(u.Email, s) || EF.Functions.ILike(u.Username, s));
            }
            var users = await query
                .OrderByDescending(u => u.Role == UserRole.Admin)
                .ThenByDescending(u => u.SiteEditor)
                .ThenBy(u => u.Username)
                .Take(50)
                .Select(u => new UserAccessDto(u.Id, u.Username, u.Email, u.Role, u.SiteEditor, u.CreatedAt, u.LastLoginAt))
                .ToListAsync();
            return Results.Ok(users);
        })
        .WithSummary("Comptes et accès au site (50 au plus, recherche par nom ou e-mail)")
        .Produces<List<UserAccessDto>>();

        // ── PUT /admin/users/{id}/access ──────────────────────────────────────────
        // Nommer / retirer un administrateur, donner / retirer le droit d'éditer le site.
        // Pris en compte à la prochaine connexion de l'utilisateur (nouveau jeton).
        admin.MapPut("/users/{id:guid}/access", async (Guid id, UpdateAccessRequest req, ClaimsPrincipal me, UserContext db) =>
        {
            var user = await db.AppUsers.FindAsync(id);
            if (user is null) return Results.NotFound();

            if (req.Admin is { } makeAdmin && makeAdmin != (user.Role == UserRole.Admin))
            {
                if (!makeAdmin)
                {
                    if (user.Id == me.GetUserId())
                        return Results.Conflict(new { message = "Vous ne pouvez pas retirer vos propres droits d'administrateur." });
                    if (await db.AppUsers.CountAsync(u => u.Role == UserRole.Admin) <= 1)
                        return Results.Conflict(new { message = "Il doit rester au moins un administrateur." });
                }
                // Un administrateur retiré redevient enseignant : le rôle d'origine n'est pas conservé
                user.Role = makeAdmin ? UserRole.Admin : (req.FallbackRole ?? UserRole.Teacher);
            }
            if (req.SiteEditor is { } editor)
                user.SiteEditor = editor;

            await db.SaveChangesAsync();
            return Results.Ok(new UserAccessDto(user.Id, user.Username, user.Email, user.Role, user.SiteEditor, user.CreatedAt, user.LastLoginAt));
        })
        .WithSummary("Nommer un administrateur ou un éditeur du site")
        .Produces<UserAccessDto>()
        .Produces(404)
        .Produces(409);

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

/// <summary>Compte et accès au site, pour la page Utilisateurs de l'administration.</summary>
public record UserAccessDto(Guid Id, string Username, string Email, UserRole Role, bool SiteEditor, DateTime CreatedAt, DateTime? LastLoginAt);

/// <summary>Champs absents = inchangés. FallbackRole : rôle donné à un administrateur retiré (Enseignant par défaut).</summary>
public record UpdateAccessRequest(bool? Admin, bool? SiteEditor, UserRole? FallbackRole = null);
