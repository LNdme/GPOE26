using System.Security.Claims;

namespace GPOE26.ServiceDefaults;

/// <summary>
/// Lecture des claims d'identité communs à tous les services.
///
/// C'est ici que vit la règle « qui a le droit de voir les données d'étude de qui ».
/// Elle est unique et partagée à dessein : dupliquée dans chaque service, elle finirait
/// par diverger, et c'est exactement le genre de divergence qui ouvre un accès aux
/// données d'un enfant qui n'est pas le sien.
/// </summary>
public static class StudyIdentity
{
    /// <summary>Élèves qu'un parent est autorisé à suivre, portés par le jeton.</summary>
    public const string ChildrenClaim = "children";

    /// <summary>Rôle, émis par le service User sous le nom « role ».</summary>
    public const string RoleClaim = "role";

    /// <summary>Identifiant de l'utilisateur connecté.</summary>
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? principal.FindFirstValue("sub");

        return Guid.TryParse(value, out var id) ? id : null;
    }

    /// <summary>
    /// Rôle déclaré dans le jeton.
    ///
    /// On lit le claim par son nom plutôt que de passer par [Authorize(Roles=…)] :
    /// le service User émet « role », et dépendre de la table de correspondance
    /// implicite du gestionnaire JWT rendrait l'autorisation sensible à une option
    /// de configuration qu'aucun de ces fichiers ne mentionne.
    /// </summary>
    public static string? GetRole(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(RoleClaim)
        ?? principal.FindFirstValue(ClaimTypes.Role);

    public static bool IsParent(this ClaimsPrincipal principal) =>
        string.Equals(principal.GetRole(), "Parent", StringComparison.OrdinalIgnoreCase);

    /// <summary>Les élèves listés dans le claim « children ».</summary>
    public static IReadOnlyCollection<Guid> GetLinkedChildren(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ChildrenClaim);
        if (string.IsNullOrWhiteSpace(value)) return [];

        var children = new List<Guid>();
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Guid.TryParse(part, out var id)) children.Add(id);
        }

        return children;
    }

    /// <summary>
    /// L'appelant peut-il consulter les données d'étude de <paramref name="studentId"/> ?
    ///
    /// Vrai pour l'élève lui-même, et pour un parent auquel il s'est rattaché.
    /// Toute autre situation est un refus — y compris un parent qui demanderait
    /// l'identifiant d'un enfant qui n'est pas le sien.
    /// </summary>
    public static bool CanViewStudent(this ClaimsPrincipal principal, Guid studentId)
    {
        if (principal.GetUserId() is { } self && self == studentId) return true;

        return principal.IsParent() && principal.GetLinkedChildren().Contains(studentId);
    }

    /// <summary>
    /// Élève dont on consulte les données : soi-même par défaut, ou l'enfant demandé
    /// si le lien existe. Renvoie null quand l'accès doit être refusé.
    /// </summary>
    public static Guid? ResolveStudentId(this ClaimsPrincipal principal, Guid? requested)
    {
        var self = principal.GetUserId();
        if (self is null) return null;

        if (requested is null || requested == self) return self;

        return principal.CanViewStudent(requested.Value) ? requested : null;
    }
}
