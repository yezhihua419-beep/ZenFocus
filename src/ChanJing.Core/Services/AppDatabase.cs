using ChanJing.Core.Models;
using Microsoft.Data.Sqlite;

namespace ChanJing.Core.Services;

/// <summary>
/// 本地 SQLite 存储。数据全在本地，零上传。
/// 负责：专注会话、应用使用时长、设置。
/// </summary>
public sealed class AppDatabase
{
    private readonly string _dbPath;

    public AppDatabase(string dbPath)
    {
        _dbPath = dbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        Initialize();
    }

    private void Initialize()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS FocusSessions (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                StartedAt       TEXT NOT NULL,
                EndedAt         TEXT NULL,
                PlannedMinutes  INTEGER NOT NULL,
                ActualMinutes   INTEGER NOT NULL,
                State           INTEGER NOT NULL,
                Wish            TEXT NULL,
                DistractionCount INTEGER NOT NULL DEFAULT 0,
                IsAdhd          INTEGER NOT NULL DEFAULT 0,
                DistractionSources TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS AppUsage (
                Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                Day        TEXT NOT NULL,
                ProcessName TEXT NOT NULL,
                TitleHash  TEXT NULL,
                Seconds    INTEGER NOT NULL DEFAULT 0,
                UNIQUE (Day, ProcessName)
            );

            CREATE TABLE IF NOT EXISTS Settings (
                Key   TEXT PRIMARY KEY,
                Value TEXT NULL
            );
            """;
        cmd.ExecuteNonQuery();

        // 迁移：旧数据库没有IsAdhd字段时添加（新数据库CREATE TABLE已包含，ALTER会报错，用try-catch忽略）
        try
        {
            using var migrateCmd = conn.CreateCommand();
            migrateCmd.CommandText = "ALTER TABLE FocusSessions ADD COLUMN IsAdhd INTEGER NOT NULL DEFAULT 0;";
            migrateCmd.ExecuteNonQuery();
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            // 列已存在，忽略
        }
        // 迁移：旧数据库没有DistractionSources字段时添加
        try
        {
            using var migrateCmd = conn.CreateCommand();
            migrateCmd.CommandText = "ALTER TABLE FocusSessions ADD COLUMN DistractionSources TEXT NULL;";
            migrateCmd.ExecuteNonQuery();
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            // 列已存在，忽略
        }
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        // 多连接并发写时等待最多 3 秒，避免 SQLITE_BUSY 直接失败。
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout = 3000;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    // ---------- 专注会话 ----------

    public void SaveFocusSession(FocusSession session)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO FocusSessions (StartedAt, EndedAt, PlannedMinutes, ActualMinutes, State, Wish, DistractionCount, IsAdhd, DistractionSources)
            VALUES ($started, $ended, $planned, $actual, $state, $wish, $distraction, $isAdhd, $distractionSources);
            """;
        cmd.Parameters.AddWithValue("$started", session.StartedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$ended", session.EndedAt?.ToString("o") ?? (object?)DBNull.Value);
        cmd.Parameters.AddWithValue("$planned", session.PlannedMinutes);
        cmd.Parameters.AddWithValue("$actual", session.ActualMinutes);
        cmd.Parameters.AddWithValue("$state", (int)session.State);
        cmd.Parameters.AddWithValue("$wish", (object?)session.Wish ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$distraction", session.DistractionCount);
        cmd.Parameters.AddWithValue("$isAdhd", session.IsAdhd ? 1 : 0);
        cmd.Parameters.AddWithValue("$distractionSources", (object?)session.DistractionSources ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<FocusSession> GetSessions(DateTime from, DateTime to)
    {
        var result = new List<FocusSession>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, StartedAt, EndedAt, PlannedMinutes, ActualMinutes, State, Wish, DistractionCount, IsAdhd, DistractionSources
            FROM FocusSessions
            WHERE StartedAt >= $from AND StartedAt < $to
            ORDER BY StartedAt DESC;
            """;
        cmd.Parameters.AddWithValue("$from", from.ToString("o"));
        cmd.Parameters.AddWithValue("$to", to.ToString("o"));
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new FocusSession
            {
                Id = reader.GetInt32(0),
                StartedAt = DateTime.Parse(reader.GetString(1)),
                EndedAt = reader.IsDBNull(2) ? null : DateTime.Parse(reader.GetString(2)),
                PlannedMinutes = reader.GetInt32(3),
                ActualMinutes = reader.GetInt32(4),
                State = (FocusSessionState)reader.GetInt32(5),
                Wish = reader.IsDBNull(6) ? null : reader.GetString(6),
                DistractionCount = reader.GetInt32(7),
                IsAdhd = reader.GetInt32(8) != 0,
                DistractionSources = reader.IsDBNull(9) ? null : reader.GetString(9)
            });
        }
        return result;
    }

    // ---------- 应用使用时长 ----------

    /// <summary>累加某应用在某天的使用秒数。</summary>
    public void AddAppUsage(string day, string processName, string? titleHash, int seconds)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AppUsage (Day, ProcessName, TitleHash, Seconds)
            VALUES ($day, $process, $hash, $seconds)
            ON CONFLICT(Day, ProcessName) DO UPDATE SET
                Seconds = Seconds + excluded.Seconds,
                TitleHash = COALESCE(excluded.TitleHash, TitleHash);
            """;
        cmd.Parameters.AddWithValue("$day", day);
        cmd.Parameters.AddWithValue("$process", processName);
        cmd.Parameters.AddWithValue("$hash", (object?)titleHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$seconds", seconds);
        cmd.ExecuteNonQuery();
    }

    public Dictionary<string, long> GetUsageByDay(string day)
    {
        var result = new Dictionary<string, long>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ProcessName, Seconds FROM AppUsage WHERE Day = $day ORDER BY Seconds DESC;";
        cmd.Parameters.AddWithValue("$day", day);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetString(0)] = reader.GetInt64(1);
        }
        return result;
    }

    // ---------- 设置 ----------

    public string? GetSetting(string key)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Settings WHERE Key = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        var value = cmd.ExecuteScalar();
        return value as string;
    }

    public void SetSetting(string key, string value)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Settings (Key, Value) VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }
}
