using Microsoft.EntityFrameworkCore;
using User.Model;

namespace User.Data
{
    public class UserContext : DbContext
    {
        

        public UserContext(DbContextOptions<UserContext>op): base(op) { }

        public DbSet<AppUser> AppUsers { get; set;  }
        
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
        }
    }
}
