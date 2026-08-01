using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Pgvector.Npgsql;

namespace Cours.Data;

/// <summary>
/// Construit un <see cref="CoursContext"/> pour les outils EF en ligne de commande.
///
/// Sans cette fabrique, `dotnet ef migrations add` démarre `Program.cs`, qui exige la
/// chaîne de connexion injectée par l'AppHost Aspire — donc les migrations ne peuvent
/// se générer qu'avec toute l'orchestration lancée. Avec elle, elles se génèrent hors
/// ligne, ce qui est le seul moment où on en a besoin.
///
/// La chaîne n'est jamais utilisée pour se connecter : générer une migration ne fait que
/// comparer le modèle au dernier instantané. Elle doit simplement être analysable.
/// </summary>
public sealed class CoursContextFactory : IDesignTimeDbContextFactory<CoursContext>
{
    public CoursContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__coursdb")
            ?? "Host=localhost;Database=coursdb;Username=postgres;Password=postgres";

        // Le plugin pgvector doit être posé ici aussi : sans lui, la propriété Vector de
        // CourseChunk n'est pas reconnue et la migration sort sans la colonne d'embedding.
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();

        var options = new DbContextOptionsBuilder<CoursContext>()
            .UseNpgsql(dataSourceBuilder.Build(), npgsql => npgsql.UseVector())
            .Options;

        return new CoursContext(options);
    }
}
