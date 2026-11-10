using GPOE26.ApiService.Model;
using Microsoft.EntityFrameworkCore;

namespace GPOE26.ApiService.Data;

public static class ApiSeeder
{
    /// <summary>Formules de départ : les prix se modifient ensuite directement en base.</summary>
    public static async Task SeedPlansAsync(ApiServiceContext db)
    {
        if (await db.SubscriptionPlans.AnyAsync()) return;

        PlanFeature F(string code, string label, int? limit = null) => new() { Code = code, Label = label, Limit = limit };
        PlanFeature Courses() => F("courses-shared", "Cours partagés et évaluations");
        PlanFeature Schedule() => F("schedule", "Emploi du temps");
        PlanFeature Quiz() => F("quiz-ai", "Quiz générés par l'IA");
        PlanFeature Tutor() => F("tutor-ai", "Tuteur IA avec mémoire");
        PlanFeature Mock() => F("mock-exams", "Épreuves blanches");
        PlanFeature Feedback() => F("ai-feedback", "Retour de l'IA sur les copies");
        PlanFeature Progress() => F("detailed-progress", "Suivi détaillé de la progression");
        PlanFeature Family() => F("family-view", "Vue parent");
        PlanFeature Assistant() => F("teacher-agent", "Assistant IA de l'enseignant");
        PlanFeature Grading() => F("ai-grading", "Aide à la correction");

        db.SubscriptionPlans.AddRange(
            new SubscriptionPlan
            {
                Code = "free", Name = "Gratuit", Category = "Tous", DisplayOrder = 0, PriceMonthly = 0, PriceYearly = 0,
                Description = "Pour découvrir la plateforme.",
                Features = [Courses(), Schedule(), F("quiz-ai", "Quiz générés par l'IA", 3)]
            },
            new SubscriptionPlan
            {
                Code = "student-premium", Name = "Élève Premium", Category = "Élève", DisplayOrder = 1, PriceMonthly = 2000, PriceYearly = 20000,
                Description = "Un tuteur personnel qui apprend à te connaître.",
                Features = [Courses(), Schedule(), Quiz(), Tutor(), Mock(), Feedback(), Progress()]
            },
            new SubscriptionPlan
            {
                Code = "family", Name = "Famille", Category = "Famille", DisplayOrder = 2, PriceMonthly = 5000, PriceYearly = 50000, Seats = 3,
                Description = "Premium pour vos enfants, et leur suivi.",
                Features = [Courses(), Schedule(), Quiz(), Tutor(), Mock(), Feedback(), Progress(), Family()]
            },
            new SubscriptionPlan
            {
                Code = "teacher", Name = "Enseignant", Category = "Enseignant", DisplayOrder = 3, PriceMonthly = 3500, PriceYearly = 35000,
                Description = "Créez vos cours et suivez vos classes avec votre assistant.",
                Features = [Courses(), Schedule(), Quiz(), Assistant(), Grading()]
            },
            new SubscriptionPlan
            {
                Code = "organization-license", Name = "Licence établissement", Category = "Établissement", DisplayOrder = 4, Seats = 30,
                Description = "Pour un établissement ou une institution.",
                Features = [Courses(), Schedule(), Quiz(), Tutor(), Mock(), Feedback(), Progress(), Assistant(), Grading()]
            });
        await db.SaveChangesAsync();
    }

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

        // Base neuve : le site est présenté complet (équipe, actualités, événements, activités fictifs)
        await DemoContent.AddMissingAsync(db);
    }
}
