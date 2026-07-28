using System.Threading.Channels;

namespace Cours.Service;

/// <summary>
/// File d'attente des cours à mettre en forme.
///
/// La transcription vision puis la structuration prennent des dizaines de secondes :
/// les faire dans la requête d'upload la ferait expirer et laisserait l'élève devant
/// un écran bloqué. L'upload répond immédiatement avec FormatStatus = Pending, et le
/// canvas de lecture affiche l'avancement.
/// </summary>
public sealed class CourseFormattingQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
    });

    public ValueTask EnqueueAsync(Guid courseId, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(courseId, ct);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}

/// <summary>
/// Consomme la file et exécute le pipeline de mise en forme, un cours à la fois.
///
/// Le traitement est séquentiel : plusieurs structurations simultanées se disputeraient
/// le quota OpenRouter et n'iraient pas plus vite au global.
/// </summary>
public sealed class CourseFormattingWorker(
    CourseFormattingQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<CourseFormattingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var courseId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                // Le service de mise en forme est scoped (il dépend du DbContext) :
                // un scope neuf par cours évite de partager un DbContext entre traitements.
                using var scope = scopeFactory.CreateScope();
                var formatting = scope.ServiceProvider.GetRequiredService<CourseFormattingService>();

                await formatting.FormatAndIndexAsync(courseId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // FormatAndIndexAsync gère déjà ses propres erreurs ; on ne passe ici que
                // si la création du scope échoue. Ne pas laisser remonter : le worker
                // doit survivre et continuer à dépiler.
                logger.LogError(ex, "Le worker de mise en forme a échoué sur le cours {CourseId}", courseId);
            }
        }
    }
}
