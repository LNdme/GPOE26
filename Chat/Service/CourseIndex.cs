using System.Security.Cryptography;
using System.Text;
using Chat.Data;
using Chat.Model;
using GPOE26.ServiceDefaults.Llm;
using Microsoft.EntityFrameworkCore;

namespace Chat.Service;

/// <summary>
/// Passages d'un cours et leurs vecteurs de sens, pour donner au tuteur les bons passages d'un long cours (H12).
/// L'index est fait à la première question sur un cours, puis refait seulement si le texte du cours change
/// (empreinte SHA-256) ou si le modèle d'embeddings change. Sans modèle d'embeddings, ou s'il ne répond pas,
/// les passages sont choisis par les mots de la question.
/// </summary>
public class CourseIndex(TutorContext db, LlmClient llm, ILogger<CourseIndex> logger)
{
    /// <summary>Un index fait sans vecteurs (modèle injoignable) est retenté au plus tôt après ce délai.</summary>
    public static readonly TimeSpan RetryEmbeddingsAfter = TimeSpan.FromMinutes(10);

    public async Task<AgentPrompts.CourseExcerpt> SelectAsync(CourseRef course, string question, CancellationToken ct)
    {
        var text = course.FullText();
        if (text.Length <= AgentPrompts.CourseBudget) return new AgentPrompts.CourseExcerpt(text, null);

        var passages = await PassagesAsync(course.Id, text, ct);
        var embedded = passages.Any(p => p.Embedding is not null);
        var questionEmbedding = embedded ? await llm.EmbedAsync(question, ct) : null;
        return AgentPrompts.SelectPassages(passages, question, questionEmbedding);
    }

    /// <summary>Passages du cours, lus dans l'index s'il est à jour, sinon refaits (et enregistrés).</summary>
    public async Task<List<AgentPrompts.CoursePassage>> PassagesAsync(Guid courseId, string text, CancellationToken ct)
    {
        var hash = Hash(text);
        var model = llm.EmbeddingModel;
        var stored = await db.CourseChunks.AsNoTracking().Where(c => c.CourseId == courseId).OrderBy(c => c.Position).ToListAsync(ct);

        var upToDate = stored.Count > 0 && stored.All(c => c.TextHash == hash)
            && (model is null                                                     // pas de modèle : les mots suffisent
                || stored.All(c => c.EmbeddingModel == model && c.Embedding is not null)
                || (stored.All(c => c.Embedding is null) && DateTime.UtcNow - stored[0].CreatedAt < RetryEmbeddingsAfter));
        if (upToDate)
            return stored.Select(c => new AgentPrompts.CoursePassage(c.Position, c.Title, c.Content, c.Embedding)).ToList();

        var passages = AgentPrompts.Passages(text);
        var vectors = model is null ? null
            : await llm.EmbedManyAsync(passages.Select(p => $"{p.Title}\n{p.Content}").ToList(), ct);
        if (vectors is not null)
            passages = passages.Select((p, i) => p with { Embedding = vectors[i] }).ToList();

        try
        {
            await db.CourseChunks.Where(c => c.CourseId == courseId).ExecuteDeleteAsync(ct);
            var now = DateTime.UtcNow;
            db.CourseChunks.AddRange(passages.Select(p => new CourseChunk
            {
                CourseId = courseId, Position = p.Position, TextHash = hash, Title = p.Title, Content = p.Content,
                Embedding = p.Embedding, EmbeddingModel = p.Embedding is null ? null : model, CreatedAt = now
            }));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Cours {CourseId} indexé : {Count} passages, {Vectors}", courseId, passages.Count,
                vectors is null ? "sans vecteurs" : $"vecteurs {model}");
        }
        catch (DbUpdateException)
        {
            // Deux questions en même temps sur un cours pas encore indexé : l'autre a enregistré l'index, celui-ci sert quand même
            db.ChangeTracker.Clear();
        }
        return passages;
    }

    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
