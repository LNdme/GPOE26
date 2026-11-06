using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using GPOE26.UI.Services;
using Microsoft.Data.Sqlite;

namespace GPOE26.Offline;

/// <summary>Tentative composée sur l'appareil (Mode = offline) ou copie en ligne qui n'a pas pu être rendue (Mode = online).</summary>
public record LocalAttempt(
    Guid Id, Guid AssessmentId, string Mode, string State, DateTime StartedAt, DateTime? Deadline,
    DateTime? SubmittedAt, string? AnswersJson, Guid? ServerId, string Title, int AttemptNumber,
    decimal MaxScore, int ScaleTo, string? Error);

public record LocalPhoto(Guid Id, Guid AttemptId, Guid AssessmentId, Guid QuestionId, int Page, string ContentType, byte[] Data);

public record OutboxEntry(long Seq, string Kind, Guid Ref, int Tries);

public static class AttemptStates
{
    public const string Composing = "composing";
    public const string Pending = "pending";
    public const string Sent = "sent";
    public const string Rejected = "rejected";
}

public static class OutboxKinds
{
    /// <summary>Copie composée hors ligne (Ref = tentative locale)</summary>
    public const string OfflineSubmission = "offline-submission";
    /// <summary>Photo d'une tentative en ligne, envoyée après une coupure (Ref = photo locale)</summary>
    public const string OnlinePhoto = "online-photo";
    /// <summary>Copie en ligne rendue pendant une coupure (Ref = tentative serveur)</summary>
    public const string OnlineSubmit = "online-submit";
}

