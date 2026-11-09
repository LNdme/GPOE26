using System.Text;
using System.Text.RegularExpressions;

namespace Cours.Service;

/// <summary>
/// Bloc de texte d'une page, dans l'ordre de lecture. Top : position relative (0 = haut, 1 = bas).
/// Inline : texte avec le gras repéré mot par mot, en Markdown.
/// </summary>
public record PdfBlock(int Page, double Top, double FontSize, bool Bold, string Text, string Inline, int Lines);

public record PdfFigureRef(int Index, int Page, double Top, string Url, bool IsScan = false);

/// <summary>
/// Transforme les blocs d'un PDF en Markdown d'après la mise en page (tailles de police, gras),
/// sans IA : c'est la version affichée dès l'import. Le texte n'est jamais reformulé.
/// </summary>
public static partial class PdfMarkdownBuilder
{
    private const int MaxHeadingLength = 140;

    /// <summary>Taille du corps de texte : celle qui couvre le plus de caractères.</summary>
    public static double BodyFontSize(IEnumerable<PdfBlock> blocks) =>
        blocks.GroupBy(b => Math.Round(b.FontSize * 2) / 2)
            .OrderByDescending(g => g.Sum(b => b.Text.Length))
            .Select(g => g.Key)
            .DefaultIfEmpty(11)
            .First();

    /// <summary>Niveau de titre Markdown d'un bloc (2 ou 3), ou 0 pour un paragraphe.</summary>
    public static int HeadingLevel(PdfBlock block, double bodySize)
    {
        var text = block.Text.Trim();
        if (text.Length == 0 || text.Length > MaxHeadingLength || block.Lines > 3) return 0;
        if (text[^1] is '.' or ';' or ',') return 0;
        if (text.Count(char.IsLetter) < 3) return 0;

        var ratio = block.FontSize / Math.Max(bodySize, 1);
        if (ratio >= 1.45) return 2;
        if (ratio >= 1.15) return 3;
        if (!block.Bold || block.Lines > 2) return 0;

        // Titre numéroté en gras : « 1.2 Définition » est un sous-titre, « 1 Définition » un titre
        var numbered = NumberedHeading().Match(text);
        if (numbered.Success) return numbered.Groups[1].Value.Contains('.') ? 3 : 2;
        return text.Length <= 90 ? 3 : 0;
    }

    /// <summary>En-têtes et pieds de page répétés (numéros ignorés), à retirer du texte.</summary>
    public static HashSet<string> RepeatedMargins(IReadOnlyList<PdfBlock> blocks, int pageCount)
    {
        var threshold = Math.Max(3, (int)Math.Ceiling(pageCount * 0.4));
        return blocks.Where(b => b.Top < 0.12 || b.Top > 0.9)
            .GroupBy(b => MarginKey(b.Text))
            .Where(g => g.Key.Length > 0 && g.Select(b => b.Page).Distinct().Count() >= threshold)
            .Select(g => g.Key)
            .ToHashSet();
    }

    public static string Build(IReadOnlyList<PdfBlock> blocks, IReadOnlyList<PdfFigureRef> figures, int pageCount)
    {
        var bodySize = BodyFontSize(blocks);
        var margins = RepeatedMargins(blocks, pageCount);
        var sb = new StringBuilder();

        void Emit(string text) => sb.Append(text).Append("\n\n");
        void EmitFigure(PdfFigureRef f) => Emit(FigureMarkdown(f));

        for (var page = 1; page <= pageCount; page++)
        {
            var pageFigures = new Queue<PdfFigureRef>(figures.Where(f => f.Page == page).OrderBy(f => f.Top));

            foreach (var block in blocks.Where(b => b.Page == page))
            {
                var isMargin = block.Top < 0.12 || block.Top > 0.9;
                if (PageNumber().IsMatch(block.Text) || (isMargin && margins.Contains(MarginKey(block.Text))))
                    continue;

                while (pageFigures.Count > 0 && pageFigures.Peek().Top <= block.Top)
                    EmitFigure(pageFigures.Dequeue());

                var level = HeadingLevel(block, bodySize);
                if (level > 0)
                    Emit($"{new string('#', level)} {Escape(block.Text.Trim())}");
                else if (Bullet().IsMatch(block.Inline))
                    Emit("- " + Bullet().Replace(block.Inline, "", 1).Trim());
                else
                    Emit(block.Inline.Trim());
            }

            while (pageFigures.Count > 0)
                EmitFigure(pageFigures.Dequeue());
        }

        return sb.ToString().Trim();
    }

