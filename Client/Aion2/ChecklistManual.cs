using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2;

/// <summary>
/// What the checklist ticks by hand: the two weekly scroll purchases (12 Verteron scrolls, 20 Abyss scrolls from the NPC).
/// The game sends nothing about them that the meter reads. (The five mandatory daily tasks come from the game: Aion2Tickets.MissionsId.) Per character, in %AppData%\Aion DPS Meter\aion2-checklist.json.
/// The weekly counters on Wednesday at 16:00 (the times of the game's own counters; the PC's clock).
/// </summary>
public static class ChecklistManual
{
    /// <summary>The key of the weekly scroll counters: the shop limit is per server ("Pro Server 5/5"), so all characters share one.</summary>
    public const string SharedKey = "(server)";

    public const int VerteronMax = 12;
    public const int AbyssMax = 20;

    public sealed class State
    {
        public int Verteron { get; set; }
        public int Abyss { get; set; }
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

    /// <summary>The latest daily reset (16:00) at or before <paramref name="now"/>.</summary>
    public static DateTime LastDailyReset(DateTime now)
    {
        DateTime today = now.Date.AddHours(16);
        return today <= now ? today : today.AddDays(-1);
    }

    /// <summary>The latest weekly reset (Wednesday 16:00) at or before <paramref name="now"/>.</summary>
    public static DateTime LastWeeklyReset(DateTime now)
    {
        DateTime d = now.Date.AddHours(16);
        while (d > now || d.DayOfWeek != DayOfWeek.Wednesday)
        {
            d = d.AddDays(-1);
        }

        return d;
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
            state.WeeklyAtMs = Ms(now);
        }
    }

    /// <summary>Counts a scroll purchase up or down (0 to the maximum).</summary>
    public static void Step(string character, bool verteron, int delta, DateTime now)
    {
        lock (Gate)
        {
            State state = Get(character, now);
            if (verteron)
            {
                state.Verteron = Math.Clamp(state.Verteron + delta, 0, VerteronMax);
            }
            else
            {
                state.Abyss = Math.Clamp(state.Abyss + delta, 0, AbyssMax);
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
