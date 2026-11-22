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
            ""WorkspaceId"" uuid,
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
            ""Id"" uuid NOT NULL DEFAULT gen_random_uuid(),
            ""LeftAt"" timestamp with time zone,
            CONSTRAINT ""PK_ClassroomMembers"" PRIMARY KEY (""Id"")
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

        // Phase 2, lot 10 : code de l'établissement (connexion par matricule) et mot de passe à choisir
        @"ALTER TABLE ""Organizations"" ADD COLUMN IF NOT EXISTS ""ShortCode"" character varying(10)",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Organizations_ShortCode"" ON ""Organizations"" (""ShortCode"") WHERE ""ShortCode"" IS NOT NULL",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""MustChangePassword"" boolean NOT NULL DEFAULT false",

        // ── Contexte d'apprentissage (plan de l'agent noyau, lot B1) : null = déduit des anciens champs ──
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""Country"" character varying(2)",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""EducationSystem"" character varying(100)",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""LevelBand"" text",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""LearningGoal"" text",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""GradeScale"" text",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""TimeZone"" character varying(64)",

        // Phase 2, lot 9 : e-mail importé et colonnes en plus du fichier de l'établissement (JSON, titre → valeur)
        @"ALTER TABLE ""OrganizationStudents"" ADD COLUMN IF NOT EXISTS ""Email"" character varying(256)",
        @"ALTER TABLE ""OrganizationStudents"" ADD COLUMN IF NOT EXISTS ""ExtraJson"" text",

        // Le code de classe commun ne sert plus (lot 7) : les nouvelles classes n'en ont pas
        @"ALTER TABLE ""Classrooms"" ALTER COLUMN ""InviteCode"" DROP NOT NULL",

        // Lot J2 (docs/PLAN-APPRENANT-AU-CENTRE.md) : une ligne par période d'appartenance, le départ ne supprime plus rien.
        // Clé primaire sur Id au lieu de (ClassroomId, StudentId) ; au plus une période en cours par élève et par classe.
        @"ALTER TABLE ""ClassroomMembers"" ADD COLUMN IF NOT EXISTS ""Id"" uuid NOT NULL DEFAULT gen_random_uuid()",
        @"ALTER TABLE ""ClassroomMembers"" ADD COLUMN IF NOT EXISTS ""LeftAt"" timestamp with time zone",
        @"DO $$ BEGIN
            IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_ClassroomMembers' AND array_length(conkey, 1) = 2) THEN
                ALTER TABLE ""ClassroomMembers"" DROP CONSTRAINT ""PK_ClassroomMembers"";
                ALTER TABLE ""ClassroomMembers"" ADD CONSTRAINT ""PK_ClassroomMembers"" PRIMARY KEY (""Id"");
            END IF;
        END $$",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ClassroomMembers_ClassroomId_StudentId"" ON ""ClassroomMembers"" (""ClassroomId"", ""StudentId"") WHERE ""LeftAt"" IS NULL",
        // Départ d'un élève de la liste d'un espace ; la contrainte (statut <=> date) est posée par la reprise v5, après remplissage
        @"ALTER TABLE ""OrganizationStudents"" ADD COLUMN IF NOT EXISTS ""LeftAt"" timestamp with time zone",
        // Lot J3 : année de naissance facultative (majorité réputée, choix T3)
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""BirthYear"" integer",
        // Lot J3 : ce qu'une personne majeure ne partage plus avec un espace ou un parent (jamais les résultats officiels)
        @"CREATE TABLE IF NOT EXISTS ""SharingOptOuts"" (
            ""Id"" uuid NOT NULL,
            ""LearnerId"" uuid NOT NULL REFERENCES ""AppUsers"" (""Id"") ON DELETE CASCADE,
            ""OrganizationId"" uuid REFERENCES ""Organizations"" (""Id"") ON DELETE CASCADE,
            ""GuardianId"" uuid REFERENCES ""AppUsers"" (""Id"") ON DELETE CASCADE,
            ""Scope"" text NOT NULL,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_SharingOptOuts"" PRIMARY KEY (""Id""),
            CONSTRAINT ""CK_SharingOptOuts_OneTarget"" CHECK ((""OrganizationId"" IS NULL) <> (""GuardianId"" IS NULL)),
            CONSTRAINT ""CK_SharingOptOuts_Scope"" CHECK (""Scope"" IN ('Progress', 'WeakPoints'))
        )",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SharingOptOuts_Organization"" ON ""SharingOptOuts"" (""LearnerId"", ""OrganizationId"", ""Scope"") WHERE ""OrganizationId"" IS NOT NULL",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SharingOptOuts_Guardian"" ON ""SharingOptOuts"" (""LearnerId"", ""GuardianId"", ""Scope"") WHERE ""GuardianId"" IS NOT NULL",

        // Reprises de données à ne jouer qu'une fois (cf. UserDataMigrations)
        @"CREATE TABLE IF NOT EXISTS ""SchemaVersions"" (
            ""Version"" integer NOT NULL,
            ""Description"" text NOT NULL,
            ""AppliedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_SchemaVersions"" PRIMARY KEY (""Version"")
        )",
        // ── Espaces et rôles contextuels (ajout rétrocompatible) ────────────────
        @"CREATE TABLE IF NOT EXISTS ""Workspaces"" (
            ""Id"" uuid NOT NULL,
            ""Name"" character varying(200) NOT NULL,
            ""Kind"" text NOT NULL,
            ""OwnerUserId"" uuid REFERENCES ""AppUsers"" (""Id"") ON DELETE CASCADE,
            ""OrganizationId"" uuid REFERENCES ""Organizations"" (""Id"") ON DELETE CASCADE,
            ""CreatedByUserId"" uuid,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_Workspaces"" PRIMARY KEY (""Id"")
        )",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Workspaces_Kind_OwnerUserId"" ON ""Workspaces"" (""Kind"", ""OwnerUserId"")",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Workspaces_OrganizationId"" ON ""Workspaces"" (""OrganizationId"")",

        @"CREATE TABLE IF NOT EXISTS ""WorkspaceMemberships"" (
            ""Id"" uuid NOT NULL,
            ""WorkspaceId"" uuid NOT NULL REFERENCES ""Workspaces"" (""Id"") ON DELETE CASCADE,
            ""UserId"" uuid NOT NULL REFERENCES ""AppUsers"" (""Id"") ON DELETE CASCADE,
            ""Status"" text NOT NULL,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            ""JoinedAt"" timestamp with time zone,
            CONSTRAINT ""PK_WorkspaceMemberships"" PRIMARY KEY (""Id"")
        )",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_WorkspaceMemberships_WorkspaceId_UserId"" ON ""WorkspaceMemberships"" (""WorkspaceId"", ""UserId"")",
        @"CREATE INDEX IF NOT EXISTS ""IX_WorkspaceMemberships_UserId_Status"" ON ""WorkspaceMemberships"" (""UserId"", ""Status"")",

        @"CREATE TABLE IF NOT EXISTS ""WorkspaceRoleGrants"" (
            ""MembershipId"" uuid NOT NULL REFERENCES ""WorkspaceMemberships"" (""Id"") ON DELETE CASCADE,
            ""Role"" text NOT NULL,
            ""GrantedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_WorkspaceRoleGrants"" PRIMARY KEY (""MembershipId"", ""Role"")
        )",

        // MCP grants are scoped to one user, workspace and extension. Process options
        // are encrypted by the application and remain unusable until independently verified.
        @"CREATE TABLE IF NOT EXISTS ""McpPermissionGrants"" (
            ""UserId"" uuid NOT NULL REFERENCES ""AppUsers"" (""Id"") ON DELETE CASCADE,
            ""WorkspaceId"" uuid NOT NULL REFERENCES ""Workspaces"" (""Id"") ON DELETE CASCADE,
            ""ExtensionId"" character varying(200) NOT NULL,
            ""CapabilityIds"" text[] NOT NULL DEFAULT ARRAY[]::text[],
            ""PermissionIds"" text[] NOT NULL DEFAULT ARRAY[]::text[],
            ""ApprovedByUserId"" uuid NOT NULL REFERENCES ""AppUsers"" (""Id"") ON DELETE RESTRICT,
            ""ApprovedAt"" timestamp with time zone NOT NULL,
            ""RevokedAt"" timestamp with time zone,
            CONSTRAINT ""PK_McpPermissionGrants"" PRIMARY KEY (""UserId"", ""WorkspaceId"", ""ExtensionId"")
        )",
        @"CREATE INDEX IF NOT EXISTS ""IX_McpPermissionGrants_WorkspaceId_UserId_RevokedAt""
          ON ""McpPermissionGrants"" (""WorkspaceId"", ""UserId"", ""RevokedAt"")",
        @"CREATE TABLE IF NOT EXISTS ""McpProcessConfigurations"" (
            ""UserId"" uuid NOT NULL REFERENCES ""AppUsers"" (""Id"") ON DELETE CASCADE,
            ""WorkspaceId"" uuid NOT NULL REFERENCES ""Workspaces"" (""Id"") ON DELETE CASCADE,
            ""ExtensionId"" character varying(200) NOT NULL,
            ""ProtectedOptions"" text NOT NULL,
            ""IsVerified"" boolean NOT NULL DEFAULT false,
            ""IsSandboxed"" boolean NOT NULL DEFAULT false,
            ""UpdatedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_McpProcessConfigurations"" PRIMARY KEY (""UserId"", ""WorkspaceId"", ""ExtensionId"")
        )",
        @"ALTER TABLE ""McpProcessConfigurations""
          ADD COLUMN IF NOT EXISTS ""IsSandboxed"" boolean NOT NULL DEFAULT false",

        // Chaque compte existant reçoit son espace personnel privé, sans déplacer ses données.
        @"INSERT INTO ""Workspaces"" (""Id"", ""Name"", ""Kind"", ""OwnerUserId"", ""OrganizationId"", ""CreatedByUserId"", ""CreatedAt"")
          SELECT gen_random_uuid(), 'Espace personnel', 'Personal', u.""Id"", NULL, u.""Id"", now()
          FROM ""AppUsers"" u
          ON CONFLICT (""Kind"", ""OwnerUserId"") DO NOTHING",
        @"INSERT INTO ""WorkspaceMemberships"" (""Id"", ""WorkspaceId"", ""UserId"", ""Status"", ""CreatedAt"", ""JoinedAt"")
          SELECT gen_random_uuid(), w.""Id"", w.""OwnerUserId"", 'Active', now(), now()
          FROM ""Workspaces"" w
          WHERE w.""Kind"" = 'Personal' AND w.""OwnerUserId"" IS NOT NULL
          ON CONFLICT (""WorkspaceId"", ""UserId"") DO NOTHING",
        @"INSERT INTO ""WorkspaceRoleGrants"" (""MembershipId"", ""Role"", ""GrantedAt"")
          SELECT m.""Id"", roles.""Role"", now()
          FROM ""WorkspaceMemberships"" m
          JOIN ""Workspaces"" w ON w.""Id"" = m.""WorkspaceId""
          CROSS JOIN (
            VALUES ('Owner'), ('Learner'),
                   ('Instructor')
          ) AS roles(""Role"")
          WHERE w.""Kind"" = 'Personal'
            AND (roles.""Role"" <> 'Instructor' OR EXISTS (
              SELECT 1 FROM ""AppUsers"" u
              WHERE u.""Id"" = m.""UserId"" AND u.""Role"" = 'Teacher'
            ))
          ON CONFLICT (""MembershipId"", ""Role"") DO NOTHING",

        // Les organisations existantes deviennent des espaces, sans modifier les licences.
        @"INSERT INTO ""Workspaces"" (""Id"", ""Name"", ""Kind"", ""OwnerUserId"", ""OrganizationId"", ""CreatedByUserId"", ""CreatedAt"")
          SELECT gen_random_uuid(), o.""Name"", 'Organization', NULL, o.""Id"", NULL, now()
          FROM ""Organizations"" o
          -- Espace d'enseignant indépendant (plan établissement) : ses classes vont dans l'espace personnel de l'enseignant
          WHERE o.""Type"" <> 'Independent'
          ON CONFLICT (""OrganizationId"") DO NOTHING",
        @"INSERT INTO ""WorkspaceMemberships"" (""Id"", ""WorkspaceId"", ""UserId"", ""Status"", ""CreatedAt"", ""JoinedAt"")
          SELECT gen_random_uuid(), w.""Id"", u.""Id"", 'Active', now(), now()
          FROM ""AppUsers"" u
          JOIN ""Workspaces"" w ON w.""OrganizationId"" = u.""OrganizationId""
          WHERE u.""OrganizationId"" IS NOT NULL
          ON CONFLICT (""WorkspaceId"", ""UserId"") DO NOTHING",
        @"INSERT INTO ""WorkspaceRoleGrants"" (""MembershipId"", ""Role"", ""GrantedAt"")
          SELECT m.""Id"",
                 CASE u.""Role"" WHEN 'Teacher' THEN 'Instructor' WHEN 'Student' THEN 'Learner' ELSE 'Member' END,
                 now()
          FROM ""WorkspaceMemberships"" m
          JOIN ""Workspaces"" w ON w.""Id"" = m.""WorkspaceId""
          JOIN ""AppUsers"" u ON u.""Id"" = m.""UserId""
          WHERE w.""Kind"" = 'Organization'
          ON CONFLICT (""MembershipId"", ""Role"") DO NOTHING",

        // Les parents reçoivent un espace familial, même avant le premier rattachement d'enfant.
        @"INSERT INTO ""Workspaces"" (""Id"", ""Name"", ""Kind"", ""OwnerUserId"", ""OrganizationId"", ""CreatedByUserId"", ""CreatedAt"")
          SELECT gen_random_uuid(), 'Espace familial', 'Family', owners.""ParentId"", NULL, owners.""ParentId"", now()
          FROM (
              SELECT DISTINCT l.""ParentId""
              FROM ""ParentChildLinks"" l
              WHERE l.""Status"" = 'Active'
              UNION
              SELECT u.""Id"" AS ""ParentId""
              FROM ""AppUsers"" u
              WHERE u.""Role"" = 'Parent'
              UNION
              SELECT DISTINCT s.""OwnerUserId""
              FROM ""Subscriptions"" s
              WHERE s.""Plan"" = 'Family' AND s.""OwnerUserId"" IS NOT NULL
          ) owners
          ON CONFLICT (""Kind"", ""OwnerUserId"") DO NOTHING",
        @"INSERT INTO ""WorkspaceMemberships"" (""Id"", ""WorkspaceId"", ""UserId"", ""Status"", ""CreatedAt"", ""JoinedAt"")
          SELECT gen_random_uuid(), w.""Id"", w.""OwnerUserId"", 'Active', now(), now()
          FROM ""Workspaces"" w
          WHERE w.""Kind"" = 'Family' AND w.""OwnerUserId"" IS NOT NULL
          ON CONFLICT (""WorkspaceId"", ""UserId"") DO NOTHING",
        @"INSERT INTO ""WorkspaceRoleGrants"" (""MembershipId"", ""Role"", ""GrantedAt"")
          SELECT m.""Id"", roles.""Role"", now()
          FROM ""WorkspaceMemberships"" m
          JOIN ""Workspaces"" w ON w.""Id"" = m.""WorkspaceId"" AND w.""OwnerUserId"" = m.""UserId""
          CROSS JOIN (VALUES ('Owner'), ('Guardian')) AS roles(""Role"")
          WHERE w.""Kind"" = 'Family'
          ON CONFLICT (""MembershipId"", ""Role"") DO NOTHING",
        @"INSERT INTO ""WorkspaceMemberships"" (""Id"", ""WorkspaceId"", ""UserId"", ""Status"", ""CreatedAt"", ""JoinedAt"")
          SELECT gen_random_uuid(), w.""Id"", l.""ChildId"", 'Active', now(), now()
          FROM ""ParentChildLinks"" l
          JOIN ""Workspaces"" w ON w.""Kind"" = 'Family' AND w.""OwnerUserId"" = l.""ParentId""
          WHERE l.""Status"" = 'Active'
          ON CONFLICT (""WorkspaceId"", ""UserId"") DO NOTHING",
        @"INSERT INTO ""WorkspaceRoleGrants"" (""MembershipId"", ""Role"", ""GrantedAt"")
          SELECT m.""Id"", 'Learner', now()
          FROM ""WorkspaceMemberships"" m
          JOIN ""Workspaces"" w ON w.""Id"" = m.""WorkspaceId""
          WHERE w.""Kind"" = 'Family' AND w.""OwnerUserId"" <> m.""UserId""
          ON CONFLICT (""MembershipId"", ""Role"") DO NOTHING",
        @"ALTER TABLE ""Classrooms"" ADD COLUMN IF NOT EXISTS ""WorkspaceId"" uuid",
        @"UPDATE ""Classrooms"" c
          SET ""WorkspaceId"" = w.""Id""
          FROM ""Workspaces"" w
          WHERE c.""WorkspaceId"" IS NULL
            AND c.""OrganizationId"" IS NOT NULL
            AND w.""Kind"" = 'Organization'
            AND w.""OrganizationId"" = c.""OrganizationId""",
        @"UPDATE ""Classrooms"" c
          SET ""WorkspaceId"" = w.""Id""
          FROM ""Workspaces"" w
          WHERE c.""WorkspaceId"" IS NULL
            AND w.""Kind"" = 'Personal'
            AND w.""OwnerUserId"" = c.""TeacherId""",
        @"CREATE INDEX IF NOT EXISTS ""IX_Classrooms_WorkspaceId"" ON ""Classrooms"" (""WorkspaceId"")",
    };

    public static async Task EnsureAsync(UserContext db)
    {
        foreach (var sql in Statements)
            await db.Database.ExecuteSqlRawAsync(sql);
    }

    /// <summary>
    /// Rejoue le remplissage des espaces de travail (création, adhésions, classes rattachées) : à appeler après
    /// le jeu de démonstration et les reprises de données, qui créent ou déplacent des classes après EnsureAsync.
    /// Idempotent : seules les lignes manquantes sont ajoutées, seules les classes sans espace de travail sont rattachées.
    /// </summary>
    public static async Task AlignWorkspacesAsync(UserContext db)
    {
        var start = Array.FindIndex(Statements, sql => sql.Contains("CREATE TABLE IF NOT EXISTS \"Workspaces\""));
        foreach (var sql in Statements.Skip(start).Where(IsFill))
            await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static bool IsFill(string sql) =>
        sql.TrimStart().StartsWith("INSERT", StringComparison.Ordinal) || sql.TrimStart().StartsWith("UPDATE", StringComparison.Ordinal);
}
