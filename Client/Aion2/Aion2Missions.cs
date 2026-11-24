using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using AionDPS.Update;

namespace AionDPS.Aion2;

/// <summary>One step of a weekly season mission: the target count and the points the weekly reward gets when the step's reward is collected.</summary>
public sealed record MissionStep(int Step, int Count, int Points, IReadOnlyDictionary<string, string> Title, IReadOnlyDictionary<string, string> Text)
{
    public string TitleIn(string language) => Title.GetValueOrDefault(language) ?? Title.GetValueOrDefault("en") ?? "";

    public string TextIn(string language) => Text.GetValueOrDefault(language) ?? Text.GetValueOrDefault("en") ?? "";
}

/// <summary>A weekly season mission (the missions tab "weekly" of the season missions window) of one race.</summary>
public sealed record WeeklyMission(int Id, string Race, string Tab, string Kind, IReadOnlyList<MissionStep> Steps, IReadOnlyList<int> Npcs, string? Event);

/// <summary>The days (0 = Sunday) and times of day (hours, local) an event of the game's schedule happens.</summary>
public sealed record MissionEvent(IReadOnlyList<int> Days, IReadOnlyList<double> At);

/// <summary>
/// The weekly season missions of the game. The catalog (assets/aion2/season/weekly_missions.json, built by Tools/aion2-dat/build_season_missions.py from the game's
/// tables) lists every weekly mission with its steps and the points of each step; the frames bring the state of a character: the login's achievements frame
/// <c>0x8356</c> (all counters) and the live frame <c>0x8456</c> (a counter changed). Both read <c>n (varint) | n x ( id u32 | counter u32 | time u64 ms | level u8 )</c>;
/// the id is the achievement's id and the level byte the last step whose reward was COLLECTED (a finished step stays at the old level until the player collects it,
/// and only the collecting adds the step's points to the week's points; checked against a recording: counter 60 of 60 with level 0, after collecting level 1 and +140 points).
/// A character's missions are kept like its tickets (the id of the achievement as the ticket id, the counter as the count, the level as the bonus).
/// </summary>
public static class Aion2Missions
{
    private static readonly object Gate = new();
    private static Catalog? _catalog;

    private sealed record Catalog(IReadOnlyList<WeeklyMission> Missions, IReadOnlySet<int> Ids, IReadOnlyList<string> Tabs, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> TabNames,
        IReadOnlyDictionary<string, MissionEvent> Events);

    public readonly record struct Entry(uint Id, uint Count, ulong TimeMs, byte Level);

    public static IReadOnlyList<WeeklyMission> Missions => Load().Missions;

    /// <summary>The tabs of the window in the game's order (Abyss, Arena, Transcendence, Nightmare, Challenge).</summary>
    public static IReadOnlyList<string> Tabs => Load().Tabs;

    public static string TabName(string tab, string language)
    {
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> names = Load().TabNames;
        return names.TryGetValue(tab, out IReadOnlyDictionary<string, string>? byLanguage) ? byLanguage.GetValueOrDefault(language) ?? byLanguage.GetValueOrDefault("en") ?? tab : tab;
    }

    public static MissionEvent? EventOf(string key) => Load().Events.GetValueOrDefault(key);

    public static bool IsMission(int id) => Load().Ids.Contains(id);

    public static WeeklyMission? Find(int id) => Load().Missions.FirstOrDefault(m => m.Id == id);

    /// <summary>Test hook: a catalog of its own (null: read the file again).</summary>
    internal static void UseCatalogText(string? json)
    {
        lock (Gate)
        {
            _catalog = json is null ? null : Parse(json);
        }
    }

    private static Catalog Load()
    {
        lock (Gate)
        {
            if (_catalog is not null)
            {
                return _catalog;
            }

            try
            {
                string path = AssetStore.Path("aion2", "season", "weekly_missions.json");
                _catalog = File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
            {
                _catalog = Empty();
            }

            return _catalog;
        }
    }

    private static Catalog Empty() => new(Array.Empty<WeeklyMission>(), new HashSet<int>(), Array.Empty<string>(), new Dictionary<string, IReadOnlyDictionary<string, string>>(), new Dictionary<string, MissionEvent>());

    private static Dictionary<string, string> Texts(JsonElement element)
    {
        var result = new Dictionary<string, string>();
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in element.EnumerateObject())
            {
                result[p.Name] = p.Value.GetString() ?? "";
            }
        }

