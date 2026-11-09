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
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

#pragma warning disable CA1416 // Rendu PDFium : Windows et Linux, les plateformes de déploiement

namespace Cours.Service;

public record PdfStructure(string Markdown, string RawText, int Figures);

/// <summary>
/// Lit un PDF comme un lecteur : ordre de lecture, titres, gras et images.
/// Les images intégrées sont extraites telles quelles ; les schémas vectoriels et les pages scannées sont capturés.
/// </summary>
public class PdfStructuringService(ILogger<PdfStructuringService> logger)
{
    private const int MaxFigures = 80;
    private const int RenderDpi = 200;
    private const double MinImageSize = 50;
    private const int MinScannedChars = 20;

    public const string StatusPending = "Pending";
    public const string StatusDone = "Done";
    public const string StatusFailed = "Failed";

    private record Candidate(int Page, double Top, byte[] Data, string Extension, bool IsScan);

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

        foreach (var page in document.GetPages())
        {
            raw.AppendLine(page.Text);
            var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters).ToList();

            // Page scannée : aucun texte exploitable, on garde son image entière
            if (page.Text.Trim().Length < MinScannedChars)
            {
                if (Render(pdf, page, null) is { } scan)
                    candidates.Add(new Candidate(page.Number, 0, scan, "png", IsScan: true));
                continue;
            }

            var regions = new List<PdfRectangle>();
            foreach (var image in page.GetImages())
            {
                if (image.Bounds.Width < MinImageSize || image.Bounds.Height < MinImageSize) continue;
                var data = ReadImage(image, pdf, page, out var extension);
                if (data is null) continue;
                candidates.Add(new Candidate(page.Number, TopRatio(page, image.Bounds.Top), data, extension, IsScan: false));
                regions.Add(image.Bounds);
            }

            foreach (var region in VectorFigures(page, words, regions))
            {
                if (Render(pdf, page, region) is not { } capture) continue;
                candidates.Add(new Candidate(page.Number, TopRatio(page, region.Top), capture, "png", IsScan: false));
                regions.Add(region);
            }

