using System.IO;
using Microsoft.Data.Sqlite;
using AionDPS.Aion2;

namespace AionDPS.History;

/// <summary>
/// What the game tells at login and while playing, written to the history database with the time, as a log of changes: the characters of the account (class, level, item
/// level, legion, last online), the counters of the checklist (tickets) and the times of the field bosses. A row is added only when a value differs from the last row of
/// its own, so the database grows with what changes. After a restart or a crash the program starts from the last rows; a login of any kind brings the real "now".
/// </summary>
public static class GameLog
{
    /// <summary>Writes the characters and tickets the checklist knows now (called after each frame that changed them, and once at the start).</summary>
    public static void WriteCharacters()
    {
        IReadOnlyList<TicketCharacter> known;
        try
        {
            known = Aion2Tickets.Snapshot();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return;
        }

        try
        {
            lock (FightHistory.Gate)
            {
                using SqliteConnection db = FightHistory.Open();
                using SqliteTransaction tx = db.BeginTransaction();
                long now = FightHistory.Ms(DateTime.Now);
                foreach (TicketCharacter c in known)
                {
                    long last = 0;
                    using (SqliteCommand latest = db.CreateCommand())
                    {
                        latest.Transaction = tx;
                        latest.CommandText = "SELECT class_code, level, item_level, IFNULL(legion,''), last_online_ms FROM character_log WHERE name=$n AND server_id=$s ORDER BY ts_ms DESC LIMIT 1";
                        latest.Parameters.AddWithValue("$n", c.Name);
                        latest.Parameters.AddWithValue("$s", c.ServerId);
                        using SqliteDataReader r = latest.ExecuteReader();
                        bool same = r.Read() && r.GetInt32(0) == c.ClassCode && r.GetInt32(1) == c.Level && r.GetInt32(2) == c.ItemLevel && r.GetString(3) == (c.Legion ?? "") && r.GetInt64(4) == c.LastOnlineMs;
                        last = same ? 1 : 0;
                    }

                    if (last == 0)
                    {
                        using SqliteCommand add = db.CreateCommand();
                        add.Transaction = tx;
                        add.CommandText = "INSERT INTO character_log VALUES ($t,$n,$s,$c,$l,$i,$g,$o)";
                        add.Parameters.AddWithValue("$t", now);
                        add.Parameters.AddWithValue("$n", c.Name);
                        add.Parameters.AddWithValue("$s", c.ServerId);
                        add.Parameters.AddWithValue("$c", c.ClassCode);
                        add.Parameters.AddWithValue("$l", c.Level);
                        add.Parameters.AddWithValue("$i", c.ItemLevel);
                        add.Parameters.AddWithValue("$g", (object?)c.Legion ?? DBNull.Value);
                        add.Parameters.AddWithValue("$o", c.LastOnlineMs);
                        add.ExecuteNonQuery();
                    }

                    foreach (var (id, value) in c.Tickets)
                    {
                        bool same;
                        using (SqliteCommand latest = db.CreateCommand())
                        {
                            latest.Transaction = tx;
                            latest.CommandText = "SELECT count, bonus, time_ms FROM ticket_log WHERE name=$n AND server_id=$s AND ticket_id=$i ORDER BY ts_ms DESC LIMIT 1";
                            latest.Parameters.AddWithValue("$n", c.Name);
                            latest.Parameters.AddWithValue("$s", c.ServerId);
                            latest.Parameters.AddWithValue("$i", id);
                            using SqliteDataReader r = latest.ExecuteReader();
                            same = r.Read() && r.GetInt32(0) == value.Count && r.GetInt32(1) == value.Bonus && r.GetInt64(2) == value.TimeMs;
                        }

                        if (!same)
                        {
                            using SqliteCommand add = db.CreateCommand();
                            add.Transaction = tx;
                            add.CommandText = "INSERT INTO ticket_log VALUES ($t,$n,$s,$i,$c,$b,$m)";
                            add.Parameters.AddWithValue("$t", now);
                            add.Parameters.AddWithValue("$n", c.Name);
                            add.Parameters.AddWithValue("$s", c.ServerId);
                            add.Parameters.AddWithValue("$i", id);
                            add.Parameters.AddWithValue("$c", value.Count);
                            add.Parameters.AddWithValue("$b", value.Bonus);
                            add.Parameters.AddWithValue("$m", value.TimeMs);
                            add.ExecuteNonQuery();
                        }
                    }
                }

                tx.Commit();
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            // not written now: the next change tries again
        }
    }

    /// <summary>Writes the field bosses whose state differs from their last row.</summary>
    public static void WriteBosses()
    {
        try
        {
            lock (FightHistory.Gate)
            {
                using SqliteConnection db = FightHistory.Open();
                using SqliteTransaction tx = db.BeginTransaction();
                DateTime nowTime = DateTime.Now;
                long now = FightHistory.Ms(nowTime);
                foreach (var map in Aion2FieldBosses.Maps)
                {
                    foreach (var boss in map.Bosses)
                    {
                        BossStatus st = Aion2FieldBosses.StatusOf(boss, nowTime);
                        if (st.SeenAt is not { } seen)
                        {
                            continue; // nothing the game said
                        }

                        long at = st.At is { } a ? FightHistory.Ms(a) : 0, seenMs = FightHistory.Ms(seen);
                        bool same;
                        using (SqliteCommand latest = db.CreateCommand())
                        {
                            latest.Transaction = tx;
                            latest.CommandText = "SELECT phase, at_ms, seen_ms FROM boss_log WHERE npc=$n ORDER BY ts_ms DESC LIMIT 1";
                            latest.Parameters.AddWithValue("$n", boss.Npc);
                            using SqliteDataReader r = latest.ExecuteReader();
                            same = r.Read() && r.GetInt32(0) == (int)st.Phase && r.GetInt64(1) == at && r.GetInt64(2) == seenMs;
                        }

                        if (!same)
                        {
                            using SqliteCommand add = db.CreateCommand();
                            add.Transaction = tx;
                            add.CommandText = "INSERT INTO boss_log VALUES ($t,$n,$p,$a,$s)";
                            add.Parameters.AddWithValue("$t", now);
                            add.Parameters.AddWithValue("$n", boss.Npc);
                            add.Parameters.AddWithValue("$p", (int)st.Phase);
                            add.Parameters.AddWithValue("$a", at);
                            add.Parameters.AddWithValue("$s", seenMs);
                            add.ExecuteNonQuery();
                        }
                    }
                }

                tx.Commit();
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            // not written now
        }
    }

    /// <summary>
    /// Keeps the database small without losing what still matters, once a day: the counters and characters of the log older than 90 days are thinned to the last value of each day
    /// (the state at the end of each day stays, the changes in between go), the field boss times older than 60 days go (a respawn time is only worth something until the boss is
    /// there again), then the file is compacted. Stays and boss fights are never touched (a few hundred bytes each).
    /// </summary>
    public static void Maintain()
    {
        try
        {
            lock (FightHistory.Gate)
            {
                using SqliteConnection db = FightHistory.Open();
                string today = "maintained|" + DateTime.Today.ToString("yyyy-MM-dd");
                using (SqliteCommand done = db.CreateCommand())
                {
                    done.CommandText = "SELECT 1 FROM files WHERE key=$k";
                    done.Parameters.AddWithValue("$k", today);
                    if (done.ExecuteScalar() is not null)
                    {
                        return;
                    }
                }

                long ninety = FightHistory.Ms(DateTime.Now.AddDays(-90)), sixty = FightHistory.Ms(DateTime.Now.AddDays(-60));
                int removed = 0;
                using (SqliteCommand thin = db.CreateCommand())
                {
                    // the last row of every day (per character and counter) stays
                    thin.CommandText = @"
DELETE FROM ticket_log WHERE ts_ms < $cut AND rowid NOT IN (
  SELECT MAX(rowid) FROM ticket_log WHERE ts_ms < $cut GROUP BY name, server_id, ticket_id, ts_ms / 86400000);
DELETE FROM character_log WHERE ts_ms < $cut AND rowid NOT IN (
  SELECT MAX(rowid) FROM character_log WHERE ts_ms < $cut GROUP BY name, server_id, ts_ms / 86400000);
DELETE FROM boss_log WHERE ts_ms < $boss;
DELETE FROM character_snapshot WHERE ts_ms < $cut AND rowid NOT IN (
  SELECT MAX(rowid) FROM character_snapshot WHERE ts_ms < $cut GROUP BY name, ts_ms / 86400000);";
                    thin.Parameters.AddWithValue("$cut", ninety);
                    thin.Parameters.AddWithValue("$boss", sixty);
                    removed = thin.ExecuteNonQuery();
                }

                using (SqliteCommand mark = db.CreateCommand())
                {
                    mark.CommandText = "DELETE FROM files WHERE key LIKE 'maintained|%'; INSERT INTO files (key) VALUES ($k);";
                    mark.Parameters.AddWithValue("$k", today);
                    mark.ExecuteNonQuery();
                }

                if (removed > 0)
                {
                    using SqliteCommand compact = db.CreateCommand();
                    compact.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); VACUUM;";
                    compact.ExecuteNonQuery();
                }
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            // tidied up the next time
        }
    }

    /// <summary>One look at the own character: the gear score (the item level of the list), the combat power, the worn pieces ("slot:item:enchant;") and the Daevanion boards ("board:nodes;").</summary>
    public sealed record Snapshot(DateTime At, int ItemLevel, int CombatPower, string Equip, string Boards);

    /// <summary>Writes a snapshot of the own character when anything differs from the last one.</summary>
    public static void WriteSnapshot(string name, int serverId, int itemLevel, int combatPower, string equip, string boards)
    {
        try
        {
            lock (FightHistory.Gate)
            {
                using SqliteConnection db = FightHistory.Open();
                using (SqliteCommand latest = db.CreateCommand())
                {
                    latest.CommandText = "SELECT item_level, combat_power, equip, boards FROM character_snapshot WHERE name=$n ORDER BY ts_ms DESC LIMIT 1";
                    latest.Parameters.AddWithValue("$n", name);
                    using SqliteDataReader r = latest.ExecuteReader();
                    if (r.Read() && r.GetInt32(0) == itemLevel && r.GetInt32(1) == combatPower && r.GetString(2) == equip && r.GetString(3) == boards)
                    {
                        return;
                    }
                }

                using SqliteCommand add = db.CreateCommand();
                add.CommandText = "INSERT INTO character_snapshot VALUES ($t,$n,$s,$i,$c,$e,$b)";
                add.Parameters.AddWithValue("$t", FightHistory.Ms(DateTime.Now));
                add.Parameters.AddWithValue("$n", name);
                add.Parameters.AddWithValue("$s", serverId);
                add.Parameters.AddWithValue("$i", itemLevel);
                add.Parameters.AddWithValue("$c", combatPower);
                add.Parameters.AddWithValue("$e", equip);
                add.Parameters.AddWithValue("$b", boards);
                add.ExecuteNonQuery();
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            // not written now
        }
    }

    /// <summary>One look at a character of the account as the list at a login showed it (read out of a recording, so it carries the time of the login).</summary>
    public sealed record CharacterRow(DateTime At, string Name, int ServerId, int ClassCode, int Level, int ItemLevel, string? Legion, long LastOnlineMs);

    /// <summary>Adds earlier looks (from recordings) to the log with their own times; a look equal to the one before it in time is left out.</summary>
    public static void AddHistoric(IEnumerable<CharacterRow> rows)
    {
        try
        {
            lock (FightHistory.Gate)
            {
                using SqliteConnection db = FightHistory.Open();
                using SqliteTransaction tx = db.BeginTransaction();
                foreach (CharacterRow c in rows.OrderBy(r => r.At))
                {
                    long at = FightHistory.Ms(c.At);
                    bool same;
                    using (SqliteCommand before = db.CreateCommand())
                    {
                        before.Transaction = tx;
                        before.CommandText = "SELECT class_code, level, item_level, IFNULL(legion,'') FROM character_log WHERE name=$n AND server_id=$s AND ts_ms<=$t ORDER BY ts_ms DESC LIMIT 1";
                        before.Parameters.AddWithValue("$n", c.Name);
                        before.Parameters.AddWithValue("$s", c.ServerId);
                        before.Parameters.AddWithValue("$t", at);
                        using SqliteDataReader r = before.ExecuteReader();
                        same = r.Read() && r.GetInt32(0) == c.ClassCode && r.GetInt32(1) == c.Level && r.GetInt32(2) == c.ItemLevel && r.GetString(3) == (c.Legion ?? "");
                    }

                    if (same)
                    {
                        continue;
                    }

                    using SqliteCommand add = db.CreateCommand();
                    add.Transaction = tx;
                    add.CommandText = "INSERT INTO character_log VALUES ($t,$n,$s,$c,$l,$i,$g,$o)";
                    add.Parameters.AddWithValue("$t", at);
                    add.Parameters.AddWithValue("$n", c.Name);
                    add.Parameters.AddWithValue("$s", c.ServerId);
                    add.Parameters.AddWithValue("$c", c.ClassCode);
                    add.Parameters.AddWithValue("$l", c.Level);
                    add.Parameters.AddWithValue("$i", c.ItemLevel);
                    add.Parameters.AddWithValue("$g", (object?)c.Legion ?? DBNull.Value);
                    add.Parameters.AddWithValue("$o", c.LastOnlineMs);
                    add.ExecuteNonQuery();
                }

                tx.Commit();
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            // not written now
        }
    }

    /// <summary>The gear score of a character as the log has it over time (from the logins), oldest first, as snapshots without worn pieces.</summary>
    public static List<Snapshot> GearScores(string name)
    {
        var list = new List<Snapshot>();
        try
        {
            lock (FightHistory.Gate)
            {
                using SqliteConnection db = FightHistory.Open();
                using SqliteCommand command = db.CreateCommand();
                command.CommandText = "SELECT ts_ms, item_level FROM character_log WHERE name=$n AND item_level > 0 ORDER BY ts_ms";
                command.Parameters.AddWithValue("$n", name);
                using SqliteDataReader r = command.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new Snapshot(FightHistory.Time(r.GetInt64(0)), r.GetInt32(1), 0, "", ""));
                }
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            // nothing logged yet
        }

        return list;
    }

    /// <summary>The snapshots of one character, oldest first.</summary>
    public static List<Snapshot> Snapshots(string name)
    {
        var list = new List<Snapshot>();
        try
        {
            lock (FightHistory.Gate)
            {
                using SqliteConnection db = FightHistory.Open();
                using SqliteCommand command = db.CreateCommand();
                command.CommandText = "SELECT ts_ms, item_level, combat_power, equip, boards FROM character_snapshot WHERE name=$n ORDER BY ts_ms";
                command.Parameters.AddWithValue("$n", name);
                using SqliteDataReader r = command.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new Snapshot(FightHistory.Time(r.GetInt64(0)), r.GetInt32(1), r.GetInt32(2), r.GetString(3), r.GetString(4)));
                }
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            // nothing logged yet
        }

        return list;
    }

    /// <summary>One character's development as the log saw it: the first and the last values with the time of the first.</summary>
    public sealed record Progress(string Name, DateTime Since, int FirstLevel, int FirstItemLevel, int Level, int ItemLevel, int ClassCode);

    public static List<Progress> Developments()
    {
        var list = new List<Progress>();
        try
        {
            lock (FightHistory.Gate)
            {
                using SqliteConnection db = FightHistory.Open();
                using SqliteCommand command = db.CreateCommand();
                command.CommandText = @"
SELECT f.name, f.ts_ms, f.level, f.item_level, l.level, l.item_level, l.class_code FROM character_log f
JOIN character_log l ON l.name = f.name AND l.server_id = f.server_id
WHERE f.ts_ms = (SELECT MIN(ts_ms) FROM character_log WHERE name=f.name AND server_id=f.server_id)
  AND l.ts_ms = (SELECT MAX(ts_ms) FROM character_log WHERE name=f.name AND server_id=f.server_id)
GROUP BY f.name, f.server_id ORDER BY l.item_level DESC";
                using SqliteDataReader r = command.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new Progress(r.GetString(0), FightHistory.Time(r.GetInt64(1)), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6)));
                }
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            // nothing logged yet
        }

        return list;
    }

