using Cours.Data;
using Cours.Model;
using GPOE26.Ai;
using GPOE26.Ai.Agents;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace Cours.Service;

/// <summary>
/// Le pipeline qui transforme un dépôt brut en cours lisible et interrogeable :
///
///   photos ──► TranscriptionAgent ─┐
///                                  ├─► StructurateurAgent ─► FormattedMarkdown
///   PDF ─────► PdfPig ─────────────┘                              │
///                                                                 ▼
///                                              MarkdownChunker ─► embeddings ─► CourseChunk
///
/// Déclenché à l'upload et par le bouton « Régénérer la mise en forme ».
/// </summary>
public sealed class CourseFormattingService(
    CoursContext db,
    PdfExtractorService extractor,
    TranscriptionAgent transcription,
    StructurateurAgent structurateur,
    OpenRouterClient openRouter,
    IWebHostEnvironment env,
    ILogger<CourseFormattingService> logger)
{
    /// <summary>Nombre de fragments vectorisés par appel à l'API d'embeddings.</summary>
    private const int EmbeddingBatchSize = 64;

    public async Task FormatAndIndexAsync(Guid courseId, CancellationToken ct = default)
    {
        var course = await db.Courses
            .Include(c => c.Assets)
            .Include(c => c.Sections)
            .FirstOrDefaultAsync(c => c.Id == courseId, ct);

        if (course is null)
        {
            logger.LogWarning("Mise en forme demandée pour un cours introuvable : {CourseId}", courseId);
            return;
        }

        course.FormatStatus = FormatStatus.Pending;
        course.FormatError = null;
        await db.SaveChangesAsync(ct);

        try
        {
            var raw = await ExtractRawContentAsync(course, ct);

            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException(
                    "Aucun texte n'a pu être extrait du document. " +
                    "Si le PDF est un scan d'images, déposez plutôt les pages en photos.");

            course.ExtractedText = raw;

            var structured = await structurateur.StructureAsync(raw, course.Title, course.Subject, ct);
            if (string.IsNullOrWhiteSpace(structured))
                throw new InvalidOperationException("L'agent de mise en forme n'a rien renvoyé.");

            course.FormattedMarkdown = structured;

            await ReindexAsync(course, structured, ct);
            await RebuildJourneyAsync(course, structured, ct);

            course.FormatStatus = FormatStatus.Ready;
            course.FormattedAt = DateTime.UtcNow;
            course.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Cours {CourseId} mis en forme et indexé ({Chunks} fragments)",
                courseId, course.Chunks.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Échec de la mise en forme du cours {CourseId}", courseId);

            course.FormatStatus = FormatStatus.Failed;
            // Message destiné à l'élève : il s'affiche dans le canvas de lecture.
            course.FormatError = ex is OpenRouterException or InvalidOperationException
                ? ex.Message
                : "La mise en forme a échoué. Réessayez dans un instant.";

            await db.SaveChangesAsync(ct);
        }
    }

    // ── Extraction du texte brut selon le type de dépôt ───────────────────────────

    private async Task<string> ExtractRawContentAsync(Course course, CancellationToken ct)
    {
        var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");

        switch (course.ContentType)
        {
            case Model.ContentType.Images:
            {
                var photos = new List<AgentImage>();

                foreach (var asset in course.Assets.Where(a => a.Kind == AssetKind.Photo).OrderBy(a => a.Order))
                {
                    var path = Path.Combine(webRoot, asset.Path);
                    if (!File.Exists(path))
                    {
                        logger.LogWarning("Photo manquante sur le disque : {Path}", path);
                        continue;
                    }

                    var mediaType = AgentImage.MediaTypeForExtension(Path.GetExtension(path));
                    if (mediaType is null) continue;

                    photos.Add(new AgentImage(await File.ReadAllBytesAsync(path, ct), mediaType));
                }

                if (photos.Count == 0)
                    throw new InvalidOperationException("Aucune photo exploitable n'a été trouvée pour ce cours.");

                return await transcription.TranscribeAsync(photos, ct);
            }

            case Model.ContentType.Pdf:
            {
                var pdf = course.Assets.FirstOrDefault(a => a.Kind == AssetKind.Pdf)?.Path ?? course.PdfPath;
                if (pdf is null)
                    throw new InvalidOperationException("Aucun PDF n'est associé à ce cours.");

                var path = Path.Combine(webRoot, pdf);
                if (!File.Exists(path))
                    throw new InvalidOperationException("Le PDF de ce cours est introuvable sur le serveur.");

                return extractor.ExtractTextFromPath(path);
            }

            default:
                // Cours saisi à la main : les sections sont déjà du contenu éditorial,
                // le Structurateur se contente d'en normaliser la forme.
                course.RebuildExtractedText();
                return course.ExtractedText ?? string.Empty;
        }
    }

    // ── Indexation vectorielle ────────────────────────────────────────────────────

    private async Task ReindexAsync(Course course, string markdown, CancellationToken ct)
    {
        // Remplacement complet : un cours remis en forme n'a plus rien à voir avec son
        // découpage précédent, et un index partiellement périmé ferait citer au tuteur
        // des passages qui n'existent plus.
        var existing = await db.CourseChunks.Where(k => k.CourseId == course.Id).ToListAsync(ct);
        db.CourseChunks.RemoveRange(existing);

        var drafts = MarkdownChunker.Split(markdown);
        if (drafts.Count == 0)
        {
            course.Chunks.Clear();
            return;
        }

        var chunks = new List<CourseChunk>(drafts.Count);

        for (var offset = 0; offset < drafts.Count; offset += EmbeddingBatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = drafts.Skip(offset).Take(EmbeddingBatchSize).ToList();
            var vectors = await openRouter.EmbedAsync(batch.Select(d => d.Content).ToList(), ct);

            for (var i = 0; i < batch.Count; i++)
            {
                chunks.Add(new CourseChunk
                {
                    CourseId = course.Id,
                    HeadingPath = Truncate(batch[i].HeadingPath, 500),
                    Content = batch[i].Content,
                    Order = batch[i].Order,
                    EstimatedTokens = batch[i].EstimatedTokens,
                    Embedding = new Vector(vectors[i]),
                });
            }
        }

        db.CourseChunks.AddRange(chunks);
        course.Chunks = chunks;
    }

    // ── Parcours d'apprentissage ──────────────────────────────────────────────────

    /// <summary>
    /// (Re)construit les étapes du parcours à partir du cours mis en forme.
    ///
    /// Le travail déjà accompli est reporté sur les étapes équivalentes : régénérer la
    /// mise en forme ne doit pas effacer les tests qu'un élève a validés. L'équivalence
    /// se fait sur le couple (nature de l'étape, section couverte) — si une partie du
    /// cours disparaît à la réécriture, son étape disparaît avec, ce qui est correct.
    /// </summary>
    private async Task RebuildJourneyAsync(Course course, string markdown, CancellationToken ct)
    {
        var previous = await db.CourseSteps
            .Where(s => s.CourseId == course.Id)
            .ToListAsync(ct);

        var (mode, steps) = CourseJourneyBuilder.Build(course.Id, markdown);

        foreach (var step in steps)
        {
            var match = previous.FirstOrDefault(p =>
                p.Kind == step.Kind &&
                string.Equals(p.HeadingPath, step.HeadingPath, StringComparison.Ordinal));

            if (match is null) continue;

            step.Status = match.Status;
            step.Score = match.Score;
            step.Total = match.Total;
            step.Attempts = match.Attempts;
            step.CompletedAt = match.CompletedAt;
            step.WeakHeadings = match.WeakHeadings;
        }

        // Après report, on réapplique la règle d'ouverture : la première étape non
        // validée doit être accessible, sinon un élève à jour se retrouverait devant
        // un parcours entièrement verrouillé.
        UnlockNext(steps);

        db.CourseSteps.RemoveRange(previous);
        db.CourseSteps.AddRange(steps);

        course.JourneyMode = mode;
        course.Steps = steps;

        logger.LogInformation("Parcours du cours {CourseId} : mode {Mode}, {Count} étapes",
            course.Id, mode, steps.Count);
    }

    /// <summary>
    /// Ouvre la première étape non validée et verrouille celles qui suivent.
    /// Une étape échouée reste accessible : l'élève doit pouvoir refaire son test.
    /// </summary>
    internal static void UnlockNext(IReadOnlyList<CourseStep> steps)
    {
        var opened = false;

        foreach (var step in steps.OrderBy(s => s.Order))
        {
            if (step.Status == StepStatus.Passed) continue;

            if (!opened)
            {
                if (step.Status == StepStatus.Locked) step.Status = StepStatus.Available;
                opened = true;
            }
            else if (step.Status is not (StepStatus.Failed or StepStatus.InProgress))
            {
                step.Status = StepStatus.Locked;
            }
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
