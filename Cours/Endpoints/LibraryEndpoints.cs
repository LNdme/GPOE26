using System.Security.Claims;
using Cours.Data;
using Cours.DTOs;
using Cours.Helpers;
using Cours.Model;
using Cours.Service;
using Microsoft.EntityFrameworkCore;

namespace Cours.Endpoints;

/// <summary>
/// Bibliothèque d'établissement (phase 2, lot 8, décisions P1 et P2) : un enseignant dépose un de ses cours
/// dans la bibliothèque de son établissement ; les enseignants et gestionnaires de l'établissement le lisent
/// et en font une copie à eux. Les droits viennent du service User (GET /organizations/{id}/library).
/// </summary>
public static class LibraryEndpoints
{
    /// <summary>Lecture d'un cours déposé : l'utilisateur voit la bibliothèque d'un établissement où il est déposé.</summary>
    public static async Task<bool> CanReadFromLibraryAsync(Course course, UserDirectoryClient users)
    {
        foreach (var orgId in course.LibraryOrganizationIds)
            if (await users.LibrarySpaceAsync(orgId) is { } space && CourseLibrary.IsVisibleIn(course, space))
                return true;
        return false;
    }

    public static void MapLibraryEndpoints(this RouteGroupBuilder cours)
    {
        // ── GET /cours/library/{orgId} ────────────────────────────────────────────
        cours.MapGet("/library/{orgId:guid}", async (Guid orgId, ClaimsPrincipal principal, CoursContext db,
            UserDirectoryClient users, string? subject) =>
        {
            var userId = ClaimsHelper.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            var space = await users.LibrarySpaceAsync(orgId);
            if (space is null) return Results.NotFound(new { message = "Bibliothèque introuvable." });

            var authors = space.Teachers.ToDictionary(t => t.UserId, t => t.Username);
            var authorIds = authors.Keys.ToList();
            var query = db.Courses.Where(c => c.LibraryOrganizationIds.Contains(orgId) && authorIds.Contains(c.OwnerId));
            if (!string.IsNullOrWhiteSpace(subject)) query = query.Where(c => c.Subject == subject);

            var courses = await query
                .OrderBy(c => c.Subject).ThenBy(c => c.Title)
                .Select(c => new
                {
                    c.Id, c.Title, c.Subject, c.Description, c.ContentType, c.UpdatedAt, c.OwnerId,
                    Copies = db.Courses.Count(x => x.SourceCourseId == c.Id),
                })
                .ToListAsync();
            return Results.Ok(new LibraryCoursesDto(space.Id, space.Name, space.CanManage, courses
                .Select(c => new LibraryCourseDto(c.Id, c.Title, c.Subject, c.Description, c.ContentType, c.UpdatedAt,
                    c.OwnerId, authors[c.OwnerId], c.OwnerId == userId, c.Copies))
                .ToList()));
        })
        .WithSummary("Cours déposés dans la bibliothèque d'un établissement")
        .Produces<LibraryCoursesDto>()
        .Produces(404);

        // ── PUT /cours/{id}/library ───────────────────────────────────────────────
        // L'auteur choisit les bibliothèques où figure son cours : il doit être enseignant de chacune qu'il ajoute.
        cours.MapPut("/{id:guid}/library", async (Guid id, LibraryDepositRequest req, ClaimsPrincipal principal,
            CoursContext db, UserDirectoryClient users) =>
        {
            var userId = ClaimsHelper.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == userId);
            if (course is null) return Results.NotFound(new { message = "Cours introuvable." });

            var wanted = req.OrganizationIds?.Distinct().ToList() ?? new();
            foreach (var orgId in wanted.Except(course.LibraryOrganizationIds))
            {
                var space = await users.LibrarySpaceAsync(orgId);
                if (space is null || !space.IsTeacher(userId.Value))
                    return Results.BadRequest(new { message = "Vous ne pouvez déposer un cours que dans la bibliothèque d'un établissement où vous enseignez." });
            }

            course.LibraryOrganizationIds = wanted;
            course.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new CourseSummaryDto(course));
        })
        .WithSummary("Déposer un cours dans des bibliothèques d'établissement (ou l'en retirer)")
        .Produces<CourseSummaryDto>()
        .Produces(400);

        // ── DELETE /cours/library/{orgId}/{id} ────────────────────────────────────
        // L'auteur, ou un responsable ou gestionnaire de l'établissement (Q13), retire un dépôt.
        // Seul le dépôt disparaît : le cours reste à son auteur, les copies restent à ceux qui les ont faites.
        cours.MapDelete("/library/{orgId:guid}/{id:guid}", async (Guid orgId, Guid id, ClaimsPrincipal principal,
            CoursContext db, UserDirectoryClient users) =>
        {
            var userId = ClaimsHelper.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id && c.LibraryOrganizationIds.Contains(orgId));
            if (course is null) return Results.NotFound(new { message = "Cours introuvable dans cette bibliothèque." });

            if (course.OwnerId != userId)
            {
                var space = await users.LibrarySpaceAsync(orgId);
                if (space is null || !CourseLibrary.IsVisibleIn(course, space))
                    return Results.NotFound(new { message = "Cours introuvable dans cette bibliothèque." });
                if (!space.CanManage) return Results.Forbid();
            }

            course.LibraryOrganizationIds = course.LibraryOrganizationIds.Where(o => o != orgId).ToList();
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithSummary("Retirer un cours d'une bibliothèque d'établissement")
        .Produces(204)
        .Produces(403)
        .Produces(404);

        // ── POST /cours/{id}/copy ─────────────────────────────────────────────────
        // Copie à soi d'un cours qu'on peut lire depuis une bibliothèque (ou de son propre cours) :
        // sections, PDF et images recopiés, pour que la copie survive à l'original.
        cours.MapPost("/{id:guid}/copy", async (Guid id, ClaimsPrincipal principal, CoursContext db,
            UserDirectoryClient users, IWebHostEnvironment env) =>
        {
            var userId = ClaimsHelper.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            var source = await db.Courses.Include(c => c.Sections).FirstOrDefaultAsync(c => c.Id == id);
            if (source is null || (source.OwnerId != userId && !await CanReadFromLibraryAsync(source, users)))
                return Results.NotFound(new { message = "Cours introuvable." });
            if (source.StructuringStatus == PdfStructuringService.StatusPending)
                return Results.Conflict(new { message = "La mise en forme de ce cours n'est pas finie : réessayez dans quelques minutes." });

            var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
            var copyId = Guid.NewGuid();
            var pdfPath = CourseLibrary.CopyFiles(source, copyId, webRoot);
            var copy = CourseLibrary.Copy(source, copyId, userId.Value, pdfPath);

            db.Courses.Add(copy);
            await db.SaveChangesAsync();
            return Results.Created($"/cours/{copy.Id}", new CourseDto(copy));
        })
        .WithSummary("Copier un cours de la bibliothèque dans mes cours")
        .Produces<CourseDto>(201)
        .Produces(404)
        .Produces(409);
    }
}
