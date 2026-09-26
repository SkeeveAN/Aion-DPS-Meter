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
    }

    public long Insert(FightDetail detail)
    {
        // A "Reload from Chat.log" re-parses fights the live recorder already filed; the same
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
                INSERT INTO fights (game, server_name, started_at, ended_at, target_name, kind, total_damage, participant_count, self_name, events_gz)
                VALUES ($game, $server, $started, $ended, $target, $kind, $total, $count, $self, $events);
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
    public List<FightSummary> Query(string? search, int limit = 500)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(search))
        {
            cmd.CommandText = "SELECT id, game, server_name, started_at, ended_at, target_name, kind, total_damage, participant_count, self_name FROM fights ORDER BY started_at DESC LIMIT $limit;";
        }
        else
        {
            cmd.CommandText = """
                SELECT id, game, server_name, started_at, ended_at, target_name, kind, total_damage, participant_count, self_name
                FROM fights
                WHERE target_name LIKE $pattern
                   OR id IN (SELECT fight_id FROM participants WHERE name LIKE $pattern)
                ORDER BY started_at DESC LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$pattern", "%" + search.Trim() + "%");
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
        using (SqliteCommand cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT id, game, server_name, started_at, ended_at, target_name, kind, total_damage, participant_count, self_name, events_gz FROM fights WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            using SqliteDataReader reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            summary = ReadSummary(reader);
            blob = (byte[])reader["events_gz"];
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
        return new FightDetail(summary, participants, events, names);
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
        reader.IsDBNull(9) ? null : reader.GetString(9));

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
