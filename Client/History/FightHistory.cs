using System.IO;
using Microsoft.Data.Sqlite;

namespace AionDPS.History;

/// <summary>One boss fight of a stay, as the history keeps it.</summary>
public sealed record AttemptRecord(string Boss, DateTime Start, DateTime End, long Counted, bool Killed)
{
    public TimeSpan Duration => End - Start;
}

/// <summary>One stay in a map or instance with its boss fights (the lasting summary of a session: what the dashboard shows when the game is off).</summary>
public sealed record StayRecord(DateTime Start, DateTime End, int ZoneId, SessionKind Kind, int Hits, List<AttemptRecord> Attempts, string Character = "");

/// <summary>
/// The lasting history of the fights: every ended session adds its summary, and the recordings of earlier runs (the debug files) are worked through once in the
/// background (see <see cref="HistoryBuilder"/>) so the dashboard has real data from the start. Kept in %AppData%\\Aion DPS Meter\\fight-history.json.
/// </summary>
public static class FightHistory
{
    internal static readonly object Gate = new();
    private static bool _ready;

    /// <summary>The database of the history (SQLite, in the program's data folder): stays and their boss fights, and the recordings already worked through.</summary>
    public static string StorePath => Path.Combine(Environment.GetEnvironmentVariable("AIONDPS_DATA_DIR") is { Length: > 0 } testDir ? testDir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aion DPS Meter"), "fight-history.db");

    internal static SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = StorePath, Pooling = false }.ToString());
        connection.Open();
        if (!_ready)
        {
            using SqliteCommand create = connection.CreateCommand();
            create.CommandText = @"
PRAGMA journal_mode=WAL;
CREATE TABLE IF NOT EXISTS stays (id INTEGER PRIMARY KEY, start_ms INTEGER NOT NULL, end_ms INTEGER NOT NULL, zone INTEGER NOT NULL, kind INTEGER NOT NULL, hits INTEGER NOT NULL, character TEXT NOT NULL DEFAULT '', UNIQUE(zone, start_ms));
CREATE TABLE IF NOT EXISTS attempts (stay_id INTEGER NOT NULL REFERENCES stays(id) ON DELETE CASCADE, boss TEXT NOT NULL, start_ms INTEGER NOT NULL, end_ms INTEGER NOT NULL, counted INTEGER NOT NULL, killed INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS attempts_boss ON attempts(boss);
CREATE TABLE IF NOT EXISTS files (key TEXT PRIMARY KEY);
CREATE TABLE IF NOT EXISTS character_log (ts_ms INTEGER NOT NULL, name TEXT NOT NULL, server_id INTEGER NOT NULL, class_code INTEGER NOT NULL, level INTEGER NOT NULL, item_level INTEGER NOT NULL, legion TEXT, last_online_ms INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS character_log_who ON character_log(name, server_id, ts_ms);
CREATE TABLE IF NOT EXISTS ticket_log (ts_ms INTEGER NOT NULL, name TEXT NOT NULL, server_id INTEGER NOT NULL, ticket_id INTEGER NOT NULL, count INTEGER NOT NULL, bonus INTEGER NOT NULL, time_ms INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS ticket_log_who ON ticket_log(name, server_id, ticket_id, ts_ms);
CREATE TABLE IF NOT EXISTS character_snapshot (ts_ms INTEGER NOT NULL, name TEXT NOT NULL, server_id INTEGER NOT NULL, item_level INTEGER NOT NULL, combat_power INTEGER NOT NULL, equip TEXT NOT NULL, boards TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS character_snapshot_who ON character_snapshot(name, ts_ms);
CREATE TABLE IF NOT EXISTS boss_log (ts_ms INTEGER NOT NULL, npc INTEGER NOT NULL, phase INTEGER NOT NULL, at_ms INTEGER NOT NULL, seen_ms INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS boss_log_npc ON boss_log(npc, ts_ms);";
            create.ExecuteNonQuery();
            // a database of an earlier version gets the column for the character
            using (SqliteCommand columns = db_cols(connection))
            {
                bool has = false;
                using SqliteDataReader r = columns.ExecuteReader();
                while (r.Read())
                {
                    has |= r.GetString(1) == "character";
                }

                r.Close();
                if (!has)
                {
                    using SqliteCommand alter = connection.CreateCommand();
                    alter.CommandText = "ALTER TABLE stays ADD COLUMN character TEXT NOT NULL DEFAULT ''";
                    alter.ExecuteNonQuery();
                }
            }

            _ready = true;
        }

        return connection;
    }

    private static SqliteCommand db_cols(SqliteConnection c)
    {
        SqliteCommand command = c.CreateCommand();
        command.CommandText = "PRAGMA table_info(stays)";
        return command;
    }

    internal static long Ms(DateTime t) => new DateTimeOffset(t).ToUnixTimeMilliseconds();

    internal static DateTime Time(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;

    /// <summary>The summary of a session (null for a stay without hits).</summary>
    public static StayRecord? Summarize(SessionInfo session, string character = "")
    {
        if (session.Data.Events.Count == 0)
        {
            return null;
        }

        var attempts = new List<AttemptRecord>();
        try
        {
            foreach (BossAttempt a in SessionAnalyzer.Analyze(session).Attempts)
            {
                attempts.Add(new AttemptRecord(a.Name, a.Start, a.End, a.Counted, a.Killed));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // a stay that cannot be analysed keeps its times only
        }

        return new StayRecord(session.Start, session.End ?? session.Data.Events[^1].Timestamp, session.ZoneId, session.Kind, session.Data.Events.Count, attempts, character);
    }

    /// <summary>Adds stays (one that is already there - same map, start within 30 seconds - stays as it was).</summary>
    public static void Add(IEnumerable<StayRecord> stays)
    {
        try
        {
            lock (Gate)
            {
                using SqliteConnection db = Open();
                using SqliteTransaction tx = db.BeginTransaction();
                foreach (StayRecord stay in stays)
                {
                    long start = Ms(stay.Start);
                    using (SqliteCommand find = db.CreateCommand())
                    {
                        find.Transaction = tx;
                        find.CommandText = "SELECT 1 FROM stays WHERE zone=$z AND ABS(start_ms-$s)<30000";
                        find.Parameters.AddWithValue("$z", stay.ZoneId);
                        find.Parameters.AddWithValue("$s", start);
                        if (find.ExecuteScalar() is not null)
                        {
                            continue;
                        }
                    }

                    long id;
                    using (SqliteCommand insert = db.CreateCommand())
                    {
                        insert.Transaction = tx;
                        insert.CommandText = "INSERT OR IGNORE INTO stays (start_ms, end_ms, zone, kind, hits, character) VALUES ($s,$e,$z,$k,$h,$c); SELECT last_insert_rowid();";
                        insert.Parameters.AddWithValue("$s", start);
                        insert.Parameters.AddWithValue("$e", Ms(stay.End));
                        insert.Parameters.AddWithValue("$z", stay.ZoneId);
                        insert.Parameters.AddWithValue("$k", (int)stay.Kind);
                        insert.Parameters.AddWithValue("$h", stay.Hits);
                        insert.Parameters.AddWithValue("$c", stay.Character ?? "");
                        id = Convert.ToInt64(insert.ExecuteScalar());
                    }

                    foreach (AttemptRecord a in stay.Attempts)
                    {
                        using SqliteCommand attempt = db.CreateCommand();
                        attempt.Transaction = tx;
                        attempt.CommandText = "INSERT INTO attempts (stay_id, boss, start_ms, end_ms, counted, killed) VALUES ($i,$b,$s,$e,$c,$k)";
                        attempt.Parameters.AddWithValue("$i", id);
                        attempt.Parameters.AddWithValue("$b", a.Boss);
                        attempt.Parameters.AddWithValue("$s", Ms(a.Start));
                        attempt.Parameters.AddWithValue("$e", Ms(a.End));
                        attempt.Parameters.AddWithValue("$c", a.Counted);
                        attempt.Parameters.AddWithValue("$k", a.Killed ? 1 : 0);
                        attempt.ExecuteNonQuery();
                    }
                }

                tx.Commit();
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            // the history cannot be written now: the next stay tries again
        }
    }

    public static bool WasRead(string fileKey)
    {
        try
        {
            lock (Gate)
            {
                using SqliteConnection db = Open();
                using SqliteCommand command = db.CreateCommand();
                command.CommandText = "SELECT 1 FROM files WHERE key=$k";
                command.Parameters.AddWithValue("$k", fileKey);
                return command.ExecuteScalar() is not null;
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            return true; // not readable: leave the file alone
        }
    }

    public static void MarkRead(string fileKey)
    {
        try
        {
            lock (Gate)
            {
                using SqliteConnection db = Open();
                using SqliteCommand command = db.CreateCommand();
                command.CommandText = "INSERT OR IGNORE INTO files (key) VALUES ($k)";
                command.Parameters.AddWithValue("$k", fileKey);
                command.ExecuteNonQuery();
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            // read again next time
        }
    }

    /// <summary>Everything known: the kept history and the sessions of this run (the sessions win where both have a stay), newest first. An open session is left out.</summary>
    public static List<StayRecord> All(IEnumerable<SessionInfo> live)
    {
        var result = new List<StayRecord>();
        foreach (SessionInfo session in live.Where(s => !s.IsOpen))
        {
            if (Summarize(session) is { } record)
            {
                result.Add(record);
            }
        }

        try
        {
            lock (Gate)
            {
                using SqliteConnection db = Open();
                var stored = new Dictionary<long, StayRecord>();
                using (SqliteCommand stays = db.CreateCommand())
                {
                    stays.CommandText = "SELECT id, start_ms, end_ms, zone, kind, hits, character FROM stays ORDER BY start_ms DESC LIMIT 5000";
                    using SqliteDataReader r = stays.ExecuteReader();
                    while (r.Read())
                    {
                        stored[r.GetInt64(0)] = new StayRecord(Time(r.GetInt64(1)), Time(r.GetInt64(2)), r.GetInt32(3), (SessionKind)r.GetInt32(4), r.GetInt32(5), new List<AttemptRecord>(), r.GetString(6));
                    }
                }

                using (SqliteCommand attempts = db.CreateCommand())
                {
                    attempts.CommandText = "SELECT stay_id, boss, start_ms, end_ms, counted, killed FROM attempts ORDER BY start_ms";
                    using SqliteDataReader r = attempts.ExecuteReader();
                    while (r.Read())
                    {
                        if (stored.TryGetValue(r.GetInt64(0), out StayRecord? stay))
                        {
                            stay.Attempts.Add(new AttemptRecord(r.GetString(1), Time(r.GetInt64(2)), Time(r.GetInt64(3)), r.GetInt64(4), r.GetInt32(5) != 0));
                        }
                    }
                }

                foreach (StayRecord s in stored.Values)
                {
                    if (!result.Any(x => x.ZoneId == s.ZoneId && Math.Abs((x.Start - s.Start).TotalSeconds) < 30))
                    {
                        result.Add(s);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            // no history to read
        }

        return result.OrderByDescending(r => r.Start).ToList();
    }
}
