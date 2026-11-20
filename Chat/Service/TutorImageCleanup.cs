using Chat.Data;
using Microsoft.EntityFrameworkCore;

namespace Chat.Service;

/// <summary>
/// Photos envoyées au tuteur mais jamais jointes à une question (envoi abandonné, erreur pendant la réponse) :
/// elles restaient en base pour toujours. Supprimées après un jour, au démarrage puis toutes les six heures.
/// </summary>
public class TutorImageCleanup(IServiceScopeFactory scopes, ILogger<TutorImageCleanup> logger) : BackgroundService
{
    public static readonly TimeSpan Grace = TimeSpan.FromDays(1);
    private static readonly TimeSpan Every = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var deleted = await DeleteOrphansAsync(scope.ServiceProvider.GetRequiredService<TutorContext>(), DateTime.UtcNow, stoppingToken);
                if (deleted > 0) logger.LogInformation("{Count} photo(s) du tuteur jamais utilisée(s) supprimée(s)", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Nettoyage des photos du tuteur impossible pour le moment");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public static Task<int> DeleteOrphansAsync(TutorContext db, DateTime now, CancellationToken ct)
    {
        var before = now - Grace;
        return db.Images
            .Where(i => i.CreatedAt < before && !db.Messages.Any(m => m.ImageId == i.Id))
            .ExecuteDeleteAsync(ct);
    }
}
