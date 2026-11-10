using Microsoft.EntityFrameworkCore;
using User.Model;

namespace User.Data;

/// <summary>
/// Premiers administrateurs du site : les comptes dont l'e-mail figure dans Admin:Emails
/// (séparés par des virgules ou des points-virgules) deviennent Admin au démarrage.
/// Seuls des comptes DÉJÀ inscrits sont promus : créez le compte d'abord, puis ajoutez son e-mail,
/// sinon n'importe qui pourrait s'inscrire avec cette adresse et hériter des droits.
/// Ensuite, un administrateur nomme les autres depuis /admin/utilisateurs.
/// </summary>
public static class AdminBootstrap
{
    public static async Task PromoteConfiguredAdminsAsync(UserContext db, IConfiguration config, ILogger logger)
    {
        var emails = (config["Admin:Emails"] ?? "")
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLowerInvariant())
            .ToList();
        if (emails.Count == 0) return;

        var users = await db.AppUsers.Where(u => emails.Contains(u.Email.ToLower())).ToListAsync();
        foreach (var user in users.Where(u => u.Role != UserRole.Admin))
        {
            user.Role = UserRole.Admin;
            logger.LogInformation("Compte {Email} promu administrateur (Admin:Emails).", user.Email);
        }
        await db.SaveChangesAsync();

        foreach (var missing in emails.Except(users.Select(u => u.Email.ToLowerInvariant())))
            logger.LogWarning("Admin:Emails : aucun compte inscrit avec l'adresse {Email}. Inscrivez-le puis redémarrez le service.", missing);
    }
}
