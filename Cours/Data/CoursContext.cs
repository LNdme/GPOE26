using Cours.Model;
using Microsoft.EntityFrameworkCore;

namespace Cours.Data
{
    public class CoursContext(DbContextOptions<CoursContext> options) : DbContext(options)
    {
        public DbSet<Course> Courses => Set<Course>();
        public DbSet<CourseSection> CourseSections => Set<CourseSection>();
        public DbSet<CourseLabSuggestion> LabSuggestions => Set<CourseLabSuggestion>();
        public DbSet<CourseFeedback> Feedback => Set<CourseFeedback>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Course>(entity =>
            {
                entity.Property(c => c.Title).HasMaxLength(200);
                entity.Property(c => c.Subject).HasMaxLength(100);

                // Stocker l'enum comme string en DB
                entity.Property(c => c.ContentType).HasConversion<string>();
                entity.Property(c => c.Status).HasConversion<string>();

                // Index pour retrouver rapidement les cours d'un utilisateur
                entity.HasIndex(c => c.OwnerId);

                // Index composite pour filtrer par utilisateur ET matière
                entity.HasIndex(c => new { c.OwnerId, c.Subject });

                // Relation 1-N : Course → Sections (cascade delete)
                entity.HasMany(c => c.Sections)
                      .WithOne(s => s.Course)
                      .HasForeignKey(s => s.CourseId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<CourseLabSuggestion>(entity =>
            {
                entity.ToTable("CourseLabSuggestions");
                entity.Property(s => s.Id).ValueGeneratedNever();
                entity.HasIndex(s => new { s.CourseId, s.Status });
            });

            modelBuilder.Entity<CourseFeedback>(entity =>
            {
                entity.ToTable("CourseFeedback");
                entity.HasKey(f => new { f.CourseId, f.UserId });
            });

            modelBuilder.Entity<CourseSection>(entity =>
            {
                entity.Property(s => s.Type).HasConversion<string>();
                entity.HasIndex(s => new { s.CourseId, s.Order });
            });
        }
    }
}
