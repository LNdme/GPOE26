namespace GPOE26.UI.Models;

// ══════════════════════════════════════════════════════════════════════════════
// Contrats du service User (enums sérialisés en texte, propriétés en camelCase).
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>Rôle sur la plateforme. Administrer est un droit à part (UserProfile.IsAdmin), qui ne remplace pas le rôle.</summary>
public enum UserRole { Student, Teacher, Parent }
public enum SchoolSubSystem { Francophone, Anglophone }
public enum EducationStage { Secondary, Higher }
public enum TeachingContext { School, Institution, Independent }
public enum LevelBand { Primary, LowerSecondary, UpperSecondary, Higher, Adult }
public enum LearningGoal { Exam, Skill, Curiosity }
public enum GradeScale { Over20, Over10, Over100, Letters }
public enum ClassroomKind { Class, Group }
public enum LinkStatus { Pending, Active, Rejected }
public enum SubscriptionPlan { StudentPremium, Family, Teacher, OrganizationLicense }
public enum BillingPeriod { Monthly, Yearly }
public enum SubscriptionStatus { PendingPayment, Trial, Active, Expired, Cancelled }
public enum WorkspaceKind { Personal, Family, Organization }
public enum WorkspaceRole { Owner, Admin, Guardian, Learner, Instructor, Member }

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
    public List<string>? TeachingSubjects { get; init; }
    public TeachingContext? TeachingContext { get; init; }
    public string? OrganizationName { get; init; }
    public List<string>? InitialClasses { get; init; }
    public bool StartTrial { get; init; }
    public string? SubscriptionKey { get; init; }
    public EducationStage? Stage { get; init; }
    // Contexte d'apprentissage (lot B5) : pays choisi d'abord, fuseau du navigateur
    public string? Country { get; init; }
    public string? TimeZone { get; init; }
    public string? EducationSystem { get; init; }
    public LevelBand? LevelBand { get; init; }
    public LearningGoal? Goal { get; init; }
}

/// <summary>Modification du profil (page « Mon profil ») : null = valeur inchangée, chaîne vide = valeur effacée.</summary>
public record EditProfileRequest
{
    public string? Username { get; init; }
    public string? Language { get; init; }
    public string? Level { get; init; }
    public string? Filiere { get; init; }
    public SchoolSubSystem? SubSystem { get; init; }
    public string? ExamTarget { get; init; }
    public DateOnly? ExamDate { get; init; }
    public List<string>? FocusSubjects { get; init; }
    public int? DailyStudyMinutes { get; init; }
    public string? Specialite { get; init; }
    public List<string>? TeachingSubjects { get; init; }
    public TeachingContext? TeachingContext { get; init; }
    // Contexte d'apprentissage : null = inchangé, chaîne vide = effacé (la valeur redevient déduite)
    public string? Country { get; init; }
    public string? EducationSystem { get; init; }
    public string? TimeZone { get; init; }
    public GradeScale? GradeScale { get; init; }
    public LevelBand? LevelBand { get; init; }
    public LearningGoal? Goal { get; init; }
    /// <summary>Lot J3 : année de naissance facultative ; 0 l'efface.</summary>
    public int? BirthYear { get; init; }
}

/// <summary>
/// Contexte d'apprentissage effectif (pays, système, tranche de niveau, objectif, échelle de notes, fuseau).
/// <see cref="Inferred"/> nomme les champs déduits de l'ancien profil plutôt que choisis.
/// </summary>
public record LearningContext(
    string? Country, string? CountryName, string? EducationSystem, string? Level, LevelBand? LevelBand, LearningGoal? Goal,
    string? ExamTarget, DateOnly? ExamDate, GradeScale GradeScale, string? TimeZone, List<string>? Inferred);

/// <summary>Préréglage d'un pays (service User) : valeurs proposées par défaut, toutes modifiables.</summary>
public record CountryPreset(string Code, string Name, string Language, GradeScale GradeScale, string TimeZone,
    List<string> Systems, List<string> Exams);

