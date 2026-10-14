using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2;

/// <summary>One counted thing of the account-status frames: the game calls them contents tickets. Entry counts, reward counts, boss kill counts,
/// access times and the Odyle energy ("loot energy") are all tickets with an id (assets/aion2/tickets/tickets.json, built by
/// Tools/aion2-dat/build_tickets.py: the names are the game's own texts in every language).</summary>
public sealed record TicketDef(
    int Id, string Internal, IReadOnlyDictionary<string, string> Names, int Max, int SubscribeMax, bool PerServer, bool IsTime,
    string? DailyAt, int DailyCount, string? WeeklyAt, int WeeklyCount, DayOfWeek WeeklyDay, IReadOnlyDictionary<string, string>? ShortNames = null)
{
    public string NameIn(string language) => Names.GetValueOrDefault(language) ?? Names.GetValueOrDefault("en") ?? "#" + Id;

    /// <summary>The short name of the content ("Albtraum", "Täglicher Dungeon: Mysteriöser Riss"); the full name where there is none.</summary>
    public string ShortIn(string language) => ShortNames?.GetValueOrDefault(language) ?? ShortNames?.GetValueOrDefault("en") ?? NameIn(language);
}

/// <summary>What the game reports for one ticket: how many are left, the bonus (Odyle energy: the part from stones), the time left in milliseconds
/// (access-time tickets). A field the frame leaves out is zero.</summary>
public readonly record struct TicketValue(int Count, int Bonus, long TimeMs);

/// <summary>One entry of a frame.</summary>
public readonly record struct TicketReading(int Id, TicketValue Value);

/// <summary>What was last seen of one character's tickets.</summary>
public sealed record TicketCharacter(string Name, int ServerId, DateTime SavedAt, IReadOnlyDictionary<int, TicketValue> Tickets, int Level = 0);

/// <summary>
/// The tickets of the characters (Tools > Checklist; no overlay). The server sends them twice:
/// <list type="bullet">
/// <item><c>0x0b61</c> at login (within 0.1 s of the login record 0x2036, a few seconds <i>before</i> the character record names the character):
/// <c>n (u8) | n x (mask u8 | id u32 | fields)</c>;</item>
/// <item><c>0x0c61</c> for a change: <c>flag u8 | mask u8 | id u32 | fields | reason u8 | (flag 1: gained u32)</c>; reason 2 = the regular refill,
/// 3 = something was used, 1 = something was gained (the Odyle bonus: 630 -> 640 comes with 10).</item>
/// </list>
/// The fields are chosen by the mask: bit 1 = time left (u64 ms), bit 4 = count (varint), bit 8 = bonus (varint); a field that is not there is zero.
/// Verified on 25 login lists and 56 changes of the debug recordings of 2026-10-10 to 13: every frame reads to its last byte, the caps in the lists
/// are the table's caps (28 keys, 35 boss kills, 14 and 7 reward counts, 540 of 560 Odyle energy).
/// The last list of every character is kept in %AppData%\Aion DPS Meter\aion2-tickets.json.
/// </summary>
public static class Aion2Tickets
{
    public const int OdyleId = 60000001;

    /// <summary>The character level a counter starts at (what the client's ContentsUnlock table says; below it nothing is counted or collected).
    /// Shugo Festival: the quest of level 13 ("Erscheinung im Rauch" / "Verspätete Verfolgung"); the Odyle energy: the level 22 story quest
    /// ("Handelsgilde Brisenwind" / "Der Daeva, der im Traum verweilte"), here taken as level 22; the daily dungeon 30; Nightmare, Ascension rite,
    /// Dimensional Invasion and the Abyss 45.</summary>
    private static readonly Dictionary<int, int> UnlockLevels = new()
    {
        [1] = 13, [7] = 13, [OdyleId] = 22, [101] = 30, [4] = 45, [8] = 45, [80000001] = 45, [3] = 45,
    };

    /// <summary>The level the counter needs; 0 = no known limit.</summary>
    public static int UnlockLevel(int ticketId) => UnlockLevels.GetValueOrDefault(ticketId);

    /// <summary>True when the character's level is known and below what the counter needs (a character seen before levels were saved counts as unlocked).</summary>
    public static bool IsLocked(TicketCharacter character, int ticketId) => character.Level > 0 && UnlockLevel(ticketId) > character.Level;

    /// <summary>The mandatory daily tasks ("Missions" of the journal: five a day) as a ticket of our own: count = handed in, bonus = taken. The id is no game id.</summary>
    public const int MissionsId = 99000001;
    public const int MissionsPerDay = 5;
    public const int MaxCharacters = 40;

    private sealed class Saved
    {
        public List<SavedCharacter> Characters { get; set; } = new();
    }