            // Le texte contenu dans une figure capturée n'est pas répété dans le cours
            var textWords = words.Where(w => !regions.Any(r => Contains(r, w.BoundingBox))).ToList();
            blocks.AddRange(ReadBlocks(page, textWords));
        }

        var figures = SaveFigures(candidates, courseId, webRoot);
        return new PdfStructure(PdfMarkdownBuilder.Build(blocks, figures, pageCount), raw.ToString().Trim(), figures.Count);
    }

    private static IEnumerable<PdfBlock> ReadBlocks(Page page, List<Word> words)
    {
        if (words.Count == 0) yield break;
        var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
        foreach (var block in UnsupervisedReadingOrderDetector.Instance.Get(blocks).OrderBy(b => b.ReadingOrder))
        {
            var letters = block.TextLines.SelectMany(l => l.Words).SelectMany(w => w.Letters).ToList();
            if (letters.Count == 0) continue;

            var size = letters.GroupBy(l => Math.Round(l.PointSize, 1)).OrderByDescending(g => g.Count()).First().Key;
            var bold = letters.Count(IsBold) >= letters.Count * 0.8;
            var text = PdfMarkdownBuilder.JoinLines(block.TextLines.Select(l => string.Join(" ", l.Words.Select(w => w.Text))));
            var inline = PdfMarkdownBuilder.JoinLines(block.TextLines.Select(InlineLine));

            yield return new PdfBlock(page.Number, TopRatio(page, block.BoundingBox.Top), size, bold, text, bold ? $"**{PdfMarkdownBuilder.Escape(text)}**" : inline, block.TextLines.Count);
        }
    }

    /// <summary>Une ligne de texte, avec les suites de mots en gras entourées de **.</summary>
    private static string InlineLine(UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine line)
    {
        var sb = new StringBuilder();
        var inBold = false;
        foreach (var word in line.Words)
        {
            var bold = word.Letters.Count(IsBold) * 2 > word.Letters.Count && word.Text.Any(char.IsLetterOrDigit);
            if (bold != inBold)
            {
                if (inBold) { if (sb.Length > 0 && sb[^1] == ' ') sb.Length--; sb.Append("** "); }
                else sb.Append("**");
                inBold = bold;
            }
            sb.Append(PdfMarkdownBuilder.Escape(word.Text)).Append(' ');
        }
        if (inBold) { if (sb.Length > 0 && sb[^1] == ' ') sb.Length--; sb.Append("**"); }
        return sb.ToString().TrimEnd();
    }

    private static bool IsBold(Letter letter)
    {
        var name = letter.FontName ?? "";
        return name.Contains("bold", StringComparison.OrdinalIgnoreCase)
            || name.Contains("black", StringComparison.OrdinalIgnoreCase)
            || name.Contains("heavy", StringComparison.OrdinalIgnoreCase);
    }

    private byte[]? ReadImage(IPdfImage image, byte[] pdf, Page page, out string extension)
    {
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
        return Render(pdf, page, image.Bounds);
    }

    /// <summary>
    /// Regroupe les tracés proches en zones : un schéma vectoriel n'a pas d'image à extraire, il faut le capturer.
    /// Une zone pleine de mots est un tableau : son texte reste du texte.
    /// </summary>
    private static List<PdfRectangle> VectorFigures(Page page, List<Word> words, List<PdfRectangle> imageRegions)
    {
        var rects = page.ExperimentalAccess.Paths
            .Select(p => p.GetBoundingRectangle())
            .Where(r => r is not null)
            .Select(r => r!.Value)
            .Where(r => r.Width < page.Width * 0.95 && r.Height < page.Height * 0.95 && (r.Width > 1 || r.Height > 1))
            .Take(3000)
            .Select(r => (Box: r, Count: 1))
            .ToList();

        const double gap = 12;
        for (var merged = true; merged;)
        {
            merged = false;
            for (var i = 0; i < rects.Count && !merged; i++)
            for (var j = i + 1; j < rects.Count; j++)
            {
                if (!Near(rects[i].Box, rects[j].Box, gap)) continue;
                rects[i] = (Union(rects[i].Box, rects[j].Box), rects[i].Count + rects[j].Count);
                rects.RemoveAt(j);
                merged = true;
                break;
            }
        }

        return rects
            .Where(c => c.Count >= 6 && c.Box.Width >= 80 && c.Box.Height >= 60)
            .Where(c => words.Count(w => Contains(c.Box, w.BoundingBox)) <= 30)
            .Where(c => !imageRegions.Any(r => Near(r, c.Box, 0)))
            .Select(c => c.Box)
            .ToList();
    }

    private byte[]? Render(byte[] pdf, Page page, PdfRectangle? region)
    {
        try
        {
            using var bitmap = Conversion.ToImage(pdf, page: page.Number - 1, options: new RenderOptions(Dpi: RenderDpi));
            var scale = bitmap.Width / page.Width;
            SKRectI? crop = null;
            if (region is { } r)
            {
                const double margin = 6;
                crop = new SKRectI(
                    Clamp((r.Left - margin) * scale, bitmap.Width),
                    Clamp((page.Height - r.Top - margin) * scale, bitmap.Height),
                    Clamp((r.Right + margin) * scale, bitmap.Width),
                    Clamp((page.Height - r.Bottom + margin) * scale, bitmap.Height));
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var subset = crop is { } area ? image.Subset(area) : image;
            using var encoded = subset.Encode(SKEncodedImageFormat.Png, 100);
            return encoded.ToArray();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogWarning(ex, "Capture impossible page {Page}", page.Number);
            return null;
        }
    }

    private List<PdfFigureRef> SaveFigures(List<Candidate> candidates, Guid courseId, string webRoot)
    {
        var folder = FiguresFolder(webRoot, courseId);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);

        // Une image répétée sur plusieurs pages est un logo ou un filigrane
        var hashed = candidates.Select(c => (Item: c, Hash: Convert.ToHexString(SHA256.HashData(c.Data)))).ToList();
        var repeated = hashed.GroupBy(h => h.Hash)
            .Where(g => g.Select(h => h.Item.Page).Distinct().Count() >= 3)
            .Select(g => g.Key)
            .ToHashSet();

        var figures = new List<PdfFigureRef>();
        var seen = new HashSet<string>();
        foreach (var (item, hash) in hashed.Where(h => h.Item.IsScan || !repeated.Contains(h.Hash)).OrderBy(h => h.Item.Page).ThenBy(h => h.Item.Top))
        {
            if (figures.Count >= MaxFigures) break;
            if (!item.IsScan && !seen.Add(hash)) continue;

            Directory.CreateDirectory(folder);
            var index = figures.Count + 1;
            var name = $"fig-{index}.{item.Extension}";
            File.WriteAllBytes(Path.Combine(folder, name), item.Data);
            figures.Add(new PdfFigureRef(index, item.Page, item.Top, $"/uploads/cours/{courseId}/{name}", item.IsScan));
        }
        return figures;
    }

    private static double TopRatio(Page page, double top) => Math.Clamp(1 - top / Math.Max(page.Height, 1), 0, 1);

    private static int Clamp(double value, int max) => (int)Math.Clamp(Math.Round(value), 0, max);

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
}
