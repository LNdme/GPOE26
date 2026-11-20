namespace Chat.Model;

// ══════════════════════════════════════════════════════════════════════════════
// Mémoire des agents, en trois parties :
//   - données structurées : profil, maîtrise par cours, emploi du temps, temps d'étude ;
//   - souvenirs retrouvés par le sens de la question (AgentMemories) ;
//   - consolidation après chaque échange (AgentReflectionWorker).
// ══════════════════════════════════════════════════════════════════════════════

public static class AgentKinds
{
    public const string Student = "student";
    public const string Teacher = "teacher";
}

public static class MemoryTypes
{
    public const string Fact = "fact";
    public const string Preference = "preference";
    public const string Difficulty = "difficulty";
    public const string Goal = "goal";
    public static readonly string[] All = [Fact, Preference, Difficulty, Goal];
}

public class AgentProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Kind { get; set; } = AgentKinds.Student;
    public string? Summary { get; set; }

    /// <summary>Enseignant : ton souhaité, niveau de détail et extraits de ses propres textes</summary>
    public string? Tone { get; set; }
    public string? DetailLevel { get; set; }
    public string? Samples { get; set; }

    /// <summary>L'utilisateur peut couper la mémoire : plus rien n'est retenu ni relu</summary>
    public bool MemoryEnabled { get; set; } = true;

    /// <summary>Dernier « Tout oublier » : une consolidation lancée avant ne doit rien réécrire après</summary>
    public DateTime? ForgottenAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CourseMasteryState
{
    public Guid UserId { get; set; }
    public Guid CourseId { get; set; }
    public string CourseTitle { get; set; } = "";
    public string? Subject { get; set; }
    public string? WeakPoints { get; set; }
    public DateTime LastStudiedAt { get; set; } = DateTime.UtcNow;
}

public static class SlotKinds
{
    public const string Class = "class";
    public const string Study = "study";
    public const string Other = "other";
}

/// <summary>Créneau hebdomadaire. Day : 1 = lundi … 7 = dimanche. Heures en minutes depuis minuit.</summary>
public class StudySlot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public int Day { get; set; }
    public int StartMinutes { get; set; }
    public int EndMinutes { get; set; }
    public string Kind { get; set; } = SlotKinds.Class;
    public string? Label { get; set; }
}

/// <summary>Temps passé sur un cours, cumulé par jour.</summary>
public class StudySession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid CourseId { get; set; }
    public DateOnly Day { get; set; }
    public int Minutes { get; set; }
    public DateTime LastAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Demandes d'IA servies, par utilisateur, fonction et jour (UTC) : quotas mensuels et plafond quotidien.</summary>
public class AiUsage
{
    public Guid UserId { get; set; }
    public string Feature { get; set; } = "";
    public DateOnly Day { get; set; }
    public int Count { get; set; }
}

public class AgentMemory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Kind { get; set; } = AgentKinds.Student;
    public string Type { get; set; } = MemoryTypes.Fact;
    public string Content { get; set; } = "";
    public int Importance { get; set; } = 3;
    public Guid? CourseId { get; set; }
    public Guid? SourceMessageId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
