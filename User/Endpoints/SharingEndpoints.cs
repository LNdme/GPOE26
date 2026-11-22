using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using User.Data;
using User.DTOs;
using User.Model;
using User.Service;

namespace User.Endpoints;

/// <summary>Une classe de l'historique : ses périodes (une par passage).</summary>
public record HistoryClassDto(Guid Id, string Name, List<MemberPeriodDto> Periods);

/// <summary>
/// Un espace (établissement, enseignant indépendant) dans l'historique de la personne. VisibleUntil : après son départ,
/// l'espace voit encore la période jusqu'à cette date. Scopes : ce qu'il voit aujourd'hui ; OptedOut : ce qu'elle a retiré.
/// </summary>
public record HistorySpaceDto(Guid OrganizationId, string Name, OrganizationType Type, bool Current, DateTime? Since,
    DateTime? LeftAt, DateTime? VisibleUntil, List<string> Scopes, List<string> OptedOut, List<HistoryClassDto> Classes);

public record HistoryGuardianDto(Guid GuardianId, string Name, List<string> Scopes, List<string> OptedOut);

/// <summary>CanChangeSharing : la personne est réputée majeure (choix T3) et peut retirer Progress ou WeakPoints.</summary>
public record HistoryDto(bool CanChangeSharing, int RetentionMonths, List<HistorySpaceDto> Spaces, List<HistoryGuardianDto> Guardians);

/// <summary>
/// Historique de la personne et partage de ses résultats (docs/PLAN-APPRENANT-AU-CENTRE.md, lot J3). Le départ ne
/// détruit rien : elle garde tout ; l'espace garde la période où elle était là, jusqu'à la fin de la conservation.
/// </summary>
public static class SharingEndpoints
{
    public static void MapSharingEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/me").WithTags("Historique et partage").RequireAuthorization();

        // ── GET /me/history ───────────────────────────────────────────────────────
        me.MapGet("/history", async (ClaimsPrincipal principal, UserContext db, VisibilityService visibility) =>
        {
            var user = await db.CurrentUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            return Results.Ok(await HistoryAsync(db, visibility, user));
        })
        .WithSummary("Mes classes et espaces, actuels et passés, et ce que chacun voit")
        .Produces<HistoryDto>();

        // ── DELETE /me/sharing/{organization|guardian}/{id}/{progress|weak-points} ─
        // Ne plus partager une portée ; PUT la partage à nouveau. Les résultats officiels ne se retirent pas (D3).
        me.MapDelete("/sharing/{kind}/{id:guid}/{scope}", async (string kind, Guid id, string scope, ClaimsPrincipal principal,
            UserContext db, VisibilityService visibility) =>
            await ChangeAsync(kind, id, scope, share: false, principal, db, visibility))
        .WithSummary("Ne plus partager ma maîtrise ou mes notions fragiles avec un espace ou un parent")
        .Produces(204).Produces(400).Produces(403).Produces(404);

