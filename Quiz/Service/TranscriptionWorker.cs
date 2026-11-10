using System.Threading.Channels;
using GPOE26.ServiceDefaults.Llm;
using Microsoft.EntityFrameworkCore;
using Quiz.Data;

namespace Quiz.Service;

/// <summary>Photos de copie à faire lire par l'IA, dans l'ordre d'envoi.</summary>
public class TranscriptionQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();
    public void Enqueue(Guid photoId) => _channel.Writer.TryWrite(photoId);
    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

/// <summary>
/// Lit les photos de copie en arrière-plan. Pendant une épreuve chronométrée, l'envoi d'une photo attendait la lecture
/// par l'IA (jusqu'à plusieurs minutes) : la photo est désormais enregistrée tout de suite, et l'élève voit la lecture
/// arriver quand elle est prête. Au démarrage, les photos restées en attente sont reprises.
/// </summary>
public class TranscriptionWorker(TranscriptionQueue queue, IServiceScopeFactory scopes, ILogger<TranscriptionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EvaluationContext>();
            foreach (var id in await db.AnswerPhotos.Where(p => p.TranscriptionPending).OrderBy(p => p.CreatedAt).Select(p => p.Id).ToListAsync(stoppingToken))
                queue.Enqueue(id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Photos en attente de lecture non reprises");
        }

        await foreach (var photoId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await TranscribeAsync(photoId, scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Lecture de la photo {PhotoId} impossible", photoId);
            }
        }
    }

    private static async Task TranscribeAsync(Guid photoId, IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<EvaluationContext>();
        var photo = await db.AnswerPhotos.FirstOrDefaultAsync(p => p.Id == photoId && p.TranscriptionPending, ct);
        if (photo is null) return;   // retirée par l'élève entre-temps, ou déjà lue

        var question = await db.AssessmentQuestions.Where(q => q.Id == photo.QuestionId).Select(q => q.Text).FirstOrDefaultAsync(ct) ?? "";
        var ai = services.GetRequiredService<AiGradingService>();
        var transcription = await ai.TranscribeAsync(question, new LlmImage(photo.ContentType, photo.Data), ct);

        // Écriture ciblée : la photo a pu être supprimée pendant la lecture
        await db.AnswerPhotos.Where(p => p.Id == photoId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Transcription, transcription)
                .SetProperty(p => p.TranscriptionPending, false), ct);
    }
}