/// <summary>
/// Base SQLite de l'utilisateur connecté (gpoe-{id}.db) : chacun ne retrouve que ses propres données
/// sur un ordinateur partagé. Cache des lectures, tentatives et photos locales, file d'envoi.
/// </summary>
public class OfflineDatabase(OfflineOptions options, SessionState session)
{
    private static readonly ConcurrentDictionary<string, bool> Initialized = new();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS cache (kind TEXT NOT NULL, key TEXT NOT NULL, json TEXT NOT NULL, saved_at TEXT NOT NULL, PRIMARY KEY (kind, key));
        CREATE TABLE IF NOT EXISTS attempts (
            id TEXT PRIMARY KEY, assessment_id TEXT NOT NULL, mode TEXT NOT NULL, state TEXT NOT NULL,
            started_at TEXT NOT NULL, deadline TEXT, submitted_at TEXT, answers_json TEXT, server_id TEXT,
            title TEXT NOT NULL, attempt_number INTEGER NOT NULL, max_score TEXT NOT NULL, scale_to INTEGER NOT NULL, error TEXT);
        CREATE TABLE IF NOT EXISTS photos (
            id TEXT PRIMARY KEY, attempt_id TEXT NOT NULL, assessment_id TEXT NOT NULL, question_id TEXT NOT NULL,
            page INTEGER NOT NULL, content_type TEXT NOT NULL, data BLOB NOT NULL);
        CREATE TABLE IF NOT EXISTS outbox (
            seq INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, ref TEXT NOT NULL, created_at TEXT NOT NULL, tries INTEGER NOT NULL DEFAULT 0);
        """;

    public string Path => System.IO.Path.Combine(options.DataDirectory,
        $"gpoe-{(session.Profile?.Id ?? throw new InvalidOperationException("Aucun utilisateur connecté.")):N}.db");

    private async Task<SqliteConnection> OpenAsync()
    {
        var path = Path;
        var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        if (!Initialized.ContainsKey(path))
        {
            Directory.CreateDirectory(options.DataDirectory);
            await connection.OpenAsync();
            await ExecAsync(connection, "PRAGMA journal_mode=WAL;" + Schema);
            Initialized[path] = true;
            return connection;
        }
        await connection.OpenAsync();
        return connection;
    }

    // ─── Cache ────────────────────────────────────────────────────────────────

    public async Task SaveAsync<T>(string kind, string key, T value)
    {
        await using var c = await OpenAsync();
        await ExecAsync(c, "INSERT OR REPLACE INTO cache (kind, key, json, saved_at) VALUES ($k, $key, $json, $at)",
            ("$k", kind), ("$key", key), ("$json", JsonSerializer.Serialize(value, Json)), ("$at", Now()));
    }

    public async Task<T?> LoadAsync<T>(string kind, string key)
    {
        await using var c = await OpenAsync();
        var json = await ScalarAsync(c, "SELECT json FROM cache WHERE kind = $k AND key = $key", ("$k", kind), ("$key", key)) as string;
        return json is null ? default : JsonSerializer.Deserialize<T>(json, Json);
    }

    public async Task<bool> HasAsync(string kind, string key)
    {
        await using var c = await OpenAsync();
        return await ScalarAsync(c, "SELECT 1 FROM cache WHERE kind = $k AND key = $key", ("$k", kind), ("$key", key)) is not null;
    }

    // ─── Tentatives ───────────────────────────────────────────────────────────

    public async Task SaveAttemptAsync(LocalAttempt a)
    {
        await using var c = await OpenAsync();
        await ExecAsync(c, """
            INSERT OR REPLACE INTO attempts (id, assessment_id, mode, state, started_at, deadline, submitted_at, answers_json,
                server_id, title, attempt_number, max_score, scale_to, error)
            VALUES ($id, $aid, $mode, $state, $start, $deadline, $sub, $answers, $server, $title, $num, $max, $scale, $error)
            """,
            ("$id", a.Id.ToString()), ("$aid", a.AssessmentId.ToString()), ("$mode", a.Mode), ("$state", a.State),
            ("$start", Iso(a.StartedAt)), ("$deadline", Iso(a.Deadline)), ("$sub", Iso(a.SubmittedAt)), ("$answers", a.AnswersJson),
            ("$server", a.ServerId?.ToString()), ("$title", a.Title), ("$num", a.AttemptNumber),
            ("$max", a.MaxScore.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("$scale", a.ScaleTo), ("$error", a.Error));
    }

    public async Task<LocalAttempt?> AttemptAsync(Guid id) =>
        (await AttemptsAsync("id = $v", id.ToString())).FirstOrDefault();

    public Task<List<LocalAttempt>> AttemptsForAsync(Guid assessmentId) => AttemptsAsync("assessment_id = $v", assessmentId.ToString());

    public Task<List<LocalAttempt>> AllAttemptsAsync() => AttemptsAsync("1 = 1", null);

    private async Task<List<LocalAttempt>> AttemptsAsync(string where, string? value)
    {
        await using var c = await OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT id, assessment_id, mode, state, started_at, deadline, submitted_at, answers_json, server_id, title, attempt_number, max_score, scale_to, error FROM attempts WHERE {where}";
        if (value is not null) cmd.Parameters.AddWithValue("$v", value);
        var list = new List<LocalAttempt>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new LocalAttempt(Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), r.GetString(2), r.GetString(3),
                ParseDate(r.GetString(4))!.Value, r.IsDBNull(5) ? null : ParseDate(r.GetString(5)), r.IsDBNull(6) ? null : ParseDate(r.GetString(6)),
                r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : Guid.Parse(r.GetString(8)), r.GetString(9), r.GetInt32(10),
                decimal.Parse(r.GetString(11), System.Globalization.CultureInfo.InvariantCulture), r.GetInt32(12), r.IsDBNull(13) ? null : r.GetString(13)));
        return list;
    }

    // ─── Photos ───────────────────────────────────────────────────────────────

    public async Task AddPhotoAsync(LocalPhoto p)
    {
        await using var c = await OpenAsync();
        await ExecAsync(c, "INSERT INTO photos (id, attempt_id, assessment_id, question_id, page, content_type, data) VALUES ($id, $att, $aid, $q, $page, $type, $data)",
            ("$id", p.Id.ToString()), ("$att", p.AttemptId.ToString()), ("$aid", p.AssessmentId.ToString()), ("$q", p.QuestionId.ToString()),
            ("$page", p.Page), ("$type", p.ContentType), ("$data", p.Data));
    }

    public async Task<LocalPhoto?> PhotoAsync(Guid id) => (await PhotosAsync("id = $v", id.ToString())).FirstOrDefault();

    public Task<List<LocalPhoto>> PhotosOfAsync(Guid attemptId) => PhotosAsync("attempt_id = $v", attemptId.ToString());

    private async Task<List<LocalPhoto>> PhotosAsync(string where, string value)
    {
        await using var c = await OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT id, attempt_id, assessment_id, question_id, page, content_type, data FROM photos WHERE {where} ORDER BY question_id, page";
        cmd.Parameters.AddWithValue("$v", value);
        var list = new List<LocalPhoto>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new LocalPhoto(Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), Guid.Parse(r.GetString(2)), Guid.Parse(r.GetString(3)),
                r.GetInt32(4), r.GetString(5), (byte[])r["data"]));
        return list;
    }

    public async Task DeletePhotoAsync(Guid id)
    {
        await using var c = await OpenAsync();
        await ExecAsync(c, "DELETE FROM photos WHERE id = $id; DELETE FROM outbox WHERE ref = $id", ("$id", id.ToString()));
    }

    public async Task RenumberPhotosAsync(Guid attemptId, Guid questionId)
    {
        var photos = (await PhotosOfAsync(attemptId)).Where(p => p.QuestionId == questionId).OrderBy(p => p.Page).ToList();
        await using var c = await OpenAsync();
        for (var i = 0; i < photos.Count; i++)
            await ExecAsync(c, "UPDATE photos SET page = $p WHERE id = $id", ("$p", i + 1), ("$id", photos[i].Id.ToString()));
    }

    // ─── File d'envoi ─────────────────────────────────────────────────────────

    public async Task EnqueueAsync(string kind, Guid reference)
    {
        await using var c = await OpenAsync();
        await ExecAsync(c, "INSERT INTO outbox (kind, ref, created_at) VALUES ($k, $r, $at)", ("$k", kind), ("$r", reference.ToString()), ("$at", Now()));
    }

    public async Task<List<OutboxEntry>> OutboxAsync()
    {
        await using var c = await OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT seq, kind, ref, tries FROM outbox ORDER BY seq";
        var list = new List<OutboxEntry>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new OutboxEntry(r.GetInt64(0), r.GetString(1), Guid.Parse(r.GetString(2)), r.GetInt32(3)));
        return list;
    }

    public async Task<bool> HasPendingForAsync(Guid attemptId)
    {
        var photoIds = (await PhotosOfAsync(attemptId)).Select(p => p.Id).ToHashSet();
        return (await OutboxAsync()).Any(e => e.Ref == attemptId || photoIds.Contains(e.Ref));
    }

    public async Task DequeueAsync(long seq)
    {
        await using var c = await OpenAsync();
        await ExecAsync(c, "DELETE FROM outbox WHERE seq = $s", ("$s", seq));
    }

    public async Task CountTryAsync(long seq)
    {
        await using var c = await OpenAsync();
        await ExecAsync(c, "UPDATE outbox SET tries = tries + 1 WHERE seq = $s", ("$s", seq));
    }

    // ─── Outils ───────────────────────────────────────────────────────────────

    private static string Now() => Iso(DateTime.UtcNow)!;
    private static string? Iso(DateTime? d) => d?.ToUniversalTime().ToString("O");
    private static DateTime? ParseDate(string s) =>
        DateTime.Parse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();

    private static async Task ExecAsync(SqliteConnection c, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqliteConnection c, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull ? null : result;
    }
}