    private sealed class SavedCharacter
    {
        public string Name { get; set; } = "";
        public int ServerId { get; set; }
        public long SavedMs { get; set; }
        public int Level { get; set; }
        public Dictionary<int, long[]> Tickets { get; set; } = new();
    }

    private static readonly object Gate = new();
    private static List<TicketDef>? _catalog;
    private static readonly Dictionary<string, TicketCharacter> Characters = new(StringComparer.Ordinal);
    private static string? _current;
    private static bool _awaitingName;
    private static Dictionary<int, TicketValue>? _pending;
    private static DateTime _pendingAt;
    private static bool _persist;
    private static bool _dirty;
    private static DateTime _savedAt = DateTime.MinValue;

    /// <summary>Raised (on the capturing thread) when a frame changed what is known.</summary>
    public static event Action? Changed;

    public static string StorePath => Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "aion2-tickets.json");

    public static IReadOnlyList<TicketDef> Catalog
    {
        get
        {
            lock (Gate)
            {
                return _catalog ??= LoadCatalog();
            }
        }
    }

    public static TicketDef? Find(int id) => Catalog.FirstOrDefault(t => t.Id == id);

    /// <summary>The meter (not the replay tools) keeps the characters in a file between two starts.</summary>
    public static void EnablePersistence()
    {
        lock (Gate)
        {
            _persist = true;
            Load();
        }
    }

    /// <summary>The characters seen so far, the one logged in last first.</summary>
    public static IReadOnlyList<TicketCharacter> Snapshot()
    {
        lock (Gate)
        {
            return Characters.Values.OrderByDescending(c => c.SavedAt).ToList();
        }
    }

    /// <summary>The name of the character logged in now (null until the game named one).</summary>
    public static string? CurrentName
    {
        get
        {
            lock (Gate)
            {
                return _current;
            }
        }
    }

    /// <summary>Forget everything (a test, a replay).</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            Characters.Clear();
            _current = null;
            _awaitingName = false;
            _pending = null;
            _dirty = false;
        }
    }

    // ---- frames ----

    /// <summary>The login list <c>0x0b61</c>. The frame (opcode included) must read to its last byte.</summary>
    public static bool TryParseList(ReadOnlySpan<byte> frame, out List<TicketReading> entries)
    {
        entries = new List<TicketReading>();
        if (frame.Length < 3)
        {
            return false;
        }

        int count = frame[2];
        int p = 3;
        for (int i = 0; i < count; i++)
        {
            if (!TryReadEntry(frame, ref p, out TicketReading reading))
            {
                return false;
            }

            entries.Add(reading);
        }

        return p == frame.Length;
    }

    /// <summary>The quest list <c>0x1c8d</c> (right after the login list and again at every hand-in): its head holds how many of the day's five missions are
    /// taken and how many are handed in. Seen in the recordings of 12 to 14 October: 0/0 at the start of the day, the second number counts 1 to 5 with every hand-in.</summary>
    public static bool TryParseMissions(ReadOnlySpan<byte> frame, out TicketReading reading)
    {
        reading = default;
        if (frame.Length < 14)
        {
            return false;
        }

        uint taken = BinaryPrimitives.ReadUInt32LittleEndian(frame[2..]);
        uint done = BinaryPrimitives.ReadUInt32LittleEndian(frame[6..]);
        if (taken > MissionsPerDay || done > MissionsPerDay)
        {
            return false;
        }

        reading = new TicketReading(MissionsId, new TicketValue((int)done, (int)taken, 0));
        return true;
    }

    /// <summary>A change <c>0x0c61</c>. <paramref name="reason"/>: 2 refill, 3 used, 1 gained.</summary>
    public static bool TryParseUpdate(ReadOnlySpan<byte> frame, out TicketReading reading, out int reason, out int gained)
    {
        reading = default;
        reason = 0;
        gained = 0;
        if (frame.Length < 3)
        {
            return false;
        }

        int flag = frame[2];
        int p = 3;
        if (!TryReadEntry(frame, ref p, out reading) || p >= frame.Length)
        {
            return false;
        }

        reason = frame[p++];
        if ((flag & 1) != 0)
        {
            if (p + 4 > frame.Length)
            {
                return false;
            }

            gained = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]);
            p += 4;
        }

        return p == frame.Length;
    }

    private static bool TryReadEntry(ReadOnlySpan<byte> frame, ref int p, out TicketReading reading)
    {
        reading = default;
        if (p + 5 > frame.Length)
        {
            return false;
        }

        int mask = frame[p++];
        if ((mask & ~0x0d) != 0)
        {
            return false;
        }

        int id = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]);
        p += 4;
        long time = 0;
        long count = 0;
        long bonus = 0;
        if ((mask & 1) != 0)
        {
            if (p + 8 > frame.Length)
            {
                return false;
            }

            time = (long)BinaryPrimitives.ReadUInt64LittleEndian(frame[p..]);
            p += 8;
        }

        if ((mask & 4) != 0 && !TryReadVarint(frame, ref p, out count))
        {
            return false;
        }

        if ((mask & 8) != 0 && !TryReadVarint(frame, ref p, out bonus))
        {
            return false;
        }

        if (count < 0 || count > 10_000_000 || bonus < 0 || bonus > 10_000_000)
        {
            return false;
        }

        reading = new TicketReading(id, new TicketValue((int)count, (int)bonus, time));
        return true;
    }

    private static bool TryReadVarint(ReadOnlySpan<byte> frame, ref int p, out long value)
    {
        value = 0;
        int shift = 0;
        while (p < frame.Length && shift < 35)
        {
            byte b = frame[p++];
            value |= (long)(b & 0x7f) << shift;
            if ((b & 0x80) == 0)
            {
                return true;
            }

            shift += 7;
        }

        return false;
    }

    // ---- state ----

    /// <summary>The login list arrived. It belongs to the character that is logging in, whose name the game sends a few seconds later
    /// (<see cref="NoteCharacter"/>): until then it waits, and so do the changes that follow it.</summary>
    public static void ReceiveList(IReadOnlyList<TicketReading> entries, DateTime at)
    {
        lock (Gate)
        {
            _pending = entries.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.Last().Value);
            _pendingAt = at;
            _awaitingName = true;
        }
    }

    public static void ReceiveUpdate(TicketReading reading, DateTime at)
    {
        bool changed = false;
        lock (Gate)
        {
            if (_awaitingName && _pending is not null)
            {
                _pending[reading.Id] = reading.Value;
            }
            else if (_current is { } name && Characters.TryGetValue(name, out TicketCharacter? known))
            {
                var tickets = new Dictionary<int, TicketValue>(known.Tickets) { [reading.Id] = reading.Value };
                Characters[name] = known with { SavedAt = DateTime.UtcNow, Tickets = tickets };
                _dirty = true;
                changed = true;
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>The game named the character that is logged in (a live record, not one restored from disk); <paramref name="receivedAt"/> is when the
    /// character record came. The login frames between the list and that record announce the old character again: only a record received after
    /// the list names the one the list belongs to.</summary>
    public static void NoteCharacter(string name, int serverId, DateTime receivedAt, int level = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        bool changed = false;
        lock (Gate)
        {
            bool named = !_awaitingName || receivedAt >= _pendingAt;
            if (named)
            {
                _current = name;
            }

            if (_awaitingName && _pending is not null && receivedAt >= _pendingAt)
            {
                Characters[name] = new TicketCharacter(name, serverId, DateTime.UtcNow, _pending, level);
                _pending = null;
                _awaitingName = false;
                _dirty = true;
                changed = true;
                Trim();
            }
            else if (Characters.TryGetValue(name, out TicketCharacter? known) && ((serverId > 0 && known.ServerId != serverId) || (level > 0 && known.Level != level)))
            {
                Characters[name] = known with { ServerId = serverId > 0 ? serverId : known.ServerId, Level = level > 0 ? level : known.Level };
                _dirty = true;
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private static void Trim()
    {
        while (Characters.Count > MaxCharacters)
        {
            string oldest = Characters.Values.OrderBy(c => c.SavedAt).First().Name;
            Characters.Remove(oldest);
        }
    }

    // ---- what is to come ----

    /// <summary>The next refill of the ticket after <paramref name="now"/>: its daily time (every day) or its weekly one (the weekday at that time); null = none.
    /// The game's times are the server's local time; for the EU servers that is the player's own clock.</summary>
    public static DateTime? NextReset(TicketDef def, DateTime now)
    {
        DateTime? best = null;
        if (TimeSpan.TryParse(def.DailyAt, out TimeSpan daily) && def.DailyCount > 0)
        {
            DateTime d = now.Date + daily;
            best = d > now ? d : d.AddDays(1);
        }

        if (TimeSpan.TryParse(def.WeeklyAt, out TimeSpan weekly) && def.WeeklyCount > 0)
        {
            DateTime w = now.Date + weekly;
            while (w <= now || w.DayOfWeek != def.WeeklyDay)
            {
                w = w.AddDays(1);
            }

            best = best is { } b && b < w ? b : w;
        }

        return best;
    }

    /// <summary>What the count should be now, from what was reported at <paramref name="seenAt"/> (local time) and the refills since: a daily
    /// refill adds its amount up to the cap, a weekly one sets the weekly amount. A guess, not what the game says: the window marks it with "~".</summary>
    public static int Expected(TicketDef def, int count, DateTime seenAt, DateTime now)
    {
        int cap = Math.Max(def.Max, def.SubscribeMax > 0 && count > def.Max ? def.SubscribeMax : 0);
        int value = count;
        if (TimeSpan.TryParse(def.WeeklyAt, out TimeSpan weekly) && def.WeeklyCount > 0)
        {
            DateTime w = seenAt.Date + weekly;
            while (w <= seenAt || w.DayOfWeek != def.WeeklyDay)
            {
                w = w.AddDays(1);
            }

            if (w <= now)
            {
                value = def.WeeklyCount;
            }
        }

        if (TimeSpan.TryParse(def.DailyAt, out TimeSpan daily) && def.DailyCount > 0)
        {
            DateTime d = seenAt.Date + daily;
            if (d <= seenAt)
            {
                d = d.AddDays(1);
            }

            for (; d <= now && value < cap; d = d.AddDays(1))
            {
                value = Math.Min(cap, value + def.DailyCount);
            }
        }

        return value;
    }

    // ---- catalog and file ----

    private static List<TicketDef> LoadCatalog()
    {
        var list = new List<TicketDef>();
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "tickets", "tickets.json");
            if (!File.Exists(path))
            {
                return list;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (JsonElement t in doc.RootElement.GetProperty("tickets").EnumerateArray())
            {
                var names = new Dictionary<string, string>();
                foreach (JsonProperty n in t.GetProperty("names").EnumerateObject())
                {
                    names[n.Name] = n.Value.GetString() ?? "";
                }

                string? dailyAt = null;
                int dailyCount = 0;
                if (t.TryGetProperty("daily", out JsonElement daily))
                {
                    dailyAt = daily.GetProperty("at").GetString();
                    dailyCount = daily.GetProperty("count").GetInt32();
                }

                string? weeklyAt = null;
                int weeklyCount = 0;
                var weeklyDay = DayOfWeek.Wednesday;
                if (t.TryGetProperty("weekly", out JsonElement weekly))
                {
                    weeklyAt = weekly.GetProperty("at").GetString();
                    weeklyCount = weekly.GetProperty("count").GetInt32();
                    Enum.TryParse(weekly.GetProperty("day").GetString(), out weeklyDay);
                }

                Dictionary<string, string>? shortNames = null;
                if (t.TryGetProperty("short", out JsonElement shortElement))
                {
                    shortNames = new Dictionary<string, string>();
                    foreach (JsonProperty n in shortElement.EnumerateObject())
                    {
                        shortNames[n.Name] = n.Value.GetString() ?? "";
                    }
                }

                list.Add(new TicketDef(
                    t.GetProperty("id").GetInt32(), t.GetProperty("internal").GetString() ?? "", names,
                    t.GetProperty("max").GetInt32(), t.GetProperty("subscribeMax").GetInt32(),
                    t.GetProperty("perServer").GetBoolean(), t.GetProperty("time").GetBoolean(),
                    dailyAt, dailyCount, weeklyAt, weeklyCount, weeklyDay, shortNames));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // a missing or broken table: the window then shows the ids; the meter keeps working
        }

        return list;
    }

    private static void Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return;
            }

            Saved? saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(StorePath));
            if (saved is null)
            {
                return;
            }

            foreach (SavedCharacter c in saved.Characters)
            {
                var tickets = c.Tickets.Where(t => t.Value.Length == 3)
                    .ToDictionary(t => t.Key, t => new TicketValue((int)t.Value[0], (int)t.Value[1], t.Value[2]));
                Characters[c.Name] = new TicketCharacter(c.Name, c.ServerId, DateTime.SpecifyKind(DateTimeOffset.FromUnixTimeMilliseconds(c.SavedMs).DateTime, DateTimeKind.Utc), tickets, c.Level);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // unreadable file: start empty
        }
    }

    /// <summary>Writes the file when something changed (at most every few seconds; the meter calls it on a timer and on exit).</summary>
    public static void Flush(bool force = false)
    {
        Saved saved;
        lock (Gate)
        {
            if (!_persist || !_dirty || (!force && DateTime.UtcNow - _savedAt < TimeSpan.FromSeconds(5)))
            {
                return;
            }

            saved = new Saved
            {
                Characters = Characters.Values.Select(c => new SavedCharacter
                {
                    Name = c.Name,
                    ServerId = c.ServerId,
                    SavedMs = new DateTimeOffset(c.SavedAt, TimeSpan.Zero).ToUnixTimeMilliseconds(),
                    Level = c.Level,
                    Tickets = c.Tickets.ToDictionary(t => t.Key, t => new[] { (long)t.Value.Count, t.Value.Bonus, t.Value.TimeMs }),
                }).ToList(),
            };
            _dirty = false;
            _savedAt = DateTime.UtcNow;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(saved));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // not saved this time; the next change tries again
        }
    }
}
