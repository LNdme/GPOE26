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
    public const string Admin = nameof(UserRole.Admin);

    public static IServiceCollection AddRolePolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(Student, p => p.RequireRole(Student))
            .AddPolicy(Teacher, p => p.RequireRole(Teacher))
            .AddPolicy(Parent, p => p.RequireRole(Parent))
            .AddPolicy(Admin, p => p.RequireRole(Admin));
        return services;
    }

    /// <summary>Utilisateur courant depuis le JWT (null si le compte n'existe plus).</summary>
    public static async Task<AppUser?> CurrentUserAsync(this UserContext db, ClaimsPrincipal principal) =>
        principal.GetUserId() is { } id ? await db.AppUsers.FindAsync(id) : null;
}
