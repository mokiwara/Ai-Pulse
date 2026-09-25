using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AIUsageHub;

public sealed class LocalStore
{
    private readonly string _databasePath;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public string DirectoryPath { get; }

    public LocalStore(string? directoryPath = null)
    {
        DirectoryPath = directoryPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIUsageHub");
        Directory.CreateDirectory(DirectoryPath);
        _databasePath = Path.Combine(DirectoryPath, "hub.db");
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS schema_migrations(version INTEGER PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS contexts(
                id TEXT PRIMARY KEY, name TEXT NOT NULL, home_path TEXT NOT NULL UNIQUE,
                is_default INTEGER NOT NULL, identity_fingerprint TEXT,
                error_code TEXT, last_attempt TEXT, next_refresh TEXT, failure_count INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS snapshots(
                context_id TEXT PRIMARY KEY REFERENCES contexts(id) ON DELETE CASCADE,
                normalized_json TEXT NOT NULL, fetched_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS alert_events(
                dedupe_key TEXT PRIMARY KEY, fired_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS ignored_contexts(home_path TEXT PRIMARY KEY COLLATE NOCASE);
            INSERT OR IGNORE INTO schema_migrations(version) VALUES(1);
            INSERT OR IGNORE INTO schema_migrations(version) VALUES(2);
            """;
        command.ExecuteNonQuery();
        using var migration = db.CreateCommand();
        migration.CommandText = "SELECT EXISTS(SELECT 1 FROM pragma_table_info('contexts') WHERE name='provider')";
        if (Convert.ToInt32(migration.ExecuteScalar()) == 0)
        {
            using var transaction = db.BeginTransaction();
            using var alter = db.CreateCommand();
            alter.Transaction = transaction;
            alter.CommandText = "ALTER TABLE contexts ADD COLUMN provider TEXT NOT NULL DEFAULT 'Codex'; INSERT OR IGNORE INTO schema_migrations(version) VALUES(3);";
            alter.ExecuteNonQuery();
            transaction.Commit();
        }
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _databasePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        try
        {
            db.Open();
            using var pragma = db.CreateCommand();
            pragma.CommandText = "PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
            return db;
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    public List<CodexContext> LoadContexts()
    {
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT c.id,c.name,c.home_path,c.is_default,c.identity_fingerprint,c.error_code,c.last_attempt,c.next_refresh,c.failure_count,s.normalized_json,c.provider FROM contexts c LEFT JOIN snapshots s ON s.context_id=c.id ORDER BY c.provider,c.is_default DESC,c.name";
            using var reader = cmd.ExecuteReader();
            var result = new List<CodexContext>();
            while (reader.Read())
            {
                result.Add(new CodexContext
                {
                    Id = reader.GetString(0), Name = reader.GetString(1), HomePath = reader.GetString(2),
                    IsDefault = reader.GetInt64(3) != 0, IdentityFingerprint = GetNullable(reader, 4),
                    ErrorCode = GetNullable(reader, 5), LastAttemptAt = ParseDate(GetNullable(reader, 6)),
                    NextRefreshAt = ParseDate(GetNullable(reader, 7)), FailureCount = reader.GetInt32(8),
                    Snapshot = reader.IsDBNull(9) ? null : ReadSnapshot(reader.GetString(9)),
                    Provider = reader.GetString(10),
                    State = ConnectionState.Stale
                });
            }
            return result;
        }
    }

    private static UsageSnapshot? ReadSnapshot(string json)
    {
        if (json.Length > 2_000_000) return null;
        try
        {
            var snapshot = JsonSerializer.Deserialize<UsageSnapshot>(json, Json);
            return snapshot?.Windows is { Count: <= 100 } && snapshot.Windows.All(w => w is not null) ? snapshot : null;
        }
        catch (JsonException) { return null; }
    }

    private static string? GetNullable(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
    private static DateTimeOffset? ParseDate(string? value) => DateTimeOffset.TryParse(value, out var date) ? date : null;

    public void SaveContext(CodexContext context)
    {
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO contexts(id,name,home_path,is_default,identity_fingerprint,error_code,last_attempt,next_refresh,failure_count,provider)
                VALUES($id,$name,$path,$default,$fingerprint,$error,$attempt,$next,$failures,$provider)
                ON CONFLICT(id) DO UPDATE SET name=$name,home_path=$path,is_default=$default,
                    identity_fingerprint=$fingerprint,error_code=$error,last_attempt=$attempt,next_refresh=$next,failure_count=$failures,provider=$provider
                """;
            cmd.Parameters.AddWithValue("$id", context.Id);
            cmd.Parameters.AddWithValue("$name", context.Name);
            cmd.Parameters.AddWithValue("$path", context.HomePath);
            cmd.Parameters.AddWithValue("$default", context.IsDefault ? 1 : 0);
            cmd.Parameters.AddWithValue("$fingerprint", (object?)context.IdentityFingerprint ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$error", (object?)context.ErrorCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$attempt", (object?)context.LastAttemptAt?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$next", (object?)context.NextRefreshAt?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$failures", context.FailureCount);
            cmd.Parameters.AddWithValue("$provider", context.Provider);
            cmd.ExecuteNonQuery();
        }
    }

    public void SaveSnapshot(CodexContext context)
    {
        if (context.Snapshot is null) return;
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "INSERT INTO snapshots(context_id,normalized_json,fetched_at) VALUES($id,$json,$fetched) ON CONFLICT(context_id) DO UPDATE SET normalized_json=$json,fetched_at=$fetched";
            cmd.Parameters.AddWithValue("$id", context.Id);
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(context.Snapshot, Json));
            cmd.Parameters.AddWithValue("$fetched", context.Snapshot.FetchedAt.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    public bool IsIgnoredHome(string path)
    {
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM ignored_contexts WHERE home_path=$path)";
            cmd.Parameters.AddWithValue("$path", path);
            return Convert.ToInt32(cmd.ExecuteScalar()) != 0;
        }
    }

    public void UnignoreHome(string path)
    {
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM ignored_contexts WHERE home_path=$path";
            cmd.Parameters.AddWithValue("$path", path);
            cmd.ExecuteNonQuery();
        }
    }

    public void RemoveContext(string id, string homePath)
    {
        lock (_gate)
        {
            using var db = Open();
            using var transaction = db.BeginTransaction();
            using var cmd = db.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT OR IGNORE INTO ignored_contexts(home_path) VALUES($path)";
            cmd.Parameters.AddWithValue("$path", homePath);
            cmd.ExecuteNonQuery();
            cmd.CommandText = "DELETE FROM contexts WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
            transaction.Commit();
        }
    }

    public HubSettings LoadSettings()
    {
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT value FROM settings WHERE key='hub'";
            var json = cmd.ExecuteScalar() as string;
            try { var settings = json is null ? new HubSettings() : JsonSerializer.Deserialize<HubSettings>(json, Json) ?? new HubSettings(); settings.Normalize(); return settings; }
            catch (JsonException) { return new HubSettings(); }
        }
    }

    public void SaveSettings(HubSettings settings)
    {
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "INSERT INTO settings(key,value) VALUES('hub',$value) ON CONFLICT(key) DO UPDATE SET value=$value";
            cmd.Parameters.AddWithValue("$value", JsonSerializer.Serialize(settings, Json));
            cmd.ExecuteNonQuery();
        }
    }

    public bool RecordAlert(string key, DateTimeOffset now)
    {
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO alert_events(dedupe_key,fired_at) VALUES($key,$time)";
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$time", now.ToString("O"));
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    public void DeleteAlert(string key)
    {
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM alert_events WHERE dedupe_key=$key";
            cmd.Parameters.AddWithValue("$key", key);
            cmd.ExecuteNonQuery();
        }
    }

    public void PruneAlerts(DateTimeOffset now)
    {
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM alert_events WHERE fired_at < $cutoff";
            cmd.Parameters.AddWithValue("$cutoff", now.AddDays(-45).ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }
}
