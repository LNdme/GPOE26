using Microsoft.EntityFrameworkCore;
using User.Endpoints;
using User.Model;

namespace User.Data;

public static class UserSeeder
{
    public static readonly Guid AdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid TeacherId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid StudentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid ParentId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid DemoSchoolId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    /// <summary>Clé de licence de l'établissement de démonstration (développement uniquement)</summary>
    public const string DemoLicenseKey = "ETB-DEMO-PROF";
    public const string DemoInviteCode = "DEM-TLC";

    public static async Task SeedAsync(UserContext db, bool isDevelopment)
    {
        if (!await db.AppUsers.AnyAsync())
            await SeedUsersAsync(db);

        await EnsureLinkCodesAsync(db);

        // Comptes Admin/Parent, classe et licence de démonstration : jamais en production,
        // car leurs mots de passe et la clé de licence sont publics (dans ce fichier).
        if (isDevelopment)
            await SeedDemoAsync(db);
    }

    private static async Task SeedUsersAsync(UserContext db)
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword("Test1234!");

        var users = new List<AppUser>
        {
            new AppUser
            {
                Id = AdminId,
                Username = "AdminGPOE",
                Email = "admin@gpoe26.cm",
                PasswordHash = passwordHash,
                Role = UserRole.Teacher,
                Language = "fr",
                CreatedAt = DateTime.UtcNow
            },
            new AppUser
            {
                Id = TeacherId,
                Username = "ProfMaths",
                Email = "prof@gpoe26.cm",
                PasswordHash = passwordHash,
                Role = UserRole.Teacher,
                Specialite = "Mathématiques",
                Language = "fr",
                CreatedAt = DateTime.UtcNow
            },
            new AppUser
            {
                Id = StudentId,
                Username = "EleveTest",
                Email = "eleve@gpoe26.cm",
                PasswordHash = passwordHash,
                Role = UserRole.Student,
                Filiere = "Sciences",
                Level = "Terminale",
                Language = "fr",
                CreatedAt = DateTime.UtcNow
            }
        };

        db.AppUsers.AddRange(users);
        await db.SaveChangesAsync();
    }

    /// <summary>Les élèves inscrits avant la phase 1 n'ont pas encore de code de rattachement parent.</summary>
    private static async Task EnsureLinkCodesAsync(UserContext db)
    {
        var students = await db.AppUsers
            .Where(u => u.Role == UserRole.Student && u.LinkCode == null)
            .ToListAsync();
        foreach (var student in students)
            student.LinkCode = await ParentEndpoints.UniqueLinkCodeAsync(db);
        await db.SaveChangesAsync();
    }

    private static async Task SeedDemoAsync(UserContext db)
    {
        if (await db.Organizations.AnyAsync(o => o.Id == DemoSchoolId))
            return;

        var admin = await db.AppUsers.FindAsync(AdminId);
        if (admin is not null) admin.Role = UserRole.Admin;

        if (!await db.AppUsers.AnyAsync(u => u.Id == ParentId || u.Email == "parent@gpoe26.cm"))
        {
            db.AppUsers.Add(new AppUser
            {
                Id = ParentId,
                Username = "ParentTest",
                Email = "parent@gpoe26.cm",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test1234!"),
                Role = UserRole.Parent
            });
            if (await db.AppUsers.AnyAsync(u => u.Id == StudentId))
                db.ParentChildLinks.Add(new ParentChildLink
                {
                    ParentId = ParentId,
                    ChildId = StudentId,
                    Status = LinkStatus.Active,
                    RespondedAt = DateTime.UtcNow
                });
        }

        db.Organizations.Add(new Organization { Id = DemoSchoolId, Name = "Lycée de démonstration", Type = OrganizationType.School, City = "Douala" });

        var license = new Subscription
        {
            Plan = SubscriptionPlan.OrganizationLicense,
            Period = BillingPeriod.Yearly,
            Status = SubscriptionStatus.Active,
            OrganizationId = DemoSchoolId,
            Seats = 50,
            StartsAt = DateTime.UtcNow,
            EndsAt = DateTime.UtcNow.AddYears(1)
        };
        db.Subscriptions.Add(license);
        db.SubscriptionKeys.Add(new SubscriptionKey { Code = DemoLicenseKey, SubscriptionId = license.Id, MaxUses = license.Seats });

        if (await db.AppUsers.AnyAsync(u => u.Id == TeacherId)
            && !await db.Classrooms.AnyAsync(c => c.InviteCode == DemoInviteCode))
        {
            var classroom = new Classroom
            {
                Name = "Terminale C",
                TeacherId = TeacherId,
                Level = "Terminale",
                SubSystem = SchoolSubSystem.Francophone,
                Subject = "Mathématiques",
                InviteCode = DemoInviteCode
            };
            db.Classrooms.Add(classroom);
            if (await db.AppUsers.AnyAsync(u => u.Id == StudentId))
                db.ClassroomMembers.Add(new ClassroomMember { ClassroomId = classroom.Id, StudentId = StudentId });
        }

        await db.SaveChangesAsync();
    }
}
