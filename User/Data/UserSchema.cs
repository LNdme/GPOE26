using Microsoft.EntityFrameworkCore;

namespace User.Data;

/// <summary>
/// Création / mise à niveau du schéma au démarrage (pas de migrations EF dans ce projet).
/// Chaque instruction est idempotente : on peut relancer le service sur une base existante.
/// </summary>
public static class UserSchema
{
    private static readonly string[] Statements =
    {
        @"CREATE TABLE IF NOT EXISTS ""AppUsers"" (
            ""Id"" uuid NOT NULL,
            ""Email"" character varying(256) NOT NULL,
            ""Username"" character varying(64) NOT NULL,
            ""PasswordHash"" text NOT NULL,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            ""Role"" text NOT NULL DEFAULT 'Student',
            ""Level"" text,
            ""Specialite"" text,
            ""Filiere"" text,
            ""Language"" text NOT NULL DEFAULT 'fr',
            ""LastLoginAt"" timestamp with time zone,
            CONSTRAINT ""PK_AppUsers"" PRIMARY KEY (""Id"")
        )",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AppUsers_Email"" ON ""AppUsers"" (""Email"")",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AppUsers_Username"" ON ""AppUsers"" (""Username"")",

        // ── Phase 1 : profil d'apprentissage, profil enseignant, rattachements ──
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""SubSystem"" text",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""Stage"" text",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""ExamTarget"" text",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""ExamDate"" date",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""FocusSubjects"" text[] NOT NULL DEFAULT ARRAY[]::text[]",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""DailyStudyMinutes"" integer",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""LinkCode"" character varying(16)",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""TeachingSubjects"" text[] NOT NULL DEFAULT ARRAY[]::text[]",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""TeachingContext"" text",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""OrganizationId"" uuid",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""OrganizationName"" text",
        // Éditeurs du contenu du site de l'établissement
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""SiteEditor"" boolean NOT NULL DEFAULT false",
        // Administrateur : un droit, plus un rôle. Les anciens comptes « Admin » gardent ce droit et redeviennent
        // enseignants (le rôle qu'avait le compte administrateur de démonstration ; modifiable ensuite en base).
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""IsAdmin"" boolean NOT NULL DEFAULT false",
        @"UPDATE ""AppUsers"" SET ""IsAdmin"" = true, ""Role"" = 'Teacher' WHERE ""Role"" = 'Admin'",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AppUsers_LinkCode"" ON ""AppUsers"" (""LinkCode"")",

