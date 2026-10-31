using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Quiz.Data;
using Quiz.Model;

namespace Quiz.Endpoints;

public static class Roles
{
    public const string Student = "Student";
    public const string Teacher = "Teacher";
    public const string Parent = "Parent";

    public static IServiceCollection AddRolePolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(Student, p => p.RequireRole(Student))
            .AddPolicy(Teacher, p => p.RequireRole(Teacher))
            .AddPolicy(Parent, p => p.RequireRole(Parent));
        return services;
    }
}

public static class EndpointHelpers
{
    /// <summary>Id utilisateur (claim "sub" — MapInboundClaims est désactivé)</summary>
    public static Guid UserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new UnauthorizedAccessException();

    /// <summary>Évaluation avec ses questions, appartenant à l'enseignant (null sinon)</summary>
    public static Task<Assessment?> OwnedAssessmentAsync(this EvaluationContext db, Guid id, Guid teacherId) =>
        db.Assessments.Include(a => a.Questions).FirstOrDefaultAsync(a => a.Id == id && a.TeacherId == teacherId);

    public static IResult Error(int status, string message) => Results.Json(new { message }, statusCode: status);
}