        me.MapPut("/sharing/{kind}/{id:guid}/{scope}", async (string kind, Guid id, string scope, ClaimsPrincipal principal,
            UserContext db, VisibilityService visibility) =>
            await ChangeAsync(kind, id, scope, share: true, principal, db, visibility))
        .WithSummary("Partager à nouveau ma maîtrise ou mes notions fragiles")
        .Produces(204).Produces(400).Produces(403).Produces(404);
    }

    private static async Task<IResult> ChangeAsync(string kind, Guid id, string scopeText, bool share, ClaimsPrincipal principal,
        UserContext db, VisibilityService visibility)
    {
        var isOrganization = kind.Equals("organization", StringComparison.OrdinalIgnoreCase);
        if (!isOrganization && !kind.Equals("guardian", StringComparison.OrdinalIgnoreCase))
            return Error(400, $"Valeur inconnue : {kind} (attendu : organization ou guardian).");
        if (VisibilityRules.ParseScope(scopeText) is not { } scope)
            return Error(400, $"Valeur inconnue : {scopeText} (attendu : progress ou weak-points).");
        if (!VisibilityRules.CanOptOut(scope))
            return Error(400, "Les résultats des évaluations assignées restent partagés tant que tu es inscrit.");

        var user = await db.CurrentUserAsync(principal);
        if (user is null) return Results.Unauthorized();

        // La relation doit exister (ou avoir existé) : sinon on ne dit rien de l'espace ou du compte visé
        var related = isOrganization
            ? await db.OrganizationStudents.AnyAsync(s => s.OrganizationId == id && s.UserId == user.Id)
              || await db.ClassroomMembers.IgnoreQueryFilters()
                  .AnyAsync(m => m.StudentId == user.Id && db.Classrooms.Any(c => c.Id == m.ClassroomId && c.OrganizationId == id))
            : await db.ParentChildLinks.AnyAsync(l => l.ChildId == user.Id && l.ParentId == id && l.Status == LinkStatus.Active);
        if (!related) return Error(404, "Introuvable.");

        if (!await visibility.IsDeemedAdultAsync(user))
            return Error(403, "Ton parent doit faire ce changement.");

        var existing = isOrganization
            ? db.SharingOptOuts.Where(o => o.LearnerId == user.Id && o.Scope == scope && o.OrganizationId == id)
            : db.SharingOptOuts.Where(o => o.LearnerId == user.Id && o.Scope == scope && o.GuardianId == id);
        if (share)
        {
            await existing.ExecuteDeleteAsync();
            return Results.NoContent();
        }

        if (await existing.AnyAsync()) return Results.NoContent();
        db.SharingOptOuts.Add(new SharingOptOut
        {
            LearnerId = user.Id,
            OrganizationId = isOrganization ? id : null,
            GuardianId = isOrganization ? null : id,
            Scope = scope
        });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Double clic ou deux onglets : l'index unique a gardé le premier retrait, l'état voulu est atteint
        }
        return Results.NoContent();
    }

    public static async Task<HistoryDto> HistoryAsync(UserContext db, VisibilityService visibility, AppUser user)
    {
        var options = visibility.Options;
        var now = DateTime.UtcNow;

        var periods = await db.ClassroomMembers.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.StudentId == user.Id)
            .Join(db.Classrooms, m => m.ClassroomId, c => c.Id, (m, c) => new { m.ClassroomId, c.Name, c.OrganizationId, m.JoinedAt, m.LeftAt })
            .ToListAsync();
        var rows = await db.OrganizationStudents.AsNoTracking().Where(s => s.UserId == user.Id).ToListAsync();

        var orgIds = periods.Select(p => p.OrganizationId).OfType<Guid>().Concat(rows.Select(r => r.OrganizationId)).Distinct().ToList();
        var orgs = await db.Organizations.AsNoTracking().Where(o => orgIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id);
        var optOuts = await db.SharingOptOuts.AsNoTracking().Where(o => o.LearnerId == user.Id).ToListAsync();

        var spaces = new List<HistorySpaceDto>();
        foreach (var orgId in orgIds.Where(orgs.ContainsKey))
        {
            var org = orgs[orgId];
            var mine = periods.Where(p => p.OrganizationId == orgId).ToList();
            var row = rows.FirstOrDefault(r => r.OrganizationId == orgId);
            var current = row?.Status is StudentStatus.Active or StudentStatus.Pending || mine.Any(p => p.LeftAt is null);
            DateTime? leftAt = current ? null : row?.LeftAt ?? mine.Max(p => p.LeftAt);
            var removed = optOuts.Where(o => o.OrganizationId == orgId).Select(o => o.Scope).ToList();
            var scopes = VisibilityRules.VisibleScopes(ViewerKind.Manager, current,
                VisibilityRules.FormerStillVisible(leftAt, now, options), removed);
            var classes = mine.GroupBy(p => new { p.ClassroomId, p.Name })
                .Select(g => new HistoryClassDto(g.Key.ClassroomId, g.Key.Name,
                    g.OrderBy(p => p.JoinedAt).Select(p => new MemberPeriodDto(p.JoinedAt, p.LeftAt)).ToList()))
                .OrderByDescending(c => c.Periods.Max(p => p.LeftAt ?? DateTime.MaxValue))
                .ToList();
            var since = mine.Select(p => (DateTime?)p.JoinedAt).Append(row?.ActivatedAt).Where(d => d is not null).Min();
            spaces.Add(new HistorySpaceDto(orgId, org.Name, org.Type, current, since, leftAt,
                VisibilityRules.VisibleUntil(leftAt, options), VisibilityService.Names(scopes), VisibilityService.Names(removed), classes));
        }

        var guardians = await db.ParentChildLinks.AsNoTracking()
            .Where(l => l.ChildId == user.Id && l.Status == LinkStatus.Active)
            .Join(db.AppUsers, l => l.ParentId, u => u.Id, (l, u) => new { u.Id, u.Username })
            .ToListAsync();
        var guardianDtos = guardians.Select(g =>
        {
            var removed = optOuts.Where(o => o.GuardianId == g.Id).Select(o => o.Scope).ToList();
            return new HistoryGuardianDto(g.Id, g.Username,
                VisibilityService.Names(VisibilityRules.VisibleScopes(ViewerKind.Guardian, true, true, removed)),
                VisibilityService.Names(removed));
        }).ToList();

        return new HistoryDto(await visibility.IsDeemedAdultAsync(user), options.RetentionMonths,
            spaces.OrderByDescending(s => s.Current).ThenByDescending(s => s.LeftAt).ToList(), guardianDtos);
    }

    private static IResult Error(int status, string message) => Results.Json(new { message }, statusCode: status);
}
