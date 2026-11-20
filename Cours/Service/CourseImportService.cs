using System.Text.RegularExpressions;
using Cours.Data;
using Cours.Model;

namespace Cours.Service;

/// <summary>Crée un cours à partir d'un lien ou de photos : l'utilisateur n'a rien d'autre à fournir.</summary>
public partial class CourseImportService(PdfStructuringService pdf, ILogger<CourseImportService> logger)
{
    private const long MaxDocumentBytes = 10 * 1024 * 1024;
    private const long MaxImageBytes = 4 * 1024 * 1024;
    private const long MaxPhotoBytes = 8 * 1024 * 1024;
    public const int MaxPhotos = 30;
    private const int MinimumLetters = 200;

    public async Task<Course> ImportLinkAsync(CoursContext db, Guid ownerId, string? url, string webRoot, bool queueForAi, CancellationToken ct)
    {
        var requested = SafeWebFetcher.ValidateUrl(url);
        var target = SafeWebFetcher.NormalizeShareLink(requested);
        var resource = await SafeWebFetcher.FetchAsync(target, MaxDocumentBytes, ct);

        var isPdf = resource.MediaType == "application/pdf" || HasSignature(resource.Data, "%PDF"u8);
        if (!isPdf && SafeWebFetcher.IsGoogleHost(resource.FinalUri) && target != requested)
            throw new CourseImportException("Ce document Google n'est pas accessible par lien. Partagez-le avec « Tous les utilisateurs disposant du lien », ou importez le PDF.");
        if (!isPdf && resource.MediaType is not ("text/html" or "application/xhtml+xml" or ""))
            throw new CourseImportException("Ce lien ne mène ni à une page web ni à un PDF. Pour un autre format, exportez-le d'abord en PDF.");

        var course = new Course { Title = "Cours importé", Subject = Course.UnclassifiedSubject, OwnerId = ownerId, SourceUrl = requested.AbsoluteUri };
        db.Courses.Add(course);

        if (isPdf)
            ImportPdf(db, course, resource, webRoot, queueForAi);
        else
            await ImportPageAsync(db, course, resource, webRoot, queueForAi, ct);

        return course;
    }

    private void ImportPdf(CoursContext db, Course course, FetchedResource resource, string webRoot, bool queueForAi)
    {
        try
        {
            pdf.Import(db, course, resource.Data, webRoot, queueForAi);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not CourseImportException)
        {
            logger.LogWarning(ex, "PDF illisible");
            throw new CourseImportException("Ce PDF n'a pas pu être lu. Vérifiez qu'il n'est ni protégé ni endommagé.");
        }

        var folder = Path.Combine(webRoot, "uploads", "cours");
        Directory.CreateDirectory(folder);
        var name = $"{Guid.NewGuid()}.pdf";
        File.WriteAllBytes(Path.Combine(folder, name), resource.Data);

        course.PdfPath = $"uploads/cours/{name}";
        course.Title = Limit(TitleFromFile(resource.FileName ?? Path.GetFileName(resource.FinalUri.AbsolutePath)), 200);
    }

    private async Task ImportPageAsync(CoursContext db, Course course, FetchedResource resource, string webRoot, bool queueForAi, CancellationToken ct)
    {
        using var html = new MemoryStream(resource.Data);
        var page = HtmlCourseExtractor.Extract(html, resource.FinalUri);

        if (page.Markdown.Count(char.IsLetter) < MinimumLetters)
            throw new CourseImportException("Cette page ne contient pas assez de texte lisible (elle est peut-être générée au chargement). Essayez d'importer le cours en PDF.");

        var markdown = await DownloadImagesAsync(page, course.Id, webRoot, ct);

        db.CourseSections.Add(new CourseSection { CourseId = course.Id, Type = SectionType.Paragraph, Content = markdown, Order = 0 });
        course.ContentType = ContentType.Text;
        course.Title = Limit(page.Title.Length > 0 ? page.Title : "Cours importé", 200);
        course.StructuringStatus = queueForAi ? PdfStructuringService.StatusPending : PdfStructuringService.StatusDone;
        course.RebuildExtractedText();
        course.ExtractedText ??= markdown;
    }

    /// <summary>Les images sont copiées : le cours reste lisible si le site d'origine change ou disparaît.</summary>
    private async Task<string> DownloadImagesAsync(HtmlCoursePage page, Guid courseId, string webRoot, CancellationToken ct)
    {
        var folder = PdfStructuringService.FiguresFolder(webRoot, courseId);
        var results = new string?[page.Images.Count];
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(60));

