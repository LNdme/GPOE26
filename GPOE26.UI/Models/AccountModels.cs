namespace GPOE26.UI.Models;

// ══════════════════════════════════════════════════════════════════════════════
// Contrats du service User (enums sérialisés en texte, propriétés en camelCase).
// ══════════════════════════════════════════════════════════════════════════════

public enum UserRole { Student, Teacher, Parent, Admin }
public enum SchoolSubSystem { Francophone, Anglophone }
public enum TeachingContext { School, Institution, Independent }
public enum ClassroomKind { Class, Group }
public enum LinkStatus { Pending, Active, Rejected }
public enum SubscriptionPlan { StudentPremium, Family, Teacher, OrganizationLicense }
public enum BillingPeriod { Monthly, Yearly }
public enum SubscriptionStatus { PendingPayment, Trial, Active, Expired, Cancelled }

public record LoginRequest(string Email, string Password);

public record RegisterRequest
{
    public required string Email { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
    public UserRole Role { get; init; } = UserRole.Student;
    public string? Level { get; init; }
    public string? Filiere { get; init; }
    public SchoolSubSystem? SubSystem { get; init; }
    public string? ExamTarget { get; init; }
    public List<string>? FocusSubjects { get; init; }
    public int? DailyStudyMinutes { get; init; }
    public string? InviteCode { get; init; }
    public List<string>? TeachingSubjects { get; init; }
    public TeachingContext? TeachingContext { get; init; }
    public string? OrganizationName { get; init; }
    public List<string>? InitialClasses { get; init; }
    public bool StartTrial { get; init; }
    public string? SubscriptionKey { get; init; }
}

public record UserProfile(
    Guid Id,
    string Email,
    string Username,
    UserRole Role,
    string? Level,
    string? Specialite,
    string? Filiere,
    string Language,
    DateTime CreatedAt,
    SchoolSubSystem? SubSystem,
    string? ExamTarget,
    DateOnly? ExamDate,
    List<string>? FocusSubjects,
    int? DailyStudyMinutes,
    string? LinkCode,
    List<string>? TeachingSubjects,
    TeachingContext? TeachingContext,
    Guid? OrganizationId,
    string? OrganizationName,
    bool IsPremium,
    SubscriptionPlan? Plan,
    DateTime? PremiumUntil,
    List<EntitledFeature>? Features = null
);

public record AuthResponse(string Token, DateTime ExpiresAt, UserProfile Profile);

// ─── Classes ──────────────────────────────────────────────────────────────────

public record Classroom(
    Guid Id, string Name, ClassroomKind Kind, string? Level, SchoolSubSystem? SubSystem, string? Subject,
    string? InviteCode, Guid TeacherId, string TeacherName, int MemberCount, DateTime CreatedAt);

public record ClassroomMember(Guid Id, string Username, string? Level, string? Filiere, DateTime JoinedAt, DateTime? LastLoginAt);

public record CreateClassroomRequest(string Name, string? Level = null, SchoolSubSystem? SubSystem = null, string? Subject = null);

// ─── Parents ──────────────────────────────────────────────────────────────────

public record ChildLink(Guid LinkId, LinkStatus Status, Guid ChildId, string Username, string? Level, string? Filiere,
    string? ExamTarget, bool IsPremium);

public record ParentLink(Guid LinkId, LinkStatus Status, Guid ParentId, string ParentName, DateTime CreatedAt);

public record CreateChildRequest(string Email, string Username, string Password, SchoolSubSystem? SubSystem = null,
    string? Level = null, string? Filiere = null, string? ExamTarget = null);

// ─── Abonnements ──────────────────────────────────────────────────────────────

public record EntitledFeature(string Code, int? Limit);

public record Entitlement(bool IsPremium, SubscriptionPlan? Plan, DateTime? Until, string? Source, List<EntitledFeature>? Features = null);

public record PlanFeature(string Code, string Label, int? Limit);

/// <summary>Formule d'abonnement (catalogue géré par le service du site).</summary>
public record PlanInfo(int Id, string Code, string Name, string Category, string? Description, int? PriceMonthly, int? PriceYearly,
    string Currency, int Seats, int DisplayOrder, List<PlanFeature> Features);

public record PlanChoice(PlanInfo Plan, BillingPeriod Period);

public record SimulatePaymentRequest(SubscriptionPlan Plan, BillingPeriod Period, string Provider);

public record SimulatedPayment(string Code, SubscriptionPlan Plan, BillingPeriod Period, string Provider, int Seats, DateTime? EndsAt);

public record SubscriptionKeyInfo(string Code, int UsedCount, int MaxUses, bool IsRevoked);

public record SubscriptionInfo(Guid Id, SubscriptionPlan Plan, BillingPeriod Period, SubscriptionStatus Status, int Seats,
    DateTime? StartsAt, DateTime? EndsAt, Guid? OrganizationId, List<SubscriptionKeyInfo> Keys);

public record MySubscriptions(Entitlement Entitlement, List<SubscriptionInfo> Owned, bool PaymentSimulation = false);

public record CheckoutRequest(SubscriptionPlan Plan, BillingPeriod Period);
