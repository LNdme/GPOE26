using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using User.Data;
using User.DTOs;
using User.Helper;
using User.Model;
using User.Service;

namespace User.Endpoints;

/// <summary>
/// Liste des élèves d'un espace et codes personnels (docs/PLAN-ETABLISSEMENT.md, lot 3) :
/// réservés au responsable et aux gestionnaires.
/// </summary>
public static class StudentEndpoints
{
    private static readonly OrganizationRole[] Managers = [OrganizationRole.Owner, OrganizationRole.Manager];

    public static void MapStudentEndpoints(this IEndpointRouteBuilder app)
    {
        var space = app.MapGroup("/organizations/{orgId:guid}").WithTags("Élèves et codes").RequireSpaceRole(Managers);

        // ── Liste des élèves ──────────────────────────────────────────────────────

        space.MapGet("/students", async (Guid orgId, Guid? classId, StudentStatus? status, UserContext db) =>
            Results.Ok(await StudentsAsync(db, orgId, s => (classId == null || s.ClassroomId == classId) && (status == null || s.Status == status))))
        .WithSummary("Élèves de l'espace (filtre par classe, par statut), avec leur code à imprimer")
        .Produces<List<StudentDto>>();

        space.MapPost("/students", async (Guid orgId, StudentRequest req, UserContext db, SpaceService spaces) =>
        {
            if (await ClassProblemAsync(db, orgId, req.ClassroomId) is { } problem) return problem;
            var matricule = string.IsNullOrWhiteSpace(req.Matricule) ? await spaces.NewMatriculeAsync(orgId) : req.Matricule.Trim();
            if (await db.OrganizationStudents.AnyAsync(s => s.OrganizationId == orgId && s.Matricule == matricule))
                return Results.Conflict(new { message = $"Le matricule {matricule} est déjà inscrit." });

            var row = new OrganizationStudent
            {
                OrganizationId = orgId,
                Matricule = matricule,
                LastName = req.LastName.Trim(),
                FirstName = req.FirstName?.Trim() ?? "",
                BirthDate = req.BirthDate,
                ClassroomId = req.ClassroomId
            };
            db.OrganizationStudents.Add(row);
            await db.SaveChangesAsync();
            return Results.Created($"/organizations/{orgId}/students/{row.Id}", (await StudentsAsync(db, orgId, s => s.Id == row.Id)).Single());
        })
        .WithSummary("Inscrire un élève à la main (matricule vide : généré)")
        .Produces<StudentDto>(201);

        space.MapPut("/students/{studentId:guid}", async (Guid orgId, Guid studentId, StudentRequest req, UserContext db, AccessCodeService codes) =>
        {
            var row = await db.OrganizationStudents.FirstOrDefaultAsync(s => s.Id == studentId && s.OrganizationId == orgId);
            if (row is null) return Results.NotFound();
            if (await ClassProblemAsync(db, orgId, req.ClassroomId) is { } problem) return problem;

            if (!string.IsNullOrWhiteSpace(req.Matricule) && req.Matricule.Trim() != row.Matricule)
            {
                var matricule = req.Matricule.Trim();
                if (await db.OrganizationStudents.AnyAsync(s => s.OrganizationId == orgId && s.Matricule == matricule))
                    return Results.Conflict(new { message = $"Le matricule {matricule} est déjà inscrit." });
                row.Matricule = matricule;
            }
            row.LastName = req.LastName.Trim();
            row.FirstName = req.FirstName?.Trim() ?? "";
            row.BirthDate = req.BirthDate;
            await codes.MoveStudentAsync(row, req.ClassroomId);
            await db.SaveChangesAsync();
            return Results.Ok((await StudentsAsync(db, orgId, s => s.Id == row.Id)).Single());
        })
        .WithSummary("Corriger un élève, ou le changer de classe (il suit, s'il a déjà son compte)")
        .Produces<StudentDto>();

        // Revoked / Left : la place est libérée et l'élève quitte les classes de l'espace ; Pending : réintégré.
        space.MapPost("/students/{studentId:guid}/status", async (Guid orgId, Guid studentId, StudentStatusRequest req,
            UserContext db, AccessCodeService codes) =>
        {
            var row = await db.OrganizationStudents.FirstOrDefaultAsync(s => s.Id == studentId && s.OrganizationId == orgId);
            if (row is null) return Results.NotFound();

            switch (req.Status)
            {
                case StudentStatus.Revoked or StudentStatus.Left:
                    await codes.RemoveStudentAsync(row, req.Status);
                    break;
                case StudentStatus.Pending when row.Status is StudentStatus.Revoked or StudentStatus.Left:
                    row.Status = StudentStatus.Pending;
                    row.ActivatedAt = null;
                    break;
                case var same when same == row.Status:
                    break;
                default:
                    return Results.BadRequest(new { message = "Un élève devient actif en activant son code." });
            }
            await db.SaveChangesAsync();
            return Results.Ok((await StudentsAsync(db, orgId, s => s.Id == row.Id)).Single());
        })
        .WithSummary("Retirer un élève (place libérée), le marquer parti, ou le réintégrer")
        .Produces<StudentDto>();

        // Un élève sans compte peut être effacé (erreur de saisie) ; un élève qui a un compte est retiré, pas effacé.
        space.MapDelete("/students/{studentId:guid}", async (Guid orgId, Guid studentId, UserContext db) =>
        {
            var row = await db.OrganizationStudents.FirstOrDefaultAsync(s => s.Id == studentId && s.OrganizationId == orgId);
            if (row is null) return Results.NotFound();
            if (row.UserId is not null)
                return Results.Conflict(new { message = "Cet élève a déjà son compte : retirez-le plutôt (sa place sera libérée)." });
            db.OrganizationStudents.Remove(row);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithSummary("Effacer un élève inscrit par erreur (sans compte)");

        // ── Import ────────────────────────────────────────────────────────────────
        // Toujours un aperçu ligne par ligne ; rien n'est enregistré tant qu'une ligne est en erreur.

        space.MapPost("/students/import", async (Guid orgId, ImportStudentsRequest req, UserContext db, SpaceService spaces) =>
        {
            var (parsed, error) = StudentImport.Parse(req.Csv);
            if (error is not null) return Results.BadRequest(new { message = error });

            var classes = await CurrentClassesAsync(db, orgId);
            var existing = (await db.OrganizationStudents.Where(s => s.OrganizationId == orgId).Select(s => s.Matricule).ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var rows = parsed.Select(r =>
            {
                var errors = new List<string>(r.Errors);
                if (r.Matricule is { } m && existing.Contains(m)) errors.Add($"matricule {m} déjà inscrit");
                if (r.ClassName is { } c && !classes.ContainsKey(Key(c))) errors.Add($"classe « {c} » inconnue cette année");
                return new ImportRowDto(r.Line, r.Matricule, r.LastName, r.FirstName, r.ClassName, r.BirthDate, errors);
            }).ToList();
            var valid = rows.Count(r => r.Errors.Count == 0);

            if (!req.Commit || valid < rows.Count)
                return Results.Ok(new ImportResultDto(rows.Count, valid, false, rows));

            foreach (var r in rows)
                db.OrganizationStudents.Add(new OrganizationStudent
                {
                    OrganizationId = orgId,
                    Matricule = r.Matricule ?? await spaces.NewMatriculeAsync(orgId),
                    LastName = r.LastName,
                    FirstName = r.FirstName,
                    BirthDate = r.BirthDate,
                    ClassroomId = r.ClassName is { } c ? classes[Key(c)] : null
                });
            await db.SaveChangesAsync();
            return Results.Ok(new ImportResultDto(rows.Count, valid, true, rows));
        })
        .WithSummary("Importer la liste des élèves (CSV : matricule, nom, prénom, classe, date de naissance)")
        .Produces<ImportResultDto>();

        // ── Codes personnels ──────────────────────────────────────────────────────

        space.MapGet("/codes", async (Guid orgId, Guid? classId, AccessCodeKind? kind, UserContext db) =>
            Results.Ok(await CodesAsync(db, orgId, classId, kind)))
        .WithSummary("Codes encore valides (à imprimer), par classe ou par sorte")
        .Produces<List<AccessCodeDto>>();

        space.MapPost("/students/{studentId:guid}/code", async (Guid orgId, Guid studentId, ClaimsPrincipal principal,
            UserContext db, AccessCodeService codes) =>
        {
            var row = await db.OrganizationStudents.FirstOrDefaultAsync(s => s.Id == studentId && s.OrganizationId == orgId);
            if (row is null) return Results.NotFound();
            if (row.Status != StudentStatus.Pending)
                return Results.Conflict(new { message = row.Status == StudentStatus.Active
                    ? "Cet élève a déjà activé son compte."
                    : "Réintégrez d'abord cet élève." });

            await codes.IssueForStudentAsync(row, principal.GetUserId());
            await db.SaveChangesAsync();
            return Results.Ok((await StudentsAsync(db, orgId, s => s.Id == row.Id)).Single());
        })
        .WithSummary("Donner (ou redonner) son code à un élève : l'ancien est annulé")
        .Produces<StudentDto>();

        // Codes des élèves d'une classe qui attendent encore le leur (ceux qui en ont un le gardent).
        space.MapPost("/classes/{classId:guid}/codes", async (Guid orgId, Guid classId, ClaimsPrincipal principal,
            UserContext db, AccessCodeService codes) =>
        {
            if (!await db.Classrooms.AnyAsync(c => c.Id == classId && c.OrganizationId == orgId)) return Results.NotFound();
            var waiting = await db.OrganizationStudents
                .Where(s => s.OrganizationId == orgId && s.ClassroomId == classId && s.Status == StudentStatus.Pending
                            && !db.AccessCodes.Any(c => c.OrganizationStudentId == s.Id && c.Status == AccessCodeStatus.Issued))
                .ToListAsync();
            foreach (var row in waiting)
                await codes.IssueAsync(orgId, AccessCodeKind.Student, principal.GetUserId(), studentId: row.Id);
            await db.SaveChangesAsync();
            return Results.Ok(await CodesAsync(db, orgId, classId, AccessCodeKind.Student));
        })
        .WithSummary("Codes de toute une classe (seulement pour les élèves qui n'en ont pas)")
        .Produces<List<AccessCodeDto>>();

        space.MapPost("/codes/blank", async (Guid orgId, BlankCodesRequest req, ClaimsPrincipal principal, UserContext db, AccessCodeService codes) =>
        {
            if (!await db.Classrooms.AnyAsync(c => c.Id == req.ClassroomId && c.OrganizationId == orgId && !c.IsArchived))
                return Results.BadRequest(new { message = "Classe introuvable dans cet espace." });
            for (var i = 0; i < req.Count; i++)
                await codes.IssueAsync(orgId, AccessCodeKind.Student, principal.GetUserId(), classroomId: req.ClassroomId);
            await db.SaveChangesAsync();
            return Results.Ok(await CodesAsync(db, orgId, req.ClassroomId, AccessCodeKind.Student));
        })
        .WithSummary("Codes « en blanc » pour une classe : l'élève saisit son nom en l'activant")
        .Produces<List<AccessCodeDto>>();

        space.MapPost("/codes/teacher", async (Guid orgId, ClaimsPrincipal principal, UserContext db, AccessCodeService codes) =>
        {
            var code = await codes.IssueAsync(orgId, AccessCodeKind.Teacher, principal.GetUserId());
            await db.SaveChangesAsync();
            return Results.Ok((await CodesAsync(db, orgId, null, AccessCodeKind.Teacher)).First(c => c.Id == code.Id));
        })
        .WithSummary("Code d'invitation pour un enseignant (usage unique)")
        .Produces<AccessCodeDto>();

        space.MapDelete("/codes/{codeId:guid}", async (Guid orgId, Guid codeId, UserContext db) =>
        {
            var revoked = await db.AccessCodes
                .Where(c => c.Id == codeId && c.OrganizationId == orgId && c.Status == AccessCodeStatus.Issued && c.Kind != AccessCodeKind.Owner)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, AccessCodeStatus.Revoked));
            return revoked == 0 ? Results.NotFound() : Results.NoContent();
        })
        .WithSummary("Annuler un code pas encore utilisé");
    }

    private static string Key(string className) => StudentImport.Normalize(className);

    /// <summary>Classes non archivées de l'année en cours, par nom comparable.</summary>
    private static async Task<Dictionary<string, Guid>> CurrentClassesAsync(UserContext db, Guid orgId)
    {
        var year = await db.SchoolYears.Where(y => y.OrganizationId == orgId && y.IsCurrent).Select(y => (Guid?)y.Id).FirstOrDefaultAsync();
        var classes = await db.Classrooms.Where(c => c.OrganizationId == orgId && !c.IsArchived && c.SchoolYearId == year)
            .Select(c => new { c.Id, c.Name }).ToListAsync();
        return classes.GroupBy(c => Key(c.Name)).ToDictionary(g => g.Key, g => g.First().Id);
    }

    private static async Task<IResult?> ClassProblemAsync(UserContext db, Guid orgId, Guid? classId) =>
        classId is { } id && !await db.Classrooms.AnyAsync(c => c.Id == id && c.OrganizationId == orgId && !c.IsArchived)
            ? Results.BadRequest(new { message = "Classe introuvable dans cet établissement." })
            : null;

    private static async Task<List<StudentDto>> StudentsAsync(UserContext db, Guid orgId,
        System.Linq.Expressions.Expression<Func<OrganizationStudent, bool>> filter)
    {
        var now = DateTime.UtcNow;
        return await db.OrganizationStudents
            .Where(s => s.OrganizationId == orgId)
            .Where(filter)
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
            .Select(s => new StudentDto(
                s.Id, s.Matricule, s.LastName, s.FirstName, s.BirthDate, s.ClassroomId,
                db.Classrooms.Where(c => c.Id == s.ClassroomId).Select(c => c.Name).FirstOrDefault(),
                s.Status, s.UserId,
                db.AppUsers.Where(u => u.Id == s.UserId).Select(u => u.Username).FirstOrDefault(),
                s.ActivatedAt,
                db.AccessCodes.Where(c => c.OrganizationStudentId == s.Id && c.Status == AccessCodeStatus.Issued && c.ExpiresAt > now)
                    .Select(c => c.Code).FirstOrDefault(),
                db.AccessCodes.Where(c => c.OrganizationStudentId == s.Id && c.Status == AccessCodeStatus.Issued && c.ExpiresAt > now)
                    .Select(c => (DateTime?)c.ExpiresAt).FirstOrDefault()))
            .ToListAsync();
    }

    private static async Task<List<AccessCodeDto>> CodesAsync(UserContext db, Guid orgId, Guid? classId, AccessCodeKind? kind)
    {
        var now = DateTime.UtcNow;
        return await db.AccessCodes
            .Where(c => c.OrganizationId == orgId && c.Status == AccessCodeStatus.Issued && c.ExpiresAt > now && c.Kind != AccessCodeKind.Owner)
            .Where(c => kind == null || c.Kind == kind)
            .GroupJoin(db.OrganizationStudents, c => c.OrganizationStudentId, s => s.Id, (c, s) => new { Code = c, Students = s })
            .SelectMany(x => x.Students.DefaultIfEmpty(), (x, s) => new { x.Code, Student = s })
            .Where(x => classId == null || x.Code.ClassroomId == classId || (x.Student != null && x.Student.ClassroomId == classId))
            .OrderBy(x => x.Student == null ? "" : x.Student.LastName).ThenBy(x => x.Code.CreatedAt)
            .Select(x => new AccessCodeDto(
                x.Code.Id, x.Code.Code, x.Code.Kind, x.Code.Status, x.Code.ExpiresAt,
                x.Student == null ? null : x.Student.Id,
                x.Student == null ? null : (x.Student.LastName + " " + x.Student.FirstName).Trim(),
                x.Student == null ? null : x.Student.Matricule,
                db.Classrooms.Where(c => c.Id == (x.Student != null ? x.Student.ClassroomId : x.Code.ClassroomId)).Select(c => c.Name).FirstOrDefault(),
                x.Code.CreatedAt))
            .ToListAsync();
    }
}
