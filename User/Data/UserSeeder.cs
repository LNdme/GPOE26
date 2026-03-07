using Microsoft.EntityFrameworkCore;
using User.Model;

namespace User.Data;

public static class UserSeeder
{
    public static readonly Guid AdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid TeacherId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid StudentId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static async Task SeedAsync(UserContext db)
    {
        if (await db.AppUsers.AnyAsync())
            return; // DB already seeded

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
}
