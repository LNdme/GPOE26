using Microsoft.Extensions.Logging;

namespace GPOE26.Ai.Agents;

/// <summary>
/// Réécrit la FORME d'un cours sans en changer le FOND.
///
/// C'est cet agent qui transforme le dump brut de PdfPig — ou la transcription des photos —
/// en un document lisible : grand titre, hiérarchie de parties, encadrés de définition,
/// formules KaTeX, tableaux. Le résultat est stocké dans Course.FormattedMarkdown et
/// devient ce que l'élève lit dans le canvas.
/// </summary>
public sealed class StructurateurAgent(OpenRouterClient client, ILogger<StructurateurAgent> logger)
{
    /// <summary>
    /// Taille maximale d'un segment envoyé au modèle, en caractères.
    ///
    /// Un cours de 40 pages dépasse le plafond de tokens en sortie : on le traite par
    /// tranches et on recolle. Le découpage se fait sur des frontières de paragraphe pour
    /// ne pas couper une phrase ou une formule en deux.
    /// </summary>
    private const int SegmentSize = 12_000;

    private const string SystemPrompt = """
        Tu es un metteur en forme de cours pour des élèves de lycée.

        Ta mission est de RESTRUCTURER un cours, pas de le réécrire. Le fond appartient au
        professeur de l'élève : tu n'ajoutes aucune notion, tu n'en retires aucune, tu ne
        corriges pas le contenu, tu n'ajoutes ni exemple ni explication de ton cru.
        Tu réorganises et tu balises.

        Ce que tu produis est du Markdown :

        1. TITRES
           - `# ` pour le titre du cours (une seule fois, tout en haut).
           - `## ` pour les grandes parties, `### ` pour les sous-parties.
           - Déduis la hiérarchie du contenu quand la mise en forme d'origine est perdue
             (numérotation « I. », « 1) », « A- », lignes en majuscules, etc.).
           - Un titre est court et descriptif. Ne le fais pas précéder de sa numérotation
             d'origine si elle est redondante avec le niveau de titre.

        2. ENCADRÉS — utilise exactement cette syntaxe, une ligne `> [!type] Titre` puis le
           contenu en citation :

           > [!definition] Fonction dérivée
           > Le nombre dérivé de f en a est la limite du taux d'accroissement…

           Types disponibles : definition, theoreme, propriete, formule, exemple, methode,
           remarque, attention, astuce, resume.
           Emploie-les quand le texte d'origine énonce clairement une définition, un théorème,
           une propriété, un exemple ou une mise en garde. N'en abuse pas : un paragraphe
           ordinaire reste un paragraphe.

        3. MATHÉMATIQUES
           - `$...$` pour une formule dans une phrase, `$$...$$` pour une formule isolée.
           - Convertis en LaTeX les notations dégradées par l'extraction PDF
             (« x2 » → `$x^2$`, « <= » → `$\le$`, « racine de x » → `$\sqrt{x}$`).

        4. AUTRES ÉLÉMENTS
           - Tableaux Markdown pour les données tabulaires.
           - Listes à puces ou numérotées pour les énumérations.
           - `**gras**` sur les termes-clés définis, avec parcimonie.
           - Blocs de code délimités par ``` avec le langage, s'il y a du code.

        5. NETTOYAGE
           - Supprime les artefacts d'extraction : numéros de page isolés, en-têtes et pieds
             de page répétés, césures en fin de ligne, retours à la ligne au milieu d'une phrase.
           - Recolle les paragraphes coupés par la mise en page d'origine.
           - Si un passage est marqué `[illisible]`, conserve-le tel quel.

        Réponds UNIQUEMENT par le Markdown du cours. Aucune phrase d'introduction,
        aucun commentaire sur ton travail, aucun bloc ``` autour de l'ensemble.
        """;

    /// <param name="rawContent">Texte brut : sortie de PdfPig, ou transcription des photos.</param>
    /// <param name="title">Titre du cours saisi par l'élève, utilisé si le document n'en porte pas.</param>
    /// <param name="subject">Matière, pour orienter le vocabulaire et les conventions de notation.</param>
    public async Task<string> StructureAsync(
        string rawContent,
        string title,
        string subject,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawContent)) return string.Empty;

        var segments = Segment(rawContent);
        logger.LogInformation("Structuration de « {Title} » ({Subject}) en {Count} segment(s)",
            title, subject, segments.Count);

        var results = new List<string>(segments.Count);

        for (var i = 0; i < segments.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var instruction = BuildInstruction(
                segments[i], title, subject,
                index: i, total: segments.Count,
                previousTail: i > 0 ? Tail(results[i - 1], 800) : null);

            var structured = await client.CompleteAsync(new AgentRequest
            {
                Kind = AgentKind.Structurateur,
                SystemPrompt = SystemPrompt,
                Turns = [AgentTurn.User(instruction)],
                // La sortie fait la taille de l'entrée, à la balise près : il faut de la marge.
                MaxTokens = 8000,
            }, ct);

            results.Add(Unfence(structured.Trim()));
        }

        return string.Join("\n\n", results.Where(r => r.Length > 0));
    }

    private static string BuildInstruction(
        string segment, string title, string subject, int index, int total, string? previousTail)
    {
        var header = total == 1
            ? $"Mets en forme ce cours de {subject}, intitulé « {title} »."
            : $"Mets en forme la partie {index + 1} sur {total} d'un cours de {subject}, intitulé « {title} ».";

        if (index > 0)
        {
            header += "\n\nNe remets PAS le titre de niveau 1 : il figure déjà au début du document."
                    + "\nVoici la fin de la partie déjà mise en forme, pour que tu enchaînes sans"
                    + " répéter ni rouvrir une section déjà ouverte :\n\n" + previousTail;
        }

        return header + "\n\n--- COURS À METTRE EN FORME ---\n\n" + segment;
    }

    /// <summary>
    /// Découpe sur des frontières de paragraphe, pour ne couper ni une phrase ni une formule.
    /// </summary>
    private static List<string> Segment(string content)
    {
        content = content.Replace("\r\n", "\n");
        if (content.Length <= SegmentSize) return [content];

        var segments = new List<string>();
        var start = 0;

        while (start < content.Length)
        {
            if (content.Length - start <= SegmentSize)
            {
                segments.Add(content[start..]);
                break;
            }

            var end = start + SegmentSize;

            // Reculer jusqu'à une ligne vide ; à défaut, jusqu'à un simple retour à la ligne.
            var cut = content.LastIndexOf("\n\n", end, end - start, StringComparison.Ordinal);
            if (cut <= start) cut = content.LastIndexOf('\n', end, end - start);
            if (cut <= start) cut = end; // texte d'un seul tenant : coupure franche

            segments.Add(content[start..cut]);
            start = cut;
        }

        return segments.Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
    }

    /// <summary>
    /// Retire le bloc ``` que certains modèles enroulent autour de leur réponse malgré
    /// la consigne — sinon tout le cours s'afficherait comme un bloc de code.
    /// </summary>
    private static string Unfence(string value)
    {
        if (!value.StartsWith("```", StringComparison.Ordinal)) return value;

        var firstNewline = value.IndexOf('\n');
        if (firstNewline < 0) return value;

        var body = value[(firstNewline + 1)..];
        var lastFence = body.LastIndexOf("```", StringComparison.Ordinal);

        return lastFence < 0 ? body.Trim() : body[..lastFence].Trim();
    }

    private static string Tail(string value, int length) =>
        value.Length <= length ? value : "…" + value[^length..];
}
