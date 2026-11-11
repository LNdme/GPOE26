using Cours.Model;

namespace Cours.Service;

/// <summary>
/// Règles de la bibliothèque d'établissement (phase 2, lot 8) : ce qu'on peut déposer, qui voit un dépôt,
/// et la copie d'un cours, qui ne doit jamais dépendre de son original (P2).
/// </summary>
public static class CourseLibrary
{
    /// <summary>
    /// Un dépôt n'est visible que si son auteur est encore enseignant actif de l'établissement :
    /// l'auteur qui part garde ses cours, et ils quittent la bibliothèque avec lui.
    /// </summary>
    public static bool IsVisibleIn(Course course, LibrarySpaceRef space) =>
        course.LibraryOrganizationIds.Contains(space.Id) && space.IsTeacher(course.OwnerId);

    /// <summary>
    /// Copie d'un cours pour un nouvel auteur : brouillon, partagé avec personne, déposé nulle part,
    /// relié à son original. Les adresses d'images de l'original sont réécrites vers le dossier de la copie
    /// (les fichiers eux-mêmes sont recopiés à part, par <see cref="CopyFiles"/>).
    /// </summary>
    public static Course Copy(Course source, Guid copyId, Guid newOwnerId, string? newPdfPath)
    {
        var copy = new Course
        {
            Id = copyId,
            Title = source.Title,
            Subject = source.Subject,
            Description = source.Description,
            ContentType = source.ContentType,
            PdfPath = newPdfPath,
            StructuringStatus = source.StructuringStatus,
            SourceUrl = source.SourceUrl,
            OwnerId = newOwnerId,
            Status = CourseStatus.Draft,
            SourceCourseId = source.Id,
        };
        copy.ExtractedText = RewriteImages(source.ExtractedText, source.Id, copy.Id);
        copy.Sections = source.Sections
            .OrderBy(s => s.Order)
            .Select(s => new CourseSection
            {
                CourseId = copy.Id,
                Type = s.Type,
                Content = RewriteImages(s.Content, source.Id, copy.Id) ?? "",
                Order = s.Order,
                Level = s.Level,
            })
            .ToList();
        return copy;
    }

    /// <summary>« /uploads/cours/{ancien}/fig-1.png » devient « /uploads/cours/{nouveau}/fig-1.png ».</summary>
    public static string? RewriteImages(string? text, Guid fromId, Guid toId) =>
        text?.Replace($"/uploads/cours/{fromId}/", $"/uploads/cours/{toId}/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Recopie le PDF (sous un nouveau nom) et le dossier d'images d'un cours. Renvoie le chemin relatif
    /// du nouveau PDF, ou null si l'original n'en a pas (ou si le fichier a disparu du disque).
    /// </summary>
    public static string? CopyFiles(Course source, Guid copyId, string webRoot)
    {
        string? pdfPath = null;
        if (source.PdfPath is not null)
        {
            var from = Path.Combine(webRoot, source.PdfPath);
            if (File.Exists(from))
            {
                var fileName = $"{Guid.NewGuid()}.pdf";
                var folder = Path.Combine(webRoot, "uploads", "cours");
                Directory.CreateDirectory(folder);
                File.Copy(from, Path.Combine(folder, fileName));
                pdfPath = Path.Combine("uploads", "cours", fileName).Replace("\\", "/");
            }
        }

        var figures = PdfStructuringService.FiguresFolder(webRoot, source.Id);
        if (Directory.Exists(figures))
        {
            var target = PdfStructuringService.FiguresFolder(webRoot, copyId);
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(figures))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }
        return pdfPath;
    }
}
