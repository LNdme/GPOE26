using Chat.Data;
using Chat.Model;
using Microsoft.EntityFrameworkCore;

namespace Chat.Service;

public record TodayPlan(List<TimeWindow> Windows, List<PlanBlock> Blocks, bool HasSchedule);

/// <summary>Rassemble, avant chaque réponse, ce que l'agent doit savoir de l'élève.</summary>
public class AgentContextService(TutorContext db, PlatformClients platform, AgentMemoryStore memories, IConfiguration config)
{
    private record ClassroomRef(string Name);
    private record MasteryRef(Guid CourseId, double Percent);
    private record ProgressRef(List<MasteryRef>? Mastery);
    private record SharedCourseRef(Guid Id, string Title);
    private record AssessmentRef(Guid Id, string Title, string? Status, DateTime? DueAt, int AttemptsUsed, bool HasAttemptInProgress);

    private TimeZoneInfo Zone
    {
        get
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(config["App:TimeZone"] ?? "Africa/Douala"); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.CreateCustomTimeZone("UTC+1", TimeSpan.FromHours(1), "UTC+1", "UTC+1"); }
        }
    }

    private DateTime LocalNow => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

    private static int IsoDay(DayOfWeek day) => day == DayOfWeek.Sunday ? 7 : (int)day;

    public async Task RecordStudyAsync(Guid userId, Guid courseId, int minutes)
    {
        var day = DateOnly.FromDateTime(LocalNow);
        var session = await db.StudySessions.FirstOrDefaultAsync(s => s.UserId == userId && s.CourseId == courseId && s.Day == day);
        if (session is null)
        {
            session = new StudySession { UserId = userId, CourseId = courseId, Day = day };
            db.StudySessions.Add(session);
        }
        session.Minutes += minutes;
        session.LastAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task TouchCourseAsync(Guid userId, Guid courseId, string title, string? subject, CancellationToken ct)
    {
        var state = await db.CourseMastery.FirstOrDefaultAsync(m => m.UserId == userId && m.CourseId == courseId, ct);
        if (state is null)
        {
            state = new CourseMasteryState { UserId = userId, CourseId = courseId };
            db.CourseMastery.Add(state);
        }
        state.CourseTitle = title;
        state.Subject = subject;
        state.LastStudiedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<TodayPlan> TodayPlanAsync(Guid userId, CancellationToken ct)
    {
        var now = LocalNow;

        // Les quatre services sont interrogés en même temps (ils l'étaient l'un après l'autre)
        var assessmentsTask = platform.GetJsonAsync<List<AssessmentRef>>("quiz", "/assessments/mine", ct);
        var progressTask = platform.GetJsonAsync<ProgressRef>("quiz", "/progress/me", ct);
        var sharedTask = platform.GetJsonAsync<List<SharedCourseRef>>("cours", "/cours/shared", ct);
        var mineTask = platform.GetJsonAsync<List<SharedCourseRef>>("cours", "/cours", ct);

        // Pendant ce temps, la base (un DbContext ne s'utilise que d'un appel à la fois)
        var slots = await db.StudySlots.AsNoTracking().Where(s => s.UserId == userId).ToListAsync(ct);
        var states = await db.CourseMastery.AsNoTracking().Where(m => m.UserId == userId).ToListAsync(ct);
        var today = slots.Where(s => s.Day == IsoDay(now.DayOfWeek)).ToList();
        var windows = StudyPlanner.FreeWindows(today, now.Hour * 60 + now.Minute);

        await Task.WhenAll(assessmentsTask, progressTask, sharedTask, mineTask);
        var assessments = assessmentsTask.Result ?? [];
        var soon = DateTime.UtcNow.AddDays(7);
        var due = assessments
            .Where(a => a.Status == "Published" && a.DueAt is { } d && d > DateTime.UtcNow && d <= soon && (a.AttemptsUsed == 0 || a.HasAttemptInProgress))
            .Select(a => new DueItem(a.Id, a.Title, a.DueAt!.Value))
            .ToList();

        var progress = progressTask.Result;
        var shared = sharedTask.Result ?? [];
        var mine = mineTask.Result ?? [];

        var courses = shared.Concat(mine).DistinctBy(c => c.Id).Select(c => new CourseFocus(
            c.Id, c.Title,
            progress?.Mastery?.FirstOrDefault(m => m.CourseId == c.Id)?.Percent,
            states.FirstOrDefault(s => s.CourseId == c.Id)?.LastStudiedAt));

        return new TodayPlan(windows, StudyPlanner.BuildDay(windows, due, courses), slots.Count > 0);
    }

    /// <summary>Bloc ajouté au message système du tuteur. Vide si l'élève a coupé la mémoire.</summary>
    public async Task<string> StudentContextAsync(Guid userId, Guid courseId, string question, CancellationToken ct)
    {
        var profile = await memories.ProfileAsync(userId, AgentKinds.Student, ct);
        if (!profile.MemoryEnabled) return "";

        var lines = new List<string>();
        if (profile.Summary is { Length: > 0 }) lines.Add($"Portrait : {profile.Summary}");

        // Classes et progression demandées en même temps ; le plan du jour réutilise la progression (cache d'une minute)
        var classesTask = platform.GetJsonAsync<List<ClassroomRef>>("user", "/classrooms/mine", ct);
        var progressTask = platform.GetJsonAsync<ProgressRef>("quiz", "/progress/me", ct);

        var states = await db.CourseMastery.AsNoTracking().Where(m => m.UserId == userId).OrderByDescending(m => m.LastStudiedAt).Take(12).ToListAsync(ct);
        var current = states.FirstOrDefault(s => s.CourseId == courseId);

        var classes = await classesTask;
        if (classes is { Count: > 0 }) lines.Add($"Classe : {string.Join(", ", classes.Select(c => c.Name))}");
        if (current?.WeakPoints is { Length: > 0 } weak) lines.Add($"Notions fragiles dans ce cours : {weak}");

        var progress = await progressTask;
        if (progress?.Mastery is { Count: > 0 } mastery)
        {
            // Un cours dont on ignore le titre n'est pas cité : « un autre cours (85 %) » n'aidait pas le tuteur
            var named = mastery.Where(m => states.Any(s => s.CourseId == m.CourseId)).ToList();
            string Title(Guid id) => states.First(s => s.CourseId == id).CourseTitle;
            if (named.Count > 1)
            {
                var best = named.OrderByDescending(m => m.Percent).First();
                var worst = named.OrderBy(m => m.Percent).First();
                lines.Add($"Comprend le mieux : {Title(best.CourseId)} ({best.Percent:0} %) ; le plus difficile : {Title(worst.CourseId)} ({worst.Percent:0} %)");
            }
            else if (named.Count == 1)
            {
                lines.Add($"Maîtrise de {Title(named[0].CourseId)} : {named[0].Percent:0} %");
            }
        }

        var plan = await TodayPlanAsync(userId, ct);
        var minutes = plan.Windows.Sum(w => w.Minutes);
        if (plan.HasSchedule)
            lines.Add(minutes > 0
                ? $"Temps d'étude encore disponible aujourd'hui : {minutes} min (à partir de {StudyPlanner.Clock(plan.Windows[0].StartMinutes)})"
                : "Plus de temps d'étude disponible aujourd'hui");

        var found = await memories.SearchAsync(userId, AgentKinds.Student, question, courseId, ct: ct);
        lines.AddRange(found.Select(m => m.Content));

        if (lines.Count == 0) return "";
        return AgentMemoryStore.Render("Ce que tu sais de cet élève", lines)
            + "\nAppuie-toi sur ces éléments sans les réciter : adapte tes exemples à ce qu'il comprend déjà, et la longueur de ton aide à son temps disponible (une seule notion si le temps est court).";
    }

    public Task<ClassProgressRef?> ClassProgressAsync(Guid classroomId, CancellationToken ct) =>
        platform.GetJsonAsync<ClassProgressRef>("quiz", $"/progress/classrooms/{classroomId}", ct);

    /// <summary>Titres des cours de l'enseignant, pour nommer les notions à revoir (et non le titre d'un devoir).</summary>
    public async Task<IReadOnlyDictionary<Guid, string>> CourseTitlesAsync(CancellationToken ct) =>
        (await platform.GetJsonAsync<List<SharedCourseRef>>("cours", "/cours", ct) ?? [])
            .DistinctBy(c => c.Id)
            .ToDictionary(c => c.Id, c => c.Title);
}
