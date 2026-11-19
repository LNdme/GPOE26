using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System.IdentityModel.Tokens.Jwt;
using GPOE26.Workspaces.Abstractions;
using User.Data;
using User.Model;
using User.Service;
using Xunit.Sdk;

namespace GPOE26.Tests;

public sealed class UserWorkspaceSchemaTests
{
    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task EnsureAsyncBackfillsLegacyWorkspacesAndIsIdempotent()
    {
        var connectionString = Environment.GetEnvironmentVariable("GPOE26_USER_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw SkipException.ForSkip("Set GPOE26_USER_TEST_CONNECTION_STRING to run PostgreSQL integration tests.");

        var connectionBuilder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!connectionBuilder.Database.Contains("test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The PostgreSQL integration-test database name must contain 'test'.");

        var schemaName = $"workspace_test_{Guid.NewGuid():N}";
        await using var adminConnection = new NpgsqlConnection(connectionBuilder.ConnectionString);
        await adminConnection.OpenAsync();
        await using (var createSchema = new NpgsqlCommand($"CREATE SCHEMA \"{schemaName}\"", adminConnection))
            await createSchema.ExecuteNonQueryAsync();

        connectionBuilder.SearchPath = schemaName;
        try
        {
            await using var connection = new NpgsqlConnection(connectionBuilder.ConnectionString);
            await connection.OpenAsync();
            await CreateLegacySchemaAndDataAsync(connection);

            var options = new DbContextOptionsBuilder<UserContext>()
                .UseNpgsql(connectionBuilder.ConnectionString)
                .Options;
            await using var db = new UserContext(options);

            await UserSchema.EnsureAsync(db);
            var initialCounts = await ReadCountsAsync(connection);
            Assert.Equal((9, 11, 20), initialCounts);

            var parentId = LegacyData.ParentId;
            var childId = LegacyData.ActiveChildId;
            var familyMembership = await db.WorkspaceMemberships
                .Include(membership => membership.Workspace)
                .Include(membership => membership.RoleGrants)
                .SingleAsync(membership =>
                    membership.UserId == parentId &&
                    membership.Workspace.Kind == WorkspaceKind.Family);
            Assert.Contains(familyMembership.RoleGrants, grant => grant.Role == WorkspaceRole.Owner);
            Assert.Contains(familyMembership.RoleGrants, grant => grant.Role == WorkspaceRole.Guardian);

            Assert.False(await db.WorkspaceMemberships.AnyAsync(membership =>
                membership.UserId == LegacyData.PendingChildId &&
                membership.Workspace.Kind == WorkspaceKind.Family));

            var organizationAdminMembership = await db.WorkspaceMemberships
                .Include(membership => membership.Workspace)
                .Include(membership => membership.RoleGrants)
                .SingleAsync(membership =>
                    membership.UserId == LegacyData.GlobalAdminId &&
                    membership.Workspace.Kind == WorkspaceKind.Organization);
            Assert.Contains(organizationAdminMembership.RoleGrants, grant => grant.Role == WorkspaceRole.Member);
            Assert.DoesNotContain(organizationAdminMembership.RoleGrants, grant => grant.Role == WorkspaceRole.Admin);

            var teacherPersonalMembership = await db.WorkspaceMemberships
                .Include(membership => membership.Workspace)
                .Include(membership => membership.RoleGrants)
                .SingleAsync(membership =>
                    membership.UserId == LegacyData.TeacherId &&
                    membership.Workspace.Kind == WorkspaceKind.Personal);
            Assert.Contains(teacherPersonalMembership.RoleGrants, grant => grant.Role == WorkspaceRole.Instructor);
            var teacherOrganizationWorkspace = (await db.Workspaces
                    .AsNoTracking()
                    .SingleAsync(workspace => workspace.OrganizationId == LegacyData.OrganizationId))
                .Id;
            var legacyClass = await db.Classrooms.AsNoTracking()
                .SingleAsync(classroom => classroom.Id == LegacyData.ClassroomId);
            Assert.Equal(teacherOrganizationWorkspace, legacyClass.WorkspaceId);

            await UserSchema.EnsureAsync(db);
            var instructorGrant = teacherPersonalMembership.RoleGrants
                .Single(grant => grant.Role == WorkspaceRole.Instructor);
            db.WorkspaceRoleGrants.Remove(instructorGrant);
            await db.SaveChangesAsync();
            await new WorkspaceService(db).EnsurePersonalWorkspaceAsync(LegacyData.TeacherId);
            await new WorkspaceService(db).EnsurePersonalWorkspaceAsync(childId);
            Assert.Contains(
                await db.WorkspaceRoleGrants
                    .Where(grant => grant.MembershipId == teacherPersonalMembership.Id)
                    .Select(grant => grant.Role)
                    .ToListAsync(),
                role => role == WorkspaceRole.Instructor);
            Assert.Equal(initialCounts, await ReadCountsAsync(connection));

            var membershipService = new WorkspaceService(db);
            var selectedMembership = await membershipService.GetMembershipAsync(familyMembership.WorkspaceId, childId);
            Assert.NotNull(selectedMembership);
            Assert.Null(await membershipService.GetMembershipAsync(
                familyMembership.WorkspaceId,
                LegacyData.PendingChildId));

            var child = await db.AppUsers.SingleAsync(user => user.Id == childId);
            var jwtConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = new string('k', 64),
                    ["Jwt:Issuer"] = "workspace-test",
                    ["Jwt:Audience"] = "workspace-test",
                    ["Jwt:ExpirationMinutes"] = "60"
                })
                .Build();
            var (token, _) = new Jwtservice(jwtConfiguration).GenerateToken(
                child,
                new Entitlement(false, null, null, null),
                selectedMembership);
            var claims = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.ToList();
            Assert.Contains(claims, claim =>
                claim.Type == WorkspaceClaimTypes.Id && claim.Value == familyMembership.WorkspaceId.ToString());
            Assert.Contains(claims, claim =>
                claim.Type == WorkspaceClaimTypes.Kind && claim.Value == WorkspaceKind.Family.ToString());
            Assert.Contains(claims, claim =>
                claim.Type == WorkspaceClaimTypes.Role && claim.Value == WorkspaceRole.Learner.ToString());
        }
        finally
        {
            await using var dropSchema = new NpgsqlCommand(
                $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE",
                adminConnection);
            await dropSchema.ExecuteNonQueryAsync();
        }
    }

    private static async Task CreateLegacySchemaAndDataAsync(NpgsqlConnection connection)
    {
        const string schemaSql = """
            CREATE TABLE "AppUsers" (
                "Id" uuid PRIMARY KEY,
                "Email" varchar(256) NOT NULL,
                "Username" varchar(64) NOT NULL,
                "PasswordHash" text NOT NULL,
                "CreatedAt" timestamptz NOT NULL,
                "Role" text NOT NULL,
                "Level" text,
                "Specialite" text,
                "Filiere" text,
                "Language" text NOT NULL DEFAULT 'fr',
                "LastLoginAt" timestamptz,
                "OrganizationId" uuid,
                "OrganizationName" text
            );
            CREATE TABLE "Organizations" (
                "Id" uuid PRIMARY KEY,
                "Name" varchar(200) NOT NULL,
                "Type" text NOT NULL,
                "City" text,
                "CreatedAt" timestamptz NOT NULL
            );
            CREATE TABLE "Classrooms" (
                "Id" uuid PRIMARY KEY,
                "Name" varchar(120) NOT NULL,
                "Kind" text NOT NULL,
                "TeacherId" uuid NOT NULL REFERENCES "AppUsers" ("Id") ON DELETE CASCADE,
                "OrganizationId" uuid REFERENCES "Organizations" ("Id") ON DELETE SET NULL,
                "Level" text,
                "SubSystem" text,
                "Subject" text,
                "InviteCode" varchar(16) NOT NULL,
                "IsArchived" boolean NOT NULL DEFAULT false,
                "CreatedAt" timestamptz NOT NULL
            );
            CREATE TABLE "ParentChildLinks" (
                "Id" uuid PRIMARY KEY,
                "ParentId" uuid NOT NULL REFERENCES "AppUsers" ("Id") ON DELETE CASCADE,
                "ChildId" uuid NOT NULL REFERENCES "AppUsers" ("Id") ON DELETE CASCADE,
                "Status" text NOT NULL,
                "CreatedAt" timestamptz NOT NULL,
                "RespondedAt" timestamptz
            );
            CREATE TABLE "Subscriptions" (
                "Id" uuid PRIMARY KEY,
                "Plan" text NOT NULL,
                "Period" text NOT NULL,
                "Status" text NOT NULL,
                "OwnerUserId" uuid REFERENCES "AppUsers" ("Id") ON DELETE CASCADE,
                "OrganizationId" uuid REFERENCES "Organizations" ("Id") ON DELETE CASCADE,
                "Seats" integer NOT NULL DEFAULT 1,
                "StartsAt" timestamptz,
                "EndsAt" timestamptz,
                "CreatedAt" timestamptz NOT NULL
            );
            """;
        await using (var createLegacySchema = new NpgsqlCommand(schemaSql, connection))
            await createLegacySchema.ExecuteNonQueryAsync();

        const string seedSql = """
            INSERT INTO "Organizations" ("Id", "Name", "Type", "City", "CreatedAt")
            VALUES (@organizationId, 'Test institution', 'Institution', NULL, now());

            INSERT INTO "AppUsers" ("Id", "Email", "Username", "PasswordHash", "CreatedAt", "Role")
            VALUES
                (@parentId, 'parent@example.test', 'parent', 'hash', now(), 'Parent'),
                (@activeChildId, 'active-child@example.test', 'active-child', 'hash', now(), 'Student'),
                (@pendingChildId, 'pending-child@example.test', 'pending-child', 'hash', now(), 'Student'),
                (@familySubscriberId, 'family@example.test', 'family', 'hash', now(), 'Parent'),
                (@teacherId, 'teacher@example.test', 'teacher', 'hash', now(), 'Teacher'),
                (@globalAdminId, 'admin@example.test', 'admin', 'hash', now(), 'Admin');

            UPDATE "AppUsers"
            SET "OrganizationId" = @organizationId
            WHERE "Id" IN (@teacherId, @globalAdminId);

            INSERT INTO "Classrooms" (
                "Id", "Name", "Kind", "TeacherId", "OrganizationId", "InviteCode", "CreatedAt")
            VALUES (
                @classroomId, 'Legacy organization class', 'Class', @teacherId, @organizationId, 'TST-CL1', now());

            INSERT INTO "ParentChildLinks" ("Id", "ParentId", "ChildId", "Status", "CreatedAt", "RespondedAt")
            VALUES
                (gen_random_uuid(), @parentId, @activeChildId, 'Active', now(), now()),
                (gen_random_uuid(), @parentId, @pendingChildId, 'Pending', now(), NULL);

            INSERT INTO "Subscriptions" (
                "Id", "Plan", "Period", "Status", "OwnerUserId", "OrganizationId",
                "Seats", "StartsAt", "EndsAt", "CreatedAt")
            VALUES (
                gen_random_uuid(), 'Family', 'Yearly', 'Active', @familySubscriberId, NULL,
                3, now(), now() + interval '1 year', now());
            """;
        await using var seed = new NpgsqlCommand(seedSql, connection);
        seed.Parameters.AddWithValue("organizationId", LegacyData.OrganizationId);
        seed.Parameters.AddWithValue("parentId", LegacyData.ParentId);
        seed.Parameters.AddWithValue("activeChildId", LegacyData.ActiveChildId);
        seed.Parameters.AddWithValue("pendingChildId", LegacyData.PendingChildId);
        seed.Parameters.AddWithValue("familySubscriberId", LegacyData.FamilySubscriberId);
        seed.Parameters.AddWithValue("teacherId", LegacyData.TeacherId);
        seed.Parameters.AddWithValue("globalAdminId", LegacyData.GlobalAdminId);
        seed.Parameters.AddWithValue("classroomId", LegacyData.ClassroomId);
        await seed.ExecuteNonQueryAsync();
    }

    private static async Task<(int Workspaces, int Memberships, int Roles)> ReadCountsAsync(
        NpgsqlConnection connection)
    {
        const string sql = """
            SELECT
                (SELECT count(*)::int FROM "Workspaces"),
                (SELECT count(*)::int FROM "WorkspaceMemberships"),
                (SELECT count(*)::int FROM "WorkspaceRoleGrants")
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
    }

    private static class LegacyData
    {
        public static readonly Guid OrganizationId = Guid.NewGuid();
        public static readonly Guid ParentId = Guid.NewGuid();
        public static readonly Guid ActiveChildId = Guid.NewGuid();
        public static readonly Guid PendingChildId = Guid.NewGuid();
        public static readonly Guid FamilySubscriberId = Guid.NewGuid();
        public static readonly Guid TeacherId = Guid.NewGuid();
        public static readonly Guid GlobalAdminId = Guid.NewGuid();
        public static readonly Guid ClassroomId = Guid.NewGuid();
    }
}
