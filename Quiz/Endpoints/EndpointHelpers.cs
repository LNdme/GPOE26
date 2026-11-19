using System.Security.Claims;
using GPOE26.Workspaces.Abstractions;
using Microsoft.EntityFrameworkCore;
using Quiz.Data;
using Quiz.Model;

namespace Quiz.Endpoints;

public static class EndpointHelpers
{
    /// <summary>Id utilisateur (claim "sub" — MapInboundClaims est désactivé)</summary>
    public static Guid UserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new UnauthorizedAccessException();

    /// <summary>Évaluation avec ses questions, appartenant à l'enseignant (null sinon)</summary>
    public static Task<Assessment?> OwnedAssessmentAsync(
        this EvaluationContext db,
        Guid id,
        Guid teacherId,
        WorkspaceTokenContext workspace) =>
        db.Assessments.Include(a => a.Questions).FirstOrDefaultAsync(a =>
            a.Id == id &&
            a.TeacherId == teacherId &&
            (a.WorkspaceId == workspace.WorkspaceId ||
             (workspace.IsPersonal && a.WorkspaceId == null)));

    public static bool IsInWorkspace(this Assessment assessment, WorkspaceTokenContext workspace) =>
        assessment.WorkspaceId == workspace.WorkspaceId ||
        (workspace.IsPersonal && assessment.WorkspaceId is null);

    public static IResult Error(int status, string message) => Results.Json(new { message }, statusCode: status);
}
