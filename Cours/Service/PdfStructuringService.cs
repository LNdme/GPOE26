using System.Security.Cryptography;
using System.Text;
using Cours.Data;
using Cours.Model;
using Microsoft.EntityFrameworkCore;
using PDFtoImage;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

#pragma warning disable CA1416 // Rendu PDFium : Windows et Linux, les plateformes de déploiement

namespace Cours.Service;

public record PdfStructure(string Markdown, string RawText, int Figures, int Formulas);

/// <summary>
/// Lit un PDF comme un lecteur : ordre de lecture, titres, gras, formules et images.
/// Les images intégrées sont extraites telles quelles ; les schémas vectoriels et les pages scannées sont capturés.
/// </summary>
public class PdfStructuringService(ILogger<PdfStructuringService> logger)
{
    private const int MaxFigures = 120;
    private const int RenderDpi = 200;
    private const double MinImageSize = 50;
    private const int MinScannedChars = 20;

    // Un schéma de cours peut être tout petit : un symbole, deux fils et une flèche
    private const int MinFigurePaths = 3;
    private const double MinFigureWidth = 18;
    private const double FigureGap = 12;
    private const int MaxWordsInFigure = 30;
    private const double LabelMargin = 14;
    private const int MaxLabelLength = 4;
    private const int MaxCaptionLength = 20;

    private const double BarThickness = 1.5;
    private const double MinBarWidth = 5;
    private const double FractionReach = 18;
    private const double NumeratorReach = 11;
    private const double DenominatorReach = 13;
    private const double SameLine = 3;

    public const string StatusPending = "Pending";
    public const string StatusDone = "Done";
    public const string StatusFailed = "Failed";

    private record Candidate(int Page, double Top, byte[] Data, string Extension, bool IsScan, bool IsCapture, double Order);
    /// <summary>Latex vide : formule trop complexe pour être relue sans erreur, elle sera capturée en image.</summary>
    private record Formula(PdfRectangle Box, string? Latex, string Text, List<Word> Words);

    private record Line(List<Word> Words, double Baseline, double Size)
    {
        public string Text => string.Join(" ", Words.Select(w => w.Text.Trim()).Where(t => t.Length > 0));
        public bool AllBold => Words.SelectMany(w => w.Letters).Where(l => !string.IsNullOrWhiteSpace(l.Value)).All(IsBold);
    }

    public static string FiguresFolder(string webRoot, Guid courseId) => Path.Combine(webRoot, "uploads", "cours", courseId.ToString());

    /// <summary>Analyse le PDF, remplace le contenu du cours et écrit ses images. L'appelant enregistre le contexte.</summary>
    public PdfStructure Import(CoursContext db, Course course, byte[] pdf, string webRoot, bool queueForAi)
    {
        var structure = Analyze(pdf, course.Id, webRoot);

        db.CourseSections.RemoveRange(course.Sections.ToList());
        db.CourseSections.Add(new CourseSection { CourseId = course.Id, Type = SectionType.Paragraph, Content = structure.Markdown, Order = 0 });
        course.ContentType = ContentType.Pdf;
        course.ExtractedText = structure.RawText;
        course.StructuringStatus = queueForAi ? StatusPending : StatusDone;
        course.UpdatedAt = DateTime.UtcNow;
        return structure;
    }

