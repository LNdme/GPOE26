using Quiz.Model;
using System.Collections.Concurrent;

namespace Quiz.Service;

public interface IQuizStore
{
    void Save(QuizSession session);
    QuizSession? Get(Guid id);
    List<QuizSession> GetAll();
}

/// <summary>
/// Stockage en mémoire — à remplacer par EF Core + DB pour la prod.
/// </summary>
public class InMemoryQuizStore : IQuizStore
{
    private readonly ConcurrentDictionary<Guid, QuizSession> _store = new();

    public void Save(QuizSession session) =>
        _store[session.Id] = session;

    public QuizSession? Get(Guid id) =>
        _store.TryGetValue(id, out var session) ? session : null;

    public List<QuizSession> GetAll() =>
        [.. _store.Values.OrderByDescending(s => s.CreatedAt)];
}