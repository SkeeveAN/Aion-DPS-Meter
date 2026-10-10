using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2;

/// <summary>When a boss with a fixed time comes: every <c>EveryMinutes</c> from <c>Base</c> ("cycle"), or on the given weekdays at <c>At</c> ("weekly",
/// 0 = Sunday ... 6 = Saturday); it stays <c>OpenMinutes</c> minutes. Times are read like the timetable's: local time.</summary>
public sealed record FixedBossRule(string Kind, string Base, int EveryMinutes, IReadOnlyList<int> Days, string At, int OpenMinutes);

/// <summary>One field boss: its NPC id, where it stands (world units), its names, and the rule when it has a fixed time.</summary>
public sealed record FieldBoss(int Npc, double X, double Y, IReadOnlyDictionary<string, string> Names, FixedBossRule? Rule)
{
    public string NameIn(string language) => Names.GetValueOrDefault(language) ?? Names.GetValueOrDefault("en") ?? "#" + Npc;
}

/// <summary>A map with its field bosses in the order of their NPC ids (the in-game list numbers them 1..n in that order). <c>Id</c> is the number the
/// game's list carries (0 = not known yet, see <see cref="Aion2FieldBosses.Update"/>).</summary>
public sealed class FieldBossMap
{
    public FieldBossMap(string key, int id, IReadOnlyDictionary<string, string> names, IReadOnlyList<FieldBoss> bosses)
    {
        Key = key;
        Id = id;
        Names = names;
        Bosses = bosses;
    }

    public string Key { get; }

    public int Id { get; set; }

    public IReadOnlyDictionary<string, string> Names { get; }

    public IReadOnlyList<FieldBoss> Bosses { get; }

    public string NameIn(string language) => Names.GetValueOrDefault(language) ?? Names.GetValueOrDefault("en") ?? Key;
}

/// <summary>How a boss stands right now, for the page, the overlay and the sounds.</summary>
public enum BossPhase
{
    /// <summary>Nothing is known (the list on the in-game map was never opened and the boss has no fixed time).</summary>
    Unknown,

    /// <summary>It is there now (alive, or its time has come, or its fixed window is open).</summary>
    Now,

    /// <summary>It is dead and comes back at <see cref="BossStatus.At"/>.</summary>
    Respawn,

    /// <summary>It comes at a fixed time: the next one is <see cref="BossStatus.At"/>.</summary>
    Fixed,
}

public sealed record BossStatus(BossPhase Phase, DateTime? At, DateTime? SeenAt)
{
    public TimeSpan? Remaining(DateTime now) => At is { } at && at > now ? at - now : null;
}

/// <summary>
/// The field bosses and what the game's list of a map says about them. The server sends that list (opcode 01 91) while the field boss list of
/// the in-game map is up: the map number, and for every boss in the order of its NPC id "alive" (with its position) or "dead", and a time in
/// milliseconds since 1970 (UTC): when the living boss appeared, or when the dead one comes back. The list holds no NPC ids; the places are
/// matched with the NPC ids of the map (assets/aion2/bosses/field_bosses.json, built by Tools/aion2-dat/build_field_bosses.py).
/// The last list of every boss is kept in %AppData%\Aion DPS Meter\aion2-fieldbosses.json, so the page can show it with the game closed.
/// </summary>
public static class Aion2FieldBosses
{
    /// <summary>One entry of the game's list.</summary>
    public readonly record struct Slot(int Number, bool Alive, double X, double Y, long TimeMs);

    private sealed class Saved
    {
        public Dictionary<string, SavedState> Bosses { get; set; } = new();

        public Dictionary<string, int> MapIds { get; set; } = new();
    }

    private sealed class SavedState
    {
        public bool Alive { get; set; }

        public long TimeMs { get; set; }

        public long SeenMs { get; set; }
    }

    private static readonly object Gate = new();
    private static List<FieldBossMap>? _maps;
    private static readonly Dictionary<int, SavedState> States = new();
    private static bool _persist;
    private static bool _dirty;
    private static DateTime _savedAt = DateTime.MinValue;

