using System.Security.Claims;

namespace User.Helper;

public static class ClaimsExtensions
{
    /// <summary>Id utilisateur depuis le claim "sub" du JWT (null si absent ou invalide).</summary>
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? principal.FindFirstValue("sub");

        return Guid.TryParse(value, out var id) ? id : null;
    }
}
