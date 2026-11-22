namespace GPOE26.UI.Models;

// Contrats des agents (service Chat)

/// <summary>Créneau hebdomadaire (classe mutable pour le formulaire). Day : 1 = lundi … 7 = dimanche.</summary>
public class StudySlot
{
    public int Day { get; set; } = 1;
    public int StartMinutes { get; set; } = 8 * 60;
    public int EndMinutes { get; set; } = 10 * 60;
    public string Kind { get; set; } = "class";
    public string? Label { get; set; }
}

public record TimeWindow(int StartMinutes, int EndMinutes);
public record PlanBlock(int StartMinutes, int EndMinutes, string Kind, string Title, Guid? TargetId);
public record DayPlan(int AvailableMinutes, List<TimeWindow> FreeWindows, List<PlanBlock> Blocks, bool HasSchedule);

/// <summary>Kind : "student" (noté par le tuteur) ou "teacher" (noté par l'assistant enseignant).</summary>
public record AgentMemoryItem(Guid Id, string Type, string Content, int Importance, Guid? CourseId, DateTime CreatedAt, string Kind = "student");
public record MemorySettings(bool Enabled, string? Summary);

public record TeacherAgentProfile(string? Tone, string? DetailLevel, string? Samples, string? Summary);
/// <summary>CourseText : le cours déjà rédigé, pour que l'assistant continue sans répéter.</summary>
public record ComposeRequest(string Mode, string? Subject, string? Title, string? Instructions, string? Selection, string? CourseText = null);
public record ComposeResult(string Markdown);

public record AtRiskStudent(Guid StudentId, string Name, double? AverageOn20, string Reason);
public record WeakCourse(Guid CourseId, string Title, double AveragePercent);
public record ClassInsights(int StudentCount, double? AverageOn20, List<AtRiskStudent> AtRisk, List<WeakCourse> WeakCourses);
public record ClassInsightsResult(ClassInsights Insights, string? Narrative);
public record AssistantAnswer(string Answer);

/// <summary>Message de la conversation avec l'assistant enseignant (Role : "user" ou "assistant").</summary>
public record AssistantMessage(Guid Id, string Role, string Content, Guid? ClassroomId, DateTime CreatedAt);