    /// <summary>The character the log saw last with its last counters - what the program starts from when the checklist's own files are not there (after a crash, a new folder).</summary>
    public static TicketCharacter? LastKnownCharacter()
    {
        try
        {
            lock (FightHistory.Gate)
            {
                using SqliteConnection db = FightHistory.Open();
                string? name = null;
                int server = 0, cls = 0, level = 0, item = 0;
                string? legion = null;
                long online = 0, at = 0;
                using (SqliteCommand last = db.CreateCommand())
                {
                    last.CommandText = "SELECT name, server_id, class_code, level, item_level, legion, last_online_ms, ts_ms FROM character_log ORDER BY ts_ms DESC LIMIT 1";
                    using SqliteDataReader r = last.ExecuteReader();
                    if (!r.Read())
                    {
                        return null;
                    }

                    name = r.GetString(0);
                    server = r.GetInt32(1);
                    cls = r.GetInt32(2);
                    level = r.GetInt32(3);
                    item = r.GetInt32(4);
                    legion = r.IsDBNull(5) ? null : r.GetString(5);
                    online = r.GetInt64(6);
                    at = r.GetInt64(7);
                }

                var tickets = new Dictionary<int, TicketValue>();
                using (SqliteCommand t = db.CreateCommand())
                {
                    t.CommandText = "SELECT ticket_id, count, bonus, time_ms FROM ticket_log l WHERE name=$n AND server_id=$s AND ts_ms=(SELECT MAX(ts_ms) FROM ticket_log WHERE name=l.name AND server_id=l.server_id AND ticket_id=l.ticket_id)";
                    t.Parameters.AddWithValue("$n", name);
                    t.Parameters.AddWithValue("$s", server);
                    using SqliteDataReader r = t.ExecuteReader();
                    while (r.Read())
                    {
                        tickets[r.GetInt32(0)] = new TicketValue(r.GetInt32(1), r.GetInt32(2), r.GetInt64(3));
                    }
                }

                return new TicketCharacter(name, server, FightHistory.Time(at), tickets, level, null, cls, item, online, legion);
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
