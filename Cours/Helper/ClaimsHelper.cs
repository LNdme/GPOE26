using System.Security.Claims;

namespace Cours.Helpers;

/// <summary>
/// Utilitaire partagé pour lire les claims du JWT dans les endpoints.
///
/// Dans les Minimal APIs, ClaimsPrincipal est injecté automatiquement
/// quand l'endpoint est protégé avec RequireAuthorization().
///
/// On centralise ici la logique pour ne pas la dupliquer dans chaque endpoint.
/// </summary>
public static class ClaimsHelper
{
    /// <summary>
    /// Extrait l'Id utilisateur depuis le claim "sub" du JWT.
    /// Retourne null si le token ne contient pas de sub valide.
    /// </summary>
    public static Guid? GetUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? principal.FindFirstValue("sub");

        return Guid.TryParse(value, out var id) ? id : null;
    }
}