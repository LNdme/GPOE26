using Cours.Model;
using Microsoft.EntityFrameworkCore;

namespace Cours.Data;

public static class CoursSeeder
{
    // The same deterministic ID used in UserSeeder for the Teacher
    public static readonly Guid TeacherId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static async Task SeedAsync(CoursContext db)
    {
        if (await db.Courses.AnyAsync())
            return; // DB already seeded

        var course = new Course
        {
            Id = Guid.NewGuid(),
            Title = "Introduction aux Mathématiques Appliquées",
            Subject = "Mathématiques",
            Description = "Ce cours couvre les concepts fondamentaux des mathématiques pour la physique et l'ingénierie.",
            ContentType = ContentType.Text,
            OwnerId = TeacherId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var sections = new List<CourseSection>
        {
            new CourseSection
            {
                CourseId = course.Id,
                Type = SectionType.Heading,
                Level = 1,
                Order = 0,
                Content = "Chapitre 1 : Les Fonctions Linéaires"
            },
            new CourseSection
            {
                CourseId = course.Id,
                Type = SectionType.Paragraph,
                Level = 0,
                Order = 1,
                Content = "Une fonction linéaire est une fonction polynomiale de degré 1 dont l'ordonnée à l'origine est nulle. Elle s'écrit sous la forme f(x) = ax."
            },
            new CourseSection
            {
                CourseId = course.Id,
                Type = SectionType.Heading,
                Level = 2,
                Order = 2,
                Content = "1.1 Définition et exemples"
            },
            new CourseSection
            {
                CourseId = course.Id,
                Type = SectionType.Paragraph,
                Level = 0,
                Order = 3,
                Content = "Prenons l'exemple de la conversion de kilomètres en mètres. La distance en mètres (m) est une fonction linéaire de la distance en kilomètres (km) : f(km) = 1000 * km. Ici le coefficient directeur a est 1000."
            }
        };

        course.Sections = sections;
        course.RebuildExtractedText();

        db.Courses.Add(course);
        await db.SaveChangesAsync();
    }
}
