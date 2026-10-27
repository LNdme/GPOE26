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
        }
    }
}
