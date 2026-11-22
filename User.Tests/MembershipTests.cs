using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using User.Data;
using User.Model;
using User.Service;

namespace User.Tests;

/// <summary>
/// Appartenances datées et réadmission sur une vraie base (docs/PLAN-APPRENANT-AU-CENTRE.md, lots J2 et J3).
/// Le départ ne détruit rien : la période est close, un retour en ouvre une nouvelle, et un ancien élève n'a plus
/// accès à la classe.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class MembershipTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Le_schema_et_les_reprises_se_rejouent_sans_erreur()
    {
        await using var db = fixture.NewContext();
        await UserSchema.EnsureAsync(db);
        await UserDataMigrations.RunAsync(db, NullLogger.Instance);

        Assert.True(await db.SchemaVersions.AnyAsync(v => v.Version == 5, Ct));
        var constraint = await db.Database.SqlQueryRaw<int>(
            """SELECT count(*)::int AS "Value" FROM pg_constraint WHERE conname = 'CK_OrganizationStudents_LeftAt'""").SingleAsync(Ct);
        Assert.Equal(1, constraint);
    }

    [Fact]
    public async Task Depart_puis_reintegration_ouvre_une_nouvelle_periode()
    {
        await using var db = fixture.NewContext();
        var spaces = new SpaceService(db, PostgresFixture.Config());
        var codes = new AccessCodeService(db, spaces);
        var classrooms = new ClassroomService(db, spaces);
        var (org, teacher, classroom) = await PostgresFixture.SpaceAsync(db, "retour");

        var student = PostgresFixture.NewUser("eleve", UserRole.Student);
        db.AppUsers.Add(student);
        var row = new OrganizationStudent { OrganizationId = org.Id, Matricule = "2026-0001", LastName = "Mbarga", ClassroomId = classroom.Id };
        db.OrganizationStudents.Add(row);
        await db.SaveChangesAsync(Ct);

        // 1. Première activation : l'élève entre dans sa classe
        var first = await codes.IssueForStudentAsync(row, teacher.Id);
        await db.SaveChangesAsync(Ct);
        Assert.Equal(CodeError.None, await PostgresFixture.RedeemAsync(db, codes, first.Code, student));
        Assert.Equal(ClassroomRole.Student, await classrooms.RoleInAsync(classroom, student.Id));

        // 2. Départ : la période est close, rien n'est supprimé, et l'ancien élève n'a plus accès à la classe (C1)
        await codes.RemoveStudentAsync(row, StudentStatus.Left);
        await db.SaveChangesAsync(Ct);
        Assert.NotNull(row.LeftAt);
        Assert.Null(await classrooms.RoleInAsync(classroom, student.Id));
        Assert.Equal(0, await db.ClassroomMembers.CountAsync(m => m.ClassroomId == classroom.Id, Ct));
        Assert.Equal(1, await db.ClassroomMembers.IgnoreQueryFilters().CountAsync(m => m.ClassroomId == classroom.Id, Ct));

        // 3. Réintégration (comme StudentEndpoints) puis nouveau code : l'élève redevient actif (C2, cassé avant J2)
        row.Status = StudentStatus.Pending;
        row.ActivatedAt = null;
        row.LeftAt = null;
        await db.SaveChangesAsync(Ct);
        var second = await codes.IssueForStudentAsync(row, teacher.Id);
        await db.SaveChangesAsync(Ct);
        Assert.Equal(CodeError.None, await PostgresFixture.RedeemAsync(db, codes, second.Code, student));

        await db.Entry(row).ReloadAsync(Ct);
        Assert.Equal(StudentStatus.Active, row.Status);
        Assert.Null(row.LeftAt);
        Assert.Equal(ClassroomRole.Student, await classrooms.RoleInAsync(classroom, student.Id));
        // AsNoTracking : lire la base, pas les lignes déjà suivies (ExecuteUpdate ne les met pas à jour)
        var periods = await db.ClassroomMembers.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.ClassroomId == classroom.Id && m.StudentId == student.Id).OrderBy(m => m.JoinedAt).ToListAsync(Ct);
        Assert.Equal(2, periods.Count);
        Assert.NotNull(periods[0].LeftAt);
        Assert.Null(periods[1].LeftAt);
    }

    [Fact]
    public async Task Changer_de_classe_puis_revenir_garde_les_deux_periodes()
    {
        await using var db = fixture.NewContext();
        var spaces = new SpaceService(db, PostgresFixture.Config());
        var codes = new AccessCodeService(db, spaces);
        var (org, teacher, classA) = await PostgresFixture.SpaceAsync(db, "aba");
        var classB = new Classroom { Name = "Classe B", TeacherId = teacher.Id, OrganizationId = org.Id };
        db.Classrooms.Add(classB);
        var student = PostgresFixture.NewUser("eleve-aba", UserRole.Student);
        db.AppUsers.Add(student);
        var row = new OrganizationStudent { OrganizationId = org.Id, Matricule = "2026-0002", LastName = "Abena", ClassroomId = classA.Id };
        db.OrganizationStudents.Add(row);
        await db.SaveChangesAsync(Ct);
        var code = await codes.IssueForStudentAsync(row, teacher.Id);
        await db.SaveChangesAsync(Ct);
        Assert.Equal(CodeError.None, await PostgresFixture.RedeemAsync(db, codes, code.Code, student));

        await codes.MoveStudentAsync(row, classB.Id);   // A → B
        await db.SaveChangesAsync(Ct);
        await codes.MoveStudentAsync(row, classA.Id);   // B → A
        await db.SaveChangesAsync(Ct);

        var inA = await db.ClassroomMembers.IgnoreQueryFilters().AsNoTracking().Where(m => m.ClassroomId == classA.Id && m.StudentId == student.Id).ToListAsync(Ct);
        Assert.Equal(2, inA.Count);
        Assert.Single(inA, m => m.LeftAt is null);
        Assert.Equal(0, await db.ClassroomMembers.CountAsync(m => m.ClassroomId == classB.Id, Ct));
        Assert.Equal(1, await db.ClassroomMembers.IgnoreQueryFilters().CountAsync(m => m.ClassroomId == classB.Id, Ct));
        // Les comptages « actuels » (nombre d'élèves d'une classe) ne voient que les périodes en cours
        Assert.Equal(1, await db.Classrooms.Where(c => c.Id == classA.Id).Select(c => c.Members.Count).SingleAsync(Ct));
    }

    [Fact]
    public async Task Deux_periodes_en_cours_dans_la_meme_classe_sont_impossibles()
    {
        await using var db = fixture.NewContext();
        var (_, _, classroom) = await PostgresFixture.SpaceAsync(db, "double");
        var student = PostgresFixture.NewUser("eleve-double", UserRole.Student);
        db.AppUsers.Add(student);
        db.ClassroomMembers.Add(new ClassroomMember { ClassroomId = classroom.Id, StudentId = student.Id });
        await db.SaveChangesAsync(Ct);

        db.ClassroomMembers.Add(new ClassroomMember { ClassroomId = classroom.Id, StudentId = student.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Un_depart_sans_date_est_refuse_par_la_base()
    {
        await using var db = fixture.NewContext();
        var (org, _, _) = await PostgresFixture.SpaceAsync(db, "check");
        db.OrganizationStudents.Add(new OrganizationStudent { OrganizationId = org.Id, Matricule = "2026-0003", LastName = "Essomba", Status = StudentStatus.Left });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Un_retrait_de_partage_vise_exactement_un_espace_ou_un_parent()
    {
        await using var db = fixture.NewContext();
        var learner = PostgresFixture.NewUser("adulte", UserRole.Student);
        db.AppUsers.Add(learner);
        await db.SaveChangesAsync(Ct);

        db.SharingOptOuts.Add(new SharingOptOut { LearnerId = learner.Id, Scope = SharingScope.Progress });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task La_majorite_reputee_lit_le_lien_parent_et_l_annee_de_naissance()
    {
        await using var db = fixture.NewContext();
        var visibility = new VisibilityService(db, PostgresFixture.Config());
        var child = PostgresFixture.NewUser("enfant", UserRole.Student);
        var parent = PostgresFixture.NewUser("parent", UserRole.Parent);
        db.AppUsers.AddRange(child, parent);
        await db.SaveChangesAsync(Ct);
        Assert.True(await visibility.IsDeemedAdultAsync(child));   // sans parent ni âge connu : majeure

        db.ParentChildLinks.Add(new ParentChildLink { ParentId = parent.Id, ChildId = child.Id, Status = LinkStatus.Active });
        await db.SaveChangesAsync(Ct);
        Assert.False(await visibility.IsDeemedAdultAsync(child));  // un parent rattaché : réputée mineure

        child.BirthYear = DateTime.UtcNow.Year - 30;
        Assert.True(await visibility.IsDeemedAdultAsync(child));   // l'année de naissance l'emporte
    }
}
