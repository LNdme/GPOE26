using Microsoft.EntityFrameworkCore;
using User.Model;

namespace User.Data
{
    public class UserContext : DbContext
    {


        public UserContext(DbContextOptions<UserContext>op): base(op) { }

        public DbSet<AppUser> AppUsers { get; set;  }

        public DbSet<StudentLinkCode> StudentLinkCodes => Set<StudentLinkCode>();
        public DbSet<ParentChild> ParentChildren => Set<ParentChild>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AppUser>(entity =>
            {
                entity.HasIndex(u => u.Email).IsUnique();
                entity.HasIndex(u => u.Username).IsUnique();
                entity.Property(u => u.Email).HasMaxLength(256);
                entity.Property(u => u.Username).HasMaxLength(64);
                entity.Property(u => u.Language).HasMaxLength(10).HasDefaultValue("fr");
                // Stocker l'enum comme string en DB (plus lisible que 0/1)
                entity.Property(u => u.Role).HasConversion<string>();
            });

            modelBuilder.Entity<StudentLinkCode>(entity =>
            {
                entity.Property(c => c.Code).HasMaxLength(16);

                // Un code doit pouvoir être retrouvé par sa seule valeur, et deux codes
                // vivants identiques rendraient le rattachement ambigu.
                entity.HasIndex(c => c.Code).IsUnique();

                entity.HasOne(c => c.Student)
                      .WithMany()
                      .HasForeignKey(c => c.StudentId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ParentChild>(entity =>
            {
                // Un même parent ne se rattache qu'une fois au même enfant.
                entity.HasIndex(l => new { l.ParentId, l.StudentId }).IsUnique();

                entity.HasOne(l => l.Parent)
                      .WithMany()
                      .HasForeignKey(l => l.ParentId)
                      .OnDelete(DeleteBehavior.Cascade);

                // Restrict côté élève : supprimer un compte élève ne doit pas effacer
                // silencieusement le lien par une seconde cascade sur la même table.
                entity.HasOne(l => l.Student)
                      .WithMany()
                      .HasForeignKey(l => l.StudentId)
                      .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
