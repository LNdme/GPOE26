using System.Text;
using System.Text.RegularExpressions;

namespace Cours.Service;

/// <summary>
/// Bloc de texte d'une page, dans l'ordre de lecture. Top : position relative (0 = haut, 1 = bas).
/// Inline : texte avec le gras repéré mot par mot, en Markdown.
/// </summary>
public record PdfBlock(int Page, double Top, double FontSize, bool Bold, string Text, string Inline, int Lines,
    double Left = 0, double Right = 0, double Order = -1)
{
    public double ReadingKey => Order >= 0 ? Order : Top;
}

/// <summary>Position d'un schéma sur une photo de page, en pourcentage de sa largeur et de sa hauteur.</summary>
public record SchemaBox(double Left, double Top, double Right, double Bottom)
{
    public bool IsValid =>
        Left >= 0 && Top >= 0 && Right <= 100 && Bottom <= 100 && Right - Left >= 5 && Bottom - Top >= 3;
}

public record PdfFigureRef(int Index, int Page, double Top, string Url, bool IsScan = false, double Order = -1)
{
    public double ReadingKey => Order >= 0 ? Order : Top;
}

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
        if (text[^1] is ';' or ',') return 0;
        if (text[^1] == '.' && !(block.Bold && IsNumberedHeading(text))) return 0;
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

    public static bool IsNumberedHeading(string text) => NumberedHeading().IsMatch(text.Trim());

    public static bool IsEquationTag(string text) => EquationTag().IsMatch(text.Trim());

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
            var (pageBlocks, bullets) = AttachBullets(AttachEquationTags(blocks.Where(b => b.Page == page).ToList()));
            var items = pageBlocks.Select(b => (b.ReadingKey, Block: (PdfBlock?)b, Figure: (PdfFigureRef?)null))
                .Concat(figures.Where(f => f.Page == page).Select(f => (f.ReadingKey, Block: (PdfBlock?)null, Figure: (PdfFigureRef?)f)))
                .OrderBy(i => i.ReadingKey);

            foreach (var (_, block, figure) in items)
            {
                if (figure is not null)
                {
                    Emit(FigureMarkdown(figure));
                    continue;
                }

                var isMargin = block!.Top < 0.12 || block.Top > 0.9;
                if (PageNumber().IsMatch(block.Text) || (isMargin && margins.Contains(MarginKey(block.Text))))
                    continue;

                var level = bullets.Contains(block) ? 0 : HeadingLevel(block, bodySize);
                if (level > 0)
                    Emit($"{new string('#', level)} {Escape(block.Text.Trim())}");
                else if (bullets.Contains(block))
                    Emit("- " + block.Inline.Trim());
                else if (Bullet().IsMatch(block.Inline))
                    Emit("- " + Bullet().Replace(block.Inline, "", 1).Trim());
                else
                    Emit(block.Inline.Trim());
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>Un numéro d'équation, « (I-26) », rejoint l'équation écrite à sa gauche.</summary>
    private static List<PdfBlock> AttachEquationTags(List<PdfBlock> blocks)
    {
        var result = blocks.ToList();
        foreach (var tag in blocks.Where(b => EquationTag().IsMatch(b.Text.Trim())))
        {
            var target = result.Where(b => b != tag && Math.Abs(b.Top - tag.Top) < 0.02 && b.Right <= tag.Left)
                .OrderByDescending(b => b.Right)
                .FirstOrDefault();
            if (target is null) continue;

            var label = tag.Text.Trim();
            var inline = target.Inline.EndsWith(" $$", StringComparison.Ordinal)
                ? $@"{target.Inline[..^3]} \qquad\text{{{label}}} $$"
                : $"{target.Inline} {label}";
            result[result.IndexOf(target)] = target with { Inline = inline };
            result.Remove(tag);
        }
        return result;
    }

    /// <summary>
    /// Un tiret de liste est souvent un bloc à part, posé à gauche du texte : il est rattaché à son paragraphe.
    /// </summary>
    private static (List<PdfBlock> Blocks, HashSet<PdfBlock> Bullets) AttachBullets(List<PdfBlock> blocks)
    {
        var dashes = blocks.Where(b => b.Text.Trim() is "-" or "–" or "•" or "▪").ToList();
        var bullets = new HashSet<PdfBlock>();
        foreach (var dash in dashes)
        {
            var target = blocks.Where(b => !dashes.Contains(b) && Math.Abs(b.Top - dash.Top) < 0.012 && b.Left > dash.Left)
                .OrderBy(b => b.Left)
                .FirstOrDefault();
            if (target is not null) bullets.Add(target);
        }
        return (blocks.Except(dashes).ToList(), bullets);
    }

    public static string FigureMarkdown(PdfFigureRef f) =>
        f.IsScan ? $"![Page {f.Page} (scan)]({f.Url})" : $"![Figure {f.Index}]({f.Url})";

    /// <summary>
    /// Protège les caractères qui seraient interprétés par Markdown ou par les formules.
    /// Les crochets deviennent des entités : « \[ » serait lu comme le début d'une formule centrée.
    /// </summary>
    public static string Escape(string text) =>
        EscapedChars().Replace(text, m => m.Value switch { "[" => "&#91;", "]" => "&#93;", _ => "\\" + m.Value });

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

    /// <summary>
    /// Une page retranscrite remplace sa photo, sauf ses schémas. Un repère [schéma: gauche, haut, droite, bas]
    /// devient l'image découpée à cette position (en % de la page) ; sans position exploitable,
    /// la photo de la page entière est remise une fois, au premier schéma.
    /// </summary>
    public static string MergeTranscription(string text, string pageImageMarkdown, Func<SchemaBox, string?>? crop = null)
    {
        var pageShown = false;
        return SchemaMarker().Replace(text, marker =>
        {
            if (ParseBox(marker) is { } box && crop?.Invoke(box) is { } figure) return $"\n\n{figure}\n\n";
            if (pageShown) return "";
            pageShown = true;
            return $"\n\n{pageImageMarkdown}\n\n";
        });
    }

    /// <summary>
    /// Retranscription visiblement interrompue : repère ou formule ouverts et jamais fermés.
    /// Mieux vaut garder la photo de la page que n'en afficher qu'une partie.
    /// </summary>
    public static bool LooksTruncated(string text)
    {
        var trimmed = text.TrimEnd();
        if (trimmed.Length == 0) return true;
        // Un repère coupé (« [sch ») ; un intervalle à la française comme ]0, 1[ n'est pas concerné
        if (Regex.IsMatch(trimmed, @"\[[\p{L}]{1,10}$")) return true;
        if (trimmed.EndsWith('\\')) return true;
        if (Regex.Count(trimmed, @"\$\$") % 2 == 1) return true;
        return Regex.Count(trimmed, @"\\begin\{") > Regex.Count(trimmed, @"\\end\{");
    }

    private static SchemaBox? ParseBox(Match marker)
    {
        if (!marker.Groups[1].Success) return null;
        var values = Enumerable.Range(1, 4)
            .Select(i => double.TryParse(marker.Groups[i].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : -1)
            .ToArray();
        var box = new SchemaBox(values[0], values[1], values[2], values[3]);
        return box.IsValid ? box : null;
    }

    public static string RestoreImages(string text, IReadOnlyList<string> images) =>
        FigureToken().Replace(text, m =>
        {
            var index = int.Parse(m.Groups[1].Value) - 1;
            return index >= 0 && index < images.Count ? images[index] : "";
        });

    private static int CountWords(string text) => FigureToken().Replace(text, " ").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string MarginKey(string text) => Digits().Replace(text, "").Trim().ToLowerInvariant();

    [GeneratedRegex(@"^((?:\d+|[IVX]+)(?:\.\w+)*)[.)]?\s+\p{L}")] private static partial Regex NumberedHeading();
    [GeneratedRegex(@"^\s*(?:page\s*)?\d+(?:\s*(?:/|sur)\s*\d+)?\s*$", RegexOptions.IgnoreCase)] private static partial Regex PageNumber();
    [GeneratedRegex(@"^\s*[•▪◦·‣\-–]\s+")] private static partial Regex Bullet();
    [GeneratedRegex(@"[*_$`#\[\]]")] private static partial Regex EscapedChars();
    [GeneratedRegex(@"\[\[FIG:(\d+)\]\]")] private static partial Regex FigureToken();
    [GeneratedRegex(@"\[sch[ée]ma(?:\s*:\s*(\d+(?:\.\d+)?)\s*[,;]\s*(\d+(?:\.\d+)?)\s*[,;]\s*(\d+(?:\.\d+)?)\s*[,;]\s*(\d+(?:\.\d+)?)\s*%?)?[^\]\n]{0,40}\]", RegexOptions.IgnoreCase)]
    private static partial Regex SchemaMarker();
    [GeneratedRegex(@"!\[[^\]]*\]\([^)]+\)")] private static partial Regex ImageMarkdown();
    [GeneratedRegex(@"^```(?:markdown|md)?\s*\n(.*?)\n?```$", RegexOptions.Singleline)] private static partial Regex CodeFence();
    [GeneratedRegex(@"\d+")] private static partial Regex Digits();
    [GeneratedRegex(@"^\([IVX]+\s*-\s*\d+\)$")] private static partial Regex EquationTag();
}
