using GPOE26.ApiService.Model;
using Microsoft.EntityFrameworkCore;

namespace GPOE26.ApiService.Data;

public static class ApiSeeder
{
    public static async Task SeedAsync(ApiServiceContext db)
    {
        if (await db.Contacts.AnyAsync())
            return; // DB already seeded (Contacts is a good indicator)

        // Contact Info
        db.Contacts.Add(new Contact
        {
            Id = 1,
            Name = "Lycée Technique de Nanga-Eboko",
            Address = "BP 42, Nanga-Eboko",
            Email = "contact@lt.nangaeboko.cm",
            Phone = "+237 600 00 00 00",
            City = "Nanga-Eboko, Cameroun"
        });

        // Hierarchy
        var hr = new List<Hierarchy>
        {
            new Hierarchy
            {
                Role = "Proviseur",
                Name = "M. DONGMO",
                PreName = "Jean",
                Department = "Direction",
                Email = "direction@lt.nangaeboko.cm"
            },
            new Hierarchy
            {
                Role = "Censeur",
                Name = "Mme. ABOMO",
                PreName = "Marie",
                Department = "Pédagogie",
                Email = "pedagogie@lt.nangaeboko.cm"
            }
        };
        db.Hierarchies.AddRange(hr);

        // Events
        var events = new List<SchoolEvent>
        {
            new SchoolEvent
            {
                Id = Guid.NewGuid(),
                Title = "Journées Portes Ouvertes 2026",
                Description = "Venez découvrir nos ateliers techniques professionnels et échanger avec les professeurs.",
                Location = "Campus Principal, LT Nanga-Eboko",
                StartDate = DateTime.UtcNow.AddDays(15),
                EndDate = DateTime.UtcNow.AddDays(17),
                Type = "Public",
                IsPublic = true,
                CreatedAt = DateTime.UtcNow
            }
        };
        db.Events.AddRange(events);

        // News
        var news = new List<NewArticle>
        {
            new NewArticle
            {
                Id = Guid.NewGuid(),
                Title = "Bienvenue sur la nouvelle plateforme GPOE26",
                Slug = "bienvenue-gpoe26",
                Excerpt = "Découvrez la plateforme numérique dédiée à nos apprentissages.",
                Content = "Notre établissement modernise ses outils pédagogiques. La plateforme permet l'accès aux cours, activités, et IA de tutorat. Bienvenue à tous nos élèves et enseignants !",
                Category = "Annonce",
                IsPublished = true,
                PublishedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        };
        db.NewsArticles.AddRange(news);

        await db.SaveChangesAsync();
    }
}
