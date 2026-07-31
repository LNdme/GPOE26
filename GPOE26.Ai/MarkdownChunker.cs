using System.Text;

namespace GPOE26.Ai;

/// <summary>Un fragment de cours prêt à être vectorisé.</summary>
/// <param name="HeadingPath">Chemin des titres qui mènent au fragment, ex. « Les dérivées › Nombre dérivé ».</param>
/// <param name="Content">Texte du fragment, préfixé de son chemin de titres.</param>
/// <param name="Order">Position du fragment dans le cours, 0-based.</param>
public readonly record struct CourseChunkDraft(string HeadingPath, string Content, int Order)
{
    /// <summary>Estimation grossière du coût en tokens (~4 caractères par token en français).</summary>
    public int EstimatedTokens => Content.Length / 4;
}

/// <summary>
/// Découpe un cours Markdown en fragments destinés à la recherche vectorielle.
///
/// Le découpage suit les titres plutôt qu'une taille fixe, et chaque fragment est préfixé
/// de son chemin de titres. Deux bénéfices : le vecteur porte le contexte de la section
/// (« Nombre dérivé » isolé est ambigu, « Les dérivées › Nombre dérivé » ne l'est pas), et
/// le tuteur peut citer sa source à l'élève sous une forme lisible.
/// </summary>
public static class MarkdownChunker
{
    /// <summary>Taille cible d'un fragment, en caractères (~500 tokens).</summary>
    private const int TargetSize = 2_000;

    /// <summary>Recouvrement entre deux fenêtres d'une même section (~50 tokens).</summary>
    private const int Overlap = 200;

    /// <summary>En deçà, un fragment est fusionné avec le suivant plutôt que vectorisé seul.</summary>
    private const int MinimumSize = 120;

    /// <summary>
    /// Une grande partie du cours : un titre de niveau 2.
    /// </summary>
    /// <param name="Title">Le titre seul, ex. « Le coefficient directeur ».</param>
    /// <param name="Path">Le chemin complet, ex. « Les fonctions affines › Le coefficient directeur ».</param>
    public readonly record struct CoursePart(string Title, string Path);

    /// <summary>
    /// Les grandes parties du cours, dans l'ordre.
    ///
    /// Sert à décider du rythme du parcours (un cours à quatre parties ou plus se lit
    /// partie par partie) et à donner leur titre aux étapes. On s'appuie sur les mêmes
    /// règles de titres que le découpage, blocs de code compris.
    /// </summary>
    public static IReadOnlyList<CoursePart> TopParts(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];

        var parts = new List<CoursePart>();
        var currentH1 = string.Empty;
        var inFence = false;

        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence) continue;

            var level = HeadingLevel(line);
            if (level == 1) currentH1 = line[1..].Trim();
            else if (level == 2)
            {
                var title = line[2..].Trim();
                if (title.Length == 0) continue;

                parts.Add(new CoursePart(
                    title,
                    currentH1.Length > 0 ? $"{currentH1} › {title}" : title));
            }
        }

        return parts;
    }

    public static IReadOnlyList<CourseChunkDraft> Split(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];

        var sections = SplitIntoSections(markdown);
        var chunks = new List<CourseChunkDraft>();
        var order = 0;

        foreach (var section in sections)
        {
            var body = section.Body.Trim();
            if (body.Length == 0) continue;

            foreach (var window in Window(body))
            {
                var content = section.Path.Length > 0
                    ? $"{section.Path}\n\n{window}"
                    : window;

                chunks.Add(new CourseChunkDraft(section.Path, content, order++));
            }
        }

        return MergeTinyChunks(chunks);
    }

    // ── Découpage par titres ──────────────────────────────────────────────────────

    private readonly record struct Section(string Path, string Body);

    private static List<Section> SplitIntoSections(string markdown)
    {
        var sections = new List<Section>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        // Titre courant à chaque niveau (index 0 = H1, 1 = H2, 2 = H3…).
        var trail = new string?[6];
        var body = new StringBuilder();
        var path = string.Empty;
        var inFence = false;

        foreach (var line in lines)
        {
            // Un `#` à l'intérieur d'un bloc de code n'est pas un titre.
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
                inFence = !inFence;

            var level = inFence ? 0 : HeadingLevel(line);

            if (level == 0)
            {
                body.AppendLine(line);
                continue;
            }

            // Nouveau titre : on clôt la section en cours.
            if (body.Length > 0)
            {
                sections.Add(new Section(path, body.ToString()));
                body.Clear();
            }

            trail[level - 1] = line[level..].Trim();
            for (var deeper = level; deeper < trail.Length; deeper++) trail[deeper] = null;

            path = string.Join(" › ", trail.Where(t => !string.IsNullOrWhiteSpace(t)));
        }

        if (body.Length > 0) sections.Add(new Section(path, body.ToString()));

        return sections;
    }

    private static int HeadingLevel(string line)
    {
        var level = 0;
        while (level < line.Length && line[level] == '#') level++;

        // « #Titre » sans espace n'est pas un titre Markdown, « ####### » non plus.
        if (level is 0 or > 6) return 0;
        return level < line.Length && line[level] == ' ' ? level : 0;
    }

    // ── Fenêtrage d'une section trop longue ───────────────────────────────────────

    private static IEnumerable<string> Window(string body)
    {
        if (body.Length <= TargetSize)
        {
            yield return body;
            yield break;
        }

        var start = 0;
        while (start < body.Length)
        {
            if (body.Length - start <= TargetSize)
            {
                yield return body[start..].Trim();
                yield break;
            }

            var end = start + TargetSize;

            // Couper sur une frontière de paragraphe, à défaut de phrase, à défaut d'espace.
            var cut = body.LastIndexOf("\n\n", end, end - start, StringComparison.Ordinal);
            if (cut <= start) cut = body.LastIndexOf(". ", end, end - start, StringComparison.Ordinal) + 1;
            if (cut <= start) cut = body.LastIndexOf(' ', end, end - start);
            if (cut <= start) cut = end;

            yield return body[start..cut].Trim();

            // Le recouvrement évite qu'une notion à cheval sur deux fenêtres ne soit
            // retrouvable dans aucune des deux.
            start = Math.Max(cut - Overlap, cut > start ? start + 1 : end);
        }
    }

    // ── Fusion des miettes ────────────────────────────────────────────────────────

    /// <summary>
    /// Un titre suivi d'une seule phrase produit un fragment trop court pour porter un
    /// vecteur utile : on le recolle au fragment suivant.
    /// </summary>
    private static List<CourseChunkDraft> MergeTinyChunks(List<CourseChunkDraft> chunks)
    {
        if (chunks.Count <= 1) return chunks;

        var merged = new List<CourseChunkDraft>(chunks.Count);
        string? carry = null;

        foreach (var chunk in chunks)
        {
            var content = carry is null ? chunk.Content : carry + "\n\n" + chunk.Content;

            if (content.Length < MinimumSize)
            {
                carry = content;
                continue;
            }

            merged.Add(new CourseChunkDraft(chunk.HeadingPath, content, merged.Count));
            carry = null;
        }

        // Reliquat final : le rattacher au dernier fragment plutôt que de le perdre.
        if (carry is not null)
        {
            if (merged.Count > 0)
            {
                var last = merged[^1];
                merged[^1] = last with { Content = last.Content + "\n\n" + carry };
            }
            else
            {
                merged.Add(new CourseChunkDraft(string.Empty, carry, 0));
            }
        }

        return merged;
    }
}
