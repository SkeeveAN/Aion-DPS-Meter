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
