using Microsoft.EntityFrameworkCore;
using User.Data;
using User.Model;

namespace User.Service;

/// <summary>Lectures en base pour VisibilityRules (lot J3) : retraits de partage et majorité réputée.</summary>
public class VisibilityService(UserContext db, IConfiguration configuration)
{
    public VisibilityOptions Options { get; } = VisibilityOptions.From(configuration);

    /// <summary>Portées retirées par chaque personne pour cet espace.</summary>
    public async Task<Dictionary<Guid, List<SharingScope>>> OrganizationOptOutsAsync(Guid? organizationId, IReadOnlyCollection<Guid> learnerIds)
    {
        if (organizationId is null || learnerIds.Count == 0) return [];
        return (await db.SharingOptOuts.AsNoTracking()
                .Where(o => o.OrganizationId == organizationId && learnerIds.Contains(o.LearnerId))
                .ToListAsync())
            .GroupBy(o => o.LearnerId)
            .ToDictionary(g => g.Key, g => g.Select(o => o.Scope).ToList());
    }

    /// <summary>Portées que chaque enfant a retirées à ce parent.</summary>
    public async Task<Dictionary<Guid, List<SharingScope>>> GuardianOptOutsAsync(Guid guardianId, IReadOnlyCollection<Guid> learnerIds)
    {
        if (learnerIds.Count == 0) return [];
        return (await db.SharingOptOuts.AsNoTracking()
                .Where(o => o.GuardianId == guardianId && learnerIds.Contains(o.LearnerId))
                .ToListAsync())
            .GroupBy(o => o.LearnerId)
            .ToDictionary(g => g.Key, g => g.Select(o => o.Scope).ToList());
    }

    /// <summary>Majorité réputée (choix T3) : année de naissance, sinon fiche d'établissement, sinon lien parent.</summary>
    public async Task<bool> IsDeemedAdultAsync(AppUser user)
    {
        var schoolBirthDate = await db.OrganizationStudents.AsNoTracking()
            .Where(s => s.UserId == user.Id && s.BirthDate != null)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.BirthDate)
            .FirstOrDefaultAsync();
        var hasGuardian = await db.ParentChildLinks.AnyAsync(l => l.ChildId == user.Id && l.Status == LinkStatus.Active);
        return VisibilityRules.IsDeemedAdult(user.BirthYear, schoolBirthDate, hasGuardian, DateOnly.FromDateTime(DateTime.UtcNow));
    }

    public static List<string> Names(IEnumerable<SharingScope> scopes) => scopes.Select(s => s.ToString()).ToList();
}
