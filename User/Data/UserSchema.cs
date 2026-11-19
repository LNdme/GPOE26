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
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""ExamTarget"" text",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""ExamDate"" date",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""FocusSubjects"" text[] NOT NULL DEFAULT ARRAY[]::text[]",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""DailyStudyMinutes"" integer",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""LinkCode"" character varying(16)",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""TeachingSubjects"" text[] NOT NULL DEFAULT ARRAY[]::text[]",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""TeachingContext"" text",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""OrganizationId"" uuid",
        @"ALTER TABLE ""AppUsers"" ADD COLUMN IF NOT EXISTS ""OrganizationName"" text",
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
}
