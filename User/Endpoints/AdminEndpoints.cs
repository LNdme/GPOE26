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
        var admin = app.MapGroup("/admin").WithTags("Administration").RequireAdmin();

        // ── GET /admin/users ──────────────────────────────────────────────────────
        // Comptes à qui donner un accès d'administration ou d'édition du site.
        admin.MapGet("/users", async (UserContext db, string? search, bool staffOnly = false) =>
        {
            var query = db.AppUsers.AsNoTracking();
            if (staffOnly)
                query = query.Where(u => u.IsAdmin || u.SiteEditor);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = $"%{search.Trim()}%";
                query = query.Where(u => EF.Functions.ILike(u.Email, s) || EF.Functions.ILike(u.Username, s));
            }
            var users = await query
                .OrderByDescending(u => u.IsAdmin)
                .ThenByDescending(u => u.SiteEditor)
                .ThenBy(u => u.Username)
                .Take(50)
                .Select(u => new UserAccessDto(u.Id, u.Username, u.Email, u.Role, u.IsAdmin, u.SiteEditor, u.CreatedAt, u.LastLoginAt))
                .ToListAsync();
            return Results.Ok(users);
        })
        .WithSummary("Comptes et accès au site (50 au plus, recherche par nom ou e-mail)")
        .Produces<List<UserAccessDto>>();

        // ── PUT /admin/users/{id}/access ──────────────────────────────────────────
        // Nommer / retirer un administrateur, donner / retirer le droit d'éditer le site. Le rôle (enseignant, parent…)
        // ne change pas. L'administration de la plateforme en tient compte aussitôt (vérifiée en base) ;
        // le site et les menus, au renouvellement du jeton (20 minutes au plus).

        admin.MapPut("/users/{id:guid}/access", async (Guid id, UpdateAccessRequest req, ClaimsPrincipal me, UserContext db) =>
        {
            var user = await db.AppUsers.FindAsync(id);
            if (user is null) return Results.NotFound();

            if (req.Admin is { } makeAdmin && makeAdmin != user.IsAdmin)
            {
                if (!makeAdmin)
                {
                    if (user.Id == me.GetUserId())
                        return Results.Conflict(new { message = "Vous ne pouvez pas retirer vos propres droits d'administrateur." });
                    if (await db.AppUsers.CountAsync(u => u.IsAdmin) <= 1)
                        return Results.Conflict(new { message = "Il doit rester au moins un administrateur." });
                }
                user.IsAdmin = makeAdmin;
            }
            if (req.SiteEditor is { } editor)
                user.SiteEditor = editor;

            await db.SaveChangesAsync();
            return Results.Ok(new UserAccessDto(user.Id, user.Username, user.Email, user.Role, user.IsAdmin, user.SiteEditor, user.CreatedAt, user.LastLoginAt));
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
        // Les espaces d'enseignants indépendants n'y figurent pas : ils se gèrent seuls.
        admin.MapGet("/organizations", async (UserContext db) =>
        {
            var orgs = await db.Organizations.Where(o => o.Type != OrganizationType.Independent).OrderBy(o => o.Name).ToListAsync();
            var licenses = await db.Subscriptions.Where(s => s.OrganizationId != null).ToListAsync();
            var ids = licenses.Select(l => l.Id).ToList();
            var keys = await db.SubscriptionKeys.Where(k => ids.Contains(k.SubscriptionId)).ToListAsync();
            var owners = await db.OrganizationMembers
                .Where(m => m.Role == OrganizationRole.Owner && m.Status == MemberStatus.Active)
                .Join(db.AppUsers, m => m.UserId, u => u.Id, (m, u) => new { m.OrganizationId, Owner = new SpaceOwnerDto(u.Id, u.Username, u.Email) })
                .ToListAsync();

            return Results.Ok(orgs.Select(o => new OrganizationDto(o.Id, o.Name, o.Type, o.City,
                licenses.Where(l => l.OrganizationId == o.Id)
                    .Select(l => new SubscriptionDto(l, keys.Where(k => k.SubscriptionId == l.Id)))
                    .ToList(),
                owners.Where(x => x.OrganizationId == o.Id).Select(x => x.Owner).ToList())));
        })
        .WithSummary("Établissements, institutions, leurs licences et leurs responsables")
        .Produces<List<OrganizationDto>>();

        // ── PUT /admin/organizations/{id}/owners ──────────────────────────────────
        // Désigne le responsable du compte d'un établissement (compte existant ; le code responsable arrive au lot 3).
        admin.MapPut("/organizations/{id:guid}/owners", async (Guid id, AddSpaceMemberRequest req, UserContext db, SpaceService spaces) =>
        {
            if (!await db.Organizations.AnyAsync(o => o.Id == id && o.Type != OrganizationType.Independent)) return Results.NotFound();
            var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Email == req.Email.Trim().ToLower());
            if (user is null)
                return Results.NotFound(new { message = "Aucun compte avec cette adresse : la personne doit d'abord s'inscrire." });
            if (user.Role == UserRole.Student)
                return Results.BadRequest(new { message = "Un compte élève ne peut pas être responsable d'un établissement." });

            await spaces.GrantRoleAsync(id, user.Id, OrganizationRole.Owner);
            await db.SaveChangesAsync();
            return Results.Ok(new SpaceOwnerDto(user.Id, user.Username, user.Email));
        })
        .WithSummary("Désigner le responsable d'un établissement")
        .Produces<SpaceOwnerDto>()
        .Produces(404);

        // ── DELETE /admin/organizations/{id}/owners/{userId} ──────────────────────
        admin.MapDelete("/organizations/{id:guid}/owners/{userId:guid}", async (Guid id, Guid userId, UserContext db, SpaceService spaces) =>
        {
            if (!await db.Organizations.AnyAsync(o => o.Id == id && o.Type != OrganizationType.Independent)) return Results.NotFound();
            if (!await spaces.RevokeRoleAsync(id, userId, OrganizationRole.Owner)) return Results.NotFound();
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithSummary("Retirer un responsable d'établissement");

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

        // ── GET /admin/subscriptions ──────────────────────────────────────────────
        // Abonnements individuels et familiaux, avec leur titulaire. Sans filtre : ceux en attente de paiement,
        // que personne ne pouvait retrouver tant que le paiement n'est pas branché.
        admin.MapGet("/subscriptions", async (UserContext db, SubscriptionStatus? status) =>
        {
            var wanted = status ?? SubscriptionStatus.PendingPayment;
            var subscriptions = await db.Subscriptions.AsNoTracking()
                .Where(s => s.Status == wanted && s.OrganizationId == null)
                .OrderBy(s => s.CreatedAt)
                .Take(200)
                .ToListAsync();

            var ownerIds = subscriptions.Where(s => s.OwnerUserId != null).Select(s => s.OwnerUserId!.Value).Distinct().ToList();
            var owners = await db.AppUsers.AsNoTracking().Where(u => ownerIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => new { u.Username, u.Email, u.Role });
            var ids = subscriptions.Select(s => s.Id).ToList();
            var keys = await db.SubscriptionKeys.AsNoTracking().Where(k => ids.Contains(k.SubscriptionId)).ToListAsync();

            return Results.Ok(subscriptions.Select(s =>
            {
                var owner = s.OwnerUserId is { } id ? owners.GetValueOrDefault(id) : null;
                return new AdminSubscriptionDto(new SubscriptionDto(s, keys.Where(k => k.SubscriptionId == s.Id)),
                    s.CreatedAt, owner?.Username, owner?.Email, owner?.Role);
            }));
        })
        .WithSummary("Abonnements à traiter (par défaut : en attente de paiement)")
        .Produces<List<AdminSubscriptionDto>>();

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

/// <summary>Compte et accès au site, pour la page Comptes et accès de l'administration.</summary>
public record UserAccessDto(Guid Id, string Username, string Email, UserRole Role, bool IsAdmin, bool SiteEditor, DateTime CreatedAt, DateTime? LastLoginAt);

/// <summary>Champs absents = inchangés. Le rôle du compte n'est jamais modifié.</summary>
public record UpdateAccessRequest(bool? Admin, bool? SiteEditor);

/// <summary>Abonnement vu par l'administration, avec son titulaire.</summary>
public record AdminSubscriptionDto(SubscriptionDto Subscription, DateTime CreatedAt, string? OwnerName, string? OwnerEmail, UserRole? OwnerRole);
