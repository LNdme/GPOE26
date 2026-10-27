namespace Quiz.Model;

// ══════════════════════════════════════════════════════════════════════════════
// Évaluations créées par les enseignants : quiz d'entraînement, devoirs,
// épreuves notées et épreuves blanches, assignées à des classes (service User).
// ══════════════════════════════════════════════════════════════════════════════

public class Assessment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Enseignant auteur (claim "sub" du JWT)</summary>
    public Guid TeacherId { get; set; }

    public required string Title { get; set; }
    public string? Description { get; set; }

    public AssessmentKind Kind { get; set; } = AssessmentKind.Practice;
    public AssessmentStatus Status { get; set; } = AssessmentStatus.Draft;

    public string? Subject { get; set; }

    /// <summary>Cours principal évalué (service Cours) — sert au suivi de la maîtrise</summary>
    public Guid? CourseId { get; set; }

    /// <summary>Classes ou groupes concernés (service User)</summary>
    public List<Guid> ClassroomIds { get; set; } = new();

    public DateTime? OpensAt { get; set; }
    public DateTime? DueAt { get; set; }

    /// <summary>Durée de composition à partir du début de la tentative (null = illimitée)</summary>
    public int? DurationMinutes { get; set; }

    public int MaxAttempts { get; set; } = 1;

    /// <summary>Mélange l'ordre des questions et des options pour chaque élève</summary>
    public bool Shuffle { get; set; }

    /// <summary>Montre la correction détaillée aux élèves une fois les résultats publiés</summary>
    public bool ShowCorrection { get; set; } = true;

    /// <summary>Composition possible hors ligne dans l'application Windows</summary>
    public bool AllowOffline { get; set; }

    /// <summary>L'enseignant peut demander à l'IA une proposition de note pour les réponses ouvertes</summary>
    public bool AiAssistedGrading { get; set; } = true;

    /// <summary>Barème d'affichage de la note (sur 20 par défaut)</summary>
    public int ScaleTo { get; set; } = 20;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }

    public List<AssessmentQuestion> Questions { get; set; } = new();

    public decimal MaxScore => Questions.Sum(q => q.Points);

    /// <summary>
    /// Les élèves voient leur note (et la correction si ShowCorrection) :
    /// tout de suite pour un entraînement, sinon après la date limite ou la clôture.
    /// </summary>
    public bool ResultsReleased(DateTime now) =>
        Kind == AssessmentKind.Practice || Status == AssessmentStatus.Closed || (DueAt is not null && DueAt <= now);
}

public class AssessmentQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AssessmentId { get; set; }
    public Assessment Assessment { get; set; } = null!;

    public int Order { get; set; }

    public QuestionType Type { get; set; }

    public required string Text { get; set; }

    public decimal Points { get; set; } = 1;

    // ── Choix (SingleChoice, MultipleChoice) ──
    public List<string> Options { get; set; } = new();
    public List<int> CorrectOptions { get; set; } = new();

    // ── Vrai / Faux ──
    public bool? TrueFalseAnswer { get; set; }

    // ── Réponse courte : variantes acceptées (casse et accents ignorés) ──
    public List<string> AcceptedAnswers { get; set; } = new();

    // ── Numérique ──
    public double? NumericAnswer { get; set; }
    public double? Tolerance { get; set; }
    public string? Unit { get; set; }

    // ── Réponse ouverte : barème / critères de correction ──
    public string? Criteria { get; set; }

    /// <summary>Explication montrée avec la correction</summary>
    public string? Explanation { get; set; }

    /// <summary>Cours et section liés (service Cours) — prioritaire sur Assessment.CourseId</summary>
    public Guid? CourseId { get; set; }
    public Guid? SectionId { get; set; }
}

/// <summary>Une tentative d'un élève</summary>
public class Submission
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AssessmentId { get; set; }
    public Assessment Assessment { get; set; } = null!;

    public Guid StudentId { get; set; }

    public int AttemptNumber { get; set; } = 1;

    public SubmissionStatus Status { get; set; } = SubmissionStatus.InProgress;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }

    /// <summary>Rendu après la date limite ou la durée autorisée</summary>
    public bool IsLate { get; set; }

    /// <summary>Note finale (renseignée quand toutes les réponses sont corrigées)</summary>
    public decimal? Score { get; set; }
    public decimal MaxScore { get; set; }

    public DateTime? GradedAt { get; set; }

    public List<SubmissionAnswer> Answers { get; set; } = new();
}

public class SubmissionAnswer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SubmissionId { get; set; }
    public Submission Submission { get; set; } = null!;

    public Guid QuestionId { get; set; }

    public List<int> SelectedOptions { get; set; } = new();
    public bool? TrueFalse { get; set; }

    /// <summary>Réponse courte, numérique ou rédigée</summary>
    public string? Text { get; set; }

    /// <summary>Photo de la copie ou PDF (réponse ouverte)</summary>
    public string? AttachmentUrl { get; set; }

    public bool? IsCorrect { get; set; }

    /// <summary>Points obtenus (null tant qu'une réponse ouverte n'est pas corrigée)</summary>
    public decimal? Points { get; set; }

    public bool NeedsManualGrading { get; set; }

    public decimal? AiSuggestedPoints { get; set; }
    public string? AiFeedback { get; set; }

    /// <summary>Commentaire de l'enseignant</summary>
    public string? Feedback { get; set; }
}

public enum AssessmentKind
{
    Practice,
    Homework,
    GradedExam,
    MockExam
}

public enum AssessmentStatus
{
    Draft,
    Published,
    Closed
}

public enum QuestionType
{
    SingleChoice,
    MultipleChoice,
    TrueFalse,
    ShortAnswer,
    Numeric,
    OpenEnded
}

public enum SubmissionStatus
{
    InProgress,
    Submitted,
    Graded
}
