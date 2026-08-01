using System.Collections.Concurrent;
using GPOE26.Harness.Contract;

namespace GPOE26.Harness.Stub;

/// <summary>
/// Les sessions du stub, en mémoire.
///
/// Le stub n'a pas à persister quoi que ce soit : il existe pour exercer la suite de
/// conformité, pas pour servir des élèves. Une base ici n'ajouterait qu'une dépendance à
/// démarrer avant de pouvoir lancer les tests.
/// </summary>
public sealed class SessionStore
{
    private readonly ConcurrentDictionary<Guid, StoredSession> _sessions = new();

    public StoredSession Open(Guid studentId, Guid courseId, SessionKind kind)
    {
        var session = new StoredSession(Guid.NewGuid(), studentId, courseId, kind, DateTime.UtcNow);
        _sessions[session.Id] = session;
        return session;
    }

    public StoredSession? Find(Guid id) => _sessions.TryGetValue(id, out var s) ? s : null;
}

public sealed class StoredSession(Guid id, Guid studentId, Guid courseId, SessionKind kind, DateTime startedAt)
{
    private readonly List<TurnDto> _turns = [];
    private readonly Lock _gate = new();

    public Guid Id { get; } = id;
    public Guid StudentId { get; } = studentId;
    public Guid CourseId { get; } = courseId;
    public SessionKind Kind { get; } = kind;
    public DateTime StartedAt { get; } = startedAt;

    public DateTime LastActivityAt { get; private set; } = startedAt;
    public DateTime? CompactedAt { get; private set; }

    public void Record(string role, string content, IReadOnlyList<string>? citations = null)
    {
        lock (_gate)
        {
            _turns.Add(new TurnDto(_turns.Count, role, content, DateTime.UtcNow, citations));
            LastActivityAt = DateTime.UtcNow;
        }
    }

    public void Compact()
    {
        lock (_gate)
        {
            // Compaction du stub : on ne garde que le dernier échange. Le vrai harness
            // résumera ; ce qui compte ici est que la date soit posée et l'historique réduit.
            if (_turns.Count > 2) _turns.RemoveRange(0, _turns.Count - 2);
            CompactedAt = DateTime.UtcNow;
        }
    }

    public IReadOnlyList<TurnDto> Turns
    {
        get
        {
            // ForgetHistory : une session reprise revient vide, ce que le test de reprise
            // doit refuser.
            if (SabotageSettings.Is(Sabotage.ForgetHistory)) return [];

            lock (_gate) return [.. _turns];
        }
    }

    public SessionDto ToDto() => new(
        Id, StudentId, CourseId, Kind, StartedAt, LastActivityAt,
        Turns.Count(t => t.Role == "user"),
        CompactedAt,
        StubTools.For(Kind));
}
