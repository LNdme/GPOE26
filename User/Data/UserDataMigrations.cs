using Microsoft.EntityFrameworkCore;
using User.Model;
using User.Service;

namespace User.Data;

/// <summary>
/// Reprises de données jouées UNE fois, après UserSchema (qui ne fait que créer tables et colonnes)
/// et après le jeu de démonstration. Chaque version est notée dans SchemaVersions, dans la même transaction
/// que ses modifications : une reprise interrompue est rejouée entière au démarrage suivant.
/// </summary>
public static class UserDataMigrations
{
    public static async Task RunAsync(UserContext db, ILogger logger, DateTime? now = null)
    {
        var at = now ?? DateTime.UtcNow;
        await ApplyAsync(db, logger, 2,
            "Espaces : établissements et enseignants indépendants, années scolaires, enseignants des classes, liste des élèves",
            () => SpacesV2Async(db, at));
        await ApplyAsync(db, logger, 3,
            "Codes personnels : les clés partagées des établissements (ETB-/ORG-) sont retirées",
            () => RetireSchoolKeysV3Async(db));
    }

    /// <summary>
    /// Version 3 (lot 3). Ceux qui avaient activé une clé d'établissement sont déjà sur sa liste ou parmi ses
    /// enseignants (reprise v2, puis tenue à jour) : leur accès passe par l'espace, la clé peut être retirée.
    /// Les clés de licences sans établissement (paiement simulé) et les clés familiales ne changent pas.
    /// </summary>
    private static Task RetireSchoolKeysV3Async(UserContext db) =>
        db.Database.ExecuteSqlRawAsync("""
            UPDATE "SubscriptionKeys" k SET "IsRevoked" = true
            FROM "Subscriptions" s
            WHERE s."Id" = k."SubscriptionId" AND s."Plan" = 'OrganizationLicense' AND s."OrganizationId" IS NOT NULL
              AND NOT k."IsRevoked"
            """);

