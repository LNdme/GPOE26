using GPOE26.Harness.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GPOE26.Harness.Data;

public class HarnessContext(DbContextOptions<HarnessContext> options) : DbContext(options)
{
    public DbSet<HarnessSession> Sessions => Set<HarnessSession>();
    public DbSet<HarnessTurn> Turns => Set<HarnessTurn>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HarnessSession>(entity =>
        {
            entity.Property(s => s.Kind).HasConversion<string>().HasMaxLength(20);

            // L'accès dominant : « la session ouverte de cet élève sur ce cours », à
            // chaque tour. Puis « ses sessions récentes », pour la reprise.
            entity.HasIndex(s => new { s.StudentId, s.CourseId, s.LastActivityAt });

            entity.HasMany(s => s.Turns)
                .WithOne(t => t.Session)
                .HasForeignKey(t => t.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HarnessTurn>(entity =>
        {
            entity.Property(t => t.Role).HasMaxLength(20);
            entity.HasIndex(t => new { t.SessionId, t.Index });
        });
    }
}

/// <summary>
/// Fabrique de conception, pour que `dotnet ef` n'ait pas besoin de l'AppHest lancé.
/// Voir <c>Cours/Data/CoursContextFactory.cs</c> pour le raisonnement.
/// </summary>
public sealed class HarnessContextFactory : IDesignTimeDbContextFactory<HarnessContext>
{
    public HarnessContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__harnessdb")
            ?? "Host=localhost;Database=harnessdb;Username=postgres;Password=postgres";

        return new HarnessContext(
            new DbContextOptionsBuilder<HarnessContext>().UseNpgsql(connectionString).Options);
    }
}
