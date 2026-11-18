using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2;

/// <summary>
/// What the checklist ticks by hand: the two weekly scroll purchases (12 Verteron scrolls, 20 Abyss scrolls from the NPC).
/// The shop's purchase counters (frames 0x038c / 0x018c, see NoteGoods) tell the meter what was bought; the hand tick is kept for what it did not see. (The five mandatory daily tasks come from the game: Aion2Tickets.MissionsId.) Per character, in %AppData%\Aion DPS Meter\aion2-checklist.json.
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

        /// <summary>What the game said about the shop goods of the scrolls (goods id, bought this week), see <see cref="NoteGoods"/>.</summary>
        public Dictionary<string, int> Goods { get; set; } = new();
    }

    /// <summary>The shop goods of the weekly Verteron scroll (Elyos 133019901, Asmodians 233019901; limit 12 per server and week).</summary>
    private static readonly uint[] VerteronGoods = { 133019901, 233019901 };

    /// <summary>The Abyss scroll goods by tier (Common, Rare, Legend, Unique): per tier the goods of both lists (134… and 140…, Elyos; 234… and 240…, Asmodians) share one limit of 5 a week, as the Wingbeat fork counts them
    /// (taken over without a recording of the second list, 18 November).</summary>
    private static readonly uint[][] AbyssTiers =
    {
        new uint[] { 134019901, 140019901, 234019901, 240019901 },
        new uint[] { 134019902, 140019902, 234019902, 240019902 },
        new uint[] { 134019903, 140019903, 234019903, 240019903 },
        new uint[] { 134019904, 140019904, 234019904, 240019904 },
    };

    private const int AbyssPerTier = 5;

    private static int GameSum(State state, uint[] goods)
    {
        int sum = 0;
        foreach (uint id in goods)
        {
            if (state.Goods.TryGetValue(id.ToString(System.Globalization.CultureInfo.InvariantCulture), out int bought))
            {
                sum += bought;
            }
        }

        return sum;
    }

    /// <summary>The Verteron scrolls bought this week: what the game said (the shop's purchase counter), or the hand-ticked count where that is higher.</summary>
    public static int VerteronCount(State state) => Math.Clamp(Math.Max(state.Verteron, GameSum(state, VerteronGoods)), 0, VerteronMax);

    /// <summary>The Abyss scrolls bought this week (same rule as <see cref="VerteronCount"/>).</summary>
    public static int AbyssCount(State state)
    {
        int game = 0;
        foreach (uint[] tier in AbyssTiers)
        {
            game += Math.Min(GameSum(state, tier), AbyssPerTier);
        }

        return Math.Clamp(Math.Max(state.Abyss, game), 0, AbyssMax);
    }

    /// <summary>The shop sends the weekly purchase counter of a goods (frames 0x038c at a purchase = the amount bought, added up; 0x018c when the shop opens = the week's total, replaces it); the scroll goods are kept, per server.</summary>
    public static void NoteGoods(IReadOnlyList<(uint Goods, int Bought)> list, DateTime now, bool added)
    {
        bool changed = false;
        lock (Gate)
        {
            State state = Get(SharedKey, now);
            foreach ((uint goods, int bought) in list)
            {
                if (Array.IndexOf(VerteronGoods, goods) < 0 && !AbyssTiers.Any(t => Array.IndexOf(t, goods) >= 0))
                {
                    continue;
                }

                string key = goods.ToString(System.Globalization.CultureInfo.InvariantCulture);
                // a purchase (0x038c) carries the amount of that purchase, the opened shop (0x018c) the week's total
                state.Goods.TryGetValue(key, out int old);
                int total = added ? old + bought : bought;
                if (total != old || !state.Goods.ContainsKey(key))
                {
                    state.Goods[key] = total;
                    changed = true;
                }
            }

            if (changed)
            {
                state.WeeklyAtMs = Ms(now);
                Save();
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
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
            state.Goods.Clear();
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