        @"CREATE TABLE IF NOT EXISTS ""Organizations"" (
            ""Id"" uuid NOT NULL,
            ""Name"" character varying(200) NOT NULL,
            ""Type"" text NOT NULL,
            ""City"" text,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_Organizations"" PRIMARY KEY (""Id"")
        )",

        @"CREATE TABLE IF NOT EXISTS ""Classrooms"" (
            ""Id"" uuid NOT NULL,
            ""Name"" character varying(120) NOT NULL,
            ""Kind"" text NOT NULL,
            ""TeacherId"" uuid NOT NULL REFERENCES ""AppUsers"" (""Id"") ON DELETE CASCADE,
            ""OrganizationId"" uuid REFERENCES ""Organizations"" (""Id"") ON DELETE SET NULL,
            ""Level"" text,
            ""SubSystem"" text,
            ""Subject"" text,
            ""InviteCode"" character varying(16) NOT NULL,
            ""IsArchived"" boolean NOT NULL DEFAULT false,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_Classrooms"" PRIMARY KEY (""Id"")
        )",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Classrooms_InviteCode"" ON ""Classrooms"" (""InviteCode"")",
        @"CREATE INDEX IF NOT EXISTS ""IX_Classrooms_TeacherId"" ON ""Classrooms"" (""TeacherId"")",

        @"CREATE TABLE IF NOT EXISTS ""ClassroomMembers"" (
            ""ClassroomId"" uuid NOT NULL REFERENCES ""Classrooms"" (""Id"") ON DELETE CASCADE,
            ""StudentId"" uuid NOT NULL REFERENCES ""AppUsers"" (""Id"") ON DELETE CASCADE,
            ""JoinedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_ClassroomMembers"" PRIMARY KEY (""ClassroomId"", ""StudentId"")
        )",
        @"CREATE INDEX IF NOT EXISTS ""IX_ClassroomMembers_StudentId"" ON ""ClassroomMembers"" (""StudentId"")",

        @"CREATE TABLE IF NOT EXISTS ""ParentChildLinks"" (
            ""Id"" uuid NOT NULL,
            ""ParentId"" uuid NOT NULL REFERENCES ""AppUsers"" (""Id"") ON DELETE CASCADE,
            ""ChildId"" uuid NOT NULL REFERENCES ""AppUsers"" (""Id"") ON DELETE CASCADE,
            ""Status"" text NOT NULL,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            ""RespondedAt"" timestamp with time zone,
            CONSTRAINT ""PK_ParentChildLinks"" PRIMARY KEY (""Id"")
        )",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ParentChildLinks_ParentId_ChildId"" ON ""ParentChildLinks"" (""ParentId"", ""ChildId"")",
        @"CREATE INDEX IF NOT EXISTS ""IX_ParentChildLinks_ChildId"" ON ""ParentChildLinks"" (""ChildId"")",

        @"CREATE TABLE IF NOT EXISTS ""Subscriptions"" (
            ""Id"" uuid NOT NULL,
            ""Plan"" text NOT NULL,
            ""Period"" text NOT NULL,
            ""Status"" text NOT NULL,
            ""OwnerUserId"" uuid REFERENCES ""AppUsers"" (""Id"") ON DELETE CASCADE,
            ""OrganizationId"" uuid REFERENCES ""Organizations"" (""Id"") ON DELETE CASCADE,
            ""Seats"" integer NOT NULL DEFAULT 1,
            ""StartsAt"" timestamp with time zone,
            ""EndsAt"" timestamp with time zone,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_Subscriptions"" PRIMARY KEY (""Id"")
        )",
        @"CREATE INDEX IF NOT EXISTS ""IX_Subscriptions_OwnerUserId"" ON ""Subscriptions"" (""OwnerUserId"")",

        @"CREATE TABLE IF NOT EXISTS ""SubscriptionKeys"" (
            ""Id"" uuid NOT NULL,
            ""Code"" character varying(32) NOT NULL,
            ""SubscriptionId"" uuid NOT NULL REFERENCES ""Subscriptions"" (""Id"") ON DELETE CASCADE,
            ""MaxUses"" integer NOT NULL,
            ""UsedCount"" integer NOT NULL DEFAULT 0,
            ""IsRevoked"" boolean NOT NULL DEFAULT false,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_SubscriptionKeys"" PRIMARY KEY (""Id"")
        )",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SubscriptionKeys_Code"" ON ""SubscriptionKeys"" (""Code"")",

        @"CREATE TABLE IF NOT EXISTS ""KeyRedemptions"" (
            ""Id"" uuid NOT NULL,
            ""KeyId"" uuid NOT NULL REFERENCES ""SubscriptionKeys"" (""Id"") ON DELETE CASCADE,
            ""UserId"" uuid NOT NULL REFERENCES ""AppUsers"" (""Id"") ON DELETE CASCADE,
            ""RedeemedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_KeyRedemptions"" PRIMARY KEY (""Id"")
        )",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_KeyRedemptions_KeyId_UserId"" ON ""KeyRedemptions"" (""KeyId"", ""UserId"")",
        @"CREATE INDEX IF NOT EXISTS ""IX_KeyRedemptions_UserId"" ON ""KeyRedemptions"" (""UserId"")",

        // ── Espaces d'établissement et d'enseignant indépendant (docs/PLAN-ETABLISSEMENT.md, lot 1) ──

        // Un élève activé par code peut n'avoir aucune adresse e-mail (l'index unique accepte plusieurs NULL).
        @"ALTER TABLE ""AppUsers"" ALTER COLUMN ""Email"" DROP NOT NULL",

        // Compteur des matricules générés (format AAAA-NNNN), incrémenté par un UPDATE atomique.
        @"ALTER TABLE ""Organizations"" ADD COLUMN IF NOT EXISTS ""MatriculeSeq"" integer NOT NULL DEFAULT 0",

        // Places élèves consommées (remplace le compteur de la clé partagée au lot 3) et plafond d'enseignants.
        @"ALTER TABLE ""Subscriptions"" ADD COLUMN IF NOT EXISTS ""SeatsUsed"" integer NOT NULL DEFAULT 0",
        @"ALTER TABLE ""Subscriptions"" ADD COLUMN IF NOT EXISTS ""TeacherSeats"" integer",
        @"CREATE INDEX IF NOT EXISTS ""IX_Subscriptions_OrganizationId"" ON ""Subscriptions"" (""OrganizationId"")",

        // Responsable, gestionnaires et enseignants d'un espace. Une personne peut avoir plusieurs rôles
        // (l'enseignant indépendant est Owner et Teacher de son espace) et appartenir à plusieurs espaces.
        @"CREATE TABLE IF NOT EXISTS ""OrganizationMembers"" (
            ""OrganizationId"" uuid NOT NULL REFERENCES ""Organizations"" (""Id"") ON DELETE CASCADE,
            ""UserId"" uuid NOT NULL REFERENCES ""AppUsers"" (""Id"") ON DELETE CASCADE,
            ""Role"" text NOT NULL,
            ""Status"" text NOT NULL DEFAULT 'Active',
            ""JoinedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_OrganizationMembers"" PRIMARY KEY (""OrganizationId"", ""UserId"", ""Role"")
        )",
        @"CREATE INDEX IF NOT EXISTS ""IX_OrganizationMembers_UserId"" ON ""OrganizationMembers"" (""UserId"")",

        @"CREATE TABLE IF NOT EXISTS ""SchoolYears"" (
            ""Id"" uuid NOT NULL,
            ""OrganizationId"" uuid NOT NULL REFERENCES ""Organizations"" (""Id"") ON DELETE CASCADE,
            ""Label"" character varying(20) NOT NULL,
            ""StartsOn"" date NOT NULL,
            ""EndsOn"" date NOT NULL,
            ""IsCurrent"" boolean NOT NULL DEFAULT false,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_SchoolYears"" PRIMARY KEY (""Id"")
        )",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SchoolYears_OrganizationId_Label"" ON ""SchoolYears"" (""OrganizationId"", ""Label"")",
        // Une seule année en cours par espace
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SchoolYears_Current"" ON ""SchoolYears"" (""OrganizationId"") WHERE ""IsCurrent""",

        // La classe appartient à une année ; elle survit au compte de son enseignant (le CASCADE la supprimait).
        @"ALTER TABLE ""Classrooms"" ADD COLUMN IF NOT EXISTS ""SchoolYearId"" uuid REFERENCES ""SchoolYears"" (""Id"") ON DELETE SET NULL",
        @"CREATE INDEX IF NOT EXISTS ""IX_Classrooms_OrganizationId"" ON ""Classrooms"" (""OrganizationId"")",
        @"ALTER TABLE ""Classrooms"" ALTER COLUMN ""TeacherId"" DROP NOT NULL",
        @"DO $$
        DECLARE fk text;
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_Classrooms_AppUsers_TeacherId') THEN
                FOR fk IN
                    SELECT c.conname FROM pg_constraint c
                    JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY (c.conkey)
                    WHERE c.conrelid = '""Classrooms""'::regclass AND c.contype = 'f' AND a.attname = 'TeacherId'
                LOOP
                    EXECUTE format('ALTER TABLE ""Classrooms"" DROP CONSTRAINT %I', fk);
                END LOOP;
                ALTER TABLE ""Classrooms"" ADD CONSTRAINT ""FK_Classrooms_AppUsers_TeacherId""
                    FOREIGN KEY (""TeacherId"") REFERENCES ""AppUsers"" (""Id"") ON DELETE SET NULL;
            END IF;
        END $$",

        // Enseignants d'une classe : un par matière ('' = toutes matières), un professeur principal.
        @"CREATE TABLE IF NOT EXISTS ""ClassroomTeachers"" (
            ""ClassroomId"" uuid NOT NULL REFERENCES ""Classrooms"" (""Id"") ON DELETE CASCADE,
            ""TeacherId"" uuid NOT NULL REFERENCES ""AppUsers"" (""Id"") ON DELETE CASCADE,
            ""Subject"" character varying(80) NOT NULL DEFAULT '',
            ""IsMain"" boolean NOT NULL DEFAULT false,
            ""AssignedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_ClassroomTeachers"" PRIMARY KEY (""ClassroomId"", ""TeacherId"", ""Subject"")
        )",
        @"CREATE INDEX IF NOT EXISTS ""IX_ClassroomTeachers_TeacherId"" ON ""ClassroomTeachers"" (""TeacherId"")",

        // Liste officielle des élèves d'un espace, qui existe AVANT tout compte (UserId vide jusqu'à l'activation).
        @"CREATE TABLE IF NOT EXISTS ""OrganizationStudents"" (
            ""Id"" uuid NOT NULL,
            ""OrganizationId"" uuid NOT NULL REFERENCES ""Organizations"" (""Id"") ON DELETE CASCADE,
            ""Matricule"" character varying(40) NOT NULL,
            ""LastName"" character varying(100) NOT NULL,
            ""FirstName"" character varying(100) NOT NULL DEFAULT '',
            ""BirthDate"" date,
            ""ClassroomId"" uuid REFERENCES ""Classrooms"" (""Id"") ON DELETE SET NULL,
            ""UserId"" uuid REFERENCES ""AppUsers"" (""Id"") ON DELETE SET NULL,
            ""Status"" text NOT NULL DEFAULT 'Pending',
            ""CreatedAt"" timestamp with time zone NOT NULL,
            ""ActivatedAt"" timestamp with time zone,
            CONSTRAINT ""PK_OrganizationStudents"" PRIMARY KEY (""Id"")
        )",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_OrganizationStudents_OrganizationId_Matricule"" ON ""OrganizationStudents"" (""OrganizationId"", ""Matricule"")",
        // Un compte figure au plus une fois dans la liste d'un espace (il peut figurer dans plusieurs espaces).
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_OrganizationStudents_OrganizationId_UserId"" ON ""OrganizationStudents"" (""OrganizationId"", ""UserId"") WHERE ""UserId"" IS NOT NULL",
        @"CREATE INDEX IF NOT EXISTS ""IX_OrganizationStudents_UserId"" ON ""OrganizationStudents"" (""UserId"")",
        @"CREATE INDEX IF NOT EXISTS ""IX_OrganizationStudents_ClassroomId"" ON ""OrganizationStudents"" (""ClassroomId"")",

        // Codes personnels à usage unique (élève, enseignant, responsable). Gardés en clair pour être réimprimés.
        @"CREATE TABLE IF NOT EXISTS ""AccessCodes"" (
            ""Id"" uuid NOT NULL,
            ""Code"" character varying(32) NOT NULL,
            ""OrganizationId"" uuid NOT NULL REFERENCES ""Organizations"" (""Id"") ON DELETE CASCADE,
            ""Kind"" text NOT NULL,
            ""OrganizationStudentId"" uuid REFERENCES ""OrganizationStudents"" (""Id"") ON DELETE CASCADE,
            ""Status"" text NOT NULL DEFAULT 'Issued',
            ""ExpiresAt"" timestamp with time zone NOT NULL,
            ""UsedAt"" timestamp with time zone,
            ""UsedByUserId"" uuid REFERENCES ""AppUsers"" (""Id"") ON DELETE SET NULL,
            ""CreatedByUserId"" uuid REFERENCES ""AppUsers"" (""Id"") ON DELETE SET NULL,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_AccessCodes"" PRIMARY KEY (""Id"")
        )",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AccessCodes_Code"" ON ""AccessCodes"" (""Code"")",
        @"CREATE INDEX IF NOT EXISTS ""IX_AccessCodes_OrganizationId"" ON ""AccessCodes"" (""OrganizationId"")",
        // Un seul code valide à la fois par élève : réimprimer révoque l'ancien.
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AccessCodes_OneIssuedPerStudent"" ON ""AccessCodes"" (""OrganizationStudentId"") WHERE ""Status"" = 'Issued' AND ""OrganizationStudentId"" IS NOT NULL",

        // Code « en blanc » (lot 3, Q8) : la classe où l'élève entrera, faute de ligne élève déjà préparée
        @"ALTER TABLE ""AccessCodes"" ADD COLUMN IF NOT EXISTS ""ClassroomId"" uuid REFERENCES ""Classrooms"" (""Id"") ON DELETE SET NULL",

        // Reprises de données à ne jouer qu'une fois (cf. UserDataMigrations)
        @"CREATE TABLE IF NOT EXISTS ""SchemaVersions"" (
            ""Version"" integer NOT NULL,
            ""Description"" text NOT NULL,
            ""AppliedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_SchemaVersions"" PRIMARY KEY (""Version"")
        )",
    };

    public static async Task EnsureAsync(UserContext db)
    {
        foreach (var sql in Statements)
            await db.Database.ExecuteSqlRawAsync(sql);
    }
}