        try
        {
            await Parallel.ForEachAsync(Enumerable.Range(0, page.Images.Count), new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = budget.Token }, async (index, token) =>
            {
                var image = page.Images[index];
                try
                {
                    var fetched = await SafeWebFetcher.FetchAsync(SafeWebFetcher.ValidateUrl(image.Url.AbsoluteUri), MaxImageBytes, token);
                    if (ImageExtension(fetched.Data) is not { } extension) return;

                    Directory.CreateDirectory(folder);
                    var name = $"fig-{index + 1}.{extension}";
                    await File.WriteAllBytesAsync(Path.Combine(folder, name), fetched.Data, token);
                    results[index] = $"![{EscapeAlt(image.Alt)}](/uploads/cours/{courseId}/{name})";
                }
                catch (CourseImportException ex)
                {
                    logger.LogDebug("Image ignorée : {Reason}", ex.Message);
                }
            });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogInformation("Temps alloué aux images écoulé : les suivantes sont ignorées");
        }

        var markdown = page.Markdown;
        for (var i = 0; i < page.Images.Count; i++)
            markdown = markdown.Replace(page.Images[i].Token, results[i] ?? "");
        return BlankLines().Replace(markdown, "\n\n").Trim();
    }

    /// <summary>Chaque photo devient une page à retranscrire ; sans IA, elle reste affichée telle quelle.</summary>
    public async Task<Course> ImportPhotosAsync(CoursContext db, Guid ownerId, IFormFileCollection files, string webRoot, bool queueForAi, CancellationToken ct)
    {
        if (files.Count == 0) throw new CourseImportException("Ajoutez au moins une photo du cours.");
        if (files.Count > MaxPhotos) throw new CourseImportException($"{MaxPhotos} photos au maximum par cours.");

        var course = new Course
        {
            Title = $"{Course.PhotosTitlePrefix} du {DateTime.UtcNow:dd/MM/yyyy}",
            Subject = Course.UnclassifiedSubject,
            OwnerId = ownerId,
            ContentType = ContentType.Text,
            StructuringStatus = queueForAi ? PdfStructuringService.StatusPending : PdfStructuringService.StatusDone
        };

        var folder = PdfStructuringService.FiguresFolder(webRoot, course.Id);
        var figures = new List<PdfFigureRef>();
        foreach (var file in files)
        {
            if (file.Length > MaxPhotoBytes) throw new CourseImportException("Chaque photo doit faire moins de 8 Mo.");

            using var memory = new MemoryStream();
            await file.CopyToAsync(memory, ct);
            var data = memory.ToArray();
            var extension = ImageExtension(data);
            if (extension is not ("jpg" or "png"))
                throw new CourseImportException("Formats acceptés pour les photos : JPEG ou PNG.");

            Directory.CreateDirectory(folder);
            var index = figures.Count + 1;
            var name = $"fig-{index}.{extension}";
            await File.WriteAllBytesAsync(Path.Combine(folder, name), data, ct);
            figures.Add(new PdfFigureRef(index, index, 0, $"/uploads/cours/{course.Id}/{name}", IsScan: true));
        }

        db.Courses.Add(course);
        db.CourseSections.Add(new CourseSection
        {
            CourseId = course.Id,
            Type = SectionType.Paragraph,
            Content = string.Join("\n\n", figures.Select(PdfMarkdownBuilder.FigureMarkdown)),
            Order = 0
        });
        return course;
    }

    private static bool HasSignature(byte[] data, ReadOnlySpan<byte> signature) => data.AsSpan().StartsWith(signature);

    /// <summary>Le type est lu dans le fichier lui-même : ni l'extension ni l'en-tête annoncé ne font foi (pas de SVG, qui peut porter du script).</summary>
    public static string? ImageExtension(byte[] data)
    {
        if (HasSignature(data, [0xFF, 0xD8, 0xFF])) return "jpg";
        if (HasSignature(data, [0x89, 0x50, 0x4E, 0x47])) return "png";
        if (HasSignature(data, "GIF8"u8)) return "gif";
        if (data.Length > 12 && HasSignature(data, "RIFF"u8) && data.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return "webp";
        return null;
    }

    public static string TitleFromFile(string? name)
    {
        var stem = Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(name ?? "")).Replace('_', ' ').Replace('-', ' ').Trim();
        stem = Spaces().Replace(stem, " ");
        return stem.Length == 0 || LooksGenerated().IsMatch(stem) ? "Document importé" : stem;
    }

    private static string EscapeAlt(string alt) => alt.Replace("[", "").Replace("]", "").Replace("\n", " ");

    private static string Limit(string text, int max) => text.Length > max ? text[..max].TrimEnd() : text;

    [GeneratedRegex(@"\n{3,}")] private static partial Regex BlankLines();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"^(?:[0-9a-fA-F ]{16,}|uc|export|download|view)$", RegexOptions.IgnoreCase)] private static partial Regex LooksGenerated();
}
