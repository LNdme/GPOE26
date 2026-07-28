using Chat.Model;
using Microsoft.EntityFrameworkCore;

namespace Chat.Data;

/// <summary>
/// Historique des sessions d'étude et fiche de suivi par élève et par cours.
///
/// Le service Chat était jusqu'ici sans base : le frontend renvoyait l'historique
/// complet à chaque message. Le persister ici permet de reprendre une conversation,
/// et surtout d'accumuler ce que l'élève a du mal à comprendre.
/// </summary>
public class ChatContext(DbContextOptions<ChatContext> options) : DbContext(options)
{
    public DbSet<StudyConversation> Conversations => Set<StudyConversation>();
    public DbSet<StudyMessage> Messages => Set<StudyMessage>();
    public DbSet<StudentCourseMemory> Memories => Set<StudentCourseMemory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StudyConversation>(entity =>
        {
            // Une conversation par élève et par cours : c'est le fil de révision continu
            // de cet élève sur ce cours.
            entity.HasIndex(c => new { c.StudentId, c.CourseId }).IsUnique();

            entity.HasMany(c => c.Messages)
                  .WithOne(m => m.Conversation)
                  .HasForeignKey(m => m.ConversationId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StudyMessage>(entity =>
        {
            entity.Property(m => m.Role).HasMaxLength(20);
            entity.Property(m => m.Intent).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(m => new { m.ConversationId, m.CreatedAt });
        });

        modelBuilder.Entity<StudentCourseMemory>(entity =>
        {
            entity.HasIndex(m => new { m.StudentId, m.CourseId }).IsUnique();
        });
    }
}

/// <summary>Le fil d'étude d'un élève sur un cours.</summary>
public class StudyConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StudentId { get; set; }
    public Guid CourseId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<StudyMessage> Messages { get; set; } = new();
}

public class StudyMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ConversationId { get; set; }
    public StudyConversation Conversation { get; set; } = null!;

    /// <summary>"user" ou "assistant".</summary>
    public string Role { get; set; } = "user";

    public string Content { get; set; } = string.Empty;

    /// <summary>Intention détectée par le Routeur — renseignée sur les messages élève.</summary>
    public StudentIntent? Intent { get; set; }

    /// <summary>
    /// Sections du cours citées dans la réponse, séparées par « | ».
    /// Stockées à plat : on ne fait que les réafficher, jamais les requêter.
    /// </summary>
    public string? Citations { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Ce que le répétiteur a retenu de l'élève sur ce cours : notions vues, points
/// de blocage récurrents. Alimente les réponses suivantes.
/// </summary>
public class StudentCourseMemory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StudentId { get; set; }
    public Guid CourseId { get; set; }

    /// <summary>Fiche de suivi en texte libre, tenue par le MemoireAgent.</summary>
    public string Notes { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
