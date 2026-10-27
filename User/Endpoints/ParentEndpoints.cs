using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using User.Data;
using User.DTOs;
using User.Helper;
using User.Model;
using User.Service;

namespace User.Endpoints;

public static class ParentEndpoints
{
    public static void MapParentEndpoints(this IEndpointRouteBuilder app)
    {
        // ═══ Côté parent ═══════════════════════════════════════════════════════════
        var parents = app.MapGroup("/parents/children").WithTags("Parents").RequireAuthorization(Policies.Parent);

        // ── POST /parents/children/link ───────────────────────────────────────────
        // Le parent saisit le code personnel de l'élève → demande en attente de l'accord de l'élève.
        parents.MapPost("/link", async (LinkChildRequest req, ClaimsPrincipal principal, UserContext db, SubscriptionService subs) =>
        {
            var parentId = principal.GetUserId()!.Value;
            var code = CodeGenerator.Normalize(req.Code);
            var child = await db.AppUsers.FirstOrDefaultAsync(u => u.LinkCode == code && u.Role == UserRole.Student);
            if (child is null)
                return Results.NotFound(new { message = "Aucun élève ne correspond à ce code." });

            var link = await db.ParentChildLinks.FirstOrDefaultAsync(l => l.ParentId == parentId && l.ChildId == child.Id);
            if (link is null)
            {
                link = new ParentChildLink { ParentId = parentId, ChildId = child.Id };
                db.ParentChildLinks.Add(link);
            }
            else if (link.Status == LinkStatus.Rejected)
            {
                link.Status = LinkStatus.Pending;
                link.RespondedAt = null;
            }
            await db.SaveChangesAsync();

            return Results.Ok(await ToChildDtoAsync(link, child, subs));
        })
        .WithSummary("Rattacher un enfant avec son code (en attente de son accord)")
        .Produces<ChildLinkDto>()
        .Produces(404);

        // ── POST /parents/children ────────────────────────────────────────────────
        // Le parent crée le compte de son enfant : le lien est actif tout de suite
        // et l'enfant reçoit une place de l'abonnement familial s'il en reste.
        parents.MapPost("", async (CreateChildRequest req, ClaimsPrincipal principal, UserContext db, SubscriptionService subs) =>
        {
            var parentId = principal.GetUserId()!.Value;
            if (await db.AppUsers.AnyAsync(u => u.Email == req.Email.ToLower()))
                return Results.Conflict(new { message = "Un compte avec cet email existe déjà." });
            if (await db.AppUsers.AnyAsync(u => u.Username == req.Username))
                return Results.Conflict(new { message = "Ce nom d'utilisateur est déjà pris." });

            var child = new AppUser
            {
                Email = req.Email.ToLower(),
                Username = req.Username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
                Role = UserRole.Student,
                SubSystem = req.SubSystem,
                Level = req.Level,
                Filiere = req.Filiere,
                ExamTarget = req.ExamTarget,
                LinkCode = await UniqueLinkCodeAsync(db)
            };
            var link = new ParentChildLink
            {
                ParentId = parentId,
                ChildId = child.Id,
                Status = LinkStatus.Active,
                RespondedAt = DateTime.UtcNow
            };
            db.AppUsers.Add(child);
            db.ParentChildLinks.Add(link);
            await db.SaveChangesAsync();

            await subs.TryAssignFamilySeatAsync(parentId, child);
            return Results.Created($"/parents/children/{link.Id}", await ToChildDtoAsync(link, child, subs));
        })
        .WithSummary("Créer le compte de son enfant")
        .Produces<ChildLinkDto>(201)
        .Produces(409);

        // ── GET /parents/children ─────────────────────────────────────────────────
        parents.MapGet("", async (ClaimsPrincipal principal, UserContext db, SubscriptionService subs) =>
        {
            var parentId = principal.GetUserId()!.Value;
            var rows = await db.ParentChildLinks
                .Where(l => l.ParentId == parentId && l.Status != LinkStatus.Rejected)
                .Join(db.AppUsers, l => l.ChildId, u => u.Id, (l, u) => new { Link = l, Child = u })
                .OrderBy(r => r.Link.CreatedAt)
                .ToListAsync();

            var result = new List<ChildLinkDto>();
            foreach (var r in rows)
                result.Add(await ToChildDtoAsync(r.Link, r.Child, subs));
            return Results.Ok(result);
        })
        .WithSummary("Mes enfants")
        .Produces<List<ChildLinkDto>>();

        // ── DELETE /parents/children/{linkId} ─────────────────────────────────────
        parents.MapDelete("/{linkId:guid}", async (Guid linkId, ClaimsPrincipal principal, UserContext db) =>
        {
            var parentId = principal.GetUserId();
            var deleted = await db.ParentChildLinks
                .Where(l => l.Id == linkId && l.ParentId == parentId)
                .ExecuteDeleteAsync();
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        })
        .WithSummary("Retirer le rattachement d'un enfant");

        // ═══ Côté élève ════════════════════════════════════════════════════════════
        var mine = app.MapGroup("/me/parents").WithTags("Parents").RequireAuthorization(Policies.Student);

        // ── GET /me/parents ───────────────────────────────────────────────────────
        mine.MapGet("", async (ClaimsPrincipal principal, UserContext db) =>
        {
            var childId = principal.GetUserId();
            var links = await db.ParentChildLinks
                .Where(l => l.ChildId == childId && l.Status != LinkStatus.Rejected)
                .Join(db.AppUsers, l => l.ParentId, u => u.Id,
                    (l, u) => new ParentLinkDto(l.Id, l.Status, u.Id, u.Username, l.CreatedAt))
                .ToListAsync();
            return Results.Ok(links);
        })
        .WithSummary("Mes parents rattachés et demandes en attente")
        .Produces<List<ParentLinkDto>>();

        // ── POST /me/parents/{linkId}/approve | reject ────────────────────────────
        mine.MapPost("/{linkId:guid}/{decision:regex(^(approve|reject)$)}",
            async (Guid linkId, string decision, ClaimsPrincipal principal, UserContext db, SubscriptionService subs) =>
        {
            var child = await db.CurrentUserAsync(principal);
            if (child is null) return Results.Unauthorized();

            var link = await db.ParentChildLinks.FirstOrDefaultAsync(l => l.Id == linkId && l.ChildId == child.Id);
            if (link is null) return Results.NotFound();
            if (link.Status != LinkStatus.Pending)
                return Results.Conflict(new { message = "Cette demande a déjà reçu une réponse." });

            link.Status = decision == "approve" ? LinkStatus.Active : LinkStatus.Rejected;
            link.RespondedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            if (link.Status == LinkStatus.Active)
                await subs.TryAssignFamilySeatAsync(link.ParentId, child);
            return Results.NoContent();
        })
        .WithSummary("Accepter ou refuser la demande d'un parent");
    }

    public static async Task<string> UniqueLinkCodeAsync(UserContext db)
    {
        while (true)
        {
            var code = CodeGenerator.LinkCode();
            if (!await db.AppUsers.AnyAsync(u => u.LinkCode == code)) return code;
        }
    }

    private static async Task<ChildLinkDto> ToChildDtoAsync(ParentChildLink link, AppUser child, SubscriptionService subs)
    {
        // Le parent ne voit le détail de l'enfant qu'une fois le lien accepté
        var active = link.Status == LinkStatus.Active;
        var premium = active && (await subs.GetEntitlementAsync(child.Id)).IsPremium;
        return new ChildLinkDto(link.Id, link.Status, child.Id, child.Username,
            active ? child.Level : null, active ? child.Filiere : null, active ? child.ExamTarget : null, premium);
    }
}
