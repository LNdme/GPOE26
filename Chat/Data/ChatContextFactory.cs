using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Chat.Data;

/// <summary>
/// Construit un <see cref="ChatContext"/> pour les outils EF en ligne de commande.
/// Voir <c>Cours/Data/CoursContextFactory.cs</c> pour le pourquoi.
/// </summary>
public sealed class ChatContextFactory : IDesignTimeDbContextFactory<ChatContext>
{
    public ChatContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__chatdb")
            ?? "Host=localhost;Database=chatdb;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<ChatContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ChatContext(options);
    }
}
