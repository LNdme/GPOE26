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
    };

    public static async Task EnsureAsync(UserContext db)
    {
        foreach (var sql in Statements)
            await db.Database.ExecuteSqlRawAsync(sql);
    }
}
