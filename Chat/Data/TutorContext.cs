using Chat.Model;
using Microsoft.EntityFrameworkCore;

namespace Chat.Data;

public class TutorContext(DbContextOptions<TutorContext> options) : DbContext(options)
{
    public DbSet<TutorConversation> Conversations => Set<TutorConversation>();
    public DbSet<TutorMessage> Messages => Set<TutorMessage>();
    public DbSet<TutorImage> Images => Set<TutorImage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TutorConversation>(e =>
        {
            e.ToTable("TutorConversations");
            e.Property(c => c.Id).ValueGeneratedNever();
            e.HasIndex(c => new { c.UserId, c.CourseId }).IsUnique();
            e.HasMany(c => c.Messages).WithOne(m => m.Conversation).HasForeignKey(m => m.ConversationId);
        });
        modelBuilder.Entity<TutorMessage>(e =>
        {
            e.ToTable("TutorMessages");
            e.Property(m => m.Id).ValueGeneratedNever();
            e.HasIndex(m => new { m.ConversationId, m.CreatedAt });
        });
        modelBuilder.Entity<TutorImage>(e =>
        {
            e.ToTable("TutorImages");
            e.Property(i => i.Id).ValueGeneratedNever();
        });
    }
}

/// <summary>Création idempotente du schéma au démarrage (pas de migrations EF, comme les autres services).</summary>
public static class TutorSchema
{
    private static readonly string[] Statements =
    {
        @"CREATE TABLE IF NOT EXISTS ""TutorConversations"" (
            ""Id"" uuid NOT NULL,
            ""UserId"" uuid NOT NULL,
            ""CourseId"" uuid NOT NULL,
            ""CourseTitle"" text NOT NULL,
            ""Subject"" text,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            ""UpdatedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_TutorConversations"" PRIMARY KEY (""Id"")
        )",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TutorConversations_UserId_CourseId"" ON ""TutorConversations"" (""UserId"", ""CourseId"")",
        @"CREATE TABLE IF NOT EXISTS ""TutorImages"" (
            ""Id"" uuid NOT NULL,
            ""UserId"" uuid NOT NULL,
            ""ContentType"" text NOT NULL,
            ""Data"" bytea NOT NULL,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_TutorImages"" PRIMARY KEY (""Id"")
        )",
        @"CREATE TABLE IF NOT EXISTS ""TutorMessages"" (
            ""Id"" uuid NOT NULL,
            ""ConversationId"" uuid NOT NULL REFERENCES ""TutorConversations"" (""Id"") ON DELETE CASCADE,
            ""Role"" text NOT NULL,
            ""Content"" text NOT NULL,
            ""ImageId"" uuid,
            ""Pinned"" boolean NOT NULL DEFAULT false,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_TutorMessages"" PRIMARY KEY (""Id"")
        )",
        @"CREATE INDEX IF NOT EXISTS ""IX_TutorMessages_ConversationId_CreatedAt"" ON ""TutorMessages"" (""ConversationId"", ""CreatedAt"")",
    };

    public static async Task EnsureAsync(TutorContext db)
    {
        foreach (var sql in Statements)
            await db.Database.ExecuteSqlRawAsync(sql);
    }
}