public record UserProfile(
    Guid Id,
    string? Email,
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
    List<EntitledFeature>? Features = null,
    EducationStage? Stage = null,
    bool SiteEditor = false,
    bool IsAdmin = false,
    bool MustChangePassword = false,
    LearningContext? LearningContext = null,
    List<string>? Hats = null,
    int? BirthYear = null
)
{
    /// <summary>Peut gérer le contenu du site de l'établissement (administrateur ou éditeur).</summary>
    public bool CanEditSite => IsAdmin || SiteEditor;

    /// <summary>
    /// Casquettes (docs/PLAN-PUBLICS.md, lot P3). Un profil enregistré avant P3 n'en a pas : elles sont alors
    /// déduites du rôle, comme le fait le service User (HatRules.FromRole).
    /// </summary>
    public IReadOnlyList<string> EffectiveHats => Hats is { Count: > 0 } hats ? hats : UserHats.FromRole(Role);
}

/// <summary>Casquettes d'une personne : toute personne apprend, et peut en plus suivre des enfants, enseigner, gérer, payer.</summary>
public static class UserHats
{
    public const string Learner = "learner";
    public const string Guardian = "guardian";
    public const string Teacher = "teacher";
    public const string Manager = "manager";
    public const string Payer = "payer";

    public static IReadOnlyList<string> FromRole(UserRole role) => role switch
    {
        UserRole.Teacher => [Learner, Teacher],
        UserRole.Parent => [Learner, Guardian],
        _ => [Learner]
    };
}

public record AuthResponse(string Token, DateTime ExpiresAt, UserProfile Profile, WorkspaceSummary? ActiveWorkspace = null);

public record WorkspaceSummary(
    Guid Id,
    string Name,
    WorkspaceKind Kind,
    Guid? OrganizationId,
    IReadOnlyList<WorkspaceRole> Roles,
    DateTime? JoinedAt);

public record McpWorkspaceMember(Guid UserId, string Username, IReadOnlyList<WorkspaceRole> Roles);

public record McpActiveGrant(
    string ExtensionId,
    IReadOnlyList<string> CapabilityIds,
    IReadOnlyList<string> PermissionIds,
    DateTime ApprovedAt);

public record McpExtensionSummary(
    string Id,
    string Name,
    string Description,
    string PublisherName,
    IReadOnlyList<McpCapabilitySummary> Capabilities,
    IReadOnlyList<McpPermissionSummary> Permissions);

public record McpCapabilitySummary(
    string Id,
    string Description,
    IReadOnlyList<string> RequiredPermissionIds);

public record McpPermissionSummary(string Id, string Description, bool Required);

public record McpProcessConfigurationSummary(
    Guid UserId,
    Guid WorkspaceId,
    string ExtensionId,
    bool IsVerified,
    bool IsSandboxed,
    DateTime UpdatedAt);

public record ConfigureMcpProcessRequest(
    Guid UserId,
    Guid WorkspaceId,
    string ExtensionId,
    string ImageReference,
    IReadOnlyList<string>? Arguments,
    IReadOnlyDictionary<string, string?>? EnvironmentVariables);

// ─── Classes ──────────────────────────────────────────────────────────────────

/// <summary>
/// Classe vue par l'utilisateur. Une classe peut avoir plusieurs enseignants (Teachers) ; TeacherName est le professeur
/// principal. Les élèves y entrent avec leur code personnel. SpaceType : établissement ou enseignant indépendant.
/// </summary>
public record Classroom(
    Guid Id, string Name, ClassroomKind Kind, string? Level, SchoolSubSystem? SubSystem, string? Subject,
    Guid TeacherId, string TeacherName, int MemberCount, DateTime CreatedAt,
    ClassroomRole? MyRole = null, List<string>? MySubjects = null, List<ClassTeacher>? Teachers = null,
    Guid? OrganizationId = null, Guid? SchoolYearId = null, OrganizationType? SpaceType = null, bool IsManager = false,
    string? OrganizationName = null)
{
    /// <summary>
    /// Espace de la classe, tel que l'enseignant le lit (lot 13) : le nom de l'établissement, « Mon espace » pour son
    /// espace d'enseignant indépendant, « Autres classes » sans espace.
    /// </summary>
    public string SpaceLabel => SpaceType == OrganizationType.Independent ? "Mon espace"
        : OrganizationName is { Length: > 0 } name ? name : "Autres classes";

    /// <summary>Ordre des groupes : établissements, puis l'espace personnel, puis le reste.</summary>
    public int SpaceOrder => SpaceType switch { OrganizationType.Independent => 1, null => 2, _ => 0 };
}

