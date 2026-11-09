using System.Text.Json;
using System.Text.RegularExpressions;
using Chat.Model;

namespace Chat.Service;

// ── Fonctions sans dépendance : planification, consolidation de la mémoire, suivi de classe ──

public record TimeWindow(int StartMinutes, int EndMinutes)
{
    public int Minutes => EndMinutes - StartMinutes;
}

public record DueItem(Guid Id, string Title, DateTime DueAt);
public record CourseFocus(Guid CourseId, string Title, double? Percent, DateTime? LastStudiedAt);
public record PlanBlock(int StartMinutes, int EndMinutes, string Kind, string Title, Guid? TargetId);

public static class StudyPlanner
{
    private const int EveningStart = 16 * 60;
    private const int EveningEnd = 21 * 60;
    private const int MinBlock = 15;
    private const int MaxBlock = 45;

    /// <summary>
    /// Temps libre restant aujourd'hui : les créneaux d'étude si l'élève en a déclaré,
    /// sinon sa soirée, en retirant les cours et activités.
    /// </summary>
    public static List<TimeWindow> FreeWindows(IReadOnlyCollection<StudySlot> today, int nowMinutes)
    {
        var study = today.Where(s => s.Kind == SlotKinds.Study).ToList();
        var windows = study.Count > 0
            ? study.Select(s => new TimeWindow(s.StartMinutes, s.EndMinutes)).ToList()
            : Subtract(new TimeWindow(EveningStart, EveningEnd), today.Select(s => new TimeWindow(s.StartMinutes, s.EndMinutes)));

        return windows
            .Select(w => w with { StartMinutes = Math.Max(w.StartMinutes, nowMinutes) })
            .Where(w => w.Minutes >= MinBlock)
            .OrderBy(w => w.StartMinutes)
            .ToList();
    }

    private static List<TimeWindow> Subtract(TimeWindow range, IEnumerable<TimeWindow> busy)
    {
        var result = new List<TimeWindow>();
        var cursor = range.StartMinutes;
        foreach (var b in busy.OrderBy(b => b.StartMinutes))
        {
            if (b.EndMinutes <= cursor || b.StartMinutes >= range.EndMinutes) continue;
            if (b.StartMinutes > cursor) result.Add(new TimeWindow(cursor, b.StartMinutes));
            cursor = Math.Max(cursor, b.EndMinutes);
        }
        if (cursor < range.EndMinutes) result.Add(new TimeWindow(cursor, range.EndMinutes));
        return result;
    }

    /// <summary>Remplit le temps libre : d'abord les évaluations proches, puis les cours les moins maîtrisés.</summary>
    public static List<PlanBlock> BuildDay(IEnumerable<TimeWindow> windows, IEnumerable<DueItem> due, IEnumerable<CourseFocus> courses)
    {
        var queue = new Queue<(string Kind, string Title, Guid Id)>(
            due.OrderBy(d => d.DueAt).Select(d => ("assessment", d.Title, d.Id))
                .Concat(courses
                    .OrderBy(c => c.Percent ?? double.MaxValue)
                    .ThenBy(c => c.LastStudiedAt ?? DateTime.MinValue)
                    .Select(c => ("course", c.Title, c.CourseId))));

        var blocks = new List<PlanBlock>();
        foreach (var window in windows)
        {
            var start = window.StartMinutes;
            while (window.EndMinutes - start >= MinBlock && queue.Count > 0)
            {
                var end = Math.Min(start + MaxBlock, window.EndMinutes);
                var (kind, title, id) = queue.Dequeue();
                blocks.Add(new PlanBlock(start, end, kind, title, id));
                start = end;
            }
        }
        return blocks;
    }

    public static string Clock(int minutes) => $"{minutes / 60:00}h{minutes % 60:00}";
}

public record ReflectionAdd(string Type, string Content, int Importance);
public record ReflectionUpdate(Guid Id, string Content);
public record Reflection(List<ReflectionAdd> Add, List<ReflectionUpdate> Update, List<Guid> Forget, string? Summary, string? WeakPoints);

public static partial class MemoryRules
{
    private const int MaxAddPerExchange = 5;
    private const int MaxContentLength = 240;
    private const double DuplicateThreshold = 0.8;

