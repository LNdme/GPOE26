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
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<SchoolClass> SchoolClasses => Set<SchoolClass>();
        public DbSet<ClassEnrollment> ClassEnrollments => Set<ClassEnrollment>();

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

            modelBuilder.Entity<RefreshToken>(entity =>
            {
                // L'empreinte est ce qu'on reçoit et ce qu'on cherche : elle doit être
                // indexée, et unique — deux jetons distincts ne peuvent pas partager
                // la même empreinte sans que quelque chose aille très mal.
                entity.Property(t => t.TokenHash).HasMaxLength(64);
                entity.HasIndex(t => t.TokenHash).IsUnique();

                // Pour révoquer toute la chaîne d'un utilisateur en une requête.
                entity.HasIndex(t => new { t.UserId, t.ExpiresAt });

                entity.HasOne(t => t.User)
                      .WithMany()
                      .HasForeignKey(t => t.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<SchoolClass>(entity =>
            {
                entity.Property(c => c.Name).HasMaxLength(100);
                entity.Property(c => c.Subject).HasMaxLength(100);
                entity.Property(c => c.Level).HasMaxLength(50);
                entity.Property(c => c.SchoolYear).HasMaxLength(20);
                entity.Property(c => c.Code).HasMaxLength(12);

                // Le code est ce qu'un élève saisit : il doit se retrouver en une requête,
                // et deux classes ne peuvent pas le partager.
                entity.HasIndex(c => c.Code).IsUnique();
                entity.HasIndex(c => new { c.TeacherId, c.SchoolYear });

                entity.HasOne(c => c.Teacher)
                      .WithMany()
                      .HasForeignKey(c => c.TeacherId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ClassEnrollment>(entity =>
            {
                // Un élève ne rejoint pas deux fois la même classe.
                entity.HasIndex(e => new { e.ClassId, e.StudentId }).IsUnique();

                // L'accès dominant du suivi : « tous les élèves de mes classes », en une
                // requête. Sans cet index, un enseignant de cent cinquante élèves ferait
                // parcourir la table entière à chaque consultation.
                entity.HasIndex(e => e.StudentId);

                entity.HasOne(e => e.Class)
                      .WithMany(c => c.Enrollments)
                      .HasForeignKey(e => e.ClassId)
                      .OnDelete(DeleteBehavior.Cascade);

                // Restrict côté élève, comme pour ParentChild : supprimer un compte élève
                // ne doit pas effacer silencieusement l'inscription par une seconde cascade.
                entity.HasOne(e => e.Student)
                      .WithMany()
                      .HasForeignKey(e => e.StudentId)
                      .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
