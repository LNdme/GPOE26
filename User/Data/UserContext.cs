using Microsoft.EntityFrameworkCore;
using User.Model;

namespace User.Data
{
    public class UserContext : DbContext
    {
        public UserContext(DbContextOptions<UserContext> op) : base(op) { }

        public DbSet<AppUser> AppUsers { get; set; }
        public DbSet<Organization> Organizations { get; set; }
        public DbSet<Classroom> Classrooms { get; set; }
        public DbSet<ClassroomMember> ClassroomMembers { get; set; }
        public DbSet<ParentChildLink> ParentChildLinks { get; set; }
        public DbSet<Subscription> Subscriptions { get; set; }
        public DbSet<SubscriptionKey> SubscriptionKeys { get; set; }
        public DbSet<KeyRedemption> KeyRedemptions { get; set; }
        public DbSet<OrganizationMember> OrganizationMembers { get; set; }
        public DbSet<SchoolYear> SchoolYears { get; set; }
        public DbSet<ClassroomTeacher> ClassroomTeachers { get; set; }
        public DbSet<OrganizationStudent> OrganizationStudents { get; set; }
        public DbSet<AccessCode> AccessCodes { get; set; }
        public DbSet<SchemaVersion> SchemaVersions { get; set; }
        public DbSet<Workspace> Workspaces { get; set; }
        public DbSet<WorkspaceMembership> WorkspaceMemberships { get; set; }
        public DbSet<WorkspaceRoleGrant> WorkspaceRoleGrants { get; set; }
        public DbSet<McpPermissionGrantRecord> McpPermissionGrants { get; set; }
        public DbSet<McpProcessConfiguration> McpProcessConfigurations { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Les enums sont stockés en texte (plus lisible que 0/1) — cf. UserSchema
            modelBuilder.Entity<AppUser>(entity =>
            {
                entity.HasIndex(u => u.Email).IsUnique();
                entity.HasIndex(u => u.Username).IsUnique();
                entity.HasIndex(u => u.LinkCode).IsUnique();
                entity.Property(u => u.Email).HasMaxLength(256);
                entity.Property(u => u.Username).HasMaxLength(64);
                entity.Property(u => u.Language).HasMaxLength(10).HasDefaultValue("fr");
                entity.Property(u => u.LinkCode).HasMaxLength(16);
                entity.Property(u => u.Role).HasConversion<string>();
                entity.Property(u => u.SubSystem).HasConversion<string>();
                entity.Property(u => u.Stage).HasConversion<string>();
                entity.Property(u => u.TeachingContext).HasConversion<string>();
                entity.Property(u => u.Country).HasMaxLength(2);
                entity.Property(u => u.EducationSystem).HasMaxLength(100);
                entity.Property(u => u.LevelBand).HasConversion<string>();
                entity.Property(u => u.LearningGoal).HasConversion<string>();
                entity.Property(u => u.GradeScale).HasConversion<string>();
                entity.Property(u => u.TimeZone).HasMaxLength(64);
            });

            modelBuilder.Entity<Organization>(entity =>
            {
                entity.Property(o => o.Name).HasMaxLength(200);
                entity.Property(o => o.ShortCode).HasMaxLength(10);
                entity.HasIndex(o => o.ShortCode).IsUnique();
                entity.Property(o => o.Type).HasConversion<string>();
            });

            modelBuilder.Entity<Classroom>(entity =>
            {
                entity.HasIndex(c => c.InviteCode).IsUnique();
                entity.HasIndex(c => c.TeacherId);
                entity.HasIndex(c => c.WorkspaceId);
                entity.Property(c => c.Name).HasMaxLength(120);
                entity.Property(c => c.InviteCode).HasMaxLength(16);
                entity.Property(c => c.Kind).HasConversion<string>();
                entity.Property(c => c.SubSystem).HasConversion<string>();
                // Déclarées pour l'ordre des insertions : une classe créée avec son espace et son année
                // (premier enseignant indépendant) doit être insérée après eux.
                entity.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.SetNull);
                entity.HasOne<SchoolYear>().WithMany().HasForeignKey(c => c.SchoolYearId).OnDelete(DeleteBehavior.SetNull);
                entity.HasOne<AppUser>().WithMany().HasForeignKey(c => c.TeacherId).OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<ClassroomMember>(entity =>
            {
                entity.HasKey(m => new { m.ClassroomId, m.StudentId });
                entity.HasIndex(m => m.StudentId);
                entity.HasOne(m => m.Classroom).WithMany(c => c.Members).HasForeignKey(m => m.ClassroomId);
            });

            modelBuilder.Entity<ParentChildLink>(entity =>
            {
                entity.HasIndex(l => new { l.ParentId, l.ChildId }).IsUnique();
                entity.HasIndex(l => l.ChildId);
                entity.Property(l => l.Status).HasConversion<string>();
            });

            modelBuilder.Entity<Subscription>(entity =>
            {
                entity.HasIndex(s => s.OwnerUserId);
                entity.Property(s => s.Plan).HasConversion<string>();
                entity.Property(s => s.Period).HasConversion<string>();
                entity.Property(s => s.Status).HasConversion<string>();
            });

            modelBuilder.Entity<SubscriptionKey>(entity =>
            {
                entity.HasIndex(k => k.Code).IsUnique();
                entity.Property(k => k.Code).HasMaxLength(32);
                entity.HasOne(k => k.Subscription).WithMany().HasForeignKey(k => k.SubscriptionId);
            });

            modelBuilder.Entity<KeyRedemption>(entity =>
            {
                entity.HasIndex(r => new { r.KeyId, r.UserId }).IsUnique();
                entity.HasIndex(r => r.UserId);
                entity.HasOne(r => r.Key).WithMany().HasForeignKey(r => r.KeyId);
            });

            // ── Espaces (cf. UserSchema). Les clés étrangères sont déclarées pour que EF insère
            // les lignes dans le bon ordre quand un compte et son rattachement sont créés ensemble.
            modelBuilder.Entity<OrganizationMember>(entity =>
            {
                entity.HasKey(m => new { m.OrganizationId, m.UserId, m.Role });
                entity.HasIndex(m => m.UserId);
                entity.Property(m => m.Role).HasConversion<string>();
                entity.Property(m => m.Status).HasConversion<string>();
                entity.HasOne<Organization>().WithMany().HasForeignKey(m => m.OrganizationId);
                entity.HasOne<AppUser>().WithMany().HasForeignKey(m => m.UserId);
            });

            modelBuilder.Entity<SchoolYear>(entity =>
            {
                entity.HasIndex(y => new { y.OrganizationId, y.Label }).IsUnique();
                entity.Property(y => y.Label).HasMaxLength(20);
                entity.HasOne<Organization>().WithMany().HasForeignKey(y => y.OrganizationId);
            });

            modelBuilder.Entity<ClassroomTeacher>(entity =>
            {
                entity.HasKey(t => new { t.ClassroomId, t.TeacherId, t.Subject });
                entity.HasIndex(t => t.TeacherId);
                entity.Property(t => t.Subject).HasMaxLength(80);
                entity.HasOne<Classroom>().WithMany().HasForeignKey(t => t.ClassroomId);
                entity.HasOne<AppUser>().WithMany().HasForeignKey(t => t.TeacherId);
            });

            modelBuilder.Entity<OrganizationStudent>(entity =>
            {
                entity.HasIndex(s => new { s.OrganizationId, s.Matricule }).IsUnique();
                entity.HasIndex(s => s.UserId);
                entity.Property(s => s.Matricule).HasMaxLength(40);
                entity.Property(s => s.LastName).HasMaxLength(100);
                entity.Property(s => s.FirstName).HasMaxLength(100);
                entity.Property(s => s.Email).HasMaxLength(256);
                entity.Property(s => s.Status).HasConversion<string>();
                entity.HasOne<Organization>().WithMany().HasForeignKey(s => s.OrganizationId);
                entity.HasOne<Classroom>().WithMany().HasForeignKey(s => s.ClassroomId).OnDelete(DeleteBehavior.SetNull);
                entity.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<AccessCode>(entity =>
            {
                entity.HasIndex(c => c.Code).IsUnique();
                entity.Property(c => c.Code).HasMaxLength(32);
                entity.Property(c => c.Kind).HasConversion<string>();
                entity.Property(c => c.Status).HasConversion<string>();
                entity.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId);
                entity.HasOne<OrganizationStudent>().WithMany().HasForeignKey(c => c.OrganizationStudentId);
                entity.HasOne<Classroom>().WithMany().HasForeignKey(c => c.ClassroomId).OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<SchemaVersion>(entity => entity.HasKey(v => v.Version));
            modelBuilder.Entity<Workspace>(entity =>
            {
                entity.Property(workspace => workspace.Name).HasMaxLength(200);
                entity.Property(workspace => workspace.Kind).HasConversion<string>();
                entity.HasIndex(workspace => new { workspace.Kind, workspace.OwnerUserId }).IsUnique();
                entity.HasIndex(workspace => workspace.OrganizationId).IsUnique();
                entity.HasOne<AppUser>()
                    .WithMany()
                    .HasForeignKey(workspace => workspace.OwnerUserId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<Organization>()
                    .WithMany()
                    .HasForeignKey(workspace => workspace.OrganizationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<WorkspaceMembership>(entity =>
            {
                entity.HasIndex(membership => new { membership.WorkspaceId, membership.UserId }).IsUnique();
                entity.HasIndex(membership => new { membership.UserId, membership.Status });
                entity.Property(membership => membership.Status).HasConversion<string>();
                entity.HasOne(membership => membership.Workspace)
                    .WithMany(workspace => workspace.Memberships)
                    .HasForeignKey(membership => membership.WorkspaceId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(membership => membership.User)
                    .WithMany()
                    .HasForeignKey(membership => membership.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<WorkspaceRoleGrant>(entity =>
            {
                entity.HasKey(grant => new { grant.MembershipId, grant.Role });
                entity.Property(grant => grant.Role).HasConversion<string>();
                entity.HasOne(grant => grant.Membership)
                    .WithMany(membership => membership.RoleGrants)
                    .HasForeignKey(grant => grant.MembershipId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<McpPermissionGrantRecord>(entity =>
            {
                entity.HasKey(grant => new { grant.UserId, grant.WorkspaceId, grant.ExtensionId });
                entity.Property(grant => grant.ExtensionId).HasMaxLength(200);
                entity.Property(grant => grant.CapabilityIds).HasColumnType("text[]");
                entity.Property(grant => grant.PermissionIds).HasColumnType("text[]");
                entity.HasIndex(grant => new { grant.WorkspaceId, grant.UserId, grant.RevokedAt });
                entity.HasOne<Workspace>()
                    .WithMany()
                    .HasForeignKey(grant => grant.WorkspaceId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<AppUser>()
                    .WithMany()
                    .HasForeignKey(grant => grant.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<AppUser>()
                    .WithMany()
                    .HasForeignKey(grant => grant.ApprovedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<McpProcessConfiguration>(entity =>
            {
                entity.HasKey(configuration => new
                {
                    configuration.UserId,
                    configuration.WorkspaceId,
                    configuration.ExtensionId
                });
                entity.Property(configuration => configuration.ExtensionId).HasMaxLength(200);
                entity.Property(configuration => configuration.ProtectedOptions).IsRequired();
                entity.HasOne<Workspace>()
                    .WithMany()
                    .HasForeignKey(configuration => configuration.WorkspaceId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<AppUser>()
                    .WithMany()
                    .HasForeignKey(configuration => configuration.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
