using Cours.Model;
using Microsoft.EntityFrameworkCore;

namespace Cours.Data
{
    public class CoursContext(DbContextOptions<CoursContext> options) : DbContext(options)
    {
        /// <summary>
        /// Dimension des vecteurs stockés, figée dans le schéma PostgreSQL.
        ///
        /// ⚠️ Doit correspondre à OpenRouter:EmbeddingDimensions. La changer impose une
        /// nouvelle migration EF et une réindexation complète de tous les cours : pgvector
        /// n'autorise pas la comparaison de vecteurs de dimensions différentes.
        /// 1536 = openai/text-embedding-3-small.
        /// </summary>
        public const int EmbeddingDimensions = 1536;

        public DbSet<Course> Courses => Set<Course>();
        public DbSet<CourseSection> CourseSections => Set<CourseSection>();
        public DbSet<CourseAsset> CourseAssets => Set<CourseAsset>();
        public DbSet<CourseChunk> CourseChunks => Set<CourseChunk>();
        public DbSet<CourseStep> CourseSteps => Set<CourseStep>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Active l'extension pgvector : la migration émettra CREATE EXTENSION IF NOT EXISTS vector.
            modelBuilder.HasPostgresExtension("vector");

            modelBuilder.Entity<Course>(entity =>
            {
                entity.Property(c => c.Title).HasMaxLength(200);
                entity.Property(c => c.Subject).HasMaxLength(100);

                // Stocker les enums comme string en DB
                entity.Property(c => c.ContentType).HasConversion<string>();
                entity.Property(c => c.FormatStatus).HasConversion<string>();

                // Index pour retrouver rapidement les cours d'un utilisateur
                entity.HasIndex(c => c.OwnerId);

                // Index composite pour filtrer par utilisateur ET matière
                entity.HasIndex(c => new { c.OwnerId, c.Subject });

                // Relation 1-N : Course → Sections (cascade delete)
                entity.HasMany(c => c.Sections)
                      .WithOne(s => s.Course)
                      .HasForeignKey(s => s.CourseId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(c => c.Assets)
                      .WithOne(a => a.Course)
                      .HasForeignKey(a => a.CourseId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(c => c.Chunks)
                      .WithOne(k => k.Course)
                      .HasForeignKey(k => k.CourseId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(c => c.Steps)
                      .WithOne(s => s.Course)
                      .HasForeignKey(s => s.CourseId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.Property(c => c.JourneyMode).HasConversion<string>();
            });

            modelBuilder.Entity<CourseSection>(entity =>
            {
                entity.Property(s => s.Type).HasConversion<string>();
                entity.HasIndex(s => new { s.CourseId, s.Order });
            });

            modelBuilder.Entity<CourseAsset>(entity =>
            {
                entity.Property(a => a.Kind).HasConversion<string>();
                entity.Property(a => a.Path).HasMaxLength(400);
                entity.Property(a => a.OriginalFileName).HasMaxLength(260);
                entity.Property(a => a.ContentType).HasMaxLength(100);
                entity.HasIndex(a => new { a.CourseId, a.Order });
            });

            modelBuilder.Entity<CourseStep>(entity =>
            {
                entity.Property(s => s.Kind).HasConversion<string>().HasMaxLength(20);
                entity.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
                entity.Property(s => s.Title).HasMaxLength(300);
                entity.Property(s => s.HeadingPath).HasMaxLength(500);
                entity.HasIndex(s => new { s.CourseId, s.Order });
            });

            modelBuilder.Entity<CourseChunk>(entity =>
            {
                entity.Property(k => k.HeadingPath).HasMaxLength(500);
                entity.Property(k => k.Embedding).HasColumnType($"vector({EmbeddingDimensions})");

                entity.HasIndex(k => new { k.CourseId, k.Order });

                // Index HNSW pour la recherche par similarité cosinus.
                // vector_cosine_ops doit correspondre à l'opérateur utilisé à la requête
                // (CosineDistance) : un index construit pour une autre distance est ignoré.
                entity.HasIndex(k => k.Embedding)
                      .HasMethod("hnsw")
                      .HasOperators("vector_cosine_ops");
            });
        }
    }
}
