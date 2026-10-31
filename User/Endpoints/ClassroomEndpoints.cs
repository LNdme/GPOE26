using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using User.Data;
using User.DTOs;
using User.Helper;
using User.Model;
using User.Service;

namespace User.Endpoints;

public static class ClassroomEndpoints
{
    public static void MapClassroomEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/classrooms").WithTags("Classes").RequireAuthorization();

        // ── POST /classrooms ──────────────────────────────────────────────────────
        group.MapPost("", async (CreateClassroomRequest req, ClaimsPrincipal principal, UserContext db, ClassroomService classrooms) =>
        {
            var teacher = await db.CurrentUserAsync(principal);
            if (teacher is null) return Results.Unauthorized();

            var classroom = await classrooms.CreateAsync(teacher, req.Name, req.Level, req.SubSystem, req.Subject);
            await db.SaveChangesAsync();
            return Results.Created($"/classrooms/{classroom.Id}", ToDto(classroom, teacher.Username, 0, isOwner: true));
        })
        .RequireAuthorization(Policies.Teacher)
        .WithSummary("Créer une classe (ou un groupe)")
        .Produces<ClassroomDto>(201);

        // ── GET /classrooms/mine ──────────────────────────────────────────────────
        // Enseignant : ses classes. Élève : les classes qu'il a rejointes.
        group.MapGet("/mine", async (ClaimsPrincipal principal, UserContext db) =>
        {
            var user = await db.CurrentUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            var query = user.Role == UserRole.Teacher
                ? db.Classrooms.Where(c => c.TeacherId == user.Id)
                : db.Classrooms.Where(c => c.Members.Any(m => m.StudentId == user.Id));

            var rows = await query
                .Where(c => !c.IsArchived)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new
                {
                    Classroom = c,
                    Teacher = db.AppUsers.Where(u => u.Id == c.TeacherId).Select(u => u.Username).FirstOrDefault(),
                    Count = c.Members.Count
                })
                .ToListAsync();

            return Results.Ok(rows.Select(r => ToDto(r.Classroom, r.Teacher ?? "", r.Count, r.Classroom.TeacherId == user.Id)));
        })
        .WithSummary("Mes classes")
        .Produces<List<ClassroomDto>>();

        // ── GET /classrooms/{id} ──────────────────────────────────────────────────
        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal principal, UserContext db) =>
        {
            var userId = principal.GetUserId();
            var row = await db.Classrooms
                .Where(c => c.Id == id && (c.TeacherId == userId || c.Members.Any(m => m.StudentId == userId)))
                .Select(c => new
                {
                    Classroom = c,
                    Teacher = db.AppUsers.Where(u => u.Id == c.TeacherId).Select(u => u.Username).FirstOrDefault(),
                    Count = c.Members.Count
                })
                .FirstOrDefaultAsync();

            return row is null
                ? Results.NotFound()
                : Results.Ok(ToDto(row.Classroom, row.Teacher ?? "", row.Count, row.Classroom.TeacherId == userId));
        })
        .WithSummary("Détail d'une classe (enseignant ou membre)")
        .Produces<ClassroomDto>()
        .Produces(404);

        // ── GET /classrooms/{id}/members ──────────────────────────────────────────
        group.MapGet("/{id:guid}/members", async (Guid id, ClaimsPrincipal principal, UserContext db) =>
        {
            var userId = principal.GetUserId();
            if (!await db.Classrooms.AnyAsync(c => c.Id == id && c.TeacherId == userId))
                return Results.NotFound();

            var members = await db.ClassroomMembers
                .Where(m => m.ClassroomId == id)
                .Join(db.AppUsers, m => m.StudentId, u => u.Id, (m, u) => new { Member = m, Student = u })
                .OrderBy(r => r.Student.Username)
                .Select(r => new ClassroomMemberDto(r.Student.Id, r.Student.Username, r.Student.Level,
                    r.Student.Filiere, r.Member.JoinedAt, r.Student.LastLoginAt))
                .ToListAsync();

            return Results.Ok(members);
        })
        .RequireAuthorization(Policies.Teacher)
        .WithSummary("Élèves d'une classe (enseignant propriétaire)")
        .Produces<List<ClassroomMemberDto>>();

        // ── POST /classrooms/join ─────────────────────────────────────────────────
        group.MapPost("/join", async (JoinClassroomRequest req, ClaimsPrincipal principal, UserContext db, ClassroomService classrooms) =>
        {
            var student = await db.CurrentUserAsync(principal);
            if (student is null) return Results.Unauthorized();

            var classroom = await classrooms.JoinAsync(student, req.Code);
            if (classroom is null)
                return Results.NotFound(new { message = "Code d'invitation invalide." });

            await db.SaveChangesAsync();
            var teacher = await db.AppUsers.Where(u => u.Id == classroom.TeacherId).Select(u => u.Username).FirstAsync();
            var count = await db.ClassroomMembers.CountAsync(m => m.ClassroomId == classroom.Id);
            return Results.Ok(ToDto(classroom, teacher, count, isOwner: false));
        })
        .RequireAuthorization(Policies.Student)
        .WithSummary("Rejoindre une classe avec son code d'invitation")
        .Produces<ClassroomDto>()
        .Produces(404);

        // ── DELETE /classrooms/{id}/members/{studentId} ───────────────────────────
        // L'enseignant retire un élève, ou l'élève quitte la classe.
        group.MapDelete("/{id:guid}/members/{studentId:guid}", async (Guid id, Guid studentId, ClaimsPrincipal principal, UserContext db) =>
        {
            var userId = principal.GetUserId();
            var isOwner = await db.Classrooms.AnyAsync(c => c.Id == id && c.TeacherId == userId);
            if (!isOwner && studentId != userId) return Results.NotFound();

            var deleted = await db.ClassroomMembers
                .Where(m => m.ClassroomId == id && m.StudentId == studentId)
                .ExecuteDeleteAsync();
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        })
        .WithSummary("Retirer un élève / quitter la classe");

        // ── POST /classrooms/{id}/invite-code ─────────────────────────────────────
        // Régénère le code (par ex. s'il a circulé hors de la classe).
        group.MapPost("/{id:guid}/invite-code", async (Guid id, ClaimsPrincipal principal, UserContext db, ClassroomService classrooms) =>
        {
            var userId = principal.GetUserId();
            var classroom = await db.Classrooms.FirstOrDefaultAsync(c => c.Id == id && c.TeacherId == userId);
            if (classroom is null) return Results.NotFound();

            classroom.InviteCode = await classrooms.UniqueInviteCodeAsync();
            await db.SaveChangesAsync();
            return Results.Ok(new { classroom.InviteCode });
        })
        .RequireAuthorization(Policies.Teacher)
        .WithSummary("Régénérer le code d'invitation");

        // ── DELETE /classrooms/{id} ───────────────────────────────────────────────
        // Archivage : les élèves et l'historique sont conservés.
        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal principal, UserContext db) =>
        {
            var userId = principal.GetUserId();
            var archived = await db.Classrooms
                .Where(c => c.Id == id && c.TeacherId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsArchived, true));
            return archived == 0 ? Results.NotFound() : Results.NoContent();
        })
        .RequireAuthorization(Policies.Teacher)
        .WithSummary("Archiver une classe");
    }

    private static ClassroomDto ToDto(Classroom c, string teacherName, int memberCount, bool isOwner) =>
        new(c.Id, c.Name, c.Kind, c.Level, c.SubSystem, c.Subject,
            isOwner ? c.InviteCode : null, c.TeacherId, teacherName, memberCount, c.CreatedAt);
}
