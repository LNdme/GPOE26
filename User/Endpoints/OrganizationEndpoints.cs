using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using User.Data;
using User.DTOs;
using User.Helper;
using User.Model;
using User.Service;

namespace User.Endpoints;

/// <summary>
/// Gestion d'un espace par son responsable et ses gestionnaires (docs/PLAN-ETABLISSEMENT.md, lot 2) :
/// membres, années scolaires, classes et affectation des enseignants.
/// Les élèves et les codes personnels arrivent au lot 3.
/// </summary>
public static class OrganizationEndpoints
{
    private static readonly OrganizationRole[] Managers = [OrganizationRole.Owner, OrganizationRole.Manager];

    public static void MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/organizations").WithTags("Espaces").RequireAuthorization();

        // ── GET /organizations/mine ───────────────────────────────────────────────
        group.MapGet("/mine", async (ClaimsPrincipal principal, UserContext db) =>
        {
            var userId = principal.GetUserId();
            var rows = await db.OrganizationMembers
                .Where(m => m.UserId == userId && m.Status == MemberStatus.Active)
                .Join(db.Organizations, m => m.OrganizationId, o => o.Id, (m, o) => new { o.Id, o.Name, o.Type, o.City, o.ShortCode, m.Role })
                .ToListAsync();
            return Results.Ok(rows.GroupBy(r => r.Id)
                .Select(g => new MySpaceDto(g.Key, g.First().Name, g.First().Type, g.First().City, g.Select(r => r.Role).Order().ToList(), g.First().ShortCode))
                .OrderBy(s => s.Name));
        })
        .WithSummary("Mes espaces (établissements, espace d'enseignant indépendant) et mes rôles")
        .Produces<List<MySpaceDto>>();

        var space = group.MapGroup("/{orgId:guid}");

        // ── GET /organizations/{orgId} ────────────────────────────────────────────
        space.MapGet("", async (Guid orgId, UserContext db, SpaceService spaces) =>
        {
            var org = await db.Organizations.FindAsync(orgId);
            if (org is null) return Results.NotFound();

            var now = DateTime.UtcNow;
            var current = await db.SchoolYears.FirstOrDefaultAsync(y => y.OrganizationId == orgId && y.IsCurrent);
            var license = await db.Subscriptions
                .Where(s => s.OrganizationId == orgId
                            && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial) && s.EndsAt > now)
                .OrderByDescending(s => s.EndsAt)
                .FirstOrDefaultAsync();
            var students = await db.OrganizationStudents.Where(s => s.OrganizationId == orgId)
                .GroupBy(s => s.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            var classes = db.Classrooms.Where(c => c.OrganizationId == orgId && !c.IsArchived);
            if (current is not null)
                classes = classes.Where(c => c.SchoolYearId == current.Id);

            return Results.Ok(new SpaceOverviewDto(
                org.Id, org.Name, org.Type, org.City,
                current is null ? null : new SchoolYearDto(current),
                await classes.CountAsync(),
                await db.OrganizationMembers.CountAsync(m => m.OrganizationId == orgId && m.Role == OrganizationRole.Teacher && m.Status == MemberStatus.Active),
                students.Where(s => s.Key == StudentStatus.Active).Sum(s => s.Count),
                students.Where(s => s.Key == StudentStatus.Pending).Sum(s => s.Count),
                await spaces.SeatLimitAsync(orgId), license?.TeacherSeats, license?.EndsAt,
                await spaces.StudentsPremiumAsync(orgId), org.ShortCode));
        })
        .RequireSpaceRole(Managers)
        .WithSummary("Tableau de bord de l'espace")
        .Produces<SpaceOverviewDto>();

        // ── Membres ───────────────────────────────────────────────────────────────

