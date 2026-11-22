using Cours.Model;
using Cours.Service;

namespace GPOE26.Logic.Tests;

/// <summary>Bibliothèque d'établissement côté Cours (docs/PLAN-ETABLISSEMENT-2.md, lot 8).</summary>
public class CourseLibraryTests
{
    private readonly Guid _author = Guid.NewGuid();
    private readonly Guid _colleague = Guid.NewGuid();
    private readonly Guid _school = Guid.NewGuid();

    private LibrarySpaceRef Space(params Guid[] teachers) =>
        new(_school, "Lycée", false, teachers.Select(t => new LibraryAuthorRef(t, "prof")).ToList());

    private Course Deposited() => new()
    {
        Title = "Les suites",
        Subject = "Mathématiques",
        OwnerId = _author,
        LibraryOrganizationIds = [_school],
    };

    [Fact]
    public void Un_depot_est_visible_tant_que_son_auteur_enseigne_dans_l_etablissement()
    {
        Assert.True(CourseLibrary.IsVisibleIn(Deposited(), Space(_author, _colleague)));
        // L'auteur parti garde son cours ; il quitte la bibliothèque avec lui (P2)
        Assert.False(CourseLibrary.IsVisibleIn(Deposited(), Space(_colleague)));
    }

    [Fact]
    public void Un_cours_non_depose_n_est_pas_visible()
    {
        var course = Deposited();
        course.LibraryOrganizationIds = [Guid.NewGuid()];
        Assert.False(CourseLibrary.IsVisibleIn(course, Space(_author, _colleague)));
    }

    [Fact]
    public void La_copie_est_un_brouillon_au_copieur_sans_partage_ni_depot()
    {
        var source = Deposited();
        source.Status = CourseStatus.Published;
        source.ClassroomIds = [Guid.NewGuid()];
        source.PublishedAt = DateTime.UtcNow;
        var copyId = Guid.NewGuid();

        var copy = CourseLibrary.Copy(source, copyId, _colleague, "uploads/cours/copie.pdf");

        Assert.Equal(copyId, copy.Id);
        Assert.Equal(_colleague, copy.OwnerId);
        Assert.Equal(CourseStatus.Draft, copy.Status);
        Assert.Empty(copy.ClassroomIds);
        Assert.Empty(copy.LibraryOrganizationIds);
        Assert.Null(copy.PublishedAt);
        Assert.Equal(source.Id, copy.SourceCourseId);
        Assert.Equal("uploads/cours/copie.pdf", copy.PdfPath);
        Assert.Equal((source.Title, source.Subject), (copy.Title, copy.Subject));
    }

    [Fact]
    public void La_copie_a_ses_propres_sections_et_ses_propres_images()
    {
        var source = Deposited();
        source.Sections =
        [
            new() { CourseId = source.Id, Type = SectionType.Paragraph, Order = 1, Content = $"Voir ![figure](/uploads/cours/{source.Id}/fig-1.png)" },
            new() { CourseId = source.Id, Type = SectionType.Heading, Order = 0, Level = 1, Content = "Définition" },
            new() { CourseId = source.Id, Type = SectionType.Image, Order = 2, Content = $"/uploads/cours/{source.Id}/fig-2.png" },
        ];
        source.ExtractedText = $"Définition /uploads/cours/{source.Id}/fig-1.png";
        var copyId = Guid.NewGuid();

        var copy = CourseLibrary.Copy(source, copyId, _colleague, null);

        Assert.Equal(["Définition", $"Voir ![figure](/uploads/cours/{copyId}/fig-1.png)", $"/uploads/cours/{copyId}/fig-2.png"],
            copy.Sections.Select(s => s.Content));
        Assert.Equal([0, 1, 2], copy.Sections.Select(s => s.Order));
        Assert.All(copy.Sections, s => Assert.Equal(copyId, s.CourseId));
        Assert.DoesNotContain(copy.Sections, s => source.Sections.Any(o => o.Id == s.Id));
        Assert.DoesNotContain(source.Id.ToString(), copy.ExtractedText);

        // Modifier la copie ne touche pas l'original (P2)
        copy.Sections[0].Content = "Autre";
        Assert.Equal("Définition", source.Sections.Single(s => s.Order == 0).Content);
    }

    [Fact]
    public void Les_fichiers_sont_recopies_sous_d_autres_noms()
    {
        var root = Path.Combine(Path.GetTempPath(), "gpoe-lib-" + Guid.NewGuid());
        try
        {
            var source = Deposited();
            Directory.CreateDirectory(Path.Combine(root, "uploads", "cours"));
            File.WriteAllText(Path.Combine(root, "uploads", "cours", "original.pdf"), "%PDF");
            source.PdfPath = "uploads/cours/original.pdf";
            var figures = PdfStructuringService.FiguresFolder(root, source.Id);
            Directory.CreateDirectory(figures);
            File.WriteAllText(Path.Combine(figures, "fig-1.png"), "png");
            var copyId = Guid.NewGuid();

            var pdf = CourseLibrary.CopyFiles(source, copyId, root);

            Assert.NotNull(pdf);
            Assert.NotEqual(source.PdfPath, pdf);
            Assert.True(File.Exists(Path.Combine(root, pdf)));
            Assert.True(File.Exists(Path.Combine(PdfStructuringService.FiguresFolder(root, copyId), "fig-1.png")));

            // L'original supprimé, la copie garde ses fichiers
            File.Delete(Path.Combine(root, source.PdfPath));
            Directory.Delete(figures, recursive: true);
            Assert.True(File.Exists(Path.Combine(root, pdf)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Un_cours_sans_fichier_n_en_recopie_aucun()
    {
        var root = Path.Combine(Path.GetTempPath(), "gpoe-lib-" + Guid.NewGuid());
        var copyId = Guid.NewGuid();
        Assert.Null(CourseLibrary.CopyFiles(Deposited(), copyId, root));
        Assert.False(Directory.Exists(PdfStructuringService.FiguresFolder(root, copyId)));
    }
}
