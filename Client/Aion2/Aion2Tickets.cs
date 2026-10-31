using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using AionDPS.Update;

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
public sealed record TicketCharacter(
    string Name, int ServerId, DateTime SavedAt, IReadOnlyDictionary<int, TicketValue> Tickets, int Level = 0,
    string? Account = null, int ClassCode = 0, int ItemLevel = 0, long LastOnlineMs = 0, string? Legion = null);

/// <summary>
/// The tickets of the characters (Tools > Checklist; no overlay). The server sends them twice:
/// <list type="bullet">
/// <item><c>0x0b61</c> at login (within 0.1 s of the login record 0x2036, a few seconds <i>before</i> the character record names the character):
/// <c>n (u8) | n x (mask u8 | id u32 | fields)</c>;</item>
/// <item><c>0x0c61</c> for a change: <c>flag u8 | mask u8 | id u32 | fields | reason u8 | (flag 1: gained u32)</c>; reason 2 = the regular refill,
/// 3 = something was used, 1 = something was gained (the Odyle bonus: 630 -> 640 comes with 10).</item>
/// </list>
/// The fields are chosen by the mask: bit 1 = time left (u64 ms), bit 2 = one more u64 (skipped), bit 4 = count (varint), bit 8 = bonus (varint); a field that is not there is zero.
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

    /// <summary>The Quai premium membership as a ticket of our own: the time field holds the end of the membership as milliseconds since 1970 (0 = none). The id is no game id.</summary>
    public const int PremiumId = 99000002;

    /// <summary>The mandatory daily tasks ("Missions" of the journal: five a day) as a ticket of our own: count = handed in, bonus = taken. The id is no game id.</summary>
    public const int MissionsId = 99000001;
    public const int MissionsPerDay = 5;

    /// <summary>A character holds at most 20 quests at once; a list head with a bigger number is something else.</summary>
    private const int MaxTakenMissions = 20;

    /// <summary>The weekly purchase of Odyle energy in the Brisenwind trade guild (limit 20 a week per character) as a ticket of our own: count = bought, time = the
    /// game's reset time (ms since 1970; an older one means the week is over). The id is no game id; the game's own counter id is <see cref="ShopOdyleCounter"/>.</summary>
    public const int ShopOdyleId = 99000003;
    public const int ShopOdyleCounter = 100134;
    public const int ShopOdyleMax = 20;
    public const int MaxCharacters = 40;

    /// <summary>The week's points of the season missions (the weekly reward steps 1,500 / 3,000 / 4,500 / 6,000 / 8,000) as a ticket of our own: count = points, time = when the game
    /// last changed it (ms since 1970). The id is no game id; the game's own achievement id is <see cref="WeeklyPointsAchievement"/>.</summary>
    public const int WeeklyPointsId = 99000004;
    public const int WeeklyPointsAchievement = 210000100;
    public const int WeeklyPointsMax = 8000;

    /// <summary>
    /// The achievements frame <c>0x8356</c> (login): <c>n (varint) | n x ( id u32 | counter u32 | time u64 ms | level u8 )</c>; only the weekly points are read.
    /// </summary>
    public static bool TryParseAchievements(ReadOnlySpan<byte> frame, out TicketReading reading)
    {
        reading = default;
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

        for (long i = 0; i < n && p + 17 <= frame.Length; i++, p += 17)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]) != WeeklyPointsAchievement)
            {
                continue;
            }

            uint points = BinaryPrimitives.ReadUInt32LittleEndian(frame[(p + 4)..]);
            ulong time = BinaryPrimitives.ReadUInt64LittleEndian(frame[(p + 8)..]);
            if (points > 100_000 || time is < 1_500_000_000_000 or > 4_000_000_000_000)
            {
                return false;
            }

            reading = new TicketReading(WeeklyPointsId, new TicketValue((int)points, 0, (long)time));
            return true;
        }

        return false;
    }

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
        // from the character list of the login (every character of the account, also those never logged in with the meter)
        public string? Account { get; set; }
        public int ClassCode { get; set; }
        public int ItemLevel { get; set; }
        public int CombatPower { get; set; } // 1.10.1 saved the item level under this name; read on, no longer written
        public long LastOnlineMs { get; set; }
        public string? Legion { get; set; }
    }

    private static readonly object Gate = new();
    private static List<TicketDef>? _catalog;
    private static readonly Dictionary<string, TicketCharacter> Characters = new(StringComparer.Ordinal);
    private static string? _current;
    private static string? _likely;
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
            return Characters.Values.OrderByDescending(c => c.Tickets.Count > 0).ThenByDescending(c => c.SavedAt).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();
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
            _likely = null;
            _awaitingName = false;
            _pending = null;
            _pendingAt = default;
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

    /// <summary>The membership frame <c>0xe33b</c>: <c>count (u8)</c> then 21 bytes each (effect id, a flag word, start and end as milliseconds since 1970). The effects 34 to 42 are the
    /// benefits of the Quai premium membership (remote storage, market, Odyle energy 840, 21 Shugo Festa keys ...): they come together with one end, which is the one the
    /// membership page of the game shows (16 days 20 hours left on the day of the recording = 31 October 15:18). No such effect, or one that is over, means no membership.</summary>
    public static bool TryParseMembership(ReadOnlySpan<byte> frame, out TicketReading reading)
    {
        reading = default;
        if (frame.Length < 3 || frame.Length != 3 + 21 * frame[2])
        {
            return false;
        }

        long end = 0;
        for (int i = 0; i < frame[2]; i++)
        {
            ReadOnlySpan<byte> entry = frame.Slice(3 + 21 * i, 21);
            ulong stop = BinaryPrimitives.ReadUInt64LittleEndian(entry[13..]);
            if (entry[0] is >= 34 and <= 42 && stop is > 1_500_000_000_000 and < 4_000_000_000_000)
            {
                end = Math.Max(end, (long)stop);
            }
        }

        reading = new TicketReading(PremiumId, new TicketValue(0, 0, end));
        return true;
    }

    /// <summary>True/false when the membership is known (read at login), null when this character was saved before it was read.</summary>
    public static bool? HasPremium(TicketCharacter character, DateTime utcNow) =>
        character.Tickets.TryGetValue(PremiumId, out TicketValue value) ? value.TimeMs > new DateTimeOffset(utcNow, TimeSpan.Zero).ToUnixTimeMilliseconds() : null;

    /// <summary>The pet auto-loot ticket ("Auto. Aufsammeln durch Pets", 30 days, bound to the character) as a ticket of our own: the time field holds its end
    /// (ms since 1970, 0 = no ticket). The id is no game id. It is entry 44 of the membership frame <c>0x3be3</c> (the eight benefits are 34 to 42);
    /// confirmed on 19 October 2026 with the time the game showed for Nunzio (29 d 5 h) and Aahz (12 d 8 h).</summary>
    public const int AutoLootId = 99000004;
    private const int AutoLootEntry = 44;

    /// <summary>The membership frame as the readings it holds: the membership (the benefits 34 to 42) and the pet auto-loot ticket (entry 44). A frame with
    /// the ticket alone (it comes alone when a ticket is used) leaves the membership as it was; an empty list means neither.</summary>
    public static bool TryParseMembershipFrame(ReadOnlySpan<byte> frame, out List<TicketReading> readings)
    {
        readings = new List<TicketReading>();
        if (frame.Length < 3 || frame.Length != 3 + 21 * frame[2])
        {
            return false;
        }

        long member = 0, loot = 0;
        bool hasBenefit = false, hasLoot = false;
        for (int i = 0; i < frame[2]; i++)
        {
            ReadOnlySpan<byte> entry = frame.Slice(3 + 21 * i, 21);
            ulong stop = BinaryPrimitives.ReadUInt64LittleEndian(entry[13..]);
            bool plausible = stop is > 1_500_000_000_000 and < 4_000_000_000_000;
            if (entry[0] is >= 34 and <= 42)
            {
                hasBenefit = true;
                if (plausible)
                {
                    member = Math.Max(member, (long)stop);
                }
            }
            else if (entry[0] == AutoLootEntry)
            {
                hasLoot = true;
                if (plausible)
                {
                    loot = Math.Max(loot, (long)stop);
                }
            }
        }

        bool empty = frame[2] == 0;
        if (empty || hasBenefit)
        {
            readings.Add(new TicketReading(PremiumId, new TicketValue(0, 0, member)));
        }

        if (empty || hasBenefit || hasLoot)
        {
            readings.Add(new TicketReading(AutoLootId, new TicketValue(0, 0, loot)));
        }

        return readings.Count > 0;
    }

    /// <summary>One Daeva pass as the game counts it: the id of its entry in the pass frame <c>0x24e3</c>, the id of the ticket of our own that holds it
    /// (count = all experience, bonus = experience of this week, time = when it was last gained), the weekly limit and the end of the pass.</summary>
    public sealed record PassDef(int TicketId, int FrameId, string NameKey, int WeeklyLimit, DateTime End, int AsmodianFrameId = 0, string? AsmodianNameKey = null)
    {
        /// <summary>The text key of the pass' name for a character of a faction (the server id's thousands: 2 = Asmodian).</summary>
        public string NameKeyFor(int serverId) => serverId / 1000 == 2 && AsmodianNameKey is not null ? AsmodianNameKey : NameKey;
    }

    /// <summary>The two passes of the first season: "Dawn Legion" (<c>String_UI_BATTLEPASSGROUP_LIGHT_S1_1</c>, entry 10044) and "Planet's Call" (<c>..._LIGHT_S1_2</c>, 10045) on
    /// the Elyos side, "Ishalgen Mercenary" (<c>..._DARK_S1_1</c>, 20044) and "Planet's Call" (<c>..._DARK_S1_2</c>, 20045) on the Asmodian side. The limits and the ends are the
    /// same for both sides in the game's table (BattlePassGroup: 50,000 / 15,000 a week, 16 December and 28 October 2026 16:00 UTC) and are those the game showed on
    /// 19 October 2026 (the ends are on the player's clock). The Asmodian ids were read from two Asmodian recordings of 20 October 2026.</summary>
    public static readonly PassDef[] Passes =
    {
        new(99000010, 10044, "Checklist.PassLight1", 50000, new DateTime(2026, 12, 16, 9, 0, 0, DateTimeKind.Local), 20044, "Checklist.PassDark1"),
        new(99000011, 10045, "Checklist.PassLight2", 15000, new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Local), 20045, "Checklist.PassDark2"),
    };

    /// <summary>The pass frame <c>0x24e3</c> (at login with both passes, later with the one that changed): an entry is <c>id u32 | all experience u32 |
    /// experience of this week u32 | time of the last gain u64 | ...</c>. Checked on the 40 login dumps of 19 October 2026 and against the numbers the game showed
    /// for Nunzio, Chumley and Skeeve (the second number is the weekly one, the limit is 50,000 and 15,000).</summary>
    public static bool TryParsePass(ReadOnlySpan<byte> frame, out List<TicketReading> readings)
    {
        readings = new List<TicketReading>();
        foreach (PassDef pass in Passes)
        {
            for (int at = 3; at + 20 <= frame.Length; at++)
            {
                uint entry = BinaryPrimitives.ReadUInt32LittleEndian(frame[at..]);
                if (entry != (uint)pass.FrameId && entry != (uint)pass.AsmodianFrameId)
                {
                    continue;
                }

                uint all = BinaryPrimitives.ReadUInt32LittleEndian(frame[(at + 4)..]);
                uint week = BinaryPrimitives.ReadUInt32LittleEndian(frame[(at + 8)..]);
                ulong gained = BinaryPrimitives.ReadUInt64LittleEndian(frame[(at + 12)..]);
                if (all <= 100_000_000 && week <= all && gained is > 1_500_000_000_000 and < 4_000_000_000_000)
                {
                    readings.Add(new TicketReading(pass.TicketId, new TicketValue((int)all, (int)week, (long)gained)));
                    break;
                }
            }
        }

        return readings.Count > 0;
    }

    /// <summary>A pass level worked out from all experience: the level, the experience in it and what it needs.</summary>
    public readonly record struct PassLevel(int Level, int Into, int Need);

    /// <summary>The level of a pass for all experience, or null where the cost of the levels is not known: the first pass costs 400 for the first level and 1,600 for
    /// the next ones, the second 700 each (both checked up to the level 16 and 15 on 19 October 2026); higher levels cost more (level 82: 2,300, level 51: 5,000),
    /// the table between is not known.</summary>
    public static PassLevel? PassLevelOf(int ticketId, int all)
    {
        if (ticketId == Passes[0].TicketId)
        {
            const int FirstLevel = 400, Level = 1600, KnownUntil = FirstLevel + 15 * Level;
            if (all < FirstLevel)
            {
                return new PassLevel(1, all, FirstLevel);
            }

            return all < KnownUntil ? new PassLevel(2 + (all - FirstLevel) / Level, (all - FirstLevel) % Level, Level) : null;
        }

        if (ticketId == Passes[1].TicketId)
        {
            const int Level = 700, KnownUntil = 15 * Level;
            return all < KnownUntil ? new PassLevel(1 + all / Level, all % Level, Level) : null;
        }

        return null;
    }

    /// <summary>How much of what the next daily refill brings is lost because the counter is full: <c>count + refill - cap</c>, at most the refill, 0 where
    /// the ticket has no daily refill. The Odyle energy and the weekly tickets (the weekly refill sets the amount) have none.</summary>
    public static int LostAtNextRefill(TicketDef def, int count, bool premium)
    {
        if (def.DailyCount <= 0 || !TimeSpan.TryParse(def.DailyAt, out _))
        {
            return 0;
        }

        int cap = premium && def.SubscribeMax > def.Max ? def.SubscribeMax : def.Max;
        return cap <= 0 ? 0 : Math.Clamp(count + def.DailyCount - cap, 0, def.DailyCount);
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
        // the first number is not the count of accepted missions (on 17 October it reached 6 with only five accepted, the game's own list showed five), so it is only kept and not used; only a number beyond the 20 quests a character can hold at once is no list head
        if (taken > MaxTakenMissions || done > MaxTakenMissions)
        {
            return false;
        }

        reading = new TicketReading(MissionsId, new TicketValue((int)Math.Min(done, MissionsPerDay), (int)taken, 0));
        return true;
    }

    private static readonly TimeZoneInfo MissionsZone = FindMissionsZone();

    private static TimeZoneInfo FindMissionsZone()
    {
        foreach (string id in new[] { "Europe/Berlin", "W. Europe Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Local;
    }

    /// <summary>The latest daily reset of the mandatory tasks at or before <paramref name="now"/>, as local time. It is 09:00 on the clock of the German servers all year:
    /// the server follows the change between summer and winter time (SkeeveTV, 2026-10-30), so the Korean 16:00 of the other counters is not used here.</summary>
    public static DateTime LastMissionsReset(DateTime now)
    {
        DateTime server = TimeZoneInfo.ConvertTimeFromUtc(now.ToUniversalTime(), MissionsZone);
        DateTime reset = server.Date.AddHours(9);
        if (reset > server)
        {
            reset = reset.AddDays(-1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(reset, DateTimeKind.Unspecified), MissionsZone).ToLocalTime();
    }

    /// <summary>Whether the missions count was read before the last daily reset, so the five are to be done again (the idea, the reading time and this check are Stacy's, from her fork
    /// StacysView/Aion-DPS-Meter, MIT, 27 to 29 October 2026; here with the German servers' 09:00 reset). The game sends the count only at a login, at an accept or a hand-in and
    /// when the journal is opened, never at the reset itself: what the meter holds is yesterday's until then (a reading with no time, stored before the time was kept, is of an unknown day).</summary>
    public static bool MissionsStale(TicketValue missions, DateTime now) =>
        missions.TimeMs <= 0 || DateTimeOffset.FromUnixTimeMilliseconds(missions.TimeMs).LocalDateTime < LastMissionsReset(now);

    /// <summary>How long after a login list a missions head still counts as the login's own (the head follows the list by a second or two).</summary>
    private static readonly TimeSpan LoginHeadWindow = TimeSpan.FromSeconds(15);

    /// <summary>Whether a missions head sent at a login is to be left out: at the first login of a day (nothing read since the last reset) the game still sends yesterday's count
    /// (found and solved by Stacy in her fork, 2026-10-28: 5 of 5 at 18:50 after the reset). The count then stays at 0 from the reset; a relog the same day has a reading since the reset and keeps the game's.
    /// A character the meter has never seen has nothing to go by: it takes the head.</summary>
    private static bool FirstLoginOfDay(TicketCharacter? before, DateTime at) =>
        before is not null && (!before.Tickets.TryGetValue(MissionsId, out TicketValue old) || MissionsStale(old, at));

    /// <summary>The shop purchase counters <c>0x8e56</c> (the whole list a little after the login, one entry again after every purchase):
    /// <c>00 00 | n (u8) | n x ( counter id u32 | bought u32 | reset time u64 ms )</c>. Seen on 14 October: counter 100134 = 20 after the 20 purchases of Odyle energy
    /// (100,000 Kinah each), the reset of Wednesday 16:00 Korean time. Only the Odyle counter is read.</summary>
    public static bool TryParseShopCounters(ReadOnlySpan<byte> frame, out TicketReading reading)
    {
        reading = default;
        if (frame.Length < 5 || frame.Length != 5 + 16 * frame[4])
        {
            return false;
        }

        for (int i = 0; i < frame[4]; i++)
        {
            ReadOnlySpan<byte> entry = frame.Slice(5 + 16 * i, 16);
            if (BinaryPrimitives.ReadUInt32LittleEndian(entry) == ShopOdyleCounter)
            {
                uint bought = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
                ulong reset = BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]);
                if (bought > 1000 || reset is < 1_500_000_000_000 or > 4_000_000_000_000)
                {
                    return false;
                }

                reading = new TicketReading(ShopOdyleId, new TicketValue((int)bought, 0, (long)reset));
                return true;
            }
        }

        return false;
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
        if ((mask & ~0x0f) != 0)
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

        // Bit 2: one more u64 after the time (3,600,000 = an hour for ticket 3 in an alt's login list of another player, 17 October). Not used;
        // unknown, it made the whole list unreadable and that character missing from the checklist. None of our own 142 login lists has it.
        if ((mask & 2) != 0)
        {
            if (p + 8 > frame.Length)
            {
                return false;
            }

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
        // The missions' head carries no time of its own: it gets the time it was read, so a daily reset since then is known (a relog keeps it). Stacy's approach (her fork, MIT).
        if (reading.Id == MissionsId && reading.Value.TimeMs == 0)
        {
            reading = reading with { Value = reading.Value with { TimeMs = new DateTimeOffset(at.ToUniversalTime()).ToUnixTimeMilliseconds() } };
        }

        bool changed = false;
        lock (Gate)
        {
            if (_awaitingName && _pending is not null)
            {
                _pending[reading.Id] = reading.Value;
            }
            else if ((_current ?? _likely) is { } name)
            {
                if (Characters.TryGetValue(name, out TicketCharacter? known))
                {
                    if (reading.Id == MissionsId && at - _pendingAt >= TimeSpan.Zero && at - _pendingAt < LoginHeadWindow && FirstLoginOfDay(known, at))
                    {
                        return;
                    }

                    var tickets = new Dictionary<int, TicketValue>(known.Tickets) { [reading.Id] = reading.Value };
                    Characters[name] = known with { SavedAt = DateTime.UtcNow, Tickets = tickets };
                }
                else
                {
                    // a character the meter has no login list for (it started while this one was already in game): what comes in is still its own, the rest stays unread
                    Characters[name] = new TicketCharacter(name, _likelyServerId, DateTime.UtcNow, new Dictionary<int, TicketValue> { [reading.Id] = reading.Value });
                    Trim();
                }

                _dirty = true;
                changed = true;
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private static int _likelyServerId;

    /// <summary>The character most likely in game when the meter starts while one already is (its last saved record): the changes that come
    /// (a mission handed in, a purchase of Odyle energy) go to it until a live record names the character (<see cref="NoteCharacter"/> replaces it).
    /// Without it they were dropped and the checklist stayed at the login value. It is only a guess for those changes: <see cref="CurrentName"/> stays empty.</summary>
    public static void NoteLikelyCharacter(string? name, int serverId = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        lock (Gate)
        {
            if (_current is null && !_awaitingName)
            {
                _likely = name;
                _likelyServerId = serverId;
            }
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
                _likely = null;
            }

            if (_awaitingName && _pending is not null && receivedAt >= _pendingAt)
            {
                // The purchases of Odyle energy come only when the shop is opened (not with the login): a new login list keeps what it does not hold.
                if (Characters.TryGetValue(name, out TicketCharacter? before) && !_pending.ContainsKey(ShopOdyleId) && before.Tickets.TryGetValue(ShopOdyleId, out TicketValue shop))
                {
                    _pending[ShopOdyleId] = shop;
                }

                // the first login of a day sends yesterday's missions count: the old reading stays (stale, so the checklist shows it as an estimate)
                TicketCharacter? previous = Characters.GetValueOrDefault(name);
                if (_pending.ContainsKey(MissionsId) && FirstLoginOfDay(previous, receivedAt))
                {
                    if (previous!.Tickets.TryGetValue(MissionsId, out TicketValue stale))
                    {
                        _pending[MissionsId] = stale;
                    }
                    else
                    {
                        _pending.Remove(MissionsId);
                    }
                }

                Characters[name] = Characters.TryGetValue(name, out TicketCharacter? listed)
                    ? listed with { ServerId = serverId > 0 ? serverId : listed.ServerId, SavedAt = DateTime.UtcNow, Tickets = _pending, Level = level > 0 ? level : listed.Level }
                    : new TicketCharacter(name, serverId, DateTime.UtcNow, _pending, level);
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

    /// <summary>The character list of a login: every character of the account gets an entry (for the checklist's character switch), also those that never logged in
    /// with the meter and so have no counters yet. Basics (class, level, item level, legion, last online) are refreshed for all of them; an entry that
    /// only the list ever showed and that is gone from the list (deleted) is removed again.</summary>
    public static void NoteListedCharacters(string? account, IReadOnlyList<AccountCharacter> listed)
    {
        bool changed = false;
        lock (Gate)
        {
            var names = new HashSet<string>(listed.Select(c => c.Name), StringComparer.Ordinal);
            foreach (AccountCharacter c in listed)
            {
                if (Characters.TryGetValue(c.Name, out TicketCharacter? known))
                {
                    TicketCharacter updated = known with
                    {
                        ServerId = c.ServerId > 0 ? c.ServerId : known.ServerId,
                        Level = c.Level > 0 ? c.Level : known.Level,
                        Account = account ?? known.Account,
                        ClassCode = c.ClassCode,
                        ItemLevel = c.ItemLevel,
                        LastOnlineMs = c.LastOnlineMs,
                        Legion = c.Legion,
                    };
                    if (updated != known)
                    {
                        Characters[c.Name] = updated;
                        changed = true;
                    }
                }
                else
                {
                    Characters[c.Name] = new TicketCharacter(c.Name, c.ServerId, DateTime.UtcNow, new Dictionary<int, TicketValue>(), c.Level, account, c.ClassCode, c.ItemLevel, c.LastOnlineMs, c.Legion);
                    changed = true;
                }
            }

            if (account is not null)
            {
                foreach (TicketCharacter gone in Characters.Values.Where(c => c.Account == account && c.Tickets.Count == 0 && !names.Contains(c.Name)).ToList())
                {
                    Characters.Remove(gone.Name);
                    changed = true;
                }
            }

            if (changed)
            {
                Trim();
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
        DateTime? onServer = NextResetOnServer(def, ServerTime.FromLocal(now));
        return onServer is { } at ? ServerTime.ToLocal(at) : null;
    }

    private static DateTime? NextResetOnServer(TicketDef def, DateTime now)
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
    public static int Expected(TicketDef def, int count, DateTime seenAt, DateTime now, bool premium = true)
    {
        // the refills run on the game's clock (Korean time): both moments are looked at there
        seenAt = ServerTime.FromLocal(seenAt);
        now = ServerTime.FromLocal(now);
        int cap = premium ? Math.Max(def.Max, def.SubscribeMax) : def.Max; // the member's cap (Shugo Festival keys 21) or the plain one (12)
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
            string path = AssetStore.Path("aion2", "tickets", "tickets.json");
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
                Characters[c.Name] = new TicketCharacter(c.Name, c.ServerId, DateTime.SpecifyKind(DateTimeOffset.FromUnixTimeMilliseconds(c.SavedMs).DateTime, DateTimeKind.Utc), tickets, c.Level, c.Account, c.ClassCode, c.ItemLevel != 0 ? c.ItemLevel : c.CombatPower, c.LastOnlineMs, c.Legion);
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
                    Account = c.Account,
                    ClassCode = c.ClassCode,
                    ItemLevel = c.ItemLevel,
                    LastOnlineMs = c.LastOnlineMs,
                    Legion = c.Legion,
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
