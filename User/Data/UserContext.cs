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
                entity.Property(u => u.TeachingContext).HasConversion<string>();
            });

            modelBuilder.Entity<Organization>(entity =>
            {
                entity.Property(o => o.Name).HasMaxLength(200);
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