    public PdfStructure Analyze(byte[] pdf, Guid courseId, string webRoot)
    {
        using var document = PdfDocument.Open(pdf);
        var blocks = new List<PdfBlock>();
        var candidates = new List<Candidate>();
        var raw = new StringBuilder();
        var pageCount = document.NumberOfPages;
        var formulas = 0;

        foreach (var page in document.GetPages())
        {
            raw.AppendLine(page.Text);
            using var rendered = new PageRenderer(pdf, page, logger);

            // Page scannée : aucun texte exploitable, on garde son image entière
            if (page.Text.Trim().Length < MinScannedChars)
            {
                if (rendered.Capture(null) is { } scan)
                    candidates.Add(new Candidate(page.Number, 0, scan, "png", IsScan: true, IsCapture: true, 0));
                continue;
            }

            var words = AttachScripts(NearestNeighbourWordExtractor.Instance.GetWords(page.Letters).ToList());
            var rects = DrawnShapes(page);
            var bars = FractionBars(page, rects);
            var pageFormulas = ReadFormulas(page, bars, words);
            var drawings = rects.Except(bars.Select(b => b.Rect)).ToList();

            var regions = new List<PdfRectangle>();
            var figures = new List<(PdfRectangle Box, byte[] Data, string Extension, bool IsCapture)>();

            foreach (var image in page.GetImages())
            {
                if (image.Bounds.Width < MinImageSize || image.Bounds.Height < MinImageSize) continue;
                var data = ReadImage(image, rendered, page, out var extension, out var captured);
                if (data is null) continue;
                figures.Add((image.Bounds, data, extension, captured));
                regions.Add(image.Bounds);
            }

            // Les équations déjà lues ne sont pas des légendes : le schéma voisin ne doit pas les avaler
            var inFormulas = pageFormulas.SelectMany(f => f.Words).ToHashSet();
            foreach (var found in VectorFigures(page, drawings, words.Where(w => !inFormulas.Contains(w)).ToList(), regions))
            {
                // Une formule écrite à côté d'un circuit est une étiquette du schéma
                var box = found;
                foreach (var label in pageFormulas.Where(f => IsBeside(found, f.Box)).ToList())
                {
                    box = Union(box, label.Box);
                    pageFormulas.Remove(label);
                }
                if (rendered.Capture(box) is not { } capture) continue;
                figures.Add((box, capture, "png", true));
                regions.Add(box);
            }

            // Une formule imbriquée mal relue serait fausse : l'image, elle, est toujours exacte
            foreach (var complex in pageFormulas.Where(f => f.Latex is null).ToList())
            {
                if (rendered.Capture(complex.Box) is { } capture)
                {
                    figures.Add((complex.Box, capture, "png", true));
                    regions.Add(complex.Box);
                }
                pageFormulas.Remove(complex);
            }

            var claimed = pageFormulas.SelectMany(f => f.Words).ToHashSet();
            var textWords = words.Where(w => !claimed.Contains(w) && !regions.Any(r => Contains(r, w.BoundingBox))).ToList();
            var pageBlocks = ReadBlocks(page, textWords);

            foreach (var formula in pageFormulas)
                pageBlocks.Add(new PdfBlock(page.Number, TopRatio(page, formula.Box.Top), 0, false, formula.Text, $"$$ {formula.Latex} $$", 1, formula.Box.Left, formula.Box.Right));
            formulas += pageFormulas.Count;

            // Texte, formules et images sont rangés ensemble : un schéma reste dans sa colonne
            var boxes = pageBlocks.Select(b => (b.Left, b.Right, b.Top))
                .Concat(figures.Select(f => (f.Box.Left, f.Box.Right, TopRatio(page, f.Box.Top))))
                .ToList();
            var ranks = PdfLayout.Order(page.Width, boxes);

            blocks.AddRange(pageBlocks.Select((b, i) => b with { Order = ranks[i] }));
            candidates.AddRange(figures.Select((f, i) => new Candidate(page.Number, TopRatio(page, f.Box.Top), f.Data, f.Extension, IsScan: false, f.IsCapture, ranks[pageBlocks.Count + i])));
        }

        var saved = SaveFigures(candidates, courseId, webRoot);
        return new PdfStructure(PdfMarkdownBuilder.Build(blocks, saved, pageCount), raw.ToString().Trim(), saved.Count, formulas);
    }

