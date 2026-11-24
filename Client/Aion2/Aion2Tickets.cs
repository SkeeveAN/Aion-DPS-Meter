using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text.Json;
using AionDPS.Ui;
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
    /// <summary>Since November the shop sells the Odyle energy as two goods: 4 a week per character (counter 1209) and 16 a week per server (counter 1210), 20 together (the old counter 100134 is gone).
    /// The server one is a ticket of our own as well (it is the same in the list of every character).</summary>
    public const int ShopOdyleCounter = 1209;
    public const int ShopOdyleServerId = 99000008;
    public const int ShopOdyleServerCounter = 1210;
    /// <summary>The two weekly conversions of Odyle energy (recipe 31070001 / 32070001 = 16 a week per server, 31070002 / 32070002 = 4 a week per character; Elyos / Asmodians): tickets of our own again,
    /// the reset time is the next Wednesday 16:00 Korean time (the game does not send one).</summary>
    public const int ConvertServerId = 99000005;
    public const int ConvertCharacterId = 99000006;
    public const int ShopOdylePerCharacter = 4;
    public const int ShopOdylePerServer = 16;
    public const int ShopOdyleMax = ShopOdylePerCharacter + ShopOdylePerServer;
    public const int MaxCharacters = 40;

    /// <summary>The week's points of the season missions (the weekly reward steps 1,500 / 3,000 / 4,500 / 6,000 / 8,000) as a ticket of our own: count = points, time = when the game
    /// last changed it (ms since 1970). The id is no game id; the game's own achievement id is <see cref="WeeklyPointsAchievement"/>.</summary>
    public const int WeeklyPointsId = 99000007;
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

    /// <summary>The frame <c>0xe305</c> (entering the lower Abyss): which side holds each of the three artifacts. The frame must read to its last byte.</summary>
    public static bool TryParseAbyssOwners(ReadOnlySpan<byte> frame, out List<(int Artifact, int Side)> owners)
    {
        owners = new List<(int, int)>();
        const int Head = 2 + 23;
        if (frame.Length < Head + 1 || BinaryPrimitives.ReadUInt32LittleEndian(frame[3..]) != 1001)
        {
            return false;
        }

        int count = frame[Head];
        if (count is < 1 or > 6 || frame.Length != Head + 1 + 17 * count)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            int p = Head + 1 + 17 * i;
            int artifact = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]);
            int side = frame[p + 8];
            if (artifact is < 1001 or > 1003 || side is < 1 or > 2)
            {
                return false;
            }

            owners.Add((artifact, side));
        }

        return true;
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
    // Characters whose Odyle energy the meter saw in this run (a login list or a change); the file of an earlier run does not count.
    private static readonly HashSet<string> OdyleSeen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the Odyle energy of the character was read from the data stream in this run (not just restored from the file).</summary>
    public static bool OdyleSeenThisRun(string name)
    {
        lock (Gate)
        {
            return OdyleSeen.Contains(name);
        }
    }
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
        FlushEarly(default, true);
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
            OdyleSeen.Clear();
            _current = null;
            _likely = null;
            _awaitingName = false;
            _pending = null;
            _pendingAt = default;
            _early = null;
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

    /// <summary>The cost of the levels of a pass where it is known: the first level, each next one, and the experience below which the level can be worked out.</summary>
    public sealed record PassLevels(int First, int Step, int Known);

    /// <summary>One Daeva pass as the game counts it: the id of its entry in the pass frame <c>0x24e3</c> (Elyos and Asmodian side), the id of the ticket of our own that holds it
    /// (count = all experience, bonus = experience of this week, time = when it was last gained), the weekly limit, the end of the pass (on the clock the game shows) and its
    /// names. The list comes from <c>aion2/tickets/passes.json</c> (see <see cref="Passes"/>), so a new pass needs no release.</summary>
    public sealed record PassDef(int TicketId, int FrameId, int AsmodianFrameId, int WeeklyLimit, DateTime End, Dictionary<string, string> LightNames, Dictionary<string, string> DarkNames,
        PassLevels? Levels = null, string? NameKey = null, string? AsmodianNameKey = null)
    {
        /// <summary>The name of the pass for a character of a faction (the server id's thousands: 2 = Asmodian) in a language: the game's own text, English where the language has none.</summary>
        public string NameFor(int serverId, string language)
        {
            bool dark = serverId / 1000 == 2;
            Dictionary<string, string> names = dark && DarkNames.Count > 0 ? DarkNames : LightNames;
            if (names.TryGetValue(language, out string? text) && !string.IsNullOrEmpty(text))
            {
                return text;
            }

            if (names.TryGetValue("en", out text) && !string.IsNullOrEmpty(text))
            {
                return text;
            }

            string? key = dark ? AsmodianNameKey ?? NameKey : NameKey;
            return key is null ? "?" : LocalizationManager.Instance[key];
        }

        /// <summary>The text key of the pass' name for a character of a faction, for the passes the code knows by itself (the fallback without the list).</summary>
        public string NameKeyFor(int serverId) => serverId / 1000 == 2 && AsmodianNameKey is not null ? AsmodianNameKey : NameKey ?? "";
    }

    private static volatile PassDef[]? _passes;

    /// <summary>A passes list taken from the server counts at once: the next reader loads it again (no restart).</summary>
    private static readonly Action<string> PassesWatcher = AssetStoreWatch();

    private static Action<string> AssetStoreWatch()
    {
        Action<string> handler = path =>
        {
            if (path == "aion2/tickets/passes.json")
            {
                _passes = null;
            }
        };
        AssetStore.Changed += handler;
        return handler;
    }

    /// <summary>The passes of the global server: the list of <c>aion2/tickets/passes.json</c> (built from the game's table BattlePassGroup by Tools/aion2-dat/build_passes.py and delivered
    /// by the data update, so a new season or event pass is recognised without a release; a file taken from the server counts from the next start), the first two passes of the
    /// first season where there is no readable list. The ends are on the player's clock as the game shows them (16 December and 28 October 2026 16:00 UTC), the limits are those of the game's table.</summary>
    public static PassDef[] Passes => _passes ??= LoadPasses();

    private static PassDef[] LoadPasses()
    {
        var list = new List<PassDef>();
        try
        {
            string path = AssetStore.Path("aion2", "tickets", "passes.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (JsonElement p in doc.RootElement.GetProperty("passes").EnumerateArray())
                {
                    PassLevels? levels = null;
                    if (p.TryGetProperty("levels", out JsonElement l))
                    {
                        levels = new PassLevels(l.GetProperty("first").GetInt32(), l.GetProperty("step").GetInt32(), l.GetProperty("known").GetInt32());
                    }

                    JsonElement names = p.GetProperty("names");
                    list.Add(new PassDef(p.GetProperty("ticket").GetInt32(), p.GetProperty("light").GetInt32(), p.GetProperty("dark").GetInt32(), p.GetProperty("limit").GetInt32(),
                        DateTime.SpecifyKind(DateTime.ParseExact(p.GetProperty("end").GetString()!, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture), DateTimeKind.Local),
                        ReadNames(names.GetProperty("light")), ReadNames(names.GetProperty("dark")), levels));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            list.Clear();
        }

        if (list.Count == 0)
        {
            list.Add(new PassDef(99000010, 10044, 20044, 50000, new DateTime(2026, 12, 16, 9, 0, 0, DateTimeKind.Local), new(), new(), new PassLevels(400, 1600, 24400), "Checklist.PassLight1", "Checklist.PassDark1"));
            list.Add(new PassDef(99000011, 10045, 20045, 15000, new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Local), new(), new(), new PassLevels(700, 700, 10500), "Checklist.PassLight2", "Checklist.PassDark2"));
        }

        return list.ToArray();
    }

    private static Dictionary<string, string> ReadNames(JsonElement element)
    {
        var names = new Dictionary<string, string>();
        foreach (JsonProperty n in element.EnumerateObject())
        {
            names[n.Name] = n.Value.GetString() ?? "";
        }

        return names;
    }

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

    /// <summary>The level of a pass for all experience, or null where the cost of the levels is not known (<see cref="PassDef.Levels"/>): the first pass costs 400 for the first level and
    /// 1,600 for the next ones, the second 700 each (both checked up to the level 16 and 15 on 19 October 2026); higher levels cost more (level 82: 2,300, level 51: 5,000), the table between is not known.</summary>
    public static PassLevel? PassLevelOf(int ticketId, int all)
    {
        PassDef? pass = Passes.FirstOrDefault(p => p.TicketId == ticketId);
        if (pass?.Levels is not { } levels || all >= levels.Known)
        {
            return null;
        }

        return all < levels.First ? new PassLevel(1, all, levels.First) : new PassLevel(2 + (all - levels.First) / levels.Step, (all - levels.First) % levels.Step, levels.Step);
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
    /// <c>00 00 | n (u8) | n x ( counter id u32 | bought u32 | reset time u64 ms )</c>. Seen on 18 November: counter 1209 = 4 (per character) and 1210 = 16 (per server) after the 20 purchases of Odyle energy
    /// (100,000 Kinah each; the shop shows "Pro Charakter 4/4" and "Pro Server 16/16"), the reset of Wednesday 16:00 Korean time. Only the Odyle counters are read.</summary>
    public static bool TryParseShopCounters(ReadOnlySpan<byte> frame, out List<TicketReading> readings)
    {
        readings = new List<TicketReading>();
        if (frame.Length < 5 || frame.Length != 5 + 16 * frame[4])
        {
            return false;
        }

        for (int i = 0; i < frame[4]; i++)
        {
            ReadOnlySpan<byte> entry = frame.Slice(5 + 16 * i, 16);
            uint counter = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            if (counter != ShopOdyleCounter && counter != ShopOdyleServerCounter)
            {
                continue;
            }

            uint bought = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            ulong reset = BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]);
            if (bought > 1000 || reset is < 1_500_000_000_000 or > 4_000_000_000_000)
            {
                readings.Clear();
                return false;
            }

            readings.Add(new TicketReading(counter == ShopOdyleCounter ? ShopOdyleId : ShopOdyleServerId, new TicketValue((int)bought, 0, (long)reset)));
        }

        return readings.Count > 0;
    }

    private static bool ReadVarint(ReadOnlySpan<byte> frame, ref int at, out int value)
    {
        value = 0;
        for (int shift = 0; shift < 21 && at < frame.Length; shift += 7)
        {
            byte b = frame[at++];
            value |= (b & 0x7f) << shift;
            if ((b & 0x80) == 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The conversion counters: <c>0x8f8d</c> (all at the login: <c>00 00 | 2 bytes | n varint | n x (recipe u32, count per server u32) | m varint | m x (recipe u32, count per character u32)</c>) and <c>0x8b8d</c> (after a conversion:
    /// <c>00 00 | n varint | n x (recipe u32 | 26 bytes | per server u32 | per character u32)</c>). Layout as in the Wingbeat fork, checked on 18 November against a login (12 + 90 entries, list ends exactly at the frame's end).
    /// Recipes 31070001 / 32070001 (x16 per server) and 31070002 / 32070002 (x4 per character); Elyos and Asmodians each have their own, the larger one counts.</summary>
    public static bool TryParseConversionCounters(ReadOnlySpan<byte> frame, DateTime now, out List<TicketReading> readings)
    {
        readings = new List<TicketReading>();
        if (frame.Length < 5 || (frame[0] != 0x8f && frame[0] != 0x8b) || frame[1] != 0x8d || frame[2] != 0 || frame[3] != 0)
        {
            return false;
        }

        bool full = frame[0] == 0x8f;
        int at = full ? 6 : 4;
        int server = -1;
        int character = -1;
        void Note(uint recipe, uint count, bool perServer)
        {
            if (count > 1000)
            {
                return;
            }

            if (perServer && (recipe is 31070001 or 32070001))
            {
                server = Math.Max(server, (int)count);
            }
            else if (!perServer && (recipe is 31070002 or 32070002))
            {
                character = Math.Max(character, (int)count);
            }
        }

        if (full)
        {
            for (int list = 0; list < 2; list++)
            {
                if (!ReadVarint(frame, ref at, out int n) || n > 4096 || at + 8 * n > frame.Length)
                {
                    return false;
                }

                for (int i = 0; i < n; i++, at += 8)
                {
                    Note(BinaryPrimitives.ReadUInt32LittleEndian(frame[at..]), BinaryPrimitives.ReadUInt32LittleEndian(frame[(at + 4)..]), list == 0);
                }
            }

            if (at != frame.Length)
            {
                return false;
            }
        }
        else
        {
            if (!ReadVarint(frame, ref at, out int n) || n > 4096 || at + 38 * n != frame.Length)
            {
                return false;
            }

            for (int i = 0; i < n; i++, at += 38)
            {
                uint recipe = BinaryPrimitives.ReadUInt32LittleEndian(frame[at..]);
                Note(recipe, BinaryPrimitives.ReadUInt32LittleEndian(frame[(at + 30)..]), true);
                Note(recipe, BinaryPrimitives.ReadUInt32LittleEndian(frame[(at + 34)..]), false);
            }
        }

        long reset = new DateTimeOffset(ChecklistManual.LastWeeklyReset(now).AddDays(7)).ToUnixTimeMilliseconds();
        if (server >= 0)
        {
            readings.Add(new TicketReading(ConvertServerId, new TicketValue(server, 0, reset)));
        }

        if (character >= 0)
        {
            readings.Add(new TicketReading(ConvertCharacterId, new TicketValue(character, 0, reset)));
        }

        return readings.Count > 0;
    }

    /// <summary>The purchase counters of shop goods: <c>0x038c</c> after a purchase (<c>00 06 | u32 | n (u8) | n x (goods id u32, amount of this purchase u32)</c>, per the Wingbeat fork's reading: added up) and <c>0x018c</c> when the shop
    /// opens (<c>00 00 | u24 | n (u8) | n x (goods id, bought) | server time u64</c>). Seen on 18 November: the 12 Verteron scrolls bought in one go gave 133019901 = 12; the goods ids are those of GameShopGoods.</summary>
    public static bool TryParseGoodsCounters(ReadOnlySpan<byte> frame, out List<(uint Goods, int Bought)> list)
    {
        list = new List<(uint, int)>();
        int countAt;
        int first;
        int tail;
        if (frame.Length >= 2 && frame[0] == 0x03 && frame[1] == 0x8c)
        {
            countAt = 8;
            first = 9;
            tail = 0;
        }
        else if (frame.Length >= 2 && frame[0] == 0x01 && frame[1] == 0x8c)
        {
            countAt = 7;
            first = 8;
            tail = 8;
        }
        else
        {
            return false;
        }

        if (frame.Length <= countAt)
        {
            return false;
        }

        int n = frame[countAt];
        if (n == 0 || n > 60 || frame.Length != first + 8 * n + tail)
        {
            return false;
        }

        for (int i = 0; i < n; i++)
        {
            ReadOnlySpan<byte> entry = frame.Slice(first + 8 * i, 8);
            uint bought = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            if (bought > 100_000)
            {
                list.Clear();
                return false;
            }

            list.Add((BinaryPrimitives.ReadUInt32LittleEndian(entry), (int)bought));
        }

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
        FlushEarly(at, false);
        lock (Gate)
        {
            _pending = entries.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.Last().Value);
            _pendingAt = at;
            _awaitingName = true;
            // the achievements frame of this login came a moment before the list: its points are this character's, not the one logged in before
            if (_early is { } early && (at - _earlyAt).Duration() < EarlyWindow)
            {
                foreach (TicketReading r in early)
                {
                    _pending[r.Id] = r.Value;
                }

                _early = null;
            }
        }
    }

    // The achievements frame of a login comes BEFORE the ticket list of that login (in the same bundle), when the meter still thinks the character
    // logged in before is the current one. It waits a moment for the list that follows; without one it counts for the current character as before.
    private static readonly TimeSpan EarlyWindow = TimeSpan.FromSeconds(3);
    private static List<TicketReading>? _early;
    private static DateTime _earlyAt;
    private static DateTime _earlyWall;

    /// <summary>The week's points of the season missions from the login's achievements frame (see <see cref="TryParseAchievements"/>).</summary>
    public static void ReceiveAchievements(TicketReading reading, DateTime at) => ReceiveAchievements(new[] { reading }, at);

    /// <summary>The login's achievements frame: the week's points and the weekly season missions (see <see cref="Aion2Missions.TryParseReadings"/>).</summary>
    public static void ReceiveAchievements(IReadOnlyList<TicketReading> readings, DateTime at)
    {
        FlushEarly(at, false);
        lock (Gate)
        {
            _early = readings.ToList();
            _earlyAt = at;
            _earlyWall = DateTime.UtcNow;
        }
    }

    /// <summary>Hands a waiting achievements reading to the current character once no list came for it (a frame time or, with <paramref name="byClock"/>, the wall clock says so).</summary>
    private static void FlushEarly(DateTime at, bool byClock)
    {
        List<TicketReading>? due = null;
        DateTime dueAt = default;
        lock (Gate)
        {
            if (_early is { } early && (byClock ? DateTime.UtcNow - _earlyWall >= EarlyWindow : (at - _earlyAt).Duration() >= EarlyWindow))
            {
                due = early;
                dueAt = _earlyAt;
                _early = null;
            }
        }

        if (due is not null)
        {
            ReceiveUpdates(due, dueAt);
        }
    }

    /// <summary>Several readings of one frame at once (the achievements of a login): the same as one <see cref="ReceiveUpdate"/> each, with one change message.</summary>
    public static void ReceiveUpdates(IReadOnlyList<TicketReading> readings, DateTime at)
    {
        if (readings.Count == 0)
        {
            return;
        }

        bool changed = false;
        lock (Gate)
        {
            if (_awaitingName && _pending is not null)
            {
                foreach (TicketReading reading in readings)
                {
                    _pending[reading.Id] = reading.Value;
                }
            }
            else if ((_current ?? _likely) is { } name)
            {
                bool known = Characters.TryGetValue(name, out TicketCharacter? before);
                var tickets = known ? new Dictionary<int, TicketValue>(before!.Tickets) : new Dictionary<int, TicketValue>();
                foreach (TicketReading reading in readings)
                {
                    tickets[reading.Id] = reading.Value;
                }

                if (known)
                {
                    Characters[name] = before! with { SavedAt = DateTime.UtcNow, Tickets = tickets };
                }
                else
                {
                    Characters[name] = new TicketCharacter(name, _likelyServerId, DateTime.UtcNow, tickets);
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

    public static void ReceiveUpdate(TicketReading reading, DateTime at, int reason = 0)
    {
        FlushEarly(at, false);
        // The missions' head carries no time of its own: it gets the time it was read, so a daily reset since then is known (a relog keeps it). Stacy's approach (her fork, MIT).
        if (reading.Id == MissionsId && reading.Value.TimeMs == 0)
        {
            reading = reading with { Value = reading.Value with { TimeMs = new DateTimeOffset(at.ToUniversalTime()).ToUnixTimeMilliseconds() } };
        }

        bool changed = false;
        string? corridorName = null;
        lock (Gate)
        {
            if (_awaitingName && _pending is not null)
            {
                _pending[reading.Id] = reading.Value;
            }
            else if ((_current ?? _likely) is { } name)
            {
                corridorName = name;
                if (Characters.TryGetValue(name, out TicketCharacter? known))
                {
                    if (reading.Id == MissionsId && at - _pendingAt >= TimeSpan.Zero && at - _pendingAt < LoginHeadWindow && FirstLoginOfDay(known, at))
                    {
                        return;
                    }

                    var tickets = new Dictionary<int, TicketValue>(known.Tickets) { [reading.Id] = reading.Value };
                    if (reading.Id == OdyleId)
                    {
                        OdyleSeen.Add(name);
                    }

                    Characters[name] = known with { SavedAt = DateTime.UtcNow, Tickets = tickets };
                }
                else
                {
                    // a character the meter has no login list for (it started while this one was already in game): what comes in is still its own, the rest stays unread
                    if (reading.Id == OdyleId)
                    {
                        OdyleSeen.Add(name);
                    }

                    Characters[name] = new TicketCharacter(name, _likelyServerId, DateTime.UtcNow, new Dictionary<int, TicketValue> { [reading.Id] = reading.Value });
                    Trim();
                }

                _dirty = true;
                changed = true;
            }
        }

        // the usage time of an Abyss Corridor (the game's ticket 10000001-3): what it says about a visit goes to the checklist
        if (corridorName is not null && ChecklistManual.CorridorOfTicket(reading.Id) != 0 && Characters.TryGetValue(corridorName, out TicketCharacter? who) && (who.Level == 0 || who.Level >= CorridorMinLevel))
        {
            ChecklistManual.NoteCorridorTime(corridorName, reading.Id, reading.Value.TimeMs, at, reason == 2);
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>No Abyss (so no corridors and no Abyss scrolls) below this level.</summary>
    public const int CorridorMinLevel = 45;

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
                foreach (int shopId in new[] { ShopOdyleId, ShopOdyleServerId, ConvertServerId, ConvertCharacterId })
                {
                    if (Characters.TryGetValue(name, out TicketCharacter? before) && !_pending.ContainsKey(shopId) && before.Tickets.TryGetValue(shopId, out TicketValue shop))
                    {
                        _pending[shopId] = shop;
                    }
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

                if (_pending.ContainsKey(OdyleId))
                {
                    OdyleSeen.Add(name);
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
    /// is gone from the list (deleted) is removed again.</summary>
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

            if (account is not null && listed.Count > 0)
            {
                // The login list is the complete account: whatever is missing there is deleted (or renamed), also when it has counters. Entries without an account are
                // those a login created while no list was known; on a server this list covers they are removed too (they are re-created by a login if still alive).
                var servers = new HashSet<int>(listed.Select(c => c.ServerId).Where(s => s > 0));
                foreach (TicketCharacter gone in Characters.Values.Where(c => !names.Contains(c.Name)
                    && (c.Account == account || (c.Account is null && servers.Contains(c.ServerId)))).ToList())
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
