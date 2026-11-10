using System.Security.Claims;

namespace GPOE26.ApiService;

/// <summary>
/// Qui a le droit de modifier le contenu du site.
///
/// Ce service portait dix-huit endpoints d'écriture et un dépôt de fichiers sans la
/// moindre authentification : n'importe qui pouvant l'atteindre publiait sur le site du
/// lycée. Ce fichier est la règle qui ferme cette porte.
///
/// Deux niveaux, et la distinction porte un sens éditorial plutôt que technique :
///
///  · **Rédaction** — actualités, événements, activités. Ce qui s'écrit chaque semaine
///    et se corrige sans conséquence durable.
///  · **Administration** — l'équipe de direction, les coordonnées, les tarifs, les
///    comptes. Ce qui engage l'établissement, et qu'on ne confie pas à qui alimente
///    simplement la page d'actualités.
///
/// Les lectures ne sont jamais couvertes : c'est un site public, et le demander à un
/// visiteur de s'authentifier pour lire une actualité n'aurait aucun sens.
///
/// Écrit comme une **fonction pure**, sur le modèle de
/// <c>GPOE26.ServiceDefaults.StudyIdentity.CanViewStudent</c> : une table de décision
/// testable sans réseau ni base de données, et qui vaut justement parce qu'elle l'est.
/// </summary>
public static class ContentPolicies
{
    /// <summary>Publier du contenu éditorial : actualités, événements, activités.</summary>
    public const string Redaction = "Redaction";

    /// <summary>Gouverner le site : équipe, coordonnées, tarifs, comptes.</summary>
    public const string Administration = "Administration";

    /// <summary>
    /// Le claim de rôle, lu sous le nom qu'émet le service User.
    ///
    /// On ne passe pas par <c>ClaimTypes.Role</c> seul : la correspondance dépend d'une
    /// option du gestionnaire JWT qu'aucun fichier de ce dépôt ne mentionne, et une
    /// autorisation qui dépend d'un réglage invisible finit par s'ouvrir ou se fermer
    /// sans que personne ne comprenne pourquoi.
    /// </summary>
    public const string RoleClaim = "role";

    /// <summary>
    /// Rôles qui peuvent publier. Les noms sont ceux de <c>User.Model.UserRole</c>,
    /// recopiés plutôt que référencés : ce service ne dépend pas du service User, et
    /// lui donner cette dépendance pour deux chaînes de caractères coûterait plus que
    /// cela ne rapporte. La suite de vérification compare les deux listes.
    /// </summary>
    public static readonly IReadOnlySet<string> Redacteurs =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Admin", "Editor" };

    /// <summary>Rôles qui peuvent gouverner. Un administrateur, et lui seul.</summary>
    public static readonly IReadOnlySet<string> Administrateurs =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Admin" };

    /// <summary>Le rôle porté par le jeton, ou null.</summary>
    public static string? RoleOf(ClaimsPrincipal principal) =>
        principal.FindFirstValue(RoleClaim) ?? principal.FindFirstValue(ClaimTypes.Role);

    /// <summary>
    /// L'appelant porte-t-il l'un de ces rôles ?
    ///
    /// Un appelant non authentifié n'a pas de rôle et n'en porte donc aucun : il reçoit
    /// 401. Un appelant authentifié dont le rôle n'est pas dans la liste reçoit 403. La
    /// distinction compte — l'un doit se connecter, l'autre n'a rien à faire là.
    /// </summary>
    public static bool IsIn(ClaimsPrincipal principal, IReadOnlySet<string> roles)
    {
        if (principal.Identity?.IsAuthenticated != true) return false;

        var role = RoleOf(principal);
        return role is not null && roles.Contains(role);
    }

    /// <summary>Peut publier du contenu éditorial.</summary>
    public static bool CanPublish(ClaimsPrincipal principal) => IsIn(principal, Redacteurs);

    /// <summary>Peut gouverner le site.</summary>
    public static bool CanAdminister(ClaimsPrincipal principal) => IsIn(principal, Administrateurs);
}