    public static string FigureMarkdown(PdfFigureRef f) =>
        f.IsScan ? $"![Page {f.Page} (scan)]({f.Url})" : $"![Figure {f.Index}]({f.Url})";

    /// <summary>Protège les caractères qui seraient interprétés par Markdown ou par les formules.</summary>
    public static string Escape(string text) => EscapedChars().Replace(text, @"\$0");

    /// <summary>Assemble des lignes de mots ; les mots coupés en fin de ligne sont recollés.</summary>
    public static string JoinLines(IEnumerable<string> lines)
    {
        var sb = new StringBuilder();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (sb.Length > 0 && sb[^1] == '-' && char.IsLower(line[0]))
                sb.Length--;
            else if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(line);
        }
        return sb.ToString();
    }

    // ── Préparation du texte pour l'IA ─────────────────────────────────────────

    /// <summary>Découpe aux lignes vides, en morceaux de taille raisonnable pour le modèle.</summary>
    public static List<string> SplitForModel(string markdown, int maxLength = 8000)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();
        foreach (var paragraph in markdown.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length > 0 && current.Length + paragraph.Length + 2 > maxLength)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }
            if (current.Length > 0) current.Append("\n\n");
            current.Append(paragraph);
        }
        if (current.Length > 0) chunks.Add(current.ToString());
        return chunks;
    }

    /// <summary>La réponse de l'IA est gardée seulement si elle n'a rien perdu : mêmes figures, longueur comparable.</summary>
    public static bool AcceptRewrite(string input, string output)
    {
        var expected = FigureToken().Matches(input).Select(m => m.Value).Order().ToList();
        var actual = FigureToken().Matches(output).Select(m => m.Value).Order().ToList();
        if (!expected.SequenceEqual(actual)) return false;
        var inputWords = CountWords(input);
        var outputWords = CountWords(output);
        return inputWords == 0 || (outputWords >= inputWords * 0.8 && outputWords <= inputWords * 1.35);
    }

    public static string StripCodeFence(string text)
    {
        var match = CodeFence().Match(text.Trim());
        return match.Success ? match.Groups[1].Value.Trim() : text.Trim();
    }

    /// <summary>Remplace les images par des repères stables, à rétablir après le passage de l'IA.</summary>
    public static (string Text, List<string> Images) ExtractImages(string markdown)
    {
        var images = new List<string>();
        var text = ImageMarkdown().Replace(markdown, m =>
        {
            images.Add(m.Value);
            return $"[[FIG:{images.Count}]]";
        });
        return (text, images);
    }

    public static string RestoreImages(string text, IReadOnlyList<string> images) =>
        FigureToken().Replace(text, m =>
        {
            var index = int.Parse(m.Groups[1].Value) - 1;
            return index >= 0 && index < images.Count ? images[index] : "";
        });

    private static int CountWords(string text) => FigureToken().Replace(text, " ").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string MarginKey(string text) => Digits().Replace(text, "").Trim().ToLowerInvariant();

    [GeneratedRegex(@"^(\d+(?:\.\d+)*)[.)]?\s+\p{L}")] private static partial Regex NumberedHeading();
    [GeneratedRegex(@"^\s*(?:page\s*)?\d+(?:\s*(?:/|sur)\s*\d+)?\s*$", RegexOptions.IgnoreCase)] private static partial Regex PageNumber();
    [GeneratedRegex(@"^\s*[•▪◦·‣\-–]\s+")] private static partial Regex Bullet();
    [GeneratedRegex(@"[*_$`#\[\]]")] private static partial Regex EscapedChars();
    [GeneratedRegex(@"\[\[FIG:(\d+)\]\]")] private static partial Regex FigureToken();
    [GeneratedRegex(@"!\[[^\]]*\]\([^)]+\)")] private static partial Regex ImageMarkdown();
    [GeneratedRegex(@"^```(?:markdown|md)?\s*\n(.*?)\n?```$", RegexOptions.Singleline)] private static partial Regex CodeFence();
    [GeneratedRegex(@"\d+")] private static partial Regex Digits();
}
