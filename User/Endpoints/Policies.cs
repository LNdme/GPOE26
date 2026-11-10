using System.Security.Claims;
using User.Data;
using User.Helper;
using User.Model;

namespace User.Endpoints;

/// <summary>Politiques d'autorisation par rôle (claim "role" du JWT).</summary>
public static class Policies
{
    public const string Student = nameof(UserRole.Student);
    public const string Teacher = nameof(UserRole.Teacher);
    public const string Parent = nameof(UserRole.Parent);

    public static IServiceCollection AddRolePolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(Student, p => p.RequireRole(Student))
            .AddPolicy(Teacher, p => p.RequireRole(Teacher))
            .AddPolicy(Parent, p => p.RequireRole(Parent));
        return services;
    }

    /// <summary>Utilisateur courant depuis le JWT (null si le compte n'existe plus).</summary>
    public static async Task<AppUser?> CurrentUserAsync(this UserContext db, ClaimsPrincipal principal) =>
        principal.GetUserId() is { } id ? await db.AppUsers.FindAsync(id) : null;

    /// <summary>
    /// Réserve un groupe d'endpoints aux administrateurs, vérifiés EN BASE à chaque appel et non d'après le jeton :
    /// un droit retiré prend effet immédiatement, un droit donné aussi, sans attendre une reconnexion.
    /// </summary>
    public static TBuilder RequireAdmin<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization().AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var db = http.RequestServices.GetRequiredService<UserContext>();
            var user = await db.CurrentUserAsync(http.User);
            return user is { IsAdmin: true }
                ? await next(context)
                : Results.Json(new { message = "Cette action est réservée aux administrateurs." }, statusCode: StatusCodes.Status403Forbidden);
        });
}
