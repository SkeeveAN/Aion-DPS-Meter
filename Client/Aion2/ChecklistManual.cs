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

        /// <summary>The Abyss Corridors the character entered (see <see cref="NoteCorridorEntry"/>), oldest first. Not cleared by the weekly reset: the corridors have their own reset times (<see cref="LastCorridorReset"/>).</summary>
        public List<CorridorEntry> CorridorEntries { get; set; } = new();

        /// <summary>When the game granted the full usage time of a corridor (<see cref="CorridorFullMs"/>), seen at the Abyss join: the corridor is open to this character for the period. If its time is 0 later, it has been used.</summary>
        public List<CorridorEntry> CorridorGrants { get; set; } = new();

        /// <summary>Corridors the game gave no usage time at the Abyss join (0 with the refill reason): not available to this character for the period (the game says "Unavailable").</summary>
        public List<CorridorEntry> CorridorClosed { get; set; } = new();

        /// <summary>Which side of the Abyss matchup holds each artifact of the lower level (Zone = artifact id 1001-1003, Side = 1 or 2), as the game sent it when the character entered the lower Abyss.</summary>
        public List<OwnerEntry> CorridorOwners { get; set; } = new();

        /// <summary>The side (1 or 2) this character's server fights on, learned from a corridor that was open to it (0 = not known yet); the corridors of the artifacts the other side holds are unavailable.</summary>
        public int OurSide { get; set; }
    }

    /// <summary>One visit of an Abyss Corridor: when (Unix ms) and which one (zone id 503001 and up).</summary>
    public sealed class CorridorEntry
    {
        public long Ms { get; set; }
        public int Zone { get; set; }
    }

    /// <summary>How long entries are kept.</summary>
    private static readonly TimeSpan CorridorKeep = TimeSpan.FromDays(60);

    /// <summary>The corridors reset on Thursday and Saturday at 21:30 local time (the user's information, 19 November; the entries of that Thursday began at 21:48).</summary>
    public static DateTime LastCorridorReset(DateTime now)
    {
        DateTime d = now.Date.AddHours(21).AddMinutes(30);
        while (d > now || d.DayOfWeek is not (DayOfWeek.Thursday or DayOfWeek.Saturday))
        {
            d = d.AddDays(-1);
        }

        return d;
    }

    /// <summary>The character entered an Abyss Corridor (a zone frame with a new corridor zone id, 503001 and up; seen 19 November 21:48 and 21:58, each time from the lower Reshanta map).</summary>
    public static void NoteCorridorEntry(string character, int zone, DateTime now)
    {
        lock (Gate)
        {
            State state = Get(character, now);
            long ms = Ms(now);
            state.CorridorEntries.RemoveAll(e => e.Ms < ms - (long)CorridorKeep.TotalMilliseconds);
            state.CorridorEntries.Add(new CorridorEntry { Ms = ms, Zone = zone });
            Calibrate(state, zone, now);
            Save();
        }

        Changed?.Invoke();
    }

    /// <summary>The three corridors of the lower level: zone id and the artifact each is named after (the client's own texts, per language; other languages use English). The zone frames
    /// name the corridor by id; which artifact belongs to which id is the client's entrance table (503001 spends the ticket of the Sulfur Tree Island).</summary>
    public static readonly (int Zone, Dictionary<string, string> Names)[] Corridors =
    {
        (503001, new() { ["en"] = "Sulfur Tree Island", ["de"] = "Schwefelbauminsel", ["fr"] = "Île des Arbres de Soufre", ["es"] = "Isla del Árbol de Azufre", ["ru"] = "Остров серного дерева", ["pt"] = "Ilha da Árvore Sulfúrica", ["ja"] = "硫黄木の島", ["ko"] = "유황나무섬" }),
        (503002, new() { ["en"] = "Archipelago of Siel's Wings", ["de"] = "Siels Schwingen-Archipel", ["fr"] = "Archipel des ailes de Siel", ["es"] = "Archipiélago Alas de Siel", ["ru"] = "Архипелаг крыльев Сиэль", ["pt"] = "Arquipélago de Asas de Siel", ["ja"] = "シエルの翼の群島", ["ko"] = "시엘의 날개 군도" }),
        (503003, new() { ["en"] = "Reshanta's Root", ["de"] = "Wurzel von Reshanta", ["fr"] = "Racines de Reshanta", ["es"] = "Raíces de Reshanta", ["ru"] = "Корни Арэшурата", ["pt"] = "Raiz de Reshanta", ["ja"] = "エレシュランタの根", ["ko"] = "에레슈란타의 뿌리" }),
    };

    /// <summary>The holder of one artifact (see <see cref="State.CorridorOwners"/>).</summary>
    public sealed class OwnerEntry
    {
        public long Ms { get; set; }
        public int Artifact { get; set; }
        public int Side { get; set; }
    }

    /// <summary>The artifact whose capture opens a corridor (ticket 10000002 = Sulfur Tree Island = artifact 1002, and so on, in the order of the client's artifact list).</summary>
    public static int ArtifactOfZone(int zone) => zone switch { 503001 => 1002, 503002 => 1003, 503003 => 1001, _ => 0 };

    /// <summary>The game sent the holders of the three artifacts of the lower Abyss (frame 0xe305, at every entry of the lower Abyss). The side of the character is learned from the corridors that were open to it.</summary>
    public static void NoteCorridorOwners(string character, IReadOnlyList<(int Artifact, int Side)> owners, IReadOnlyDictionary<int, long> ticketTimes, DateTime now)
    {
        bool changed = false;
        lock (Gate)
        {
            State state = Get(character, now);
            long ms = Ms(now);
            var fresh = owners.Select(o => new OwnerEntry { Ms = ms, Artifact = o.Artifact, Side = o.Side }).ToList();
            changed = !state.CorridorOwners.Select(e => (e.Artifact, e.Side)).SequenceEqual(fresh.Select(e => (e.Artifact, e.Side))) || state.CorridorOwners.Any(e => e.Ms < Ms(LastCorridorReset(now)));
            state.CorridorOwners = fresh;
            // a corridor that is open (full time) is one of ours, whatever the tickets said at the login before
            foreach ((int zone, _) in Corridors)
            {
                int ticket = zone switch { 503001 => 10000002, 503002 => 10000003, _ => 10000001 };
                if (ticketTimes.TryGetValue(ticket, out long remaining) && remaining >= CorridorFullMs)
                {
                    changed |= Calibrate(state, zone, now);
                }
            }

            changed |= CalibrateFromHistory(state, now);
            if (changed)
            {
                Save();
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>The corridor was open to the character (a grant, a visit): the side holding its artifact is the character's side.</summary>
    private static bool Calibrate(State state, int zone, DateTime now)
    {
        int artifact = ArtifactOfZone(zone);
        OwnerEntry? owner = state.CorridorOwners.FirstOrDefault(o => o.Artifact == artifact && o.Ms >= Ms(LastCorridorReset(now)));
        if (owner is null || owner.Side == state.OurSide)
        {
            return false;
        }

        state.OurSide = owner.Side;
        return true;
    }

    private static bool CalibrateFromHistory(State state, DateTime now)
    {
        long from = Ms(LastCorridorReset(now));
        bool changed = false;
        foreach (CorridorEntry e in state.CorridorGrants.Concat(state.CorridorEntries).Where(e => e.Ms >= from))
        {
            changed |= Calibrate(state, e.Zone, now);
        }

        return changed;
    }

    /// <summary>The usage time every corridor holds per period (the game's ticket: 130 seconds).</summary>
    public const long CorridorFullMs = 130_000;

    /// <summary>The usage-time ticket of each corridor (the client's entrance table: 503001 spends 10000002, 503002 10000003, 503003 10000001).</summary>
    public static int CorridorOfTicket(int ticketId) => ticketId switch { 10000002 => 503001, 10000003 => 503002, 10000001 => 503003, _ => 0 };

    /// <summary>The usage time of a corridor's ticket as the game reported it (an update, or the login list): the full time = open and unused (remembered), less than the full time but
    /// more than 0 = used in part (entered), 0 after the full time was seen in this period = used up (entered). A 0 alone says nothing: unused and not available look the same.</summary>
    public static void NoteCorridorTime(string character, int ticketId, long remainingMs, DateTime now, bool atJoin = false)
    {
        int zone = CorridorOfTicket(ticketId);
        if (zone == 0)
        {
            return;
        }

        bool changed = false;
        lock (Gate)
        {
            State state = Get(character, now);
            long from = Ms(LastCorridorReset(now));
            if (atJoin && remainingMs == 0)
            {
                if (!state.CorridorClosed.Any(g => g.Ms >= from && g.Zone == zone))
                {
                    state.CorridorClosed.Add(new CorridorEntry { Ms = Ms(now), Zone = zone });
                    changed = true;
                }
            }
            else if (remainingMs >= CorridorFullMs)
            {
                if (!state.CorridorGrants.Any(g => g.Ms >= from && g.Zone == zone))
                {
                    state.CorridorGrants.Add(new CorridorEntry { Ms = Ms(now), Zone = zone });
                    Calibrate(state, zone, now);
                    changed = true;
                }
            }
            else if ((remainingMs > 0 || state.CorridorGrants.Any(g => g.Ms >= from && g.Zone == zone))
                && !state.CorridorEntries.Any(e => e.Ms >= from && e.Zone == zone))
            {
                state.CorridorEntries.Add(new CorridorEntry { Ms = Ms(now), Zone = zone });
                changed = true;
            }

            if (changed)
            {
                state.CorridorGrants.RemoveAll(g => g.Ms < Ms(now) - (long)CorridorKeep.TotalMilliseconds);
                Save();
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Whether the game gave this corridor no usage time at the Abyss join since the last reset ("Unavailable").</summary>
    public static bool CorridorClosed(string character, int zone, DateTime now)
    {
        lock (Gate)
        {
            long from = Ms(LastCorridorReset(now));
            State state = Get(character, now);
            if (state.CorridorClosed.Any(g => g.Ms >= from && g.Zone == zone))
            {
                return true;
            }

            // the other side holds the artifact of this corridor
            int artifact = ArtifactOfZone(zone);
            OwnerEntry? owner = state.CorridorOwners.FirstOrDefault(o => o.Artifact == artifact && o.Ms >= from);
            return state.OurSide != 0 && owner is not null && owner.Side != state.OurSide;
        }
    }

    /// <summary>Whether the full usage time of this corridor was seen for the character since the last reset.</summary>
    public static bool CorridorGranted(string character, int zone, DateTime now)
    {
        lock (Gate)
        {
            long from = Ms(LastCorridorReset(now));
            return Get(character, now).CorridorGrants.Any(g => g.Ms >= from && g.Zone == zone);
        }
    }

    /// <summary>Whether the character has entered this corridor since the last reset.</summary>
    public static bool CorridorEntered(string character, int zone, DateTime now)
    {
        lock (Gate)
        {
            long from = Ms(LastCorridorReset(now));
            return Get(character, now).CorridorEntries.Any(e => e.Ms >= from && e.Zone == zone);
        }
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