/// <summary>Une période d'appartenance à une classe (lot J2) ; LeftAt vide : en cours.</summary>
public record MemberPeriod(DateTime JoinedAt, DateTime? LeftAt);

/// <summary>LeftAt / VisibleUntil : ancien élève encore visible ; Scopes : ce qu'il partage (lot J3).</summary>
public record ClassroomMember(Guid Id, string Username, string? Level, string? Filiere, DateTime JoinedAt, DateTime? LastLoginAt,
    DateTime? LeftAt = null, DateTime? VisibleUntil = null, List<MemberPeriod>? Periods = null, List<string>? Scopes = null);

public record CreateClassroomRequest(string Name, string? Level = null, SchoolSubSystem? SubSystem = null, string? Subject = null);

// ─── Parents ──────────────────────────────────────────────────────────────────

/// <summary>CoveredBy / CoveredUntil : établissement ou enseignant qui couvre déjà l'enfant ; UsesFamilySeat : il a une place de la formule Famille.</summary>
public record ChildLink(Guid LinkId, LinkStatus Status, Guid ChildId, string Username, string? Level, string? Filiere,
    string? ExamTarget, bool IsPremium, string? CoveredBy = null, DateTime? CoveredUntil = null, bool UsesFamilySeat = false,
    List<string>? Scopes = null);

// ─── Historique et partage (lot J3) ────────────────────────────────────────────

/// <summary>Ce qu'un espace ou un parent peut voir. OfficialResults ne se retire pas pendant l'inscription.</summary>
public static class SharingScopes
{
    public const string OfficialResults = "OfficialResults";
    public const string Progress = "Progress";
    public const string WeakPoints = "WeakPoints";
}

public record HistoryClass(Guid Id, string Name, List<MemberPeriod> Periods);

public record HistorySpace(Guid OrganizationId, string Name, OrganizationType Type, bool Current, DateTime? Since,
    DateTime? LeftAt, DateTime? VisibleUntil, List<string> Scopes, List<string> OptedOut, List<HistoryClass> Classes);

public record HistoryGuardian(Guid GuardianId, string Name, List<string> Scopes, List<string> OptedOut);

/// <summary>CanChangeSharing : réputé majeur ; sinon c'est au parent de changer le partage.</summary>
public record LearnerHistory(bool CanChangeSharing, int RetentionMonths, List<HistorySpace> Spaces, List<HistoryGuardian> Guardians);

public record ParentLink(Guid LinkId, LinkStatus Status, Guid ParentId, string ParentName, DateTime CreatedAt);

public record CreateChildRequest(string Email, string Username, string Password, SchoolSubSystem? SubSystem = null,
    string? Level = null, string? Filiere = null, string? ExamTarget = null,
    string? Country = null, string? TimeZone = null, LevelBand? LevelBand = null);

// ─── Abonnements ──────────────────────────────────────────────────────────────

public record EntitledFeature(string Code, int? Limit);

public record Entitlement(bool IsPremium, SubscriptionPlan? Plan, DateTime? Until, string? Source, List<EntitledFeature>? Features = null,
    List<EntitlementSource>? Sources = null, CoverageOverlap? Overlap = null);

/// <summary>Origine d'un accès Premium (service User).</summary>
public enum EntitlementSourceKind { Trial, Personal, Family, School, Teacher }

/// <summary>Label : établissement, espace de l'enseignant ou parent payeur.</summary>
public record EntitlementSource(EntitlementSourceKind Kind, SubscriptionPlan Plan, DateTime Until, string? Label);

/// <summary>Abonnement payé alors qu'un établissement ou un enseignant couvre déjà la personne (P5). PaidBy : parent payeur.</summary>
public record CoverageOverlap(string CoveredBy, DateTime CoveredUntil, EntitlementSourceKind PaidKind, DateTime PaidUntil, string? PaidBy);

/// <summary>Code personnel accepté ; Overlap si un abonnement payé devient inutile.</summary>
public record RedeemCodeResult(CoverageOverlap? Overlap);

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
