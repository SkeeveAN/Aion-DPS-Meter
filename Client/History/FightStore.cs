using System.IO;
using System.IO.Compression;
using System.Text.Json;
using AionDPS.Combat;
using Microsoft.Data.Sqlite;

namespace AionDPS.History;

/// <summary>
/// The local fight history: one SQLite file under %AppData% (never next to the exe - Velopack
/// replaces that directory on update). Summaries and participants are real columns so the list
/// can be searched; the raw events travel as one gzip'd JSON blob per fight, enough to load a
/// past fight back into the meter. Nothing here ever leaves the machine.
/// </summary>
public sealed class FightStore : IDisposable
{
    private readonly SqliteConnection _connection;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aion DPS Meter", "fights.db");

    public FightStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connection = new SqliteConnection($"Data Source={path}");
        _connection.Open();
        Execute("PRAGMA journal_mode=WAL;");
        Execute("""
            CREATE TABLE IF NOT EXISTS fights (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                game TEXT NOT NULL,
                server_name TEXT,
                started_at TEXT NOT NULL,
                ended_at TEXT NOT NULL,
                target_name TEXT NOT NULL,
                kind TEXT NOT NULL,
                total_damage INTEGER NOT NULL,
                participant_count INTEGER NOT NULL,
                self_name TEXT,
                events_gz BLOB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS fights_started_at_idx ON fights(started_at);
            CREATE TABLE IF NOT EXISTS participants (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                fight_id INTEGER NOT NULL REFERENCES fights(id) ON DELETE CASCADE,
                name TEXT NOT NULL,
                class_name TEXT NOT NULL,
                faction TEXT NOT NULL,
                is_self INTEGER NOT NULL,
                is_enemy INTEGER NOT NULL,
                damage INTEGER NOT NULL,
                dps REAL,
                healing INTEGER NOT NULL,
                damage_taken INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS participants_fight_id_idx ON participants(fight_id);
            CREATE INDEX IF NOT EXISTS participants_name_idx ON participants(name);
            """);
        Execute("PRAGMA foreign_keys=ON;");
        AddColumnIfMissing("extras_gz", "BLOB");
        // the history did not keep track of uploads before: what is stored then is "unknown", not "not uploaded"
        if (AddColumnIfMissing("uploaded_at", "TEXT"))
        {
            Execute("UPDATE fights SET uploaded_at = 'unknown';");
        }
    }

    /// <summary>The table predates its newer columns (CREATE IF NOT EXISTS leaves an old one as it is), so each is added once when it is missing.</summary>
    private bool AddColumnIfMissing(string name, string type)
    {
        using SqliteCommand info = _connection.CreateCommand();
        info.CommandText = "PRAGMA table_info(fights);";
        using (SqliteDataReader reader = info.ExecuteReader())
        {
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), name, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        Execute($"ALTER TABLE fights ADD COLUMN {name} {type};");
        return true;
    }

    /// <summary>Uploads whose fight the recorder has not filed yet (it files a fight a while after it ended): applied when the fight arrives.</summary>
    private readonly List<(string Target, DateTime StartUtc, DateTime EndUtc)> _pendingMarks = new();

    private static bool Overlaps(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd, TimeSpan slack) =>
        aStart.ToUniversalTime() <= bEnd.ToUniversalTime() + slack && bStart.ToUniversalTime() <= aEnd.ToUniversalTime() + slack;

    /// <summary>The boss fight on <paramref name="target"/> between the two times (UTC) went online. Marks the stored fight, or remembers it for the one still to come.</summary>
    public void MarkUploaded(string target, DateTime startUtc, DateTime endUtc)
    {
        TimeSpan slack = TimeSpan.FromSeconds(15);
        var ids = new List<long>();
        using (SqliteCommand cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT id, started_at, ended_at FROM fights WHERE lower(target_name) = lower($target) AND uploaded_at IS NULL;";
            cmd.Parameters.AddWithValue("$target", target);
            using SqliteDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                DateTime started = DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind);
                DateTime ended = DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind);
                if (Overlaps(started, ended, startUtc, endUtc, slack))
                {
                    ids.Add(reader.GetInt64(0));
                }
            }
        }

        if (ids.Count == 0)
        {
            _pendingMarks.Add((target, startUtc, endUtc));
            if (_pendingMarks.Count > 200)
            {
                _pendingMarks.RemoveAt(0);
            }

            return;
        }

        foreach (long id in ids)
        {
            Execute("UPDATE fights SET uploaded_at = $at WHERE id = $id;", ("$at", DateTime.Now.ToString("o")), ("$id", id));
        }
    }

    /// <summary>How many boss fights in a history file were not uploaded (the number the settings show); 0 for no file.</summary>
    public static long CountNotUploaded(string path)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        try
        {
            using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            connection.Open();
            using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM fights WHERE kind = 'boss' AND uploaded_at IS NULL;";
            return cmd.ExecuteScalar() is long n ? n : 0;
        }
        catch (SqliteException)
        {
            return 0; // an older file without the column
        }
    }

    public long Insert(FightDetail detail)
    {
        // A replay can re-file fights the live recorder already stored; the same
        // target at the same second is the same fight, not a second one.
        using (SqliteCommand existing = _connection.CreateCommand())
        {
            existing.CommandText = "SELECT id FROM fights WHERE target_name = $target AND started_at = $started LIMIT 1;";
            existing.Parameters.AddWithValue("$target", detail.Summary.TargetName);
            existing.Parameters.AddWithValue("$started", detail.Summary.StartedAt.ToString("o"));
            if (existing.ExecuteScalar() is long duplicateId)
            {
                return duplicateId;
            }
        }

        using SqliteTransaction tx = _connection.BeginTransaction();
        using (SqliteCommand cmd = _connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO fights (game, server_name, started_at, ended_at, target_name, kind, total_damage, participant_count, self_name, events_gz, extras_gz, uploaded_at)
                VALUES ($game, $server, $started, $ended, $target, $kind, $total, $count, $self, $events, $extras, $uploaded);
                SELECT last_insert_rowid();
                """;
            FightSummary s = detail.Summary;
            cmd.Parameters.AddWithValue("$game", s.Game);
            cmd.Parameters.AddWithValue("$server", (object?)s.ServerName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$started", s.StartedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$ended", s.EndedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$target", s.TargetName);
            cmd.Parameters.AddWithValue("$kind", s.Kind);
            cmd.Parameters.AddWithValue("$total", s.TotalDamage);
            cmd.Parameters.AddWithValue("$count", detail.Participants.Count);
            cmd.Parameters.AddWithValue("$self", (object?)s.SelfName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$events", EventBlob.Pack(detail.Events, detail.Names));
            cmd.Parameters.AddWithValue("$extras", detail.Extras is { IsEmpty: false } extras ? ExtrasBlob.Pack(extras) : DBNull.Value);
            int pending = _pendingMarks.FindIndex(m => string.Equals(m.Target, s.TargetName, StringComparison.OrdinalIgnoreCase)
                && Overlaps(s.StartedAt, s.EndedAt, m.StartUtc, m.EndUtc, TimeSpan.FromSeconds(15)));
            cmd.Parameters.AddWithValue("$uploaded", pending >= 0 ? DateTime.Now.ToString("o") : DBNull.Value);
            if (pending >= 0)
            {
                _pendingMarks.RemoveAt(pending);
            }
            long id = (long)cmd.ExecuteScalar()!;

            foreach (FightParticipant p in detail.Participants)
            {
                using SqliteCommand pc = _connection.CreateCommand();
                pc.Transaction = tx;
                pc.CommandText = """
                    INSERT INTO participants (fight_id, name, class_name, faction, is_self, is_enemy, damage, dps, healing, damage_taken)
                    VALUES ($fight, $name, $class, $faction, $self, $enemy, $damage, $dps, $healing, $taken);
                    """;
                pc.Parameters.AddWithValue("$fight", id);
                pc.Parameters.AddWithValue("$name", p.Name);
                pc.Parameters.AddWithValue("$class", p.ClassName);
                pc.Parameters.AddWithValue("$faction", p.Faction);
                pc.Parameters.AddWithValue("$self", p.IsSelf ? 1 : 0);
                pc.Parameters.AddWithValue("$enemy", p.IsEnemy ? 1 : 0);
                pc.Parameters.AddWithValue("$damage", p.Damage);
                pc.Parameters.AddWithValue("$dps", (object?)p.Dps ?? DBNull.Value);
                pc.Parameters.AddWithValue("$healing", p.Healing);
                pc.Parameters.AddWithValue("$taken", p.DamageTaken);
                pc.ExecuteNonQuery();
            }

            tx.Commit();
            return id;
        }
    }

    /// <summary>Newest first. <paramref name="search"/> matches the target name or any participant
    /// name, case-insensitive substring; null/empty lists everything (up to the limit).</summary>
    public List<FightSummary> Query(string? search, int limit = 500, bool onlyNotUploaded = false)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(search))
        {
            cmd.CommandText = "SELECT id, game, server_name, started_at, ended_at, target_name, kind, total_damage, participant_count, self_name, uploaded_at FROM fights"
                + (onlyNotUploaded ? " WHERE kind = 'boss' AND uploaded_at IS NULL" : "") + " ORDER BY started_at DESC LIMIT $limit;";
        }
        else
        {
            cmd.CommandText = """
                SELECT id, game, server_name, started_at, ended_at, target_name, kind, total_damage, participant_count, self_name, uploaded_at
                FROM fights
                WHERE (target_name LIKE $pattern
                   OR id IN (SELECT fight_id FROM participants WHERE name LIKE $pattern))
                   AND ($onlyNot = 0 OR (kind = 'boss' AND uploaded_at IS NULL))
                ORDER BY started_at DESC LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$pattern", "%" + search.Trim() + "%");
            cmd.Parameters.AddWithValue("$onlyNot", onlyNotUploaded ? 1 : 0);
        }
        cmd.Parameters.AddWithValue("$limit", limit);

        var rows = new List<FightSummary>();
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(ReadSummary(reader));
        }

        return rows;
    }

    public FightDetail? Load(long id)
    {
        FightSummary? summary;
        byte[] blob;
        byte[]? extrasBlob;
        using (SqliteCommand cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT id, game, server_name, started_at, ended_at, target_name, kind, total_damage, participant_count, self_name, uploaded_at, events_gz, extras_gz FROM fights WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            using SqliteDataReader reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            summary = ReadSummary(reader);
            blob = (byte[])reader["events_gz"];
            extrasBlob = reader["extras_gz"] as byte[];
        }

        var participants = new List<FightParticipant>();
        using (SqliteCommand cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name, class_name, faction, is_self, is_enemy, damage, dps, healing, damage_taken FROM participants WHERE fight_id = $id ORDER BY damage DESC;";
            cmd.Parameters.AddWithValue("$id", id);
            using SqliteDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                participants.Add(new FightParticipant(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetInt64(3) != 0, reader.GetInt64(4) != 0,
                    reader.GetInt64(5), reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    reader.GetInt64(7), reader.GetInt64(8)));
            }
        }

        (List<DamageEvent> events, Dictionary<int, string> names) = EventBlob.Unpack(blob);
        return new FightDetail(summary, participants, events, names, extrasBlob is { Length: > 0 } ? ExtrasBlob.Unpack(extrasBlob) : null);
    }

    /// <summary>Drops fights older than the retention window and, beyond that, everything past the
    /// newest <paramref name="maxFights"/>. Returns how many were removed.</summary>
    public int Prune(int retentionDays, int maxFights)
    {
        string cutoff = DateTime.Now.AddDays(-retentionDays).ToString("o");
        int removed = Execute("DELETE FROM fights WHERE started_at < $cutoff;", ("$cutoff", cutoff));
        removed += Execute(
            "DELETE FROM fights WHERE id NOT IN (SELECT id FROM fights ORDER BY started_at DESC LIMIT $max);",
            ("$max", maxFights));
        return removed;
    }

    public long Count()
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM fights;";
        return (long)cmd.ExecuteScalar()!;
    }

    public void Dispose() => _connection.Dispose();

    private static FightSummary ReadSummary(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
        DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetInt64(7),
        (int)reader.GetInt64(8),
        reader.IsDBNull(9) ? null : reader.GetString(9),
        reader.FieldCount > 10 && !reader.IsDBNull(10) && reader.GetFieldType(10) == typeof(string) ? reader.GetString(10) : null);

    private int Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        return cmd.ExecuteNonQuery();
    }
}
