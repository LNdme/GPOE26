using Cours.Data;
using GPOE26.Ai;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Cours.Service;

/// <summary>Un fragment de cours retrouvé par la recherche, avec sa pertinence.</summary>
/// <param name="HeadingPath">Chemin des titres, citable tel quel dans une réponse.</param>
/// <param name="Score">Similarité cosinus dans [0, 1] — 1 = identique.</param>
public sealed record CourseSearchHit(Guid ChunkId, string HeadingPath, string Content, int Order, double Score);

/// <summary>
/// Recherche sémantique dans un cours.
///
/// C'est le seul point d'entrée du RAG : le service Chat interroge cet endpoint et ne
/// manipule jamais d'embeddings lui-même. Le contenu et son index restent la propriété
/// du service qui les possède.
/// </summary>
public sealed class CourseSearchService(
    CoursContext db,
    OpenRouterClient openRouter,
    ILogger<CourseSearchService> logger)
{
    public async Task<IReadOnlyList<CourseSearchHit>> SearchAsync(
        Guid courseId,
        string query,
        int k = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        k = Math.Clamp(k, 1, 20);

        var vectors = await openRouter.EmbedAsync([query], ct);
        var probe = new Vector(vectors[0]);

        // CosineDistance se traduit en opérateur `<=>`, celui pour lequel l'index HNSW
        // a été construit (vector_cosine_ops). Utiliser une autre distance ici ferait
        // silencieusement ignorer l'index et dégraderait la requête en scan complet.
        var hits = await db.CourseChunks
            .Where(c => c.CourseId == courseId && c.Embedding != null)
            .Select(c => new
            {
                c.Id,
                c.HeadingPath,
                c.Content,
                c.Order,
                Distance = c.Embedding!.CosineDistance(probe),
            })
            .OrderBy(x => x.Distance)
            .Take(k)
            .ToListAsync(ct);

        logger.LogInformation("Recherche dans le cours {CourseId} : {Count} fragments pour « {Query} »",
            courseId, hits.Count, Shorten(query));

        // La distance cosinus vaut 0 pour deux vecteurs identiques et 2 à l'opposé.
        // On la présente en score de similarité, plus lisible côté appelant.
        return hits
            .Select(x => new CourseSearchHit(x.Id, x.HeadingPath, x.Content, x.Order, 1d - (x.Distance / 2d)))
            .ToList();
    }

    private static string Shorten(string value) =>
        value.Length <= 80 ? value : value[..80] + "…";
}
