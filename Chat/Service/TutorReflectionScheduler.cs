using Chat.Data;
using Chat.Model;
using Microsoft.EntityFrameworkCore;

namespace Chat.Service;

/// <summary>Quand relire une discussion pour la mémoire (fonction pure, testée).</summary>
public static class ReflectionPolicy
{
    /// <summary>
    /// Assez de questions nouvelles pour un lot, ou discussion au repos depuis un moment (l'élève a fini).
    /// </summary>
    public static bool IsDue(int pendingQuestions, DateTime lastMessageAt, DateTime now, int batchSize, TimeSpan quiet) =>
        pendingQuestions > 0 && (pendingQuestions >= batchSize || now - lastMessageAt >= quiet);

    /// <summary>Paires question → réponse, dans l'ordre (une question sans réponse est ignorée).</summary>
    public static List<ReflectionExchange> Pair(IEnumerable<(string Role, string Content, bool HasPhoto)> messages)
    {
        var exchanges = new List<ReflectionExchange>();
        string? question = null;
        foreach (var (role, content, hasPhoto) in messages)
        {
            if (role == "user") question = hasPhoto ? $"{content}\n[photo de copie jointe]" : content;
            else if (question is not null) { exchanges.Add(new ReflectionExchange(question, content)); question = null; }
        }
        return exchanges;
    }
}

/// <summary>
/// Mémoire du tuteur consolidée par lots, à partir de la base :
///   - un appel au modèle pour plusieurs questions (Agents:ReflectionBatch, 4 par défaut), ou quand la discussion
///     est au repos (Agents:ReflectionQuietMinutes, 10 par défaut) — avant, chaque question en coûtait un second ;
///   - rien ne se perd au redémarrage (l'ancienne file en mémoire disparaissait) ;
///   - une discussion effacée n'est plus relue (avant, la file pouvait écrire après l'effacement).
/// </summary>
public class TutorReflectionScheduler(IServiceScopeFactory scopes, IConfiguration config, ILogger<TutorReflectionScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(1);
    private const int MaxExchangesPerBatch = 8;
    private const int ConversationsPerTick = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Consolidation de la mémoire du tuteur reportée");
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var batch = config.GetValue("Agents:ReflectionBatch", 4);
        var quiet = TimeSpan.FromMinutes(config.GetValue("Agents:ReflectionQuietMinutes", 10));
        var now = DateTime.UtcNow;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TutorContext>();

        var candidates = await db.Conversations.AsNoTracking()
            .Select(c => new
            {
                c.Id, c.UserId, c.CourseId, c.CourseTitle, c.ReflectedAt,
                Pending = c.Messages.Count(m => m.Role == "user" && (c.ReflectedAt == null || m.CreatedAt > c.ReflectedAt)),
                Last = c.Messages.Max(m => (DateTime?)m.CreatedAt)
            })
            .Where(c => c.Pending > 0)
            .OrderBy(c => c.Last)
            .Take(100)
            .ToListAsync(ct);

        foreach (var c in candidates.Where(c => ReflectionPolicy.IsDue(c.Pending, c.Last!.Value, now, batch, quiet)).Take(ConversationsPerTick))
        {
            var messages = await db.Messages.AsNoTracking()
                .Where(m => m.ConversationId == c.Id && (c.ReflectedAt == null || m.CreatedAt > c.ReflectedAt))
                .OrderBy(m => m.CreatedAt)
                .Select(m => new { m.Role, m.Content, HasPhoto = m.ImageId != null, m.CreatedAt })
                .ToListAsync(ct);

            var exchanges = ReflectionPolicy.Pair(messages.Select(m => (m.Role, m.Content, m.HasPhoto))).TakeLast(MaxExchangesPerBatch).ToList();
            var job = new ReflectionJob(c.UserId, AgentKinds.Student, c.CourseId, c.CourseTitle, exchanges, null);
            try
            {
                using var jobScope = scopes.CreateScope();
                await AgentReflectionWorker.ProcessAsync(job, ct, jobScope.ServiceProvider);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // La mémoire est un plus : un lot qui échoue (modèle saturé) est abandonné plutôt que retenté sans fin
                logger.LogWarning(ex, "Mémoire non mise à jour pour la discussion {ConversationId}", c.Id);
            }

            var reflectedUpTo = messages[^1].CreatedAt;
            await db.Conversations.Where(x => x.Id == c.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReflectedAt, reflectedUpTo), ct);
        }
    }
}
