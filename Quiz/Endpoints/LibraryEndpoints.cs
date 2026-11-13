using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Quiz.Data;
using Quiz.DTOs;
using Quiz.Model;
using Quiz.Service;

namespace Quiz.Endpoints;

/// <summary>
/// Bibliothèque d'établissement (phase 2, lot 8, décisions P1 et P2) : un enseignant dépose un devoir ou une
/// épreuve dans la bibliothèque de son établissement ; les enseignants et gestionnaires de l'établissement la
/// consultent et en font une copie à eux. Les droits viennent du service User (GET /organizations/{id}/library).
/// </summary>
public static class LibraryEndpoints
{
    /// <summary>L'évaluation (avec ses questions) si elle est déposée dans une bibliothèque que l'utilisateur voit, sinon null.</summary>
    public static async Task<Assessment?> ReadableFromLibraryAsync(EvaluationContext db, Guid id, UserDirectoryClient users)
    {
        var assessment = await db.Assessments.Include(a => a.Questions).AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (assessment is null || assessment.LibraryOrganizationIds.Count == 0) return null;
        foreach (var orgId in assessment.LibraryOrganizationIds)
            if (await users.LibrarySpaceAsync(orgId) is { } space && AssessmentLibrary.IsVisibleIn(assessment, space))
                return assessment;
        return null;
    }

    public static void MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/assessments").WithTags("Bibliothèque").RequireAuthorization();

        // ── GET /assessments/library/{orgId} ──────────────────────────────────────
        group.MapGet("/library/{orgId:guid}", async (Guid orgId, ClaimsPrincipal principal, EvaluationContext db, UserDirectoryClient users) =>
        {
            var userId = principal.UserId();
            var space = await users.LibrarySpaceAsync(orgId);
            if (space is null) return EndpointHelpers.Error(404, "Bibliothèque introuvable.");

            var authors = space.Teachers.ToDictionary(t => t.UserId, t => t.Username);
            var authorIds = authors.Keys.ToList();
            var deposited = await db.Assessments.Include(a => a.Questions)
                .Where(a => a.LibraryOrganizationIds.Contains(orgId) && authorIds.Contains(a.TeacherId))
                .AsNoTracking()
                .ToListAsync();
            var ids = deposited.Select(a => a.Id).ToList();
            var copies = await db.Assessments
                .Where(a => a.SourceAssessmentId != null && ids.Contains(a.SourceAssessmentId.Value))
                .GroupBy(a => a.SourceAssessmentId!.Value)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count);

            return Results.Ok(new LibraryAssessmentsDto(space.Id, space.Name, space.CanManage, deposited
                .OrderBy(a => a.Subject).ThenBy(a => a.Title)
                .Select(a => new LibraryAssessmentDto(a.Id, a.Title, a.Description, a.Kind, a.Subject, a.Questions.Count,
                    a.MaxScore, a.UpdatedAt, a.TeacherId, authors[a.TeacherId], a.TeacherId == userId, copies.GetValueOrDefault(a.Id)))
                .ToList()));
        })
        .WithSummary("Devoirs et épreuves déposés dans la bibliothèque d'un établissement")
        .Produces<LibraryAssessmentsDto>()
        .Produces(404);

        // ── PUT /assessments/{id}/library ─────────────────────────────────────────
        // L'auteur choisit les bibliothèques où figure son évaluation : il doit être enseignant de chacune qu'il ajoute.
        group.MapPut("/{id:guid}/library", async (Guid id, LibraryDepositRequest req, ClaimsPrincipal principal,
            EvaluationContext db, UserDirectoryClient users) =>
        {
            var userId = principal.UserId();
            var assessment = await db.OwnedAssessmentAsync(id, userId);
            if (assessment is null) return Results.NotFound();

            var wanted = req.OrganizationIds?.Distinct().ToList() ?? new();
            foreach (var orgId in wanted.Except(assessment.LibraryOrganizationIds))
            {
                var space = await users.LibrarySpaceAsync(orgId);
                if (space is null || !space.IsTeacher(userId))
                    return EndpointHelpers.Error(400, "Vous ne pouvez déposer une évaluation que dans la bibliothèque d'un établissement où vous enseignez.");
            }

            assessment.LibraryOrganizationIds = wanted;
            await db.SaveChangesAsync();
            return Results.Ok(new AssessmentDto(assessment, withQuestions: false));
        })
        .RequireAuthorization(Roles.Teacher)
        .WithSummary("Déposer une évaluation dans des bibliothèques d'établissement (ou l'en retirer)")
        .Produces<AssessmentDto>()
        .Produces(400);

        // ── DELETE /assessments/library/{orgId}/{id} ──────────────────────────────
        // L'auteur, ou un responsable ou gestionnaire de l'établissement (Q13), retire un dépôt.
        // Seul le dépôt disparaît : l'évaluation reste à son auteur, les copies à ceux qui les ont faites.
        group.MapDelete("/library/{orgId:guid}/{id:guid}", async (Guid orgId, Guid id, ClaimsPrincipal principal,
            EvaluationContext db, UserDirectoryClient users) =>
        {
            var userId = principal.UserId();
            var assessment = await db.Assessments.FirstOrDefaultAsync(a => a.Id == id && a.LibraryOrganizationIds.Contains(orgId));
            if (assessment is null) return EndpointHelpers.Error(404, "Évaluation introuvable dans cette bibliothèque.");

            if (assessment.TeacherId != userId)
            {
                var space = await users.LibrarySpaceAsync(orgId);
                if (space is null || !AssessmentLibrary.IsVisibleIn(assessment, space))
                    return EndpointHelpers.Error(404, "Évaluation introuvable dans cette bibliothèque.");
                if (!space.CanManage) return Results.Forbid();
            }

            assessment.LibraryOrganizationIds = assessment.LibraryOrganizationIds.Where(o => o != orgId).ToList();
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithSummary("Retirer une évaluation d'une bibliothèque d'établissement")
        .Produces(204)
        .Produces(403)
        .Produces(404);

        // ── POST /assessments/{id}/copy ───────────────────────────────────────────
        // Copie à soi (brouillon, sans classe ni date) d'une évaluation lue depuis une bibliothèque, ou de la sienne.
        group.MapPost("/{id:guid}/copy", async (Guid id, ClaimsPrincipal principal, EvaluationContext db, UserDirectoryClient users) =>
        {
            var userId = principal.UserId();
            var source = await db.OwnedAssessmentAsync(id, userId) ?? await ReadableFromLibraryAsync(db, id, users);
            if (source is null) return Results.NotFound();

            var copy = AssessmentLibrary.Copy(source, userId);
            db.Assessments.Add(copy);
            await db.SaveChangesAsync();
            return Results.Created($"/assessments/{copy.Id}", new AssessmentDto(copy, withQuestions: true));
        })
        .RequireAuthorization(Roles.Teacher)
        .WithSummary("Copier une évaluation de la bibliothèque dans mes évaluations")
        .Produces<AssessmentDto>(201)
        .Produces(404);
    }
}
