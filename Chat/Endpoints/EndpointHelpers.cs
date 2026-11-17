using System.Security.Claims;

namespace Chat.Endpoints;

/// <summary>Petites fonctions communes aux routes du service (avant, recopiées dans chaque fichier).</summary>
public static class EndpointHelpers
{
    /// <summary>Identifiant de la personne connectée (claim « sub ») ; jeton sans identifiant : 401.</summary>
    public static Guid UserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new UnauthorizedAccessException();

    /// <summary>Erreur au format attendu par l'interface : { "message": "…" }.</summary>
    public static IResult Error(int status, string message) => Results.Json(new { message }, statusCode: status);
}
