using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2;

/// <summary>
/// What the checklist ticks by hand: the two weekly scroll purchases (12 Verteron scrolls, 20 Abyss scrolls from the NPC).
/// The game sends nothing about them that the meter reads. (The five mandatory daily tasks come from the game: Aion2Tickets.MissionsId.) Per character, in %AppData%\Aion DPS Meter\aion2-checklist.json.
/// The weekly counters on Wednesday at 16:00 (16:00 Korean time, the game's own clock).
/// </summary>
public static class ChecklistManual
{
    /// <summary>The key of the weekly scroll counters: the shop limit is per server ("Pro Server 5/5"), so all characters share one.</summary>
    public const string SharedKey = "(server)";

    public const int VerteronMax = 12;
    public const int AbyssMax = 20;

    /// <summary>The two weekly conversions of Odyle energy (x16 and x4): the meter has not found their frame yet, so they are a note ticked by hand.</summary>
    public const int ConvertBigMax = 16;
    public const int ConvertSmallMax = 4;

    public sealed class State
    {
        public int Verteron { get; set; }
        public int Abyss { get; set; }
        public int ConvertBig { get; set; }
        public int ConvertSmall { get; set; }
        public long WeeklyAtMs { get; set; }
    }

    private sealed class Saved
    {
        public Dictionary<string, State> Characters { get; set; } = new();
    }

    private static readonly object Gate = new();
    private static Dictionary<string, State>? _states;

    /// <summary>A test uses its own file.</summary>
    public static string? PathOverride { get; set; }

    public static string StorePath => PathOverride ?? Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "aion2-checklist.json");

    /// <summary>Raised when a tick or a counter changed.</summary>
    public static event Action? Changed;

    /// <summary>The latest daily reset (16:00 on the game's clock, Korean time) at or before <paramref name="now"/>, as local time.</summary>
    public static DateTime LastDailyReset(DateTime now)
    {
        DateTime server = ServerTime.FromLocal(now);
        DateTime today = server.Date.AddHours(16);
        return ServerTime.ToLocal(today <= server ? today : today.AddDays(-1));
    }

    /// <summary>The latest weekly reset (Wednesday 16:00 on the game's clock, Korean time) at or before <paramref name="now"/>, as local time.</summary>
    public static DateTime LastWeeklyReset(DateTime now)
    {
        DateTime server = ServerTime.FromLocal(now);
        DateTime d = server.Date.AddHours(16);
        while (d > server || d.DayOfWeek != DayOfWeek.Wednesday)
        {
            d = d.AddDays(-1);
        }

        return ServerTime.ToLocal(d);
    }

    private static long Ms(DateTime local) => new DateTimeOffset(local).ToUnixTimeMilliseconds();

    private static DateTime FromMs(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;

    /// <summary>The state of the character with the resets since it was last written applied.</summary>
    public static State Get(string character, DateTime now)
    {
        lock (Gate)
        {
            Dictionary<string, State> all = Load();
            if (!all.TryGetValue(character, out State? state))
            {
                state = new State();
                all[character] = state;
            }

            ApplyResets(state, now);
            return state;
        }
    }

    private static void ApplyResets(State state, DateTime now)
    {
        if (state.WeeklyAtMs == 0 || FromMs(state.WeeklyAtMs) < LastWeeklyReset(now))
        {
            state.Verteron = 0;
            state.Abyss = 0;
            state.ConvertBig = 0;
            state.ConvertSmall = 0;
            state.WeeklyAtMs = Ms(now);
        }
    }

    /// <summary>The weekly counters that can be stepped by hand.</summary>
    public enum Counter { Verteron, Abyss, ConvertBig, ConvertSmall }

    /// <summary>Counts a hand-ticked entry up or down (0 to the maximum).</summary>
    public static void Step(string character, Counter counter, int delta, DateTime now)
    {
        lock (Gate)
        {
            State state = Get(character, now);
            switch (counter)
            {
                case Counter.Verteron:
                    state.Verteron = Math.Clamp(state.Verteron + delta, 0, VerteronMax);
                    break;
                case Counter.Abyss:
                    state.Abyss = Math.Clamp(state.Abyss + delta, 0, AbyssMax);
                    break;
                case Counter.ConvertBig:
                    state.ConvertBig = Math.Clamp(state.ConvertBig + delta, 0, ConvertBigMax);
                    break;
                default:
                    state.ConvertSmall = Math.Clamp(state.ConvertSmall + delta, 0, ConvertSmallMax);
                    break;
            }

            state.WeeklyAtMs = Ms(now);
            Save();
        }

        Changed?.Invoke();
    }

    /// <summary>Forget what was loaded (a test).</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _states = null;
        }
    }

    private static Dictionary<string, State> Load()
    {
        if (_states is not null)
        {
            return _states;
        }

        try
        {
            if (File.Exists(StorePath) && JsonSerializer.Deserialize<Saved>(File.ReadAllText(StorePath)) is { } saved)
            {
                return _states = saved.Characters;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // unreadable file: start empty
        }

        return _states = new Dictionary<string, State>();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(new Saved { Characters = Load() }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // not saved this time; the next change tries again
        }
    }
}
