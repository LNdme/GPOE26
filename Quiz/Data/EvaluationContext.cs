using Microsoft.EntityFrameworkCore;
using Quiz.Model;

namespace Quiz.Data;

public class EvaluationContext(DbContextOptions<EvaluationContext> options) : DbContext(options)
{
    public DbSet<Assessment> Assessments => Set<Assessment>();
    public DbSet<AssessmentQuestion> AssessmentQuestions => Set<AssessmentQuestion>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<SubmissionAnswer> SubmissionAnswers => Set<SubmissionAnswer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Les Id sont générés côté client (Guid.NewGuid) : sans ValueGeneratedNever, EF prendrait
        // une réponse ou une question ajoutée à une collection suivie pour une ligne existante (UPDATE au lieu d'INSERT).
        modelBuilder.Entity<Assessment>().Property(a => a.Id).ValueGeneratedNever();
        modelBuilder.Entity<AssessmentQuestion>().Property(q => q.Id).ValueGeneratedNever();
        modelBuilder.Entity<Submission>().Property(s => s.Id).ValueGeneratedNever();
        modelBuilder.Entity<SubmissionAnswer>().Property(a => a.Id).ValueGeneratedNever();

        // Enums stockés en texte, comme dans les autres services — cf. EvaluationSchema
        modelBuilder.Entity<Assessment>(entity =>
        {
            entity.HasIndex(a => a.TeacherId);
            entity.Property(a => a.Title).HasMaxLength(200);
            entity.Property(a => a.Kind).HasConversion<string>();
            entity.Property(a => a.Status).HasConversion<string>();
            entity.Ignore(a => a.MaxScore);
            entity.HasMany(a => a.Questions).WithOne(q => q.Assessment).HasForeignKey(q => q.AssessmentId);
        });

        modelBuilder.Entity<AssessmentQuestion>(entity =>
        {
            entity.HasIndex(q => new { q.AssessmentId, q.Order });
            entity.Property(q => q.Type).HasConversion<string>();
            entity.Property(q => q.Points).HasPrecision(6, 2);
        });

        modelBuilder.Entity<Submission>(entity =>
        {
            entity.HasIndex(s => new { s.AssessmentId, s.StudentId });
            entity.HasIndex(s => s.StudentId);
            entity.Property(s => s.Status).HasConversion<string>();
            entity.Property(s => s.Score).HasPrecision(8, 2);
            entity.Property(s => s.MaxScore).HasPrecision(8, 2);
            entity.HasOne(s => s.Assessment).WithMany().HasForeignKey(s => s.AssessmentId);
            entity.HasMany(s => s.Answers).WithOne(a => a.Submission).HasForeignKey(a => a.SubmissionId);
        });

        modelBuilder.Entity<SubmissionAnswer>(entity =>
        {
            entity.HasIndex(a => new { a.SubmissionId, a.QuestionId }).IsUnique();
            entity.Property(a => a.Points).HasPrecision(6, 2);
            entity.Property(a => a.AiSuggestedPoints).HasPrecision(6, 2);
        });
    }
}
