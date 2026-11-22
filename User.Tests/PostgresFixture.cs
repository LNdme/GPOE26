using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using User.Data;
using User.Model;
using User.Service;

namespace User.Tests;

/// <summary>
/// Une base PostgreSQL jetable pour toute la série : le schéma (UserSchema) et les reprises (UserDataMigrations)
/// y sont joués comme au démarrage du service. Chaque test crée ses propres comptes, espaces et classes.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string Image = "pgvector/pgvector:pg18";
    private const string Password = "tests-user-jetable";
    private string? _containerId;

    public string ConnectionString { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        // Conteneur jetable sur un port libre choisi par Docker ; supprimé à la fin (--rm)
        _containerId = (await DockerAsync($"run -d --rm -e POSTGRES_PASSWORD={Password} -e POSTGRES_DB=userdb -p 127.0.0.1::5432 {Image}")).Trim();
        // « 127.0.0.1:49153 » : le port est après le dernier deux-points de la première ligne
        var port = (await DockerAsync($"port {_containerId} 5432")).Trim().Split(Environment.NewLine.ToCharArray())[0].Split(':').Last().Trim();
        ConnectionString = $"Host=127.0.0.1;Port={port};Database=userdb;Username=postgres;Password={Password}";

        // Prêt quand une vraie connexion passe (pg_isready répond avant la fin de l'initialisation de l'image)
        for (var i = 0; ; i++)
        {
            try
            {
                await using var probe = NewContext();
                await probe.Database.ExecuteSqlRawAsync("SELECT 1");
                break;
            }
            catch (Exception) when (i < 60)
            {
                await Task.Delay(1000);
            }
        }

        await using var db = NewContext();
        await UserSchema.EnsureAsync(db);
        await UserDataMigrations.RunAsync(db, NullLogger.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        if (_containerId is { Length: > 0 } id) await DockerAsync($"stop {id}");
    }

    private static async Task<string> DockerAsync(string arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("docker", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = System.Diagnostics.Process.Start(start)
                            ?? throw new InvalidOperationException("Docker introuvable : ces tests ont besoin de Docker.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException($"docker {arguments} : {error}");
        return output;
    }

    public UserContext NewContext() =>
        new(new DbContextOptionsBuilder<UserContext>().UseNpgsql(ConnectionString).Options);

    public static IConfiguration Config() => new ConfigurationBuilder().Build();

    /// <summary>Un espace d'enseignant indépendant (30 places sans licence), son enseignant et une classe.</summary>
    public static async Task<(Organization Org, AppUser Teacher, Classroom Classroom)> SpaceAsync(UserContext db, string name)
    {
        var teacher = NewUser($"prof-{name}", UserRole.Teacher);
        var org = new Organization { Name = $"Espace {name}", Type = OrganizationType.Independent };
        db.AppUsers.Add(teacher);
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = org.Id, UserId = teacher.Id, Role = OrganizationRole.Owner });
        db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = org.Id, UserId = teacher.Id, Role = OrganizationRole.Teacher });
        var classroom = new Classroom { Name = $"Classe {name}", TeacherId = teacher.Id, OrganizationId = org.Id };
        db.Classrooms.Add(classroom);
        await db.SaveChangesAsync();
        db.ClassroomTeachers.Add(new ClassroomTeacher { ClassroomId = classroom.Id, TeacherId = teacher.Id, IsMain = true });
        await db.SaveChangesAsync();
        return (org, teacher, classroom);
    }

    public static AppUser NewUser(string name, UserRole role) => new()
    {
        Username = $"{name}-{Guid.NewGuid():N}" is var full && full.Length > 60 ? full[..60] : full,
        Email = $"{Guid.NewGuid():N}@exemple.test",
        PasswordHash = "x",
        Role = role
    };

    /// <summary>Applique un code dans une transaction, comme la page « J'ai un code ».</summary>
    public static async Task<CodeError> RedeemAsync(UserContext db, AccessCodeService codes, string code, AppUser user)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var result = await codes.ApplyAsync(code, user);
        if (result == CodeError.None) await tx.CommitAsync();
        else await tx.RollbackAsync();
        return result;
    }
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