    private static async Task ApplyAsync(UserContext db, ILogger logger, int version, string description, Func<Task> step)
    {
        if (await db.SchemaVersions.AnyAsync(v => v.Version == version)) return;

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            // Deux instances du service démarrées ensemble : la seconde attend puis voit la version notée.
            await db.Database.ExecuteSqlRawAsync(@"LOCK TABLE ""SchemaVersions"" IN EXCLUSIVE MODE");
            if (await db.SchemaVersions.AnyAsync(v => v.Version == version))
            {
                await tx.RollbackAsync();
                return;
            }

            await step();
            db.SchemaVersions.Add(new SchemaVersion { Version = version, Description = description });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            logger.LogInformation("Reprise de données {Version} appliquée : {Description}", version, description);
        });
    }

    /// <summary>
    /// Version 2 (docs/PLAN-ETABLISSEMENT.md, lot 1, section 5). Les clés ETB-/ORG- restent valides :
    /// elles donnent encore l'accès Premium jusqu'au lot 3, qui recalcule les places et les retire.
    /// </summary>
    private static async Task SpacesV2Async(UserContext db, DateTime now)
    {
        var (label, startsOn, endsOn) = SpaceRules.SchoolYearOf(DateOnly.FromDateTime(now));

        string[] statements =
        [
            // 1. Classe sans établissement d'un enseignant rattaché par clé à un établissement : vers cet établissement.
            """
            UPDATE "Classrooms" c SET "OrganizationId" = u."OrganizationId"
            FROM "AppUsers" u
            WHERE c."OrganizationId" IS NULL AND u."Id" = c."TeacherId" AND u."OrganizationId" IS NOT NULL
              AND EXISTS (SELECT 1 FROM "Organizations" o WHERE o."Id" = u."OrganizationId")
            """,

            // 2. Autres classes sans établissement : un espace « indépendant » par enseignant, dont il est responsable et enseignant.
            """
            WITH teachers AS (
                SELECT DISTINCT c."TeacherId" AS uid FROM "Classrooms" c
                WHERE c."OrganizationId" IS NULL AND c."TeacherId" IS NOT NULL
            ), spaces AS (
                SELECT uid, gen_random_uuid() AS oid FROM teachers
            ), orgs AS (
                INSERT INTO "Organizations" ("Id", "Name", "Type", "City", "CreatedAt", "MatriculeSeq")
                SELECT s.oid, 'Espace de ' || u."Username", 'Independent', NULL, {0}, 0
                FROM spaces s JOIN "AppUsers" u ON u."Id" = s.uid
                RETURNING "Id"
            )
            INSERT INTO "OrganizationMembers" ("OrganizationId", "UserId", "Role", "Status", "JoinedAt")
            SELECT s.oid, s.uid, r.role, 'Active', {0}
            FROM spaces s CROSS JOIN (VALUES ('Owner'), ('Teacher')) AS r(role)
            """,
            """
            UPDATE "Classrooms" c SET "OrganizationId" = m."OrganizationId"
            FROM "OrganizationMembers" m JOIN "Organizations" o ON o."Id" = m."OrganizationId"
            WHERE c."OrganizationId" IS NULL AND m."UserId" = c."TeacherId" AND m."Role" = 'Owner' AND o."Type" = 'Independent'
            """,

            // 3. Enseignants : ceux qui animent une classe de l'espace, et ceux qui ont activé la clé de l'établissement.
            """
            INSERT INTO "OrganizationMembers" ("OrganizationId", "UserId", "Role", "Status", "JoinedAt")
            SELECT DISTINCT ON (c."OrganizationId", c."TeacherId") c."OrganizationId", c."TeacherId", 'Teacher', 'Active', c."CreatedAt"
            FROM "Classrooms" c
            WHERE c."OrganizationId" IS NOT NULL AND c."TeacherId" IS NOT NULL
            ORDER BY c."OrganizationId", c."TeacherId", c."CreatedAt"
            ON CONFLICT DO NOTHING
            """,
            """
            INSERT INTO "OrganizationMembers" ("OrganizationId", "UserId", "Role", "Status", "JoinedAt")
            SELECT u."OrganizationId", u."Id", 'Teacher', 'Active',
                   COALESCE((SELECT min(r."RedeemedAt") FROM "KeyRedemptions" r WHERE r."UserId" = u."Id"), u."CreatedAt")
            FROM "AppUsers" u
            WHERE u."Role" = 'Teacher' AND u."OrganizationId" IS NOT NULL
              AND EXISTS (SELECT 1 FROM "Organizations" o WHERE o."Id" = u."OrganizationId")
            ON CONFLICT DO NOTHING
            """,

            // 4. Une année scolaire en cours dans chaque espace, et chaque classe rattachée à celle de son espace.
            """
            INSERT INTO "SchoolYears" ("Id", "OrganizationId", "Label", "StartsOn", "EndsOn", "IsCurrent", "CreatedAt")
            SELECT gen_random_uuid(), o."Id", {1}, {2}, {3}, true, {0}
            FROM "Organizations" o
            WHERE NOT EXISTS (SELECT 1 FROM "SchoolYears" y WHERE y."OrganizationId" = o."Id")
            """,
            """
            UPDATE "Classrooms" c SET "SchoolYearId" = y."Id"
            FROM "SchoolYears" y
            WHERE c."SchoolYearId" IS NULL AND y."OrganizationId" = c."OrganizationId" AND y."IsCurrent"
            """,

            // 5. L'enseignant de chaque classe devient son professeur principal, pour la matière de la classe.
            """
            INSERT INTO "ClassroomTeachers" ("ClassroomId", "TeacherId", "Subject", "IsMain", "AssignedAt")
            SELECT c."Id", c."TeacherId", left(COALESCE(c."Subject", ''), 80), true, c."CreatedAt"
            FROM "Classrooms" c
            WHERE c."TeacherId" IS NOT NULL
            ON CONFLICT DO NOTHING
            """,

            // 6. Liste des élèves : les membres des classes de l'espace, et les élèves qui ont activé sa clé.
            //    Matricule généré AAAA-NNNN (année d'entrée, numéro d'ordre dans l'espace) ; le nom est le nom
            //    d'utilisateur, faute d'autre nom connu. Classe retenue : la plus récente non archivée.
            """
            WITH candidates AS (
                SELECT c."OrganizationId" AS org, m."StudentId" AS uid, m."JoinedAt" AS since, c."Id" AS cls, c."IsArchived" AS archived
                FROM "ClassroomMembers" m JOIN "Classrooms" c ON c."Id" = m."ClassroomId"
                WHERE c."OrganizationId" IS NOT NULL
                UNION ALL
                SELECT u."OrganizationId", u."Id",
                       COALESCE((SELECT min(r."RedeemedAt") FROM "KeyRedemptions" r WHERE r."UserId" = u."Id"), u."CreatedAt"),
                       NULL, true
                FROM "AppUsers" u
                WHERE u."Role" = 'Student' AND u."OrganizationId" IS NOT NULL
                  AND EXISTS (SELECT 1 FROM "Organizations" o WHERE o."Id" = u."OrganizationId")
            ), students AS (
                SELECT org, uid, min(since) AS since,
                       (array_agg(cls ORDER BY archived, since DESC) FILTER (WHERE cls IS NOT NULL))[1] AS cls
                FROM candidates cd
                WHERE NOT EXISTS (SELECT 1 FROM "OrganizationStudents" s WHERE s."OrganizationId" = cd.org AND s."UserId" = cd.uid)
                GROUP BY org, uid
            ), numbered AS (
                SELECT st.*, o."MatriculeSeq" + row_number() OVER (PARTITION BY st.org ORDER BY st.since, st.uid) AS n
                FROM students st JOIN "Organizations" o ON o."Id" = st.org
            ), inserted AS (
                INSERT INTO "OrganizationStudents"
                    ("Id", "OrganizationId", "Matricule", "LastName", "FirstName", "BirthDate", "ClassroomId", "UserId", "Status", "CreatedAt", "ActivatedAt")
                SELECT gen_random_uuid(), nb.org,
                       to_char(nb.since AT TIME ZONE 'UTC', 'YYYY') || '-' || CASE WHEN nb.n < 10000 THEN lpad(nb.n::text, 4, '0') ELSE nb.n::text END,
                       left(u."Username", 100), '', NULL, nb.cls, nb.uid, 'Active', {0}, nb.since
                FROM numbered nb JOIN "AppUsers" u ON u."Id" = nb.uid
                RETURNING "OrganizationId"
            )
            UPDATE "Organizations" o SET "MatriculeSeq" = o."MatriculeSeq" + c.cnt
            FROM (SELECT "OrganizationId", count(*) AS cnt FROM inserted GROUP BY "OrganizationId") c
            WHERE o."Id" = c."OrganizationId"
            """,
        ];

        foreach (var sql in statements)
            await db.Database.ExecuteSqlRawAsync(sql, now, label, startsOn, endsOn);
    }
}
