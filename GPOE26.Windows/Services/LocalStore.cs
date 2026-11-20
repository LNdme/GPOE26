using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace GPOE26.Desktop.Services;

/// <summary>Une valeur relue depuis la copie locale, avec la date de sa dernière synchronisation.</summary>
public sealed record StoredValue<T>(T Value, DateTime SavedAt);

/// <summary>
/// Mémoire locale de l'application : une base SQLite dans %LOCALAPPDATA%\GPOE26.
/// - <c>cache</c> : la dernière réponse reçue de chaque API (cours, discussions…), par utilisateur,
///   pour relire son travail hors connexion. Deux élèves sur le même poste ne voient jamais les données de l'autre.
/// - <c>settings</c> : réglages de l'application (disposition des panneaux, modèle choisi, session chiffrée).
/// </summary>
public sealed class LocalStore
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _connectionString;
    private readonly SemaphoreSlim _init = new(1, 1);
    private bool _ready;

    public LocalStore(string? folder = null)
    {
        Folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GPOE26");
        Directory.CreateDirectory(Folder);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(Folder, "gpoe26.db") }.ToString();
    }

    /// <summary>Dossier de données de l'application (base locale, pages de lecture).</summary>
    public string Folder { get; }

    public async Task<StoredValue<T>?> GetAsync<T>(Guid userId, string key)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT json, updated_at FROM cache WHERE user_id = $u AND key = $k";
        cmd.Parameters.AddWithValue("$u", userId.ToString());
        cmd.Parameters.AddWithValue("$k", key);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        try
        {
            var value = JsonSerializer.Deserialize<T>(reader.GetString(0), Json);
            return value is null ? null : new StoredValue<T>(value, DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind));
        }
        catch (JsonException)
        {
            return null;   // format d'une ancienne version : la prochaine synchronisation le remplacera
        }
    }

    public async Task PutAsync<T>(Guid userId, string key, T value)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO cache (user_id, key, json, updated_at) VALUES ($u, $k, $j, $t)
            ON CONFLICT (user_id, key) DO UPDATE SET json = excluded.json, updated_at = excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("$u", userId.ToString());
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(value, Json));
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Efface toute la copie locale d'un utilisateur (« Oublier ce poste »).</summary>
    public async Task ClearUserAsync(Guid userId)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM cache WHERE user_id = $u";
        cmd.Parameters.AddWithValue("$u", userId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetSettingAsync(string key)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return await cmd.ExecuteScalarAsync() as string;
    }

    public async Task SetSettingAsync(string key, string? value)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        if (value is null)
        {
            cmd.CommandText = "DELETE FROM settings WHERE key = $k";
        }
        else
        {
            cmd.CommandText = "INSERT INTO settings (key, value) VALUES ($k, $v) ON CONFLICT (key) DO UPDATE SET value = excluded.value";
            cmd.Parameters.AddWithValue("$v", value);
        }
        cmd.Parameters.AddWithValue("$k", key);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var db = new SqliteConnection(_connectionString);
        await db.OpenAsync();
        if (_ready) return db;

        await _init.WaitAsync();
        try
        {
            if (!_ready)
            {
                await using var cmd = db.CreateCommand();
                cmd.CommandText = """
                    PRAGMA journal_mode = WAL;
                    CREATE TABLE IF NOT EXISTS cache (
                        user_id    TEXT NOT NULL,
                        key        TEXT NOT NULL,
                        json       TEXT NOT NULL,
                        updated_at TEXT NOT NULL,
                        PRIMARY KEY (user_id, key));
                    CREATE TABLE IF NOT EXISTS settings (
                        key   TEXT PRIMARY KEY,
                        value TEXT NOT NULL);
                    """;
                await cmd.ExecuteNonQueryAsync();
                _ready = true;
            }
        }
        finally
        {
            _init.Release();
        }
        return db;
    }
}