    /// <summary>Lit la réponse de l'extracteur. Tout ce qui est invalide est ignoré : la mémoire reste saine.</summary>
    public static Reflection? Parse(string reply, IReadOnlySet<Guid> knownIds)
    {
        var json = JsonBlock().Match(reply);
        if (!json.Success) return null;

        try
        {
            using var doc = JsonDocument.Parse(json.Value);
            var root = doc.RootElement;

            var adds = Items(root, "add")
                .Select(e => new ReflectionAdd(
                    Text(e, "type") ?? MemoryTypes.Fact,
                    Clean(Text(e, "content")),
                    e.TryGetProperty("importance", out var i) && i.TryGetInt32(out var value) ? Math.Clamp(value, 1, 5) : 3))
                .Where(a => a.Content.Length >= 4 && MemoryTypes.All.Contains(a.Type))
                .Take(MaxAddPerExchange)
                .ToList();

            var updates = Items(root, "update")
                .Select(e => (Id: Guid.TryParse(Text(e, "id"), out var id) ? id : Guid.Empty, Content: Clean(Text(e, "content"))))
                .Where(u => knownIds.Contains(u.Id) && u.Content.Length >= 4)
                .Select(u => new ReflectionUpdate(u.Id, u.Content))
                .ToList();

            var forget = Items(root, "forget")
                .Select(e => e.ValueKind == JsonValueKind.String && Guid.TryParse(e.GetString(), out var id) ? id : Guid.Empty)
                .Where(knownIds.Contains)
                .ToList();

            var summary = Text(root, "summary");
            return new Reflection(adds, updates, forget, Limit(summary, 600), Limit(Text(root, "weakPoints"), 300));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Un souvenir déjà connu, même formulé autrement, n'est pas enregistré deux fois.</summary>
    public static bool IsDuplicate(string content, IEnumerable<string> existing) =>
        existing.Any(e => Similarity(content, e) >= DuplicateThreshold);

    public static double Similarity(string a, string b)
    {
        var left = Words(a);
        var right = Words(b);
        if (left.Count == 0 || right.Count == 0) return 0;
        return (double)left.Intersect(right).Count() / left.Union(right).Count();
    }

    private static HashSet<string> Words(string text) =>
        WordPattern().Matches(text.ToLowerInvariant()).Select(m => m.Value).Where(w => w.Length > 2).ToHashSet();

    private static IEnumerable<JsonElement> Items(JsonElement root, string name) =>
        root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray() : [];

    private static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Clean(string? text) => Limit(text, MaxContentLength) ?? "";

    private static string? Limit(string? text, int max)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length > max ? trimmed[..max].TrimEnd() : trimmed;
    }

    [GeneratedRegex(@"\{.*\}", RegexOptions.Singleline)] private static partial Regex JsonBlock();
    [GeneratedRegex(@"\p{L}+|\d+")] private static partial Regex WordPattern();
}

public record ClassStudentRef(Guid StudentId, string? Username, DateTime? LastLoginAt, double? AverageOn20, List<ClassMasteryRef>? Mastery);
public record ClassMasteryRef(Guid CourseId, double Percent);
public record ClassAssessmentRef(Guid Id, string Title, Guid? CourseId);
public record ClassProgressRef(Guid ClassroomId, List<ClassAssessmentRef>? Assessments, List<ClassStudentRef>? Students);

public record AtRiskStudent(Guid StudentId, string Name, double? AverageOn20, string Reason);
public record WeakCourse(Guid CourseId, string Title, double AveragePercent);
public record ClassInsights(int StudentCount, double? AverageOn20, List<AtRiskStudent> AtRisk, List<WeakCourse> WeakCourses);

public static class ClassInsightsCalculator
{
    private const double PassMark = 10;
    private const int InactiveDays = 14;

    public static ClassInsights Compute(ClassProgressRef progress, DateTime now, IReadOnlyDictionary<Guid, string>? courseTitles = null)
    {
        var students = progress.Students ?? [];
        var graded = students.Where(s => s.AverageOn20 is not null).ToList();

        var atRisk = new List<AtRiskStudent>();
        foreach (var s in students)
        {
            var name = s.Username ?? "Élève";
            if (s.AverageOn20 < PassMark)
                atRisk.Add(new AtRiskStudent(s.StudentId, name, s.AverageOn20, "Moyenne sous 10/20"));
            else if (s.LastLoginAt is null || (now - s.LastLoginAt.Value).TotalDays > InactiveDays)
                atRisk.Add(new AtRiskStudent(s.StudentId, name, s.AverageOn20, $"Pas de connexion depuis plus de {InactiveDays} jours"));
        }

        var assessmentTitles = (progress.Assessments ?? [])
            .Where(a => a.CourseId is not null)
            .GroupBy(a => a.CourseId!.Value)
            .ToDictionary(g => g.Key, g => g.First().Title);

        var weak = students.SelectMany(s => s.Mastery ?? [])
            .GroupBy(m => m.CourseId)
            .Select(g => new WeakCourse(g.Key,
                courseTitles?.GetValueOrDefault(g.Key) ?? assessmentTitles.GetValueOrDefault(g.Key) ?? "Cours",
                Math.Round(g.Average(m => m.Percent), 1)))
            .OrderBy(c => c.AveragePercent)
            .Take(5)
            .ToList();

        return new ClassInsights(students.Count, graded.Count > 0 ? Math.Round(graded.Average(s => s.AverageOn20!.Value), 2) : null,
            atRisk.OrderBy(a => a.AverageOn20 ?? 20).ToList(), weak);
    }
}