    /// <summary>
    /// Regroupe les mots en lignes puis en paragraphes, colonne par colonne.
    /// Fait à partir des lignes de base : une équation au milieu d'une phrase reste sur sa ligne.
    /// </summary>
    private static List<PdfBlock> ReadBlocks(Page page, List<Word> words)
    {
        var result = new List<PdfBlock>();
        foreach (var column in SplitColumns(page, words))
        foreach (var lines in Paragraphs(GroupLines(column)))
        {
            var letters = lines.SelectMany(l => l.Words).SelectMany(w => w.Letters).Where(l => !string.IsNullOrWhiteSpace(l.Value)).ToList();
            if (letters.Count == 0) continue;

            var size = letters.GroupBy(l => Math.Round(l.PointSize, 1)).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
            var bold = letters.Count(IsBold) >= letters.Count * 0.8;
            var text = PdfMarkdownBuilder.JoinLines(lines.Select(l => l.Text));
            var inline = PdfMarkdownBuilder.JoinLines(lines.Select(InlineLine));

            var box = lines.SelectMany(l => l.Words).Select(w => w.BoundingBox).Aggregate(Union);
            result.Add(new PdfBlock(page.Number, TopRatio(page, box.Top), size, bold, text, inline, lines.Count, box.Left, box.Right));
        }
        return result;
    }

    private static IEnumerable<List<Word>> SplitColumns(Page page, List<Word> words)
    {
        const double band = 4;
        var center = page.Width / 2;
        bool Crosses(Word w) => w.BoundingBox.Left < center - band && w.BoundingBox.Right > center + band;

        var crossingBaselines = words.Where(Crosses).Select(Baseline).ToList();
        var rows = words.Select(w => Math.Round(Baseline(w) / SameLine)).Distinct().Count();
        var left = words.Count(w => w.BoundingBox.Right <= center);
        var right = words.Count(w => w.BoundingBox.Left >= center);
        var twoColumns = rows > 0 && crossingBaselines.Count < rows * 0.2 && left >= words.Count * 0.2 && right >= words.Count * 0.2;
        if (!twoColumns)
        {
            yield return words;
            yield break;
        }

        // Une ligne qui franchit le milieu (titre, pied de page) forme un groupe à part
        bool FullRow(Word w) => crossingBaselines.Any(b => Math.Abs(b - Baseline(w)) <= SameLine);
        yield return words.Where(FullRow).ToList();
        yield return words.Where(w => !FullRow(w) && (w.BoundingBox.Left + w.BoundingBox.Right) / 2 < center).ToList();
        yield return words.Where(w => !FullRow(w) && (w.BoundingBox.Left + w.BoundingBox.Right) / 2 >= center).ToList();
    }

    private static double WordSize(Word w) => w.Letters.Max(l => l.PointSize);

    /// <summary>Ligne de base du mot, sans ses indices.</summary>
    private static double Baseline(Word w)
    {
        var size = WordSize(w);
        return w.Letters.First(l => l.PointSize >= size - 0.5).StartBaseLine.Y;
    }

    private static List<Line> GroupLines(List<Word> words)
    {
        var lines = new List<Line>();
        foreach (var word in words.OrderByDescending(Baseline))
        {
            var baseline = Baseline(word);
            var index = lines.FindIndex(l => Math.Abs(l.Baseline - baseline) <= SameLine);
            if (index < 0)
            {
                lines.Add(new Line([word], baseline, WordSize(word)));
            }
            else
            {
                var line = lines[index];
                line.Words.Add(word);
                lines[index] = line with { Size = Math.Max(line.Size, WordSize(word)) };
            }
        }
        foreach (var line in lines) line.Words.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
        return lines;
    }

