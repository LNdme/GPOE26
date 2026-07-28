using Microsoft.Extensions.Logging;

namespace GPOE26.Ai.Agents;

/// <summary>
/// Transcrit des photos de cours (tableau, cahier, polycopié photographié) en Markdown.
///
/// C'est l'agent qui rend possible le dépôt de photos : PdfPig sait lire un PDF texte,
/// mais rien dans le produit ne savait lire une image avant lui. Le modèle employé doit
/// donc être capable de vision (voir <see cref="AgentKind.Vision"/>).
/// </summary>
public sealed class TranscriptionAgent(OpenRouterClient client, ILogger<TranscriptionAgent> logger)
{
    /// <summary>
    /// Nombre de photos envoyées par appel. Trop d'images d'un coup gonfle la requête et
    /// dégrade la fidélité de la transcription ; une par une multiplie les appels.
    /// </summary>
    private const int BatchSize = 3;

    private const string SystemPrompt = """
        Tu transcris des photos de cours d'élève (page de cahier, tableau, polycopié) en Markdown.

        Règles :
        - Transcris FIDÈLEMENT ce qui est écrit. N'invente rien, n'ajoute aucun commentaire,
          ne complète aucune phrase laissée en suspens.
        - Conserve la langue d'origine du document.
        - Restitue la hiérarchie visible : un titre souligné ou encadré devient un titre Markdown.
        - Les formules mathématiques deviennent du LaTeX : $...$ en ligne, $$...$$ pour un bloc isolé.
        - Les tableaux deviennent des tableaux Markdown.
        - Un schéma ou un dessin que tu ne peux pas transcrire devient une ligne
          `> [!remarque] Schéma non transcrit : <description courte de ce qu'il représente>`.
        - Si une partie est illisible, écris `[illisible]` à cet endroit précis plutôt que de deviner.
        - Ne numérote pas les pages et n'écris aucun en-tête du type « Page 1 ».

        Réponds uniquement par la transcription Markdown, sans phrase d'introduction ni de conclusion.
        """;

    public async Task<string> TranscribeAsync(
        IReadOnlyList<AgentImage> photos,
        CancellationToken ct = default)
    {
        if (photos.Count == 0) return string.Empty;

        var segments = new List<string>();

        for (var offset = 0; offset < photos.Count; offset += BatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = photos.Skip(offset).Take(BatchSize).ToList();
            var firstPage = offset + 1;
            var lastPage = offset + batch.Count;

            var instruction = batch.Count == 1
                ? $"Transcris la page {firstPage} du cours."
                : $"Transcris les pages {firstPage} à {lastPage} du cours, dans l'ordre où elles te sont données.";

            // On rappelle la fin du segment précédent pour que le modèle sache s'il
            // poursuit une phrase, une liste ou une démonstration entamée page d'avant.
            if (segments.Count > 0)
            {
                instruction += "\n\nCes pages font suite au texte ci-dessous. Enchaîne sans le répéter :\n\n"
                             + Tail(segments[^1], 600);
            }

            var text = await client.CompleteAsync(new AgentRequest
            {
                Kind = AgentKind.Vision,
                SystemPrompt = SystemPrompt,
                Turns = [AgentTurn.User(instruction)],
                Images = batch,
                MaxTokens = 4000,
            }, ct);

            logger.LogInformation("Transcription des pages {First}-{Last} : {Length} caractères",
                firstPage, lastPage, text.Length);

            segments.Add(text.Trim());
        }

        return string.Join("\n\n", segments.Where(s => s.Length > 0));
    }

    private static string Tail(string value, int length) =>
        value.Length <= length ? value : "…" + value[^length..];
}