        space.MapGet("/members", async (Guid orgId, UserContext db) =>
        {
            var rows = await db.OrganizationMembers
                .Where(m => m.OrganizationId == orgId && m.Status == MemberStatus.Active)
                .Join(db.AppUsers, m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.Username, u.Email, u.Role, Space = m.Role, m.JoinedAt })
                .ToListAsync();
            return Results.Ok(rows.GroupBy(r => r.Id)
                .Select(g => new SpaceMemberDto(g.Key, g.First().Username, g.First().Email, g.First().Role,
                    g.Select(r => r.Space).Order().ToList(), g.Min(r => r.JoinedAt)))
                .OrderBy(m => m.Username));
        })
        .RequireSpaceRole(Managers)
        .WithSummary("Responsable, gestionnaires et enseignants de l'espace")
        .Produces<List<SpaceMemberDto>>();

        space.MapPost("/managers", async (Guid orgId, AddSpaceMemberRequest req, UserContext db, SpaceService spaces) =>
        {
            var user = await FindAccountAsync(db, req.Email);
            if (user is null) return AccountNotFound();
            if (user.Role == UserRole.Student)
                return Results.BadRequest(new { message = "Un compte élève ne peut pas gérer un établissement." });

            await spaces.GrantRoleAsync(orgId, user.Id, OrganizationRole.Manager);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireSpaceRole(OrganizationRole.Owner)
        .WithSummary("Nommer un gestionnaire (compte existant, responsable seulement)");

        space.MapDelete("/managers/{userId:guid}", async (Guid orgId, Guid userId, UserContext db, SpaceService spaces) =>
        {
            if (!await spaces.RevokeRoleAsync(orgId, userId, OrganizationRole.Manager)) return Results.NotFound();
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireSpaceRole(OrganizationRole.Owner)
        .WithSummary("Retirer un gestionnaire (responsable seulement)");

        space.MapPost("/teachers", async (Guid orgId, AddSpaceMemberRequest req, UserContext db, SpaceService spaces) =>
        {
            var user = await FindAccountAsync(db, req.Email);
            if (user is null) return AccountNotFound();
            if (user.Role != UserRole.Teacher)
                return Results.BadRequest(new { message = "Ce compte n'est pas un compte enseignant." });

            if (!await spaces.HasRoleAsync(orgId, user.Id, OrganizationRole.Teacher) && !await spaces.CanAddTeacherAsync(orgId))
                return Results.Conflict(new { message = "La licence ne couvre pas d'enseignant de plus : toutes les places sont prises." });

            await spaces.GrantRoleAsync(orgId, user.Id, OrganizationRole.Teacher);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireSpaceRole(Managers)
        .WithSummary("Ajouter un enseignant (compte enseignant existant)");

        // Retirer un enseignant le retire aussi de toutes les classes de l'espace.
        space.MapDelete("/teachers/{userId:guid}", async (Guid orgId, Guid userId, UserContext db, SpaceService spaces, ClassroomService classrooms) =>
        {
            if (!await spaces.RevokeRoleAsync(orgId, userId, OrganizationRole.Teacher)) return Results.NotFound();

            var taught = await db.Classrooms
                .Where(c => c.OrganizationId == orgId && db.ClassroomTeachers.Any(t => t.ClassroomId == c.Id && t.TeacherId == userId))
                .ToListAsync();
            foreach (var classroom in taught)
                await classrooms.UnassignTeacherAsync(classroom, userId, subject: null);

            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireSpaceRole(Managers)
        .WithSummary("Retirer un enseignant de l'espace et de ses classes");

        // ── Années scolaires ──────────────────────────────────────────────────────

        space.MapGet("/years", async (Guid orgId, UserContext db) =>
            Results.Ok(await db.SchoolYears.Where(y => y.OrganizationId == orgId).OrderByDescending(y => y.StartsOn)
                .Select(y => new SchoolYearDto(y.Id, y.Label, y.StartsOn, y.EndsOn, y.IsCurrent)).ToListAsync()))
        .RequireSpaceRole(Managers)
        .WithSummary("Années scolaires de l'espace")
        .Produces<List<SchoolYearDto>>();

        space.MapPost("/years", async (Guid orgId, CreateSchoolYearRequest req, UserContext db) =>
        {
            if (!SpaceRules.TryParseSchoolYear(req.Label, out var parsed))
                return Results.BadRequest(new { message = "Écrivez l'année sous la forme 2027-2028." });
            if (await db.SchoolYears.AnyAsync(y => y.OrganizationId == orgId && y.Label == parsed.Label))
                return Results.Conflict(new { message = $"L'année {parsed.Label} existe déjà." });

            var year = new SchoolYear
            {
                OrganizationId = orgId,
                Label = parsed.Label,
                StartsOn = parsed.StartsOn,
                EndsOn = parsed.EndsOn,
                IsCurrent = !await db.SchoolYears.AnyAsync(y => y.OrganizationId == orgId && y.IsCurrent)
            };
            db.SchoolYears.Add(year);
            await db.SaveChangesAsync();
            return Results.Created($"/organizations/{orgId}/years", new SchoolYearDto(year));
        })
        .RequireSpaceRole(Managers)
        .WithSummary("Préparer une année scolaire (en cours seulement si l'espace n'en a pas)")
        .Produces<SchoolYearDto>(201);

        // Une seule année en cours (index unique) : l'ancienne est d'abord quittée, dans la même transaction.
        space.MapPost("/years/{yearId:guid}/current", async (Guid orgId, Guid yearId, UserContext db) =>
        {
            if (!await db.SchoolYears.AnyAsync(y => y.Id == yearId && y.OrganizationId == orgId)) return Results.NotFound();

            await using var tx = await db.Database.BeginTransactionAsync();
            await db.SchoolYears.Where(y => y.OrganizationId == orgId && y.IsCurrent && y.Id != yearId)
                .ExecuteUpdateAsync(s => s.SetProperty(y => y.IsCurrent, false));
            await db.SchoolYears.Where(y => y.Id == yearId).ExecuteUpdateAsync(s => s.SetProperty(y => y.IsCurrent, true));
            await tx.CommitAsync();
            return Results.NoContent();
        })
        .RequireSpaceRole(Managers)
        .WithSummary("Passer à une autre année scolaire");

        // ── Classes ───────────────────────────────────────────────────────────────

        space.MapGet("/classes", async (Guid orgId, Guid? yearId, UserContext db) =>
        {
            var year = yearId ?? await db.SchoolYears.Where(y => y.OrganizationId == orgId && y.IsCurrent)
                .Select(y => (Guid?)y.Id).FirstOrDefaultAsync();
            var classes = await db.Classrooms
                .Where(c => c.OrganizationId == orgId && !c.IsArchived && c.SchoolYearId == year)
                .OrderBy(c => c.Name)
                .Select(c => new { Classroom = c, Count = c.Members.Count })
                .ToListAsync();

            var ids = classes.Select(c => c.Classroom.Id).ToList();
            var teachers = await TeachersOfAsync(db, ids);
            return Results.Ok(classes.Select(c => new SpaceClassDto(c.Classroom.Id, c.Classroom.Name, c.Classroom.Kind,
                c.Classroom.Level, c.Classroom.SubSystem, c.Classroom.SchoolYearId, c.Count,
                teachers.GetValueOrDefault(c.Classroom.Id) ?? [])));
        })
        .RequireSpaceRole(Managers)
        .WithSummary("Classes de l'espace (année en cours par défaut) et leurs enseignants")
        .Produces<List<SpaceClassDto>>();

        space.MapPost("/classes", async (Guid orgId, SpaceClassRequest req, UserContext db, ClassroomService classrooms) =>
        {
            var org = await db.Organizations.FindAsync(orgId);
            if (org is null) return Results.NotFound();
            var yearId = req.SchoolYearId ?? await db.SchoolYears.Where(y => y.OrganizationId == orgId && y.IsCurrent)
                .Select(y => (Guid?)y.Id).FirstOrDefaultAsync();
            if (yearId is null || !await db.SchoolYears.AnyAsync(y => y.Id == yearId && y.OrganizationId == orgId))
                return Results.BadRequest(new { message = "Choisissez d'abord l'année scolaire de la classe." });

            var classroom = new Classroom
            {
                Name = req.Name.Trim(),
                Kind = org.Type == OrganizationType.Institution ? ClassroomKind.Group : ClassroomKind.Class,
                OrganizationId = orgId,
                SchoolYearId = yearId,
                Level = req.Level,
                SubSystem = req.SubSystem
            };
            db.Classrooms.Add(classroom);
            await db.SaveChangesAsync();
            return Results.Created($"/classrooms/{classroom.Id}",
                new SpaceClassDto(classroom.Id, classroom.Name, classroom.Kind, classroom.Level, classroom.SubSystem, classroom.SchoolYearId, 0, []));
        })
        .RequireSpaceRole(Managers)
        .WithSummary("Créer une classe (sans enseignant : on les affecte ensuite)")
        .Produces<SpaceClassDto>(201);

        space.MapPut("/classes/{classId:guid}", async (Guid orgId, Guid classId, SpaceClassRequest req, UserContext db) =>
        {
            var classroom = await db.Classrooms.FirstOrDefaultAsync(c => c.Id == classId && c.OrganizationId == orgId);
            if (classroom is null) return Results.NotFound();
            if (req.SchoolYearId is { } yearId && !await db.SchoolYears.AnyAsync(y => y.Id == yearId && y.OrganizationId == orgId))
                return Results.BadRequest(new { message = "Cette année scolaire n'appartient pas à l'établissement." });

            classroom.Name = req.Name.Trim();
            classroom.Level = req.Level;
            classroom.SubSystem = req.SubSystem;
            classroom.SchoolYearId = req.SchoolYearId ?? classroom.SchoolYearId;
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireSpaceRole(Managers)
        .WithSummary("Renommer une classe, changer son niveau ou son année");

        space.MapDelete("/classes/{classId:guid}", async (Guid orgId, Guid classId, UserContext db) =>
        {
            var archived = await db.Classrooms.Where(c => c.Id == classId && c.OrganizationId == orgId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsArchived, true));
            return archived == 0 ? Results.NotFound() : Results.NoContent();
        })
        .RequireSpaceRole(Managers)
        .WithSummary("Archiver une classe (élèves et historique conservés)");

        space.MapPut("/classes/{classId:guid}/teachers", async (Guid orgId, Guid classId, AssignTeacherRequest req,
            UserContext db, SpaceService spaces, ClassroomService classrooms) =>
        {
            var classroom = await db.Classrooms.FirstOrDefaultAsync(c => c.Id == classId && c.OrganizationId == orgId);
            if (classroom is null) return Results.NotFound();
            if (!await spaces.HasRoleAsync(orgId, req.TeacherId, OrganizationRole.Teacher))
                return Results.BadRequest(new { message = "Ajoutez d'abord cet enseignant à l'établissement." });

            await classrooms.AssignTeacherAsync(classroom, req.TeacherId, req.Subject, req.IsMain);
            await db.SaveChangesAsync();
            return Results.Ok((await TeachersOfAsync(db, [classId])).GetValueOrDefault(classId) ?? []);
        })
        .RequireSpaceRole(Managers)
        .WithSummary("Affecter un enseignant à une classe pour une matière")
        .Produces<List<ClassTeacherDto>>();

        space.MapDelete("/classes/{classId:guid}/teachers/{teacherId:guid}", async (Guid orgId, Guid classId, Guid teacherId, string? subject,
            UserContext db, ClassroomService classrooms) =>
        {
            var classroom = await db.Classrooms.FirstOrDefaultAsync(c => c.Id == classId && c.OrganizationId == orgId);
            if (classroom is null || !await classrooms.UnassignTeacherAsync(classroom, teacherId, subject)) return Results.NotFound();
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireSpaceRole(Managers)
        .WithSummary("Retirer un enseignant d'une classe (une matière, ou toutes sans « subject »)");
    }

    /// <summary>Enseignants de chaque classe, professeur principal en tête.</summary>
    public static async Task<Dictionary<Guid, List<ClassTeacherDto>>> TeachersOfAsync(UserContext db, List<Guid> classIds)
    {
        var rows = await db.ClassroomTeachers
            .Where(t => classIds.Contains(t.ClassroomId))
            .Join(db.AppUsers, t => t.TeacherId, u => u.Id, (t, u) => new { t.ClassroomId, t.TeacherId, u.Username, t.Subject, t.IsMain })
            .ToListAsync();
        return rows.GroupBy(r => r.ClassroomId).ToDictionary(g => g.Key, g => g
            .OrderByDescending(r => r.IsMain).ThenBy(r => r.Subject).ThenBy(r => r.Username)
            .Select(r => new ClassTeacherDto(r.TeacherId, r.Username, r.Subject, r.IsMain)).ToList());
    }

    private static Task<AppUser?> FindAccountAsync(UserContext db, string email) =>
        db.AppUsers.FirstOrDefaultAsync(u => u.Email == email.Trim().ToLower());

    private static IResult AccountNotFound() =>
        Results.NotFound(new { message = "Aucun compte avec cette adresse : la personne doit d'abord s'inscrire." });
}
