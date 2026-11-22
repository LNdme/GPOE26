using User.Model;

namespace User.Service;

/// <summary>Qui regarde les résultats d'une personne (docs/PLAN-APPRENANT-AU-CENTRE.md, §3).</summary>
public enum ViewerKind
{
    /// <summary>La personne elle-même : tout, toujours.</summary>
    Self,
    /// <summary>Enseignant de la classe.</summary>
    Teacher,
    /// <summary>Responsable ou gestionnaire de l'espace de la classe.</summary>
    Manager,
    /// <summary>Parent rattaché (lien actif).</summary>
    Guardian
}

/// <summary>Réglages : <c>Visibility:RetentionMonths</c> (12 par défaut, choix T4) et <c>Visibility:Enabled</c> (retour arrière).</summary>
public record VisibilityOptions(int RetentionMonths = 12, bool Enabled = true)
{
    public static VisibilityOptions From(IConfiguration configuration) => new(
        Math.Clamp(configuration.GetValue("Visibility:RetentionMonths", 12), 0, 120),
        configuration.GetValue("Visibility:Enabled", true));
}

/// <summary>
/// Règles pures de visibilité (lot J3), testées sans base. Le départ ne détruit rien : la personne garde tout,
/// l'organisation garde la période où la personne était là, jusqu'à la fin de la conservation, et rien de nouveau.
/// Les discussions avec le tuteur ne sont jamais une portée : personne d'autre ne les voit.
/// </summary>
public static class VisibilityRules
{
    public static readonly SharingScope[] AllScopes = [SharingScope.OfficialResults, SharingScope.Progress, SharingScope.WeakPoints];

    /// <summary>Date jusqu'à laquelle l'organisation voit encore un ancien membre (null : membre actuel).</summary>
    public static DateTime? VisibleUntil(DateTime? leftAt, VisibilityOptions options) =>
        leftAt?.AddMonths(options.RetentionMonths);

    /// <summary>Membre actuel, ou ancien membre dont la conservation court encore (et retour arrière non demandé).</summary>
    public static bool FormerStillVisible(DateTime? leftAt, DateTime now, VisibilityOptions options) =>
        leftAt is null || (options.Enabled && now < leftAt.Value.AddMonths(options.RetentionMonths));

    /// <summary>
    /// Un enseignant ne voit un ancien élève que s'il était déjà affecté à la classe avant son départ (M1) :
    /// un enseignant arrivé ensuite ne l'a jamais eu.
    /// </summary>
    public static bool TeacherKnewMember(DateTime teacherAssignedAt, DateTime? memberLeftAt) =>
        memberLeftAt is null || teacherAssignedAt <= memberLeftAt.Value;

    /// <summary>Seules la maîtrise et les notions fragiles se retirent ; les résultats officiels jamais (décision D3).</summary>
    public static bool CanOptOut(SharingScope scope) => scope != SharingScope.OfficialResults;

    /// <summary>
    /// Portées visibles pour un regard donné. current : la personne est encore dans la classe ou l'espace.
    /// Un ancien membre ne montre jamais ses notions fragiles : elles sont réécrites en continu et ne se datent pas (H3).
    /// </summary>
    public static IReadOnlyList<SharingScope> VisibleScopes(ViewerKind viewer, bool current, bool stillVisible,
        IEnumerable<SharingScope> optedOut)
    {
        if (viewer == ViewerKind.Self) return AllScopes;
        if (!current && !stillVisible) return [];
        var removed = optedOut.Where(CanOptOut).ToHashSet();
        return AllScopes
            .Where(s => !removed.Contains(s))
            .Where(s => current || s != SharingScope.WeakPoints)
            .ToList();
    }

    /// <summary>
    /// Majorité réputée (choix T3). Avec l'année de naissance : majeure si elle a au moins 18 ans même née le
    /// 31 décembre (prudent). Sinon : mineure si sa fiche d'établissement dit moins de 18 ans, ou si un parent
    /// est rattaché ; majeure dans les autres cas.
    /// </summary>
    public static bool IsDeemedAdult(int? birthYear, DateOnly? schoolBirthDate, bool hasActiveGuardian, DateOnly today)
    {
        if (birthYear is { } year) return today.Year - year - 1 >= 18;
        if (schoolBirthDate is { } born) return AgeOn(born, today) >= 18;
        return !hasActiveGuardian;
    }

    public static int AgeOn(DateOnly born, DateOnly today)
    {
        var age = today.Year - born.Year;
        return born.AddYears(age) > today ? age - 1 : age;
    }

    /// <summary>Nom d'une portée dans les routes (/me/sharing/…/{scope}) ; null si inconnu.</summary>
    public static SharingScope? ParseScope(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "progress" => SharingScope.Progress,
        "weak-points" => SharingScope.WeakPoints,
        "official-results" => SharingScope.OfficialResults,
        _ => null
    };

    public static string Name(SharingScope scope) => scope switch
    {
        SharingScope.Progress => "progress",
        SharingScope.WeakPoints => "weak-points",
        _ => "official-results"
    };
}