    private static List<List<Line>> Paragraphs(List<Line> lines)
    {
        var groups = new List<List<Line>>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var previous = i > 0 ? lines[i - 1] : null;
            var next = i + 1 < lines.Count ? lines[i + 1] : null;

            var loneBold = line.AllBold && previous is not { AllBold: true } && next is not { AllBold: true }
                           && !char.IsLower(line.Text.FirstOrDefault(char.IsLetterOrDigit));
            var heading = loneBold || (line.AllBold && PdfMarkdownBuilder.IsNumberedHeading(line.Text));
            var startsNew = heading || previous is null
                || previous.Baseline - line.Baseline > 1.55 * Math.Max(previous.Size, line.Size)
                || Math.Abs(previous.Size - line.Size) > 1.5
                || line.Text.TrimStart().StartsWith("- ", StringComparison.Ordinal)
                || (previous.AllBold && PdfMarkdownBuilder.IsNumberedHeading(previous.Text))
                || (i >= 2 && IsLoneBold(lines, i - 1));

            if (startsNew) groups.Add([line]);
            else groups[^1].Add(line);
        }
        return groups;
    }

    private static bool IsLoneBold(List<Line> lines, int i) =>
        lines[i].AllBold && (i == 0 || !lines[i - 1].AllBold) && (i + 1 >= lines.Count || !lines[i + 1].AllBold)
        && !char.IsLower(lines[i].Text.FirstOrDefault(char.IsLetterOrDigit));

    private static string InlineLine(Line line) =>
        PdfFormulaBuilder.InlineLine(line.Words.SelectMany(w => w.Letters).Select(ToGlyph).ToList());

    private static PdfGlyph ToGlyph(Letter letter) => new(
        letter.Value, letter.GlyphRectangle.Left, letter.GlyphRectangle.Right, letter.StartBaseLine.Y, letter.PointSize, letter.FontName ?? "");

    private static bool IsBold(Letter letter)
    {
        var name = letter.FontName ?? "";
        return name.Contains("bold", StringComparison.OrdinalIgnoreCase)
            || name.Contains("black", StringComparison.OrdinalIgnoreCase)
            || name.Contains("heavy", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Rattache chaque indice ou exposant au mot qui le précède : sinon « uAB » est lu comme deux mots
    /// sur deux lignes, parce que sa taille et sa hauteur diffèrent.
    /// </summary>
    private static List<Word> AttachScripts(List<Word> words)
    {
        double Size(Word w) => w.Letters.Max(l => l.PointSize);
        var hosts = words.ToDictionary(w => w, w => new List<Letter>(w.Letters));
        var absorbed = new HashSet<Word>();

        foreach (var script in words.OrderBy(w => w.BoundingBox.Left))
        {
            var host = words.Where(w => w != script && !absorbed.Contains(w)
                                     && Size(script) < Size(w) * 0.88
                                     && script.BoundingBox.Left - hosts[w].Max(l => l.GlyphRectangle.Right) is > -3 and < 3
                                     && Math.Abs(script.Letters[0].StartBaseLine.Y - w.Letters[0].StartBaseLine.Y) <= Size(w) * 0.6)
                .OrderBy(w => Math.Abs(script.BoundingBox.Left - hosts[w].Max(l => l.GlyphRectangle.Right)))
                .FirstOrDefault();
            if (host is null) continue;
            hosts[host].AddRange(script.Letters);
            absorbed.Add(script);
        }

        // Flèche de vecteur : un glyphe à part, posé au-dessus de sa lettre
        foreach (var accent in words.Where(w => w.Letters.All(l => (l.FontName ?? "").Contains("MTExtra", StringComparison.OrdinalIgnoreCase))))
        {
            var below = words.Where(w => w != accent && !absorbed.Contains(w) && Baseline(w) < Baseline(accent) && Baseline(accent) - Baseline(w) <= 14
                                      && w.BoundingBox.Left - 2 <= accent.BoundingBox.Right && accent.BoundingBox.Left - 2 <= w.BoundingBox.Right)
                .OrderBy(w => Baseline(accent) - Baseline(w))
                .FirstOrDefault();
            if (below is null) continue;
            hosts[below].AddRange(accent.Letters);
            absorbed.Add(accent);
        }

        return words.Where(w => !absorbed.Contains(w))
            .Select(w => hosts[w].Count == w.Letters.Count ? w : new Word(hosts[w].OrderBy(l => l.GlyphRectangle.Left).ToList()))
            .ToList();
    }

    private byte[]? ReadImage(IPdfImage image, PageRenderer rendered, Page page, out string extension, out bool captured)
    {
        captured = false;
        try
        {
            if (image.TryGetPng(out var png))
            {
                extension = "png";
                return png;
            }
            var bytes = image.RawMemory.ToArray();
            if (bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8)
            {
                extension = "jpg";
                return bytes;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogDebug(ex, "Image illisible page {Page}, capture de la zone", page.Number);
        }

        extension = "png";
        captured = true;
        return rendered.Capture(image.Bounds);
    }

    /// <summary>Tracés de la page, sans l'en-tête ni le pied de page, ni les doublons (un trait dessiné deux fois).</summary>
    private static List<PdfRectangle> DrawnShapes(Page page)
    {
        var top = page.Height * 0.95;
        var bottom = page.Height * 0.08;
        return page.ExperimentalAccess.Paths
            .Where(p => !p.IsClipping && (p.IsFilled || p.IsStroked)) // les zones de découpage ne se voient pas : ce ne sont pas des dessins
            .Select(p => p.GetBoundingRectangle())
            .Where(r => r is not null)
            .Select(r => r!.Value)
            .Where(r => r.Bottom >= bottom && r.Top <= top)
            .Where(r => r.Width < page.Width * 0.6 && r.Height < page.Height * 0.6 && (r.Width > 1 || r.Height > 1))
            .DistinctBy(r => (Math.Round(r.Left), Math.Round(r.Right), Math.Round(r.Top), Math.Round(r.Bottom)))
            .Take(3000)
            .ToList();
    }

    /// <summary>
    /// Un trait de fraction est isolé (rien ne s'y raccorde, contrairement à un fil de circuit)
    /// et encadré : du texte au-dessus et en dessous, à bonne distance (un soulignement colle au texte).
    /// </summary>
    private static List<(PdfBar Bar, PdfRectangle Rect)> FractionBars(Page page, List<PdfRectangle> rects)
    {
        var result = new List<(PdfBar, PdfRectangle)>();
        foreach (var r in rects.Where(r => r.Height <= BarThickness && r.Width >= MinBarWidth))
        {
            var y = (r.Top + r.Bottom) / 2;
            // Un fil de circuit touche un composant ou passe à côté d'une flèche
            var connected = rects.Any(o => o != r && (TouchesEnd(r, o) || (o.Width > 2 && o.Height > 2 && Near(r, o, 3))));
            if (connected) continue;

            var inSpan = page.Letters.Where(l => (l.GlyphRectangle.Left + l.GlyphRectangle.Right) / 2 >= r.Left - 1
                                               && (l.GlyphRectangle.Left + l.GlyphRectangle.Right) / 2 <= r.Right + 1).ToList();
            var above = inSpan.Where(l => l.StartBaseLine.Y > y && l.StartBaseLine.Y - y <= FractionReach).ToList();
            var below = inSpan.Where(l => l.StartBaseLine.Y < y && y - l.StartBaseLine.Y <= FractionReach).ToList();
            if (above.Count == 0 || below.Count == 0) continue;
            if (above.Min(l => l.StartBaseLine.Y) - y < 2.5) continue;

            result.Add((new PdfBar(r.Left, r.Right, y), r));
        }
        return result;
    }

    private static bool TouchesEnd(PdfRectangle bar, PdfRectangle other)
    {
        const double reach = 2.5;
        var y = (bar.Top + bar.Bottom) / 2;
        if (other.Bottom - reach > y || other.Top + reach < y) return false;
        return (other.Left - reach <= bar.Left && other.Right + reach >= bar.Left) || (other.Left - reach <= bar.Right && other.Right + reach >= bar.Right);
    }

    /// <summary>Regroupe les traits de fraction d'une même équation et lit tout ce qui se trouve sur sa ligne.</summary>
    private static List<Formula> ReadFormulas(Page page, List<(PdfBar Bar, PdfRectangle Rect)> bars, List<Word> words)
    {
        var groups = new List<List<PdfBar>>();
        foreach (var bar in bars.Select(b => b.Bar).OrderByDescending(b => b.Y))
        {
            var group = groups.FirstOrDefault(g => g.Any(o => Math.Abs(o.Y - bar.Y) <= FractionReach * 1.6 && bar.Left <= o.Right + 60 && o.Left <= bar.Right + 60));
            if (group is null) groups.Add([bar]);
            else group.Add(bar);
        }

        var center = page.Width / 2;
        var result = new List<Formula>();
        foreach (var group in groups)
        {
            var left = group.Min(b => b.Left);
            var right = group.Max(b => b.Right);
            var columnLeft = left < center ? 0 : center;
            var columnRight = right <= center + 15 ? center + 15 : page.Width;

            bool InColumn(Word w) => Mid(w) >= columnLeft && Mid(w) <= columnRight;
            bool InFraction(Word w) => group.Any(b => Mid(w) >= b.Left - 3 && Mid(w) <= b.Right + 3
                                                      && Baseline(w) - b.Y <= NumeratorReach && b.Y - Baseline(w) <= DenominatorReach);
            bool OnAxis(Word w) => group.Any(b => Math.Abs(Baseline(w) - b.Y) <= 6) && !InFraction(w);

            var members = words.Where(w => InColumn(w) && InFraction(w)).ToList();
            var axis = words.Where(w => InColumn(w) && OnAxis(w)).ToList();

            // Sur l'axe, on s'étend de part et d'autre tant qu'on reste dans l'équation : la phrase autour reste du texte
            foreach (var word in axis.Where(w => Mid(w) < left).OrderByDescending(Mid).TakeWhile(IsEquationPart)) members.Add(word);
            foreach (var word in axis.Where(w => Mid(w) > right).OrderBy(Mid).TakeWhile(IsEquationPart)) members.Add(word);
            members.AddRange(axis.Where(w => Mid(w) >= left && Mid(w) <= right));    // « = C · » entre deux fractions
            members.AddRange(axis.Where(w => Mid(w) > right && PdfMarkdownBuilder.IsEquationTag(w.Text)));
            if (members.Count == 0) continue;

            var glyphs = members.SelectMany(w => w.Letters).Select(ToGlyph).Where(g => !string.IsNullOrWhiteSpace(g.Text)).ToList();
            var box = members.Select(w => w.BoundingBox).Aggregate(Union);
            var text = string.Join(" ", members.OrderByDescending(w => w.BoundingBox.Top).ThenBy(w => w.BoundingBox.Left).Select(w => w.Text));
            var latex = PdfFormulaBuilder.IsReliable(glyphs, group) ? PdfFormulaBuilder.WithEquationTag(PdfFormulaBuilder.ToLatex(glyphs, group)) : null;
            result.Add(new Formula(box, latex, text, members));
        }
        return result;
    }

    private static double Mid(Word w) => (w.BoundingBox.Left + w.BoundingBox.Right) / 2;

    /// <summary>Un mot de phrase, une ponctuation de phrase ou un numéro d'équation marquent la fin de l'équation.</summary>
    private static bool IsEquationPart(Word word)
    {
        var text = word.Text.Trim();
        if (text is ":" or ";" || PdfMarkdownBuilder.IsEquationTag(text)) return false;
        var letters = word.Letters.Where(l => l.Value.Any(char.IsLetter)).ToList();
        return letters.Count < 3 || letters.Count(l => (l.FontName ?? "").Contains("Italic", StringComparison.OrdinalIgnoreCase)) * 2 >= letters.Count;
    }

    /// <summary>
    /// Regroupe les tracés proches en schémas. Une zone pleine de mots est un tableau : son texte reste du texte.
    /// La zone capturée inclut les étiquettes posées autour du schéma (i, u, R1, A, B…).
    /// </summary>
    private static List<PdfRectangle> VectorFigures(Page page, List<PdfRectangle> shapes, List<Word> words, List<PdfRectangle> imageRegions)
    {
        var clusters = shapes.Select(r => (Box: r, Count: 1, Solid: r.Width > 2 && r.Height > 2 ? 1 : 0)).ToList();

        for (var merged = true; merged;)
        {
            merged = false;
            for (var i = 0; i < clusters.Count && !merged; i++)
            for (var j = i + 1; j < clusters.Count; j++)
            {
                if (!Near(clusters[i].Box, clusters[j].Box, FigureGap)) continue;
                clusters[i] = (Union(clusters[i].Box, clusters[j].Box), clusters[i].Count + clusters[j].Count, clusters[i].Solid + clusters[j].Solid);
                clusters.RemoveAt(j);
                merged = true;
                break;
            }
        }

        return clusters
            .Where(c => c.Count >= MinFigurePaths && c.Solid >= 1 && c.Box.Width >= MinFigureWidth)
            .Where(c => words.Count(w => Contains(c.Box, w.BoundingBox)) <= MaxWordsInFigure)
            .Where(c => !imageRegions.Any(r => Near(r, c.Box, 0)))
            .Select(c => WithLabels(c.Box, words))
            .ToList();
    }

    private static PdfRectangle WithLabels(PdfRectangle box, List<Word> words)
    {
        var result = box;
        foreach (var word in words.Where(w => IsLabel(w) && Near(box, w.BoundingBox, LabelMargin) && StandsAlone(w, words)))
            result = Union(result, word.BoundingBox);

        // Une légende courte collée au schéma (« E1 + E2 », « U = k.i' ou kv' ») en fait partie
        foreach (var row in GroupLines(words.Where(w => Near(box, w.BoundingBox, LabelMargin)).ToList()))
        {
            var chars = row.Words.Sum(w => w.Text.Trim().Length);
            if (chars <= MaxCaptionLength && row.Words.All(w => IsEquationPart(w) || w.Text.Trim() is "ou" or "et") && IsWholeRow(row, words))
                result = row.Words.Aggregate(result, (current, w) => Union(current, w.BoundingBox));
        }
        return result;
    }

    /// <summary>La ligne ne se prolonge pas au-delà de la zone du schéma : sinon c'est une phrase.</summary>
    private static bool IsWholeRow(Line row, List<Word> words) =>
        !words.Except(row.Words).Any(o => !string.IsNullOrWhiteSpace(o.Text)
                                          && Math.Abs(Baseline(o) - row.Baseline) <= SameLine
                                          && row.Words.Any(w => o.BoundingBox.Left - 20 <= w.BoundingBox.Right && w.BoundingBox.Left - 20 <= o.BoundingBox.Right));

    /// <summary>Une étiquette est seule sur sa ligne : le « 0 » de « a = 0 » appartient à la phrase, pas au schéma voisin.</summary>
    private static bool StandsAlone(Word word, List<Word> words) =>
        !words.Any(o => o != word && !IsLabel(o) && !string.IsNullOrWhiteSpace(o.Text)
                        && Math.Abs(Baseline(o) - Baseline(word)) <= SameLine
                        && o.BoundingBox.Left - 30 <= word.BoundingBox.Right && word.BoundingBox.Left - 30 <= o.BoundingBox.Right);

    private static bool IsLabel(Word word)
    {
        var text = word.Text;
        return text.Count(char.IsLetterOrDigit) is > 0 and <= MaxLabelLength && text.All(c => char.IsLetterOrDigit(c) || c == '\'');
    }

    private static bool IsBeside(PdfRectangle figure, PdfRectangle formula)
    {
        var y = (formula.Top + formula.Bottom) / 2;
        var inside = formula.Left >= figure.Left && formula.Right <= figure.Right;
        var aside = formula.Left >= figure.Right - 2 || formula.Right <= figure.Left + 2;
        return y <= figure.Top && y >= figure.Bottom && Near(figure, formula, LabelMargin) && (inside || aside);
    }

    private List<PdfFigureRef> SaveFigures(List<Candidate> candidates, Guid courseId, string webRoot)
    {
        var folder = FiguresFolder(webRoot, courseId);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);

        // Une image intégrée répétée sur plusieurs pages est un logo ou un filigrane.
        // Un schéma capturé peut légitimement revenir à l'identique : il est toujours conservé.
        var hashed = candidates.Select(c => (Item: c, Hash: Convert.ToHexString(SHA256.HashData(c.Data)))).ToList();
        var repeated = hashed.Where(h => !h.Item.IsCapture)
            .GroupBy(h => h.Hash)
            .Where(g => g.Select(h => h.Item.Page).Distinct().Count() >= 3)
            .Select(g => g.Key)
            .ToHashSet();

        var figures = new List<PdfFigureRef>();
        var seen = new HashSet<string>();
        foreach (var (item, hash) in hashed.Where(h => h.Item.IsCapture || !repeated.Contains(h.Hash)).OrderBy(h => h.Item.Page).ThenBy(h => h.Item.Order))
        {
            if (figures.Count >= MaxFigures) break;
            if (!item.IsCapture && !seen.Add(hash)) continue;

            Directory.CreateDirectory(folder);
            var index = figures.Count + 1;
            var name = $"fig-{index}.{item.Extension}";
            File.WriteAllBytes(Path.Combine(folder, name), item.Data);
            figures.Add(new PdfFigureRef(index, item.Page, item.Top, $"/uploads/cours/{courseId}/{name}", item.IsScan, item.Order));
        }
        return figures;
    }

    private static double TopRatio(Page page, double top) => Math.Clamp(1 - top / Math.Max(page.Height, 1), 0, 1);

    private static bool Contains(PdfRectangle outer, PdfRectangle inner)
    {
        var x = (inner.Left + inner.Right) / 2;
        var y = (inner.Top + inner.Bottom) / 2;
        return x >= outer.Left && x <= outer.Right && y >= outer.Bottom && y <= outer.Top;
    }

    private static bool Near(PdfRectangle a, PdfRectangle b, double gap) =>
        a.Left - gap <= b.Right && b.Left - gap <= a.Right && a.Bottom - gap <= b.Top && b.Bottom - gap <= a.Top;

    private static PdfRectangle Union(PdfRectangle a, PdfRectangle b) =>
        new(Math.Min(a.Left, b.Left), Math.Min(a.Bottom, b.Bottom), Math.Max(a.Right, b.Right), Math.Max(a.Top, b.Top));

    /// <summary>Rend la page une seule fois, à la première capture, puis découpe les zones demandées.</summary>
    private sealed class PageRenderer(byte[] pdf, Page page, ILogger logger) : IDisposable
    {
        private SKBitmap? _bitmap;
        private bool _failed;

        public byte[]? Capture(PdfRectangle? region)
        {
            try
            {
                if (_failed) return null;
                _bitmap ??= Conversion.ToImage(pdf, page: page.Number - 1, options: new RenderOptions(Dpi: RenderDpi));
                var scale = _bitmap.Width / page.Width;

                using var image = SKImage.FromBitmap(_bitmap);
                if (region is not { } r)
                {
                    using var whole = image.Encode(SKEncodedImageFormat.Png, 100);
                    return whole.ToArray();
                }

                const double margin = 2.5;
                var area = new SKRectI(
                    Clamp((r.Left - margin) * scale, _bitmap.Width),
                    Clamp((page.Height - r.Top - margin) * scale, _bitmap.Height),
                    Clamp((r.Right + margin) * scale, _bitmap.Width),
                    Clamp((page.Height - r.Bottom + margin) * scale, _bitmap.Height));
                using var subset = image.Subset(area);
                using var encoded = subset.Encode(SKEncodedImageFormat.Png, 100);
                return encoded.ToArray();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                logger.LogWarning(ex, "Capture impossible page {Page}", page.Number);
                _failed = true;
                return null;
            }
        }

        private static int Clamp(double value, int max) => (int)Math.Clamp(Math.Round(value), 0, max);

        public void Dispose() => _bitmap?.Dispose();
    }
}
