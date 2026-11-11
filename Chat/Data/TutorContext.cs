using Chat.Model;
using Microsoft.EntityFrameworkCore;

namespace Chat.Data;

public class TutorContext(DbContextOptions<TutorContext> options) : DbContext(options)
{
    public DbSet<TutorConversation> Conversations => Set<TutorConversation>();
    public DbSet<TutorMessage> Messages => Set<TutorMessage>();
    public DbSet<TutorImage> Images => Set<TutorImage>();
    public DbSet<AgentProfile> AgentProfiles => Set<AgentProfile>();
    public DbSet<AgentMemory> AgentMemories => Set<AgentMemory>();
    public DbSet<CourseMasteryState> CourseMastery => Set<CourseMasteryState>();
    public DbSet<StudySlot> StudySlots => Set<StudySlot>();
    public DbSet<StudySession> StudySessions => Set<StudySession>();
    public DbSet<AiUsage> AiUsage => Set<AiUsage>();
    public DbSet<AssistantMessage> AssistantMessages => Set<AssistantMessage>();

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
        modelBuilder.Entity<AgentProfile>(e =>
        {
            e.ToTable("AgentProfiles");
            e.Property(p => p.Id).ValueGeneratedNever();
        });
        modelBuilder.Entity<AgentMemory>(e =>
        {
            e.ToTable("AgentMemories");
            e.Property(m => m.Id).ValueGeneratedNever();
        });
        modelBuilder.Entity<CourseMasteryState>(e =>
        {
            e.ToTable("CourseMastery");
            e.HasKey(m => new { m.UserId, m.CourseId });
        });
        modelBuilder.Entity<StudySlot>(e =>
        {
            e.ToTable("StudySlots");
            e.Property(s => s.Id).ValueGeneratedNever();
        });
        modelBuilder.Entity<StudySession>(e =>
        {
            e.ToTable("StudySessions");
            e.Property(s => s.Id).ValueGeneratedNever();
        });
        modelBuilder.Entity<AssistantMessage>(e =>
        {
            e.ToTable("AssistantMessages");
            e.Property(m => m.Id).ValueGeneratedNever();
            e.HasIndex(m => new { m.UserId, m.CreatedAt });
        });
        modelBuilder.Entity<AiUsage>(e =>
        {
            e.ToTable("AiUsage");
            e.HasKey(u => new { u.UserId, u.Feature, u.Day });
        });
    }
}

