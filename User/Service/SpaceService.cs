using Microsoft.EntityFrameworkCore;
using User.Data;
using User.Model;

namespace User.Service;

/// <summary>
/// Tient à jour les espaces (établissement ou enseignant indépendant) : membres, année en cours, liste des élèves.
/// Comme ClassroomService, prépare les lignes sans SaveChanges ; les lignes créées dans la même unité de travail
/// (inscription d'un enseignant avec plusieurs classes, par exemple) sont cherchées aussi dans le suivi d'EF.
/// </summary>
public class SpaceService(UserContext db, IConfiguration config)
{
    /// <summary>Places élèves d'un enseignant indépendant (Q1) : réglable par Subscriptions:IndependentSeats.</summary>
    public int IndependentSeats => config.GetValue("Subscriptions:IndependentSeats", 30);

    /// <summary>
    /// Places élèves de l'espace : celles des licences en cours d'un établissement (0 sans licence),
    /// ou le forfait d'un enseignant indépendant.
    /// </summary>
    public async Task<int> SeatLimitAsync(Guid organizationId)
    {
        var type = await db.Organizations.Where(o => o.Id == organizationId).Select(o => (OrganizationType?)o.Type).FirstOrDefaultAsync();
        if (type == OrganizationType.Independent) return IndependentSeats;

        var now = DateTime.UtcNow;
        return await db.Subscriptions
            .Where(s => s.OrganizationId == organizationId && s.Plan == SubscriptionPlan.OrganizationLicense
                        && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial) && s.EndsAt > now)
            .SumAsync(s => s.Seats);
    }

    /// <summary>Élèves qui occupent une place : ceux dont le compte est activé.</summary>
    public Task<int> SeatsUsedAsync(Guid organizationId) =>
        db.OrganizationStudents.CountAsync(s => s.OrganizationId == organizationId && s.Status == StudentStatus.Active);

    /// <summary>Plafond d'enseignants de la licence en cours ; vide : sans plafond.</summary>
    public async Task<bool> CanAddTeacherAsync(Guid organizationId)
    {
        var now = DateTime.UtcNow;
        var limit = await db.Subscriptions
            .Where(s => s.OrganizationId == organizationId && s.Status == SubscriptionStatus.Active && s.EndsAt > now)
            .OrderByDescending(s => s.EndsAt).Select(s => s.TeacherSeats).FirstOrDefaultAsync();
        return limit is not { } max
               || await db.OrganizationMembers.CountAsync(m => m.OrganizationId == organizationId
                      && m.Role == OrganizationRole.Teacher && m.Status == MemberStatus.Active) < max;
    }

    /// <summary>
    /// Verrouille la ligne de l'espace jusqu'à la fin de la transaction en cours : les activations simultanées
    /// comptent les places l'une après l'autre et ne dépassent jamais la licence.
    /// </summary>
    public Task LockAsync(Guid organizationId) =>
        db.Database.ExecuteSqlInterpolatedAsync($@"SELECT 1 FROM ""Organizations"" WHERE ""Id"" = {organizationId} FOR UPDATE");

    /// <summary>Date limite d'un code : fin de l'année scolaire en cours de l'espace (Q7), en fin de journée UTC.</summary>
    public async Task<DateTime> CodeExpiryAsync(Guid organizationId)
    {
        var end = await db.SchoolYears.Where(y => y.OrganizationId == organizationId && y.IsCurrent)
                      .Select(y => (DateOnly?)y.EndsOn).FirstOrDefaultAsync()
                  ?? SpaceRules.SchoolYearOf(DateOnly.FromDateTime(DateTime.UtcNow)).EndsOn;
        return end.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc);
    }

    /// <summary>Matricule généré (AAAA-NNNN) pour un élève saisi ou importé sans matricule.</summary>
    public async Task<string> NewMatriculeAsync(Guid organizationId) =>
        SpaceRules.Matricule(DateTime.UtcNow.Year, await NextMatriculeNumberAsync(organizationId));

    /// <summary>
    /// Espace où un enseignant crée ses classes : l'établissement dont il a activé la clé,
    /// sinon son espace indépendant, créé au besoin (il en est responsable et enseignant).
    /// </summary>
    public async Task<(Guid OrganizationId, Guid SchoolYearId)> TeacherSpaceAsync(AppUser teacher)
    {
        Guid orgId;
        if (teacher.OrganizationId is { } verified && await OrganizationExistsAsync(verified))
        {
            orgId = verified;
            await EnsureMemberAsync(orgId, teacher.Id, OrganizationRole.Teacher);
        }
        else
        {
            orgId = await IndependentSpaceOfAsync(teacher.Id) ?? CreateIndependentSpace(teacher);
        }
        return (orgId, await CurrentYearIdAsync(orgId));
    }

    /// <summary>La personne a-t-elle, ACTIF, l'un de ces rôles dans l'espace ? Vérifié en base, jamais d'après le jeton.</summary>
    public Task<bool> HasRoleAsync(Guid organizationId, Guid userId, params OrganizationRole[] roles) =>
        db.OrganizationMembers.AnyAsync(m => m.OrganizationId == organizationId && m.UserId == userId
                                             && m.Status == MemberStatus.Active && roles.Contains(m.Role));

    /// <summary>Espaces où la personne gère (responsable ou gestionnaire).</summary>
    public Task<List<Guid>> ManagedSpaceIdsAsync(Guid userId) =>
        db.OrganizationMembers
            .Where(m => m.UserId == userId && m.Status == MemberStatus.Active
                        && (m.Role == OrganizationRole.Owner || m.Role == OrganizationRole.Manager))
            .Select(m => m.OrganizationId).Distinct().ToListAsync();

    /// <summary>
    /// Donne le rôle, ou le rend s'il avait été retiré (ajout explicite par un responsable ou l'administration).
    /// Vrai si quelque chose a changé.
    /// </summary>
    public async Task<bool> GrantRoleAsync(Guid organizationId, Guid userId, OrganizationRole role)
    {
        var row = await db.OrganizationMembers.FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId && m.Role == role);
        if (row is null)
        {
            db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = organizationId, UserId = userId, Role = role });
            return true;
        }
        if (row.Status == MemberStatus.Active) return false;
        row.Status = MemberStatus.Active;
        row.JoinedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>Retire le rôle (la ligne reste, au statut Removed, pour l'historique). Faux s'il n'était pas actif.</summary>
    public async Task<bool> RevokeRoleAsync(Guid organizationId, Guid userId, OrganizationRole role)
    {
        var row = await db.OrganizationMembers.FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId
                                                                        && m.Role == role && m.Status == MemberStatus.Active);
        if (row is null) return false;
        row.Status = MemberStatus.Removed;
        return true;
    }

    /// <summary>Ajoute le rôle s'il manque. Un rôle retiré (Removed) n'est pas rendu ici.</summary>
    public async Task EnsureMemberAsync(Guid organizationId, Guid userId, OrganizationRole role)
    {
        var exists = db.OrganizationMembers.Local.Any(m => m.OrganizationId == organizationId && m.UserId == userId && m.Role == role)
            || await db.OrganizationMembers.AnyAsync(m => m.OrganizationId == organizationId && m.UserId == userId && m.Role == role);
        if (!exists)
            db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = organizationId, UserId = userId, Role = role });
    }

    /// <summary>
    /// Inscrit sur la liste de l'espace un élève entré par l'ancien chemin (code de classe ou clé partagée),
    /// avec un matricule généré. S'il y figure déjà, renseigne seulement sa classe si elle manquait.
    /// </summary>
    public async Task EnsureStudentAsync(Guid organizationId, AppUser student, Guid? classroomId)
    {
        var row = db.OrganizationStudents.Local.FirstOrDefault(s => s.OrganizationId == organizationId && s.UserId == student.Id)
            ?? await db.OrganizationStudents.FirstOrDefaultAsync(s => s.OrganizationId == organizationId && s.UserId == student.Id);
        if (row is not null)
        {
            row.ClassroomId ??= classroomId;
            return;
        }

        var now = DateTime.UtcNow;
        db.OrganizationStudents.Add(new OrganizationStudent
        {
            OrganizationId = organizationId,
            Matricule = SpaceRules.Matricule(now.Year, await NextMatriculeNumberAsync(organizationId)),
            LastName = student.Username,
            ClassroomId = classroomId,
            UserId = student.Id,
            Status = StudentStatus.Active,
            ActivatedAt = now
        });
    }

    /// <summary>
    /// Numéro suivant, réservé par un UPDATE atomique : deux inscriptions simultanées n'ont jamais le même.
    /// Un espace pas encore enregistré (créé dans cette unité de travail) est compté en mémoire.
    /// </summary>
    private async Task<int> NextMatriculeNumberAsync(Guid organizationId)
    {
        var pending = db.Organizations.Local.FirstOrDefault(o => o.Id == organizationId);
        if (pending is not null && db.Entry(pending).State == EntityState.Added)
            return ++pending.MatriculeSeq;

        var reserved = await db.Database
            .SqlQuery<int>($@"UPDATE ""Organizations"" SET ""MatriculeSeq"" = ""MatriculeSeq"" + 1 WHERE ""Id"" = {organizationId} RETURNING ""MatriculeSeq"" AS ""Value""")
            .ToListAsync();
        return reserved.Count == 1 ? reserved[0] : throw new InvalidOperationException($"Espace {organizationId} introuvable.");
    }

    private async Task<bool> OrganizationExistsAsync(Guid id) =>
        db.Organizations.Local.Any(o => o.Id == id) || await db.Organizations.AnyAsync(o => o.Id == id);

    private async Task<Guid?> IndependentSpaceOfAsync(Guid teacherId)
    {
        var local = db.OrganizationMembers.Local
            .Where(m => m.UserId == teacherId && m.Role == OrganizationRole.Owner)
            .Select(m => db.Organizations.Local.FirstOrDefault(o => o.Id == m.OrganizationId && o.Type == OrganizationType.Independent))
            .FirstOrDefault(o => o is not null);
        if (local is not null) return local.Id;

        var stored = await db.OrganizationMembers
            .Where(m => m.UserId == teacherId && m.Role == OrganizationRole.Owner && m.Status == MemberStatus.Active)
            .Join(db.Organizations.Where(o => o.Type == OrganizationType.Independent), m => m.OrganizationId, o => o.Id, (m, o) => o.Id)
            .FirstOrDefaultAsync();
        return stored == Guid.Empty ? null : stored;
    }

    private Guid CreateIndependentSpace(AppUser teacher)
    {
        var org = new Organization { Name = SpaceRules.IndependentSpaceName(teacher.Username), Type = OrganizationType.Independent };
        db.Organizations.Add(org);
        db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = org.Id, UserId = teacher.Id, Role = OrganizationRole.Owner });
        db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = org.Id, UserId = teacher.Id, Role = OrganizationRole.Teacher });
        return org.Id;
    }

    /// <summary>Année en cours de l'espace ; créée (année scolaire d'aujourd'hui) s'il n'en a pas.</summary>
    private async Task<Guid> CurrentYearIdAsync(Guid organizationId)
    {
        var current = db.SchoolYears.Local.FirstOrDefault(y => y.OrganizationId == organizationId && y.IsCurrent)
            ?? await db.SchoolYears.FirstOrDefaultAsync(y => y.OrganizationId == organizationId && y.IsCurrent);
        if (current is not null) return current.Id;

        var (label, startsOn, endsOn) = SpaceRules.SchoolYearOf(DateOnly.FromDateTime(DateTime.UtcNow));
        var year = new SchoolYear { OrganizationId = organizationId, Label = label, StartsOn = startsOn, EndsOn = endsOn, IsCurrent = true };
        db.SchoolYears.Add(year);
        return year.Id;
    }
}