    /// <summary>Raised (on the capturing thread) when a list changed the state of a boss.</summary>
    public static event Action? Changed;

    public static string StorePath => Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "aion2-fieldbosses.json");

    public static IReadOnlyList<FieldBossMap> Maps
    {
        get
        {
            lock (Gate)
            {
                return _maps ??= LoadMaps();
            }
        }
    }

    /// <summary>The meter (not the replay tools) keeps the state in a file between two starts.</summary>
    public static void EnablePersistence()
    {
        lock (Gate)
        {
            _persist = true;
            _maps ??= LoadMaps();
            Load();
        }
    }

    private static List<FieldBossMap> LoadMaps()
    {
        var maps = new List<FieldBossMap>();
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "bosses", "field_bosses.json");
            if (!File.Exists(path))
            {
                return maps;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (JsonElement m in doc.RootElement.GetProperty("maps").EnumerateArray())
            {
                var bosses = new List<FieldBoss>();
                foreach (JsonElement b in m.GetProperty("bosses").EnumerateArray())
                {
                    FixedBossRule? rule = null;
                    if (b.TryGetProperty("fixed", out JsonElement f))
                    {
                        rule = new FixedBossRule(
                            f.GetProperty("kind").GetString() ?? "",
                            f.TryGetProperty("base", out JsonElement bs) ? bs.GetString() ?? "00:00" : "00:00",
                            f.TryGetProperty("everyMinutes", out JsonElement em) ? em.GetInt32() : 0,
                            f.TryGetProperty("days", out JsonElement days) ? days.EnumerateArray().Select(d => d.GetInt32()).ToList() : new List<int>(),
                            f.TryGetProperty("at", out JsonElement at) ? at.GetString() ?? "00:00" : "00:00",
                            f.TryGetProperty("openMinutes", out JsonElement om) ? om.GetInt32() : 20);
                    }

                    bosses.Add(new FieldBoss(b.GetProperty("npc").GetInt32(), b.GetProperty("x").GetDouble(), b.GetProperty("y").GetDouble(),
                        b.GetProperty("names").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? ""), rule));
                }

                maps.Add(new FieldBossMap(m.GetProperty("key").GetString() ?? "", m.GetProperty("id").GetInt32(),
                    m.GetProperty("names").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? ""), bosses));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // no boss list: the page stays empty
        }

        return maps;
    }

    private static void Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return;
            }

            var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(StorePath));
            if (saved is null)
            {
                return;
            }

            foreach (var map in _maps!)
            {
                if (map.Id == 0 && saved.MapIds.TryGetValue(map.Key, out int id))
                {
                    map.Id = id;
                }
            }

            foreach (var (npc, state) in saved.Bosses)
            {
                if (int.TryParse(npc, out int id))
                {
                    States[id] = state;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // nothing saved
        }
    }

    /// <summary>Writes a state that is still waiting for the next save (called when the meter closes).</summary>
    public static void Flush()
    {
        lock (Gate)
        {
            _savedAt = DateTime.MinValue;
            Save();
        }
    }

    private static void Save()
    {
        if (!_persist || !_dirty || (DateTime.UtcNow - _savedAt).TotalSeconds < 10)
        {
            return;
        }

        try
        {
            var saved = new Saved
            {
                Bosses = States.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                MapIds = _maps!.Where(m => m.Id != 0).ToDictionary(m => m.Key, m => m.Id),
            };
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            string temp = StorePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(saved));
            File.Move(temp, StorePath, overwrite: true);
            _dirty = false;
            _savedAt = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // try again with the next list
        }
    }

    /// <summary>
    /// Reads the game's list: <c>opcode(2) | 0 u16 | map u32 | count u8 | count x { alive u8, place varint (map * 100 + n), [alive: x y z f32],
    /// [one more byte on some places], time i64 ms }</c>, then zeros. The one more byte shows only by the next place (or the end) not fitting
    /// otherwise, so both readings are tried. False for anything that does not read as such a list.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> frame, out int map, out List<Slot> slots)
    {
        slots = new List<Slot>();
        map = 0;
        if (frame.Length < 9 || frame[2] != 0 || frame[3] != 0)
        {
            return false;
        }

        map = (int)BitConverter.ToUInt32(frame[4..8]);
        int count = frame[8];
        if (map is < 1000 or > 99999 || count is 0 or > 64)
        {
            return false;
        }

        int o = 9;
        for (int n = 0; n < count; n++)
        {
            if (!ReadHeader(frame, o, map, out bool alive, out int number, out int at))
            {
                return false;
            }

            double x = 0, y = 0;
            if (alive)
            {
                if (at + 12 > frame.Length)
                {
                    return false;
                }

                x = BitConverter.ToSingle(frame.Slice(at, 4));
                y = BitConverter.ToSingle(frame.Slice(at + 4, 4));
                at += 12;
            }

            bool read = false;
            for (int extra = 0; extra <= 1 && !read; extra++)
            {
                int t = at + extra;
                if (t + 8 > frame.Length)
                {
                    break;
                }

                long time = BitConverter.ToInt64(frame.Slice(t, 8));
                if (time != 0 && (time < 1_600_000_000_000 || time > 2_600_000_000_000))
                {
                    continue;
                }

                int end = t + 8;
                bool last = n == count - 1;
                bool fits = last ? end <= frame.Length && frame[end..].IndexOfAnyExcept((byte)0) < 0 && frame.Length - end <= 8 : ReadHeader(frame, end, map, out _, out _, out _);
                if (!fits)
                {
                    continue;
                }

                slots.Add(new Slot(number, alive, x, y, time));
                o = end;
                read = true;
            }

            if (!read)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary><c>alive u8 (0 or 1), place varint</c> with the place inside this map's range.</summary>
    private static bool ReadHeader(ReadOnlySpan<byte> frame, int o, int map, out bool alive, out int number, out int next)
    {
        alive = false;
        number = 0;
        next = o;
        if (o >= frame.Length || frame[o] > 1)
        {
            return false;
        }

        alive = frame[o] == 1;
        int p = o + 1;
        long value = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            if (p >= frame.Length)
            {
                return false;
            }

            byte b = frame[p++];
            value |= (long)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                long place = value - (long)map * 100;
                if (place is < 1 or > 99)
                {
                    return false;
                }

                number = (int)place;
                next = p;
                return true;
            }
        }

        return false;
    }

    /// <summary>Takes a list the server sent: the state of every boss of that map is replaced.</summary>
    public static void Update(int mapNumber, IReadOnlyList<Slot> slots)
    {
        bool changed = false;
        lock (Gate)
        {
            _maps ??= LoadMaps();
            FieldBossMap? map = _maps.FirstOrDefault(m => m.Id == mapNumber) ?? LearnMap(mapNumber, slots);
            if (map is null)
            {
                return;
            }

            foreach (Slot slot in slots)
            {
                if (slot.Number < 1 || slot.Number > map.Bosses.Count)
                {
                    continue;
                }

                var state = new SavedState { Alive = slot.Alive, TimeMs = slot.TimeMs, SeenMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
                int npc = map.Bosses[slot.Number - 1].Npc;
                if (!States.TryGetValue(npc, out var old) || old.Alive != state.Alive || old.TimeMs != state.TimeMs)
                {
                    changed = true;
                }

                States[npc] = state;
            }

            if (changed)
            {
                _dirty = true;
            }

            Save();
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>A list for a map number nobody knows yet: it belongs to the map without a number whose boss count fits and whose bosses stand where
    /// the living ones of the list are.</summary>
    private static FieldBossMap? LearnMap(int mapNumber, IReadOnlyList<Slot> slots)
    {
        var alive = slots.Where(s => s.Alive).ToList();
        if (alive.Count == 0)
        {
            return null;
        }

        var fitting = _maps!.Where(m => m.Id == 0 && m.Bosses.Count == slots.Count
            && alive.All(s => s.Number <= m.Bosses.Count && Math.Abs(m.Bosses[s.Number - 1].X - s.X) < 500 && Math.Abs(m.Bosses[s.Number - 1].Y - s.Y) < 500)).ToList();
        if (fitting.Count != 1)
        {
            return null;
        }

        fitting[0].Id = mapNumber;
        _dirty = true;
        return fitting[0];
    }

    /// <summary>The boss's name; when several bosses of the map share it (the three Nahmas), they are numbered in the order of their NPC ids.</summary>
    public static string DisplayName(FieldBossMap map, FieldBoss boss, string language)
    {
        string name = boss.NameIn(language);
        var same = map.Bosses.Where(b => b.NameIn(language) == name).ToList();
        return same.Count > 1 ? $"{name} ({same.IndexOf(boss) + 1})" : name;
    }

    /// <summary>The map a boss is listed under, or null.</summary>
    public static FieldBossMap? MapOf(FieldBoss boss) => Maps.FirstOrDefault(m => m.Bosses.Contains(boss));

    /// <summary>Where a boss stands now: what the game last said, or its fixed time.</summary>
    public static BossStatus StatusOf(FieldBoss boss, DateTime now)
    {
        SavedState? state;
        lock (Gate)
        {
            States.TryGetValue(boss.Npc, out state);
        }

        // A boss with a fixed time (the Abyss) follows its schedule, not the last list the player happened to open on that map.
        if (boss.Rule is { } fixedRule && FixedWindow(fixedRule, now) is { } fixedWindow)
        {
            DateTime? fixedSeen = state is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(state.SeenMs).LocalDateTime;
            return fixedWindow.Start <= now ? new BossStatus(BossPhase.Now, fixedWindow.Start, fixedSeen) : new BossStatus(BossPhase.Fixed, fixedWindow.Start, fixedSeen);
        }

        if (state is not null)
        {
            DateTime seen = DateTimeOffset.FromUnixTimeMilliseconds(state.SeenMs).LocalDateTime;
            if (state.Alive)
            {
                return new BossStatus(BossPhase.Now, null, seen);
            }

            if (state.TimeMs > 0)
            {
                DateTime back = DateTimeOffset.FromUnixTimeMilliseconds(state.TimeMs).LocalDateTime;
                return back > now ? new BossStatus(BossPhase.Respawn, back, seen) : new BossStatus(BossPhase.Now, back, seen);
            }
        }

        return new BossStatus(BossPhase.Unknown, null, state is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(state.SeenMs).LocalDateTime);
    }

    /// <summary>The window that is open now, else the next one.</summary>
    public static (DateTime Start, DateTime End)? FixedWindow(FixedBossRule rule, DateTime now)
    {
        TimeSpan open = TimeSpan.FromMinutes(Math.Max(1, rule.OpenMinutes));
        var starts = new List<DateTime>();
        if (rule.Kind == "cycle" && rule.EveryMinutes > 0)
        {
            TimeSpan every = TimeSpan.FromMinutes(rule.EveryMinutes);
            DateTime baseTime = now.Date + ParseTime(rule.Base);
            long k = (long)Math.Floor((now - baseTime) / every);
            starts.Add(baseTime + every * (k - 1));
            starts.Add(baseTime + every * k);
            starts.Add(baseTime + every * (k + 1));
        }
        else if (rule.Kind == "weekly")
        {
            TimeSpan at = ParseTime(rule.At);
            for (int i = -1; i <= 8; i++)
            {
                DateTime day = now.Date.AddDays(i);
                if (rule.Days.Contains((int)day.DayOfWeek))
                {
                    starts.Add(day + at);
                }
            }
        }

        foreach (DateTime start in starts.OrderBy(s => s))
        {
            if (start + open > now)
            {
                return (start, start + open);
            }
        }

        return null;
    }

    private static TimeSpan ParseTime(string text) => TimeSpan.TryParse(text, out var t) ? t : TimeSpan.Zero;
}
