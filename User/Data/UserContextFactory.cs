using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace User.Data;

/// <summary>
/// Construit un <see cref="UserContext"/> pour les outils EF en ligne de commande.
/// Voir <c>Cours/Data/CoursContextFactory.cs</c> pour le pourquoi.
/// </summary>
public sealed class UserContextFactory : IDesignTimeDbContextFactory<UserContext>
{
    public UserContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__userdb")
            ?? "Host=localhost;Database=userdb;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<UserContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new UserContext(options);
    }
}