        return result;
    }

    private static Catalog Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        var missions = new List<WeeklyMission>();
        foreach (JsonElement m in root.GetProperty("missions").EnumerateArray())
        {
            var steps = new List<MissionStep>();
            foreach (JsonElement s in m.GetProperty("steps").EnumerateArray())
            {
                steps.Add(new MissionStep(s.GetProperty("step").GetInt32(), s.GetProperty("count").GetInt32(), s.GetProperty("points").GetInt32(), Texts(s.GetProperty("title")), Texts(s.GetProperty("text"))));
            }

            var npcs = new List<int>();
            if (m.TryGetProperty("npcs", out JsonElement npcList))
            {
                foreach (JsonElement n in npcList.EnumerateArray())
                {
                    npcs.Add(n.GetInt32());
                }
            }

            missions.Add(new WeeklyMission(m.GetProperty("id").GetInt32(), m.GetProperty("race").GetString() ?? "L", m.GetProperty("tab").GetString() ?? "", m.GetProperty("kind").GetString() ?? "",
                steps, npcs, m.TryGetProperty("event", out JsonElement ev) ? ev.GetString() : null));
        }

        var tabs = new List<string>();
        foreach (JsonElement t in root.GetProperty("tabs").EnumerateArray())
        {
            tabs.Add(t.GetString() ?? "");
        }

        var tabNames = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        foreach (JsonProperty p in root.GetProperty("tabNames").EnumerateObject())
        {
            tabNames[p.Name] = Texts(p.Value);
        }

        var events = new Dictionary<string, MissionEvent>();
        foreach (JsonProperty p in root.GetProperty("events").EnumerateObject())
        {
            events[p.Name] = new MissionEvent(p.Value.GetProperty("days").EnumerateArray().Select(d => d.GetInt32()).ToList(), p.Value.GetProperty("at").EnumerateArray().Select(d => d.GetDouble()).ToList());
        }

        return new Catalog(missions, missions.Select(m => m.Id).ToHashSet(), tabs, tabNames, events);
    }

    // ---- frames ----

    /// <summary>The entries of an achievements frame (<c>0x8356</c> at login, <c>0x8456</c> for a change): <c>n (varint) | n x 17 bytes</c> after the opcode.</summary>
    public static bool TryParseEntries(ReadOnlySpan<byte> frame, out List<Entry> entries)
    {
        entries = new List<Entry>();
        int p = 2, shift = 0;
        long n = 0;
        while (true)
        {
            if (p >= frame.Length || shift > 28)
            {
                return false;
            }

            byte b = frame[p++];
            n |= (long)(b & 0x7f) << shift;
            if ((b & 0x80) == 0)
            {
                break;
            }

            shift += 7;
        }

        if (n <= 0 || p + n * 17 > frame.Length)
        {
            return false;
        }

        for (long i = 0; i < n; i++, p += 17)
        {
            entries.Add(new Entry(BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]), BinaryPrimitives.ReadUInt32LittleEndian(frame[(p + 4)..]), BinaryPrimitives.ReadUInt64LittleEndian(frame[(p + 8)..]), frame[p + 16]));
        }

        return true;
    }

    /// <summary>
    /// What the meter keeps of an achievements frame: the week's points (as <see cref="Aion2Tickets.WeeklyPointsId"/>) and the counter and level of every weekly season mission
    /// (as a ticket with the achievement's id). Returns false when the frame holds none of them.
    /// </summary>
    public static bool TryParseReadings(ReadOnlySpan<byte> frame, out List<TicketReading> readings)
    {
        readings = new List<TicketReading>();
        if (!TryParseEntries(frame, out List<Entry> entries))
        {
            return false;
        }

        foreach (Entry e in entries)
        {
            if (Reading(e) is { } reading)
            {
                readings.Add(reading);
            }
        }

        return readings.Count > 0;
    }

    /// <summary>The reading of one entry, or null when it is neither the week's points nor a weekly mission.</summary>
    public static TicketReading? Reading(Entry e)
    {
        if (e.Id == Aion2Tickets.WeeklyPointsAchievement)
        {
            if (e.Count > 100_000 || e.TimeMs is < 1_500_000_000_000 or > 4_000_000_000_000)
            {
                return null;
            }

            return new TicketReading(Aion2Tickets.WeeklyPointsId, new TicketValue((int)e.Count, 0, (long)e.TimeMs));
        }

        if (e.Id <= int.MaxValue && IsMission((int)e.Id) && e.Count <= 100_000_000 && (e.TimeMs == 0 || e.TimeMs is >= 1_500_000_000_000 and <= 4_000_000_000_000))
        {
            return new TicketReading((int)e.Id, new TicketValue((int)e.Count, e.Level, (long)e.TimeMs));
        }

        return null;
    }
}
