using System.ComponentModel.DataAnnotations;
using User.Model;
using User.Service;

namespace User.DTOs;

// ══════════════════════════════════════════════════════════════════════════════
// Classes / groupes, rattachement parent ↔ enfant, abonnements et administration.
// ══════════════════════════════════════════════════════════════════════════════

// ─── Classes et groupes ───────────────────────────────────────────────────────

public record CreateClassroomRequest(
    [Required, MinLength(1), MaxLength(120)] string Name,
    string? Level = null,
    SchoolSubSystem? SubSystem = null,
    string? Subject = null
);

public record JoinClassroomRequest([Required] string Code);

/// <summary>InviteCode n'est renseigné que pour l'enseignant propriétaire.</summary>
public record ClassroomDto(
    Guid Id,
    string Name,
    ClassroomKind Kind,
    string? Level,
    SchoolSubSystem? SubSystem,
    string? Subject,
    string? InviteCode,
    Guid TeacherId,
    string TeacherName,
    int MemberCount,
    DateTime CreatedAt
);

public record ClassroomMemberDto(
    Guid Id,
    string Username,
    string? Level,
    string? Filiere,
    DateTime JoinedAt,
    DateTime? LastLoginAt
);

// ─── Parents ──────────────────────────────────────────────────────────────────

public record LinkChildRequest([Required] string Code);

/// <summary>Le parent crée lui-même le compte de son enfant (lien actif immédiatement).</summary>
public record CreateChildRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(3), MaxLength(64)] string Username,
    [Required, MinLength(8)] string Password,
    SchoolSubSystem? SubSystem = null,
    string? Level = null,
    string? Filiere = null,
    string? ExamTarget = null
);

public record ChildLinkDto(
    Guid LinkId,
    LinkStatus Status,
    Guid ChildId,
    string Username,
    string? Level,
    string? Filiere,
    string? ExamTarget,
    bool IsPremium
);

public record ParentLinkDto(Guid LinkId, LinkStatus Status, Guid ParentId, string ParentName, DateTime CreatedAt);

// ─── Abonnements ──────────────────────────────────────────────────────────────

public record RedeemKeyRequest([Required] string Code);

public record CheckoutRequest(SubscriptionPlan Plan, BillingPeriod Period = BillingPeriod.Monthly);

public record SubscriptionKeyDto(string Code, int UsedCount, int MaxUses, bool IsRevoked)
{
    public SubscriptionKeyDto(SubscriptionKey key) : this(key.Code, key.UsedCount, key.MaxUses, key.IsRevoked) { }
}

public record SubscriptionDto(
    Guid Id,
    SubscriptionPlan Plan,
    BillingPeriod Period,
    SubscriptionStatus Status,
    int Seats,
    DateTime? StartsAt,
    DateTime? EndsAt,
    Guid? OrganizationId,
    List<SubscriptionKeyDto> Keys
)
{
    public SubscriptionDto(Subscription s, IEnumerable<SubscriptionKey> keys) : this(
        s.Id, s.Plan, s.Period, s.Status, s.Seats, s.StartsAt, s.EndsAt, s.OrganizationId,
        keys.Select(k => new SubscriptionKeyDto(k)).ToList())
    { }
}

public record MySubscriptionsDto(Entitlement Entitlement, List<SubscriptionDto> Owned);

// ─── Administration ───────────────────────────────────────────────────────────

public record CreateOrganizationRequest(
    [Required, MinLength(2), MaxLength(200)] string Name,
    OrganizationType Type = OrganizationType.School,
    string? City = null
);

public record CreateLicenseRequest([Range(1, 100000)] int Seats, [Range(1, 36)] int Months = 12);

public record ActivateSubscriptionRequest([Range(1, 36)] int? Months = null);

public record OrganizationDto(Guid Id, string Name, OrganizationType Type, string? City, List<SubscriptionDto> Licenses);