/// <summary>Création idempotente du schéma au démarrage (pas de migrations EF, comme les autres services).</summary>
public static partial class AgentSchema
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

        // Mémoire des agents
        @"CREATE TABLE IF NOT EXISTS ""AgentProfiles"" (
            ""Id"" uuid NOT NULL,
            ""UserId"" uuid NOT NULL,
            ""Kind"" text NOT NULL,
            ""Summary"" text,
            ""Tone"" text,
            ""DetailLevel"" text,
            ""Samples"" text,
            ""MemoryEnabled"" boolean NOT NULL DEFAULT true,
            ""UpdatedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_AgentProfiles"" PRIMARY KEY (""Id"")
        )",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AgentProfiles_UserId_Kind"" ON ""AgentProfiles"" (""UserId"", ""Kind"")",
        @"CREATE TABLE IF NOT EXISTS ""AgentMemories"" (
            ""Id"" uuid NOT NULL,
            ""UserId"" uuid NOT NULL,
            ""Kind"" text NOT NULL,
            ""Type"" text NOT NULL,
            ""Content"" text NOT NULL,
            ""Importance"" integer NOT NULL,
            ""CourseId"" uuid,
            ""SourceMessageId"" uuid,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            ""UpdatedAt"" timestamp with time zone NOT NULL,
            ""Search"" tsvector GENERATED ALWAYS AS (to_tsvector('french', ""Content"")) STORED,
            CONSTRAINT ""PK_AgentMemories"" PRIMARY KEY (""Id"")
        )",
        @"CREATE INDEX IF NOT EXISTS ""IX_AgentMemories_UserId_Kind"" ON ""AgentMemories"" (""UserId"", ""Kind"")",
        @"CREATE INDEX IF NOT EXISTS ""IX_AgentMemories_Search"" ON ""AgentMemories"" USING gin (""Search"")",
        @"CREATE TABLE IF NOT EXISTS ""CourseMastery"" (
            ""UserId"" uuid NOT NULL,
            ""CourseId"" uuid NOT NULL,
            ""CourseTitle"" text NOT NULL,
            ""Subject"" text,
            ""WeakPoints"" text,
            ""LastStudiedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_CourseMastery"" PRIMARY KEY (""UserId"", ""CourseId"")
        )",
        @"CREATE TABLE IF NOT EXISTS ""StudySlots"" (
            ""Id"" uuid NOT NULL,
            ""UserId"" uuid NOT NULL,
            ""Day"" integer NOT NULL,
            ""StartMinutes"" integer NOT NULL,
            ""EndMinutes"" integer NOT NULL,
            ""Kind"" text NOT NULL,
            ""Label"" text,
            CONSTRAINT ""PK_StudySlots"" PRIMARY KEY (""Id"")
        )",
        @"CREATE INDEX IF NOT EXISTS ""IX_StudySlots_UserId"" ON ""StudySlots"" (""UserId"")",
        @"CREATE TABLE IF NOT EXISTS ""StudySessions"" (
            ""Id"" uuid NOT NULL,
            ""UserId"" uuid NOT NULL,
            ""CourseId"" uuid NOT NULL,
            ""Day"" date NOT NULL,
            ""Minutes"" integer NOT NULL,
            ""LastAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_StudySessions"" PRIMARY KEY (""Id"")
        )",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_StudySessions_UserId_CourseId_Day"" ON ""StudySessions"" (""UserId"", ""CourseId"", ""Day"")",
        // Usage de l'IA : quotas mensuels des formules et plafond quotidien
        @"CREATE TABLE IF NOT EXISTS ""AiUsage"" (
            ""UserId"" uuid NOT NULL,
            ""Feature"" text NOT NULL,
            ""Day"" date NOT NULL,
            ""Count"" integer NOT NULL,
            CONSTRAINT ""PK_AiUsage"" PRIMARY KEY (""UserId"", ""Feature"", ""Day"")
        )",
        @"CREATE INDEX IF NOT EXISTS ""IX_TutorMessages_ImageId"" ON ""TutorMessages"" (""ImageId"") WHERE ""ImageId"" IS NOT NULL",
        // Mémoire consolidée par lots. Les discussions existantes sont considérées comme déjà relues (la colonne
        // reçoit la date d'ajout) : seuls les nouveaux échanges seront relus. Ensuite, une nouvelle discussion part de null.
        @"ALTER TABLE ""TutorConversations"" ADD COLUMN IF NOT EXISTS ""ReflectedAt"" timestamp with time zone DEFAULT now()",
        @"ALTER TABLE ""TutorConversations"" ALTER COLUMN ""ReflectedAt"" DROP DEFAULT",
        @"ALTER TABLE ""AgentProfiles"" ADD COLUMN IF NOT EXISTS ""ForgottenAt"" timestamp with time zone",
        // Conversation avec l'assistant enseignant
        @"CREATE TABLE IF NOT EXISTS ""AssistantMessages"" (
            ""Id"" uuid NOT NULL,
            ""UserId"" uuid NOT NULL,
            ""ClassroomId"" uuid,
            ""Role"" text NOT NULL,
            ""Content"" text NOT NULL,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_AssistantMessages"" PRIMARY KEY (""Id"")
        )",
        @"CREATE INDEX IF NOT EXISTS ""IX_AssistantMessages_UserId_CreatedAt"" ON ""AssistantMessages"" (""UserId"", ""CreatedAt"")",
    };

    /// <summary>La recherche vectorielle est un plus : sans l'extension pgvector, le plein texte suffit.</summary>
    public static bool VectorAvailable { get; private set; }

    public static async Task EnsureAsync(TutorContext db)
    {
        foreach (var sql in Statements)
            await db.Database.ExecuteSqlRawAsync(sql);

        try
        {
            await db.Database.ExecuteSqlRawAsync(@"CREATE EXTENSION IF NOT EXISTS vector");
            await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""AgentMemories"" ADD COLUMN IF NOT EXISTS ""Embedding"" vector");
            VectorAvailable = true;
        }
        catch (Npgsql.PostgresException)
        {
            VectorAvailable = false;
        }
    }
}

/// <summary>Nom historique, utilisé au démarrage du service.</summary>
public static class TutorSchema
{
    public static Task EnsureAsync(TutorContext db) => AgentSchema.EnsureAsync(db);
}
