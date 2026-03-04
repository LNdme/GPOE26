using Cours.Model;
using Microsoft.EntityFrameworkCore;

namespace Cours.Data
{
    public class CoursContext(DbContextOptions<CoursContext> options) : DbContext(options)
    {
        public DbSet<Course> Courses => Set<Course>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Course>(entity =>
            {
                entity.Property(c => c.Title).HasMaxLength(200);
                entity.Property(c => c.Subject).HasMaxLength(100);

                // Stocker l'enum comme string en DB
                entity.Property(c => c.ContentType).HasConversion<string>();

                // Index pour retrouver rapidement les cours d'un utilisateur
                entity.HasIndex(c => c.OwnerId);


                // Index composite pour filtrer par utilisateur ET matière
                entity.HasIndex(c => new { c.OwnerId, c.Subject });
            });
        }
    }
}
