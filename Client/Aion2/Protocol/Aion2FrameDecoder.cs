using System.Buffers.Binary;
using System.IO;
using System.Text;
using AionDPS.Combat;
using AionDPS.Combat.Sources;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// Turns one reassembled game frame into meter events, purely by the offsets in
/// <see cref="Aion2Protocol"/>. Knows nothing the JSON does not say: an opcode outside the tables
/// is skipped, a frame shorter than a field it needs is skipped and counted, never guessed.
/// </summary>
public sealed class Aion2FrameDecoder
{
    private readonly Aion2Protocol _protocol;
    private readonly Aion2EntityDirectory _entities;
    private readonly Queue<(string Actor, string Skill)> _skillUses = new();
    private readonly List<KillEvent> _kills = new();
    private readonly List<AvoidEvent> _avoids = new();

    // For telling apart the summons of two players of one class (see GuessSummonOwner): when each
    // entity appeared, and when each player last cast each skill variant (skill id / 10).
    private readonly Dictionary<int, DateTime> _spawnedAt = new();
    private readonly Dictionary<(int Caster, int Variant), DateTime> _lastCasts = new();

    // A shield's share of the blow a monster (actor) is striking a player (target) with, waiting for that blow's hit
    // frame (see DecodeVarintDot).
    private readonly Dictionary<(int Actor, int Target), (DateTime At, long Amount)> _shieldShares = new();

    /// <summary>AION2_FIND=text: reports every decoded frame holding that text (UTF-8 or UTF-16), for
    /// finding where something typed in the game - chat, say - shows up. Diagnostic only.</summary>
    /// <summary>AION2_FRAMES=file: every decoded frame (time;opcode;length;hex) for protocol analysis.</summary>
    private static readonly StreamWriter? DebugFrameDump = Environment.GetEnvironmentVariable("AION2_FRAMES") is { Length: > 0 } framesPath
        ? new StreamWriter(framesPath) { AutoFlush = false }
        : null;

    private static readonly byte[][] DebugNeedles = Environment.GetEnvironmentVariable("AION2_FIND") is { Length: > 0 } text
        ? new[] { System.Text.Encoding.UTF8.GetBytes(text), System.Text.Encoding.Unicode.GetBytes(text) }
        : Array.Empty<byte[]>();

    public Aion2FrameDecoder(Aion2Protocol protocol, Aion2EntityDirectory entities)
    {
        _protocol = protocol;
        _entities = entities;
    }

    /// <summary>The opcode of the field boss list of a map (the in-game map, Exploration, Field monsters).</summary>
    private const int FieldBossListOpcode = 0x0191;
    private const int ShopOpcode = 0x9156;
    /// <summary>The server's clock about every 50 ms: opcode and the UTC time in milliseconds (see <see cref="ServerClock"/>).</summary>
    private const int ServerClockOpcode = 0x0036;

    /// <summary>The instance timer: map, phase and the server's start and end of it (see <see cref="Aion2InstanceTimer"/>).</summary>
    private const int InstanceTimerOpcode = 0x0061;

    /// <summary>The zone frame: <c>opcode | counter u32 | zone id u32 | position ...</c>. Sent at login and on every map or instance change
    /// (recordings 2026-10-11: 1010 Verteron, 600001 Krao Cave as an instance).</summary>
    private const int ZoneOpcode = 0x2136;

    /// <summary>An entity going into or out of combat: <c>opcode | entity (varint) | 01 01 | server time u64</c> in, <c>| 00 00</c> out
    /// (the same entities get <c>00 01</c> frames too, which say nothing about the fight). Anchors the rage clock.</summary>
    private const int EngageOpcode = 0x218d;

    /// <summary>The account-status frames: the tickets (entry counts, reward counts, Odyle energy) as a list at login and as single changes; see <see cref="Aion2Tickets"/>.</summary>
    /// <summary>The character list at login (all characters of the account, each with name, server, legion), sent for the own account only.</summary>
    private const int CharacterListOpcode = 0x2636;
    // The first login of a game session sends the same list as 0x1536 (16 bytes more head); 0x2636 only comes when the player goes back to the character selection. Short 0x1536 frames are something else.
    private const int FirstLoginListOpcode = 0x1536;
    private const int TicketListOpcode = 0x0b61;
    private const int TicketChangeOpcode = 0x0c61;
    private const int ShopCountersOpcode = 0x8e56;

    /// <summary>The achievements and season missions of the login (the week's points are read from it).</summary>
    private const int AchievementsOpcode = 0x8356;
    private const int MissionListOpcode = 0x1c8d;
    private const int MembershipOpcode = 0x3be3;
    /// <summary>The Daeva pass state (<see cref="Aion2Tickets.TryParsePass"/>): at login with both passes, later one entry per change.</summary>
    private const int PassOpcode = 0x24e3;

    /// <summary>The local player's cooldown list (kept for later features; see <see cref="Aion2Cooldowns"/>).</summary>
    private const int CooldownOpcode = 0x4738;

    /// <summary>One stat of an entity: <c>opcode | entity (varint) | 01 | stat id u16 | value u32</c>. On a boss, stat 0xCD at 5000 or 10000 arrives
    /// exactly 300 s after it went into combat - in the four fights of the recordings that lasted that long (Talisra x3, Divine Auldor) and in none that
    /// ended before. The stat is no known name; that it is the rage is the reading of the timing (and the user's: the rage comes after about five minutes).</summary>
    private const int StatOpcode = 0x4b36;

    private const int RageStatId = 0x00CD;

    /// <summary>The stat counts as the rage from this far before the boss's rage time into the combat (the same stat shows up in short fights and in idle, at 500 and 5000).</summary>
    private static readonly TimeSpan RageTolerance = TimeSpan.FromSeconds(10);

    private readonly Dictionary<int, DateTime> _engaged = new();
    private readonly HashSet<int> _enraged = new();
    private readonly List<EnrageMark> _enrages = new();

    /// <summary>The boss rages decoded since the last call, oldest first.</summary>
    public IReadOnlyList<EnrageMark> DrainEnrages() => Drain(_enrages);

    private void DecodeEngage(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        int p = 2;
        if (!TryReadVarint(frame, ref p, out long id))
        {
            return;
        }

        int entity = unchecked((int)id);
        if (frame.Length == p + 2 && frame[p] == 0 && frame[p + 1] == 0)
        {
            _engaged.Remove(entity);
            _enraged.Remove(entity);
            _entities.NoteDisengaged(entity);
        }
        else if (frame.Length >= p + 10 && frame[p] == 1 && frame[p + 1] == 1 && !_engaged.ContainsKey(entity))
        {
            if (_engaged.Count > 5000)
            {
                _engaged.Clear();
            }

            _engaged[entity] = timestamp;
            _enraged.Remove(entity);
            _entities.NoteEngaged(entity, timestamp, exact: true);
        }
        else if (frame.Length == p + 2 && frame[p] == 0 && frame[p + 1] == 1)
        {
            // the plain "in combat" flag (most fights send only this one): good enough for the fight time on the boss bar, not for the rage mark
            _entities.NoteEngaged(entity, timestamp);
        }
    }

    private void DecodeStat(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        int p = 2;
        if (!TryReadVarint(frame, ref p, out long id) || frame.Length != p + 7 || frame[p] != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(frame[(p + 1)..]) != RageStatId)
        {
            return;
        }

        int entity = unchecked((int)id);
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(frame[(p + 3)..]);
        if (value >= 5000 && _engaged.TryGetValue(entity, out DateTime start) && timestamp - start >= TimeSpan.FromSeconds(Aion2BossRage.SecondsFor(_entities.BossNpcIdOf(entity))) - RageTolerance && _enraged.Add(entity))
        {
            _enrages.Add(new EnrageMark(timestamp, entity, start));
        }
    }

    /// <summary>A skill that is being wound up: caster, skill, aim and the time the cast takes (see <see cref="DecodeWindup"/>).</summary>
    private const int WindupOpcode = 0x0938;

    /// <summary>A skill that has been cast: the same aim block, no duration. Players' skills too.</summary>
    private const int CastOpcode = 0x0238;

    /// <summary>An actor's (player's or monster's) new target: <c>opcode | actor (varint) | 00 | target (varint, 0 = none)</c>; 0x3138 is the own character's: <c>opcode | 00 00 | target (varint) | 00 00</c>.</summary>
    private const int ActorTargetOpcode = 0x3238, OwnTargetOpcode = 0x3138;

    /// <summary>A hit that lands: <c>opcode | attacker (varint) | 00 | target (varint) | skill id u32 | position ...</c>, about 0.3 s before the
    /// damage frame (0x0438) of the same hit, which holds the amount. In a fight of a few players both arrive for every hit (one notice per
    /// damaging frame: 4,331 against 4,306 in a recording of a Urugugu run). On a crowded world boss the server sends the notices of everybody around and
    /// the damage frames of the own group only (Lagta, 2026-10-13: 12,536 notices of 1,004 players in 18 s, damage frames of 2 players). The notice holds no amount;
    /// see <see cref="Aion2HitAverages"/>.</summary>
    private const int HitNoticeOpcode = 0x0338;

    /// <summary>How long a notice waits for its damage frame before it counts as one without (the frame follows after about 0.3 s).</summary>
    private static readonly TimeSpan NoticeHold = TimeSpan.FromMilliseconds(1500);

    private readonly Dictionary<(int Actor, int Target, int Skill), Queue<DateTime>> _notices = new();
    private readonly Queue<(DateTime At, (int Actor, int Target, int Skill) Key, long Lost)> _noticeOrder = new();

    // The estimate is scaled to what the boss really lost (see EstimateScale): per world boss, the damage the game reported in full, the
    // estimates at their table value, and how many of them there were.
    private readonly HashSet<int> _estimatedTargets = new();
    private readonly Dictionary<int, long> _exactOnTarget = new();
    private readonly Dictionary<int, (long Raw, int Hits)> _rawEstimate = new();

    /// <summary>Estimated hits needed on a boss before its hit points can say how far the table is off (fewer, and the figure is the table's).</summary>
    private const int CalibrationHits = 30;

    /// <summary>Estimated hits at which the measured scale counts in full; between <see cref="CalibrationHits"/> and this it is blended in, so a few early hits cannot swing it.</summary>
    private const int FullCalibrationHits = 300;

    /// <summary>The bounds of the scale: the players of a small group hit harder or softer than the table's typical player (aiondps.com, 2026-10-20: world bosses between 81 and 115 % of their hit points),
    /// so it may go below the table's value too; a wild reading must not blow one hit up.</summary>
    private const double MinScale = 0.25, MaxScale = 40.0;

    private const int BuffOnOpcode = 0x2a38;
    private const int BuffRenewedOpcode = 0x2b38;
    private const int BuffEndOpcode = 0x2c38;

    /// <summary>A monster's stagger bar: reading (03 01, max u32, current u32) or break (00 03).</summary>
    private const int StaggerOpcode = 0x05e0;

    public string CurrentZone { get; private set; } = "";

    /// <summary>The zone id of the last zone frame (0 until one arrived).</summary>
    public int CurrentZoneId { get; private set; }

    private readonly List<ZoneChange> _zoneChanges = new();

    /// <summary>The zone changes decoded since the last call, oldest first. A repeat of the zone the player is already in
    /// (a reconnect) is not a change.</summary>
    public IReadOnlyList<ZoneChange> DrainZoneChanges() => Drain(_zoneChanges);

    private void DecodeZone(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        if (frame.Length < 10)
        {
            return;
        }

        int zoneId = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[6..]));
        if (zoneId <= 0)
        {
            return;
        }

        _entities.NoteZone(zoneId);
        Aion2Loot.NoteZone(zoneId);
        if (zoneId != CurrentZoneId && Aion2Zones.IsArena(zoneId))
        {
            _entities.NoteArenaEntered(timestamp);
        }

        if (zoneId != CurrentZoneId && Aion2Zones.IsEqualizedArena(zoneId))
        {
            _entities.NoteEqualizedArenaEntered(timestamp);
        }

        // The place the frame puts the player (x, y as floats at 18 and 22), when it is long enough to hold them.
        (float X, float Y)? place = frame.Length >= 26
            ? (BinaryPrimitives.ReadSingleLittleEndian(frame[18..]), BinaryPrimitives.ReadSingleLittleEndian(frame[22..]))
            : null;
        // What the frame was for (u32 at 34): 0 for the first one of a visit, 2 for a move inside the zone (the next run's entrance, a cut scene's door),
        // 4 for a revival at an obelisk or kisk (seen in the recordings; a kisk can stand anywhere, so only a move can start a run).
        uint kind = frame.Length >= 38 ? BinaryPrimitives.ReadUInt32LittleEndian(frame[34..]) : 0;
        // The place is also where the player stands after a teleport or a loading screen: the one exact report of his position that comes without any
        // action (checked on a recording: it matched the first effect frame and the monsters announced around him). The pet map starts from it.
        if (place is { } spawn && float.IsFinite(spawn.X) && float.IsFinite(spawn.Y) && Math.Abs(spawn.X) is > 100 and < 500_000 && Math.Abs(spawn.Y) is > 100 and < 500_000)
        {
            _entities.NoteSpawnPosition(spawn.X, spawn.Y);
        }

        if (zoneId == CurrentZoneId)
        {
            // The same zone again: a reconnect, a respawn or a move inside the instance - or the next run of an instance, entered straight from the run
            // before, which puts the player back at the entrance, where the first frame of the stay did (the entrances of runs 1 to 4 of one temple lay within 800 units of each other; the other frames of the stay, a stage and a boss room, 9,000 and more away).
            if (kind == ZoneKindMove && place is { } now && _entryPlace is { } entry && Aion2Zones.IsInstance(zoneId)
                && timestamp - _entryAt >= RestartMinGap && Math.Sqrt(Math.Pow(now.X - entry.X, 2) + Math.Pow(now.Y - entry.Y, 2)) < RestartRadius)
            {
                _entryAt = timestamp;
                _zoneChanges.Add(new ZoneChange(timestamp, zoneId, Restart: true));
            }

            return;
        }

        CurrentZoneId = zoneId;
        _entryPlace = place;
        _entryAt = timestamp;
        _zoneChanges.Add(new ZoneChange(timestamp, zoneId));
    }

    /// <summary>Where the first zone frame of the current stay put the player, and when.</summary>
    private (float X, float Y)? _entryPlace;
    private DateTime _entryAt;

    /// <summary>A repeat of the zone frame at the entrance counts as a new run when this long after the last start, and within this many units of the first frame's place.</summary>
    private static readonly TimeSpan RestartMinGap = TimeSpan.FromSeconds(90);
    private const double RestartRadius = 2000;
    private const uint ZoneKindMove = 2;

    public int SkippedShortFrames { get; private set; }
    public int UnknownOpcodes { get; private set; }

    /// <summary>Damage-opcode frames without a damage block (see <see cref="DecodeVarintDamage"/>).</summary>
    public int NoDamageFrames { get; private set; }

    /// <summary>Each server id seen in front of a player's name (appearance and party frames), with the
    /// names it came with - for working out which id is which server. Diagnostic.</summary>
    public Dictionary<int, SortedSet<string>> ServerIdsSeen { get; } = new();

    private void NoteServerId(int serverId, string name, bool inView = false)
    {
        if (!ServerIdsSeen.TryGetValue(serverId, out var names))
        {
            ServerIdsSeen[serverId] = names = new SortedSet<string>(StringComparer.Ordinal);
        }

        if (names.Count < 12)
        {
            names.Add(name);
        }

        if (inView)
        {
            _entities.NoteServerOfNameInView(name, serverId); // his own frame: no list entry of a namesake replaces it
            return;
        }

        _entities.NoteServerOfName(name, serverId); // a player's faction follows from his server
    }

    public int Bundles { get; private set; }
    public int BundleFailures { get; private set; }

    public IEnumerable<DamageEvent> Decode(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        IEnumerable<DamageEvent> events = DecodeFrame(frame, timestamp, nested: false);
        if (_estimatedTargets.Count > 0)
        {
            foreach (DamageEvent e in events)
            {
                if (!e.IsHeal && _estimatedTargets.Contains(e.TargetObjectId))
                {
                    _exactOnTarget[e.TargetObjectId] = _exactOnTarget.GetValueOrDefault(e.TargetObjectId) + e.Amount;
                }
            }
        }

        if (_noticeOrder.Count == 0 || _noticeOrder.Peek().At > timestamp - NoticeHold)
        {
            return events;
        }

        // The frames came before the notices that waited longest were expired: their hits have no damage frame, they are estimated.
        List<DamageEvent> estimated = ExpireNotices(timestamp);
        return estimated.Count == 0 ? events : events.Concat(estimated).ToList();
    }

    /// <summary>The notices older than <see cref="NoticeHold"/> that no damage frame took: hits whose amount the server did not send.</summary>
    private List<DamageEvent> ExpireNotices(DateTime now)
    {
        var estimated = new List<DamageEvent>();
        DateTime cutoff = now - NoticeHold;
        while (_noticeOrder.Count > 0 && _noticeOrder.Peek().At <= cutoff)
        {
            (DateTime at, (int Actor, int Target, int Skill) key, long lost) = _noticeOrder.Dequeue();
            if (!_notices.TryGetValue(key, out Queue<DateTime>? queue) || queue.Count == 0)
            {
                continue; // a damage frame took this one
            }

            queue.Dequeue();
            if (queue.Count == 0)
            {
                _notices.Remove(key);
            }

            if (EstimateHit(at, key.Actor, key.Target, key.Skill, lost) is { } hit)
            {
                estimated.Add(hit);
            }
        }

        return estimated;
    }

    /// <summary>The estimated hit of a notice that got no damage frame: only a player's damage skill on a world boss, at the skill's average hit.</summary>
    private DamageEvent? EstimateHit(DateTime at, int actor, int target, int skillId, long lostWhenNoticed)
    {
        if (_entities.BossNpcIdOf(target) is not int npc || !Aion2HitAverages.IsEstimatedBoss(npc)
            || _entities.IsSpawned(actor) || _entities.IsKnownPlayer(target)
            || Aion2SkillNames.IsNonDamageEffect(skillId) || Aion2SkillNames.IsHealFamily(skillId)
            || Aion2HitAverages.AverageOf(skillId) is not long average)
        {
            return null;
        }

        int source = _entities.SummonOwnerOf(actor) ?? actor;
        if (Aion2SkillNames.ClassOf(skillId) is string className)
        {
            _entities.NoteClass(source, className);
        }

        (long raw, int hits) = _rawEstimate.GetValueOrDefault(target);
        _rawEstimate[target] = (raw + average, hits + 1);
        long amount = Math.Max(1, (long)Math.Round(average * EstimateScale(target, lostWhenNoticed)));
        return new DamageEvent(at, source, target, amount, false, Aion2SkillNames.NameOf(skillId), false, SkillId: skillId, Estimated: true);
    }

    /// <summary>
    /// How much the table's average hits have to be scaled up on this boss. The server sends the hit notices of only part of the crowd's hits
    /// and the table's skills hit a little softer than the players there, so the estimates alone fall short of what the boss lost (Lagta,
    /// 2026-10-13: 1,266 players, about 11 % of its 275 M hit points; the own 46 exact hits, 215 k, were an average player's share of it).
    /// With the boss's hit points known the shortfall is measured: the hit points lost, less the damage the game did report, over the estimates at
    /// table value. Every estimate is scaled the same, so the players keep their order and the sum is the boss's real loss.
    /// Before <see cref="CalibrationHits"/> estimates there is no measure and the scale is 1; up to <see cref="FullCalibrationHits"/> the measure is blended in. <paramref name="lost"/> is what the boss had lost
    /// when the hit's notice came, not when it was settled 1.5 s later - else the scale runs ahead of the estimates and overshoots.
    /// </summary>
    private double EstimateScale(int target, long lost)
    {
        (long raw, int hits) = _rawEstimate.GetValueOrDefault(target);
        if (hits < CalibrationHits || raw <= 0)
        {
            return 1.0;
        }

        long unexplained = lost - _exactOnTarget.GetValueOrDefault(target);
        if (unexplained <= 0)
        {
            return 1.0;
        }

        double measured = Math.Clamp((double)unexplained / raw, MinScale, MaxScale);
        double weight = Math.Min(1.0, (double)(hits - CalibrationHits) / (FullCalibrationHits - CalibrationHits));
        return 1.0 + (measured - 1.0) * weight;
    }

    /// <summary>A hit notice: waits for the damage frame of the same hit (see <see cref="HitNoticeOpcode"/>).</summary>
    private void DecodeHitNotice(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        int p = 2;
        if (!TryReadVarint(frame, ref p, out long actor) || p >= frame.Length)
        {
            return;
        }

        p++; // the byte between attacker and target
        if (!TryReadVarint(frame, ref p, out long target) || frame.Length < p + 4)
        {
            return;
        }

        // Only a hit on a world boss can come to count; everything else leaves the notice out of the queue.
        if (_entities.BossNpcIdOf(unchecked((int)target)) is not int npc || !Aion2HitAverages.IsEstimatedBoss(npc))
        {
            return;
        }

        _estimatedTargets.Add(unchecked((int)target));
        var key = (unchecked((int)actor), unchecked((int)target), unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..])));
        if (!_notices.TryGetValue(key, out Queue<DateTime>? queue))
        {
            _notices[key] = queue = new Queue<DateTime>();
        }

        queue.Enqueue(timestamp);
        Aion2HitPoints points = _entities.HitPoints;
        long lost = points.HighestSeen(key.Item2) is (> 0 and long max) && points.Latest(key.Item2) is { } latest ? max - latest.Hp : 0;
        _noticeOrder.Enqueue((timestamp, key, lost));
    }

    /// <summary>The damage frame of a hit arrived: its notice is not an estimate any more.</summary>
    private void TakeNotice(int actor, int target, int skillId)
    {
        var key = (actor, target, skillId);
        if (_notices.TryGetValue(key, out Queue<DateTime>? queue) && queue.Count > 0)
        {
            queue.Dequeue();
            if (queue.Count == 0)
            {
                _notices.Remove(key);
            }
        }
    }

    /// <summary>The test modes (aion2-frame-dump) look at every frame with its opcode; null in the live meter.</summary>
    public static Action<int, ReadOnlySpan<byte>, DateTime>? FrameTap { get; set; }

    /// <summary>
    /// The login lists of what the character owns: <c>0x7956</c> titles (<c>n (varint) | n x u32 | ...</c>), <c>0x7256</c> wings (<c>n (u8) | n x (u32 | 0)</c>), <c>0x3292</c> teleporters
    /// (<c>n (u8) | n x u32</c>). Only the counts are kept; the frames are read for other things as well (the worn titles, the wings), which they still are.
    /// </summary>
    private void NoteOwnedCounts(int opcode, ReadOnlySpan<byte> frame)
    {
        switch (opcode)
        {
            case 0x7956:
            {
                int p = 2;
                if (TryReadVarint(frame, ref p, out long n) && n is > 0 and < 5000 && p + n * 4 <= frame.Length)
                {
                    _entities.NoteOwned("titles", (int)n);
                }

                break;
            }

            case 0x7256 when frame.Length == 3 + 5 * frame[2] && frame[2] > 0:
                _entities.NoteOwned("wings", frame[2]);
                break;
            case 0x3292 when frame.Length == 3 + 4 * frame[2] && frame[2] > 0:
                _entities.NoteOwned("teleporters", frame[2]);
                break;
        }
    }

    private const int PantheonSlotCount = 17;
    private const int PantheonBlockSize = 3 + 5 * PantheonSlotCount;

    /// <summary>
    /// The own Pantheon (decorations set up in the Pantheon window, three presets). The login sends it twice: <c>0x8ce2</c> (the login state of many things) ends with the active
    /// setup, <c>0xaf8a</c> lists the registered decorations (<c>n (u8) | n x (id u32 | slot u8)</c>, slot 0 = not set up in the active preset). A change or a tab switch in the window
    /// brings <c>0x96e2</c> with the setup of that tab. A setup is <c>01 | preset (1..3) | count (17, or 0 = empty) | count x (id u32 | slot u8)</c>, slot 1-12 painting, 13-16
    /// statue, 17 colossus, id 0 = empty. The ids are the game's own decoration numbers (painting: the last digits of the item number, statue 1000 + n - 1, colossus 5000 + n - 1).
    /// </summary>
    private void NotePantheon(int opcode, ReadOnlySpan<byte> frame)
    {
        switch (opcode)
        {
            case 0x8ce2 when frame.Length >= 2 + PantheonBlockSize:
                if (TryReadPantheonSetup(frame[^PantheonBlockSize..], out int loginPreset, out List<Aion2PantheonSlot> loginSlots))
                {
                    _entities.SetPantheonPreset(loginPreset, loginSlots);
                }

                break;
            case 0x96e2 when frame.Length is 2 + 3 or 2 + 3 + 1 or 2 + PantheonBlockSize or 2 + PantheonBlockSize + 1:
                if (TryReadPantheonSetup(frame[2..], out int preset, out List<Aion2PantheonSlot> slots))
                {
                    _entities.SetPantheonPreset(preset, slots);
                }

                break;
            case 0xaf8a when frame.Length >= 3 && frame[2] > 0 && frame.Length == 3 + 5 * frame[2]:
            {
                var owned = new List<int>();
                for (int k = 0; k < frame[2]; k++)
                {
                    long id = BinaryPrimitives.ReadUInt32LittleEndian(frame[(3 + 5 * k)..]);
                    if (id is <= 0 or > 100_000)
                    {
                        return; // not this list
                    }

                    owned.Add((int)id);
                }

                _entities.SetPantheonOwned(owned);
                break;
            }
        }
    }

    /// <summary>One Pantheon setup (see <see cref="NotePantheon"/>); false when the bytes are not that, so nothing foreign is taken for it.</summary>
    private static bool TryReadPantheonSetup(ReadOnlySpan<byte> block, out int preset, out List<Aion2PantheonSlot> slots)
    {
        preset = 0;
        slots = new List<Aion2PantheonSlot>();
        if (block.Length < 3 || block[0] != 1 || block[1] is < 1 or > 3 || (block[2] != 0 && block[2] != PantheonSlotCount) || block.Length < 3 + 5 * block[2])
        {
            return false;
        }

        for (int k = 0; k < block[2]; k++)
        {
            int at = 3 + 5 * k;
            if (block[at + 4] != k + 1)
            {
                return false; // the slots run 1..17 in order
            }

            uint id = BinaryPrimitives.ReadUInt32LittleEndian(block[at..]);
            if (id > 100_000)
            {
                return false;
            }

            if (id != 0)
            {
                slots.Add(new Aion2PantheonSlot(k + 1, (int)id));
            }
        }

        preset = block[1];
        return true;
    }

    private IEnumerable<DamageEvent> DecodeFrame(ReadOnlySpan<byte> frame, DateTime timestamp, bool nested)
    {
        FrameLayout layout = _protocol.FrameLayout;
        if (frame.Length < layout.OpcodeOffset + layout.OpcodeSize)
        {
            SkippedShortFrames++;
            return Array.Empty<DamageEvent>();
        }

        int opcode = (int)ReadUnsigned(frame, new FieldSpec(layout.OpcodeOffset, layout.OpcodeSize), layout.LittleEndian && !layout.OpcodeBigEndian);
        FrameTap?.Invoke(opcode, frame, timestamp);
        NoteOwnedCounts(opcode, frame);
        NotePantheon(opcode, frame);
        if (_protocol.BundleOpcode is int bundleOpcode && opcode == bundleOpcode && !nested)
        {
            return DecodeBundle(frame, timestamp);
        }

        if (Capture.DebugRecording.Enabled)
        {
            Capture.ProtocolHealth.Note(opcode, frame.Length, timestamp, _protocol.SyncOpcodes); // the health monitor: part of the debug output
        }

        if (DebugFrameDump is not null)
        {
            lock (DebugFrameDump)
            {
                DebugFrameDump.WriteLine($"{timestamp:HH:mm:ss.fff};0x{opcode:x4};{frame.Length};{Convert.ToHexString(frame)}");
            }
        }

        if (DebugNeedles.Length > 0)
        {
            foreach (byte[] needle in DebugNeedles)
            {
                int at = frame.IndexOf(needle);
                if (at >= 0)
                {
                    Console.WriteLine($"AION2_FIND {timestamp:HH:mm:ss.fff} opcode 0x{opcode:x4} len {frame.Length} at {at}: {Convert.ToHexString(frame[..Math.Min(frame.Length, 160)])}");
                }
            }
        }

        if (opcode == ServerClockOpcode && frame.Length == 10)
        {
            ServerClock.Note(System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(frame[2..10]), DateTime.UtcNow);
            return Array.Empty<DamageEvent>();
        }

        if (opcode == InstanceTimerOpcode)
        {
            if (Aion2InstanceTimer.TryParse(frame, timestamp, out InstanceTimer timer))
            {
                Aion2InstanceTimer.Receive(timer);
            }

            return Array.Empty<DamageEvent>();
        }

        if (opcode == FieldBossListOpcode)
        {
            // the field boss list of a map (see Aion2FieldBosses); other frames of this opcode do not read as such a list and are skipped
            if (Aion2FieldBosses.TryParse(frame, out int bossMap, out List<Aion2FieldBosses.Slot> bossSlots))
            {
                Aion2FieldBosses.Update(bossMap, bossSlots);
            }

            return Array.Empty<DamageEvent>();
        }

        if (opcode == ShopOpcode)
        {
            DecodeShop(frame);
            return Array.Empty<DamageEvent>();
        }

        if ((opcode & 0xFF) == 0xE2)
        {
            NoteNamedServers(frame); // not final: the frame is read by whatever else knows its opcode
        }

        if (opcode == ZoneOpcode)
        {
            DecodeZone(frame, timestamp);
            return Array.Empty<DamageEvent>();
        }

        if (opcode == EngageOpcode)
        {
            DecodeEngage(frame, timestamp);
            return Array.Empty<DamageEvent>();
        }

        if (opcode == CooldownOpcode)
        {
            if (Aion2Cooldowns.TryParse(frame, out List<CooldownEntry> cooldowns))
            {
                Aion2Cooldowns.Receive(cooldowns, timestamp);
            }

            return Array.Empty<DamageEvent>();
        }

        if (opcode == CharacterListOpcode || (opcode == FirstLoginListOpcode && frame.Length > 500))
        {
            DecodeCharacterList(frame);
            return Array.Empty<DamageEvent>();
        }

        if (opcode == TicketListOpcode)
        {
            if (Aion2Tickets.TryParseList(frame, out List<TicketReading> tickets))
            {
                Aion2Tickets.ReceiveList(tickets, timestamp);
            }

            return Array.Empty<DamageEvent>();
        }

        if (opcode == MembershipOpcode)
        {
            if (Aion2Tickets.TryParseMembershipFrame(frame, out List<TicketReading> membership))
            {
                foreach (TicketReading reading in membership)
                {
                    Aion2Tickets.ReceiveUpdate(reading, timestamp);
                }
            }

            return Array.Empty<DamageEvent>();
        }

        if (opcode == PassOpcode)
        {
            if (Aion2Tickets.TryParsePass(frame, out List<TicketReading> passes))
            {
                foreach (TicketReading reading in passes)
                {
                    Aion2Tickets.ReceiveUpdate(reading, timestamp);
                }
            }

            return Array.Empty<DamageEvent>();
        }

        if (opcode == ActorTargetOpcode || opcode == OwnTargetOpcode)
        {
            DecodeTargetChange(frame, timestamp, opcode == OwnTargetOpcode);
            return Array.Empty<DamageEvent>();
        }

        if (opcode == Aion2Rankings.OwnRankOpcode)
        {
            if (Aion2Rankings.TryParseOwnRank(frame, timestamp, out OwnRank ownRank))
            {
                _entities.NoteOwnRank(ownRank);
            }

            return Array.Empty<DamageEvent>();
        }

        if (opcode == Aion2LegionList.Opcode)
        {
            if (Aion2LegionList.TryParse(frame, out List<LegionMember> legion))
            {
                _entities.NoteLegionRoster(legion);
            }

            return Array.Empty<DamageEvent>();
        }

        if (opcode == Aion2Chains.OpenOpcode || opcode == Aion2Chains.CloseOpcode)
        {
            if (opcode == Aion2Chains.OpenOpcode)
            {
                Aion2Chains.NoteOpen(frame, timestamp);
            }
            else
            {
                Aion2Chains.NoteClose(frame);
            }

            return Array.Empty<DamageEvent>();
        }

        if (opcode == Aion2Recipes.LevelUpOpcode)
        {
            Aion2Recipes.NoteLevelUp(frame);
            return Array.Empty<DamageEvent>();
        }

        if (opcode == Aion2Recipes.Opcode)
        {
            Aion2Recipes.Note(frame);
            return Array.Empty<DamageEvent>();
        }

        if (opcode == AchievementsOpcode)
        {
            if (Aion2Tickets.TryParseAchievements(frame, out TicketReading points))
            {
                Aion2Tickets.ReceiveUpdate(points, timestamp);
            }

            return Array.Empty<DamageEvent>();
        }

        if (opcode == ShopCountersOpcode)
        {
            if (Aion2Tickets.TryParseShopCounters(frame, out TicketReading shop))
            {
                Aion2Tickets.ReceiveUpdate(shop, timestamp);
            }

            return Array.Empty<DamageEvent>();
        }

        if (opcode == MissionListOpcode)
        {
            if (Aion2Tickets.TryParseMissions(frame, out TicketReading missions))
            {
                Aion2Tickets.ReceiveUpdate(missions, timestamp);
            }

            return Array.Empty<DamageEvent>();
        }

        if (opcode == TicketChangeOpcode)
        {
            if (Aion2Tickets.TryParseUpdate(frame, out TicketReading ticket, out _, out _))
            {
                Aion2Tickets.ReceiveUpdate(ticket, timestamp);
            }

            return Array.Empty<DamageEvent>();
        }

        if (opcode == StatOpcode)
        {
            DecodeStat(frame, timestamp);
            return Array.Empty<DamageEvent>();
        }

        switch (opcode)
        {
            case WindupOpcode:
                DecodeWindup(frame, timestamp);
                return Array.Empty<DamageEvent>();
            case CastOpcode:
                DecodeCastNotice(frame, timestamp);
                return Array.Empty<DamageEvent>();
            case HitNoticeOpcode:
                DecodeHitNotice(frame, timestamp);
                return Array.Empty<DamageEvent>();
            case BuffOnOpcode:
            case BuffRenewedOpcode:
                DecodeBuff(frame, timestamp, renewed: opcode == BuffRenewedOpcode);
                return Array.Empty<DamageEvent>();
            case BuffEndOpcode:
                DecodeBuffEnd(frame, timestamp);
                return Array.Empty<DamageEvent>();
            case StaggerOpcode:
                DecodeStagger(frame, timestamp);
                return Array.Empty<DamageEvent>();
            case Aion2Loot.GroundItemOpcode:
                Aion2Loot.NoteGroundItem(frame);
                return Array.Empty<DamageEvent>();
            case Aion2Loot.GroundGoneOpcode:
                Aion2Loot.NoteGroundGone(frame, timestamp);
                return Array.Empty<DamageEvent>();
            default:
                if ((opcode & 0xff) == 0x37 && PositionOffset((opcode >> 8) & 0xff) is int positionOffset)
                {
                    DecodePosition(frame, timestamp, positionOffset);
                    return Array.Empty<DamageEvent>();
                }

                break;
        }

        OpcodeFamily family = _protocol.FamilyOf(opcode);
        IReadOnlyDictionary<string, FieldSpec> fields = _protocol.FieldsOf(family);

        switch (family)
        {
            case OpcodeFamily.Damage when string.Equals(_protocol.DamageLayout, "varint-v1", StringComparison.Ordinal):
                return DecodeVarintDamage(frame, timestamp);
            case OpcodeFamily.HpUpdate when string.Equals(_protocol.HpLayout, "varint-v1", StringComparison.Ordinal):
                DecodeHp(frame, timestamp);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Dot when string.Equals(_protocol.DotLayout, "varint-v1", StringComparison.Ordinal):
                return DecodeVarintDot(frame, timestamp);
            case OpcodeFamily.Damage:
            case OpcodeFamily.Dot:
            case OpcodeFamily.Heal:
                return DecodeAmount(frame, timestamp, fields, isHeal: family == OpcodeFamily.Heal, layout.LittleEndian);
            case OpcodeFamily.Nickname when string.Equals(_protocol.NicknameLayout, "varint-v1", StringComparison.Ordinal):
                DecodeVarintNickname(frame, timestamp);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Roster:
                DecodeRoster(frame, timestamp);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Equipment:
                Aion2Stock.NoteSnapshot(frame, timestamp);
                DecodeEquipment(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Daevanion:
                DecodeDaevanion(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Species:
                DecodePetList(frame);
                DecodeSpecies(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.LocalPosition:
                DecodeLocalPosition(frame, timestamp);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.TargetSelect:
                DecodeTargetSelect(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.PetProgress:
                DecodePetProgress(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.PetLevel:
                if (frame.Length >= 12)
                {
                    _entities.NotePetLevel(unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[4..])), unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[8..])));
                }

                return Array.Empty<DamageEvent>();
            case OpcodeFamily.PetAdded:
                if (frame.Length >= 12)
                {
                    _entities.NotePetAdded(unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[4..])), unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[8..])));
                }

                return Array.Empty<DamageEvent>();
            case OpcodeFamily.InventoryChange:
                DecodeInventoryChange(frame, timestamp);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Titles:
                DecodeTitles(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.TitleChange:
                if (frame.Length == 7 && frame[2] is >= 1 and <= 3)
                {
                    _entities.SetLocalTitles([new Aion2TitleSlot(frame[2], (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[3..]))]);
                }

                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Attributes:
                DecodeAttributes(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Skills:
                DecodeSkills(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.SkillEntry:
            case OpcodeFamily.SkillVariant:
                DecodeSkillEntry(frame, family == OpcodeFamily.SkillVariant);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.SkillRemoved:
                DecodeSkillRemoved(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Stigmas:
                DecodeStigmas(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.SkillBar:
                DecodeSkillBar(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.SpecSlots:
                DecodeSpecSlots(frame, 2);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.PresetSwitch:
                DecodePresetSwitch(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Inspect:
                DecodeInspect(frame, timestamp);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Character:
                DecodeCharacter(frame, timestamp);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Appearance:
                DecodeAppearance(frame, timestamp);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.PlayerList:
                DecodePlayerList(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.NpcSpawn:
                DecodeNpcSpawn(frame, timestamp);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.NpcRemove:
                DecodeNpcRemove(frame, timestamp);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.EnvSpawn:
                DecodeEnvSpawn(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.EnvRemove:
                DecodeEnvRemove(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.EnvOpenStart:
                DecodeEnvOpenStart(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Nickname:
                DecodeNickname(frame, fields, layout.LittleEndian);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Session:
                if (TryReadInt(frame, fields, "localPlayerId", layout.LittleEndian, out long localId))
                {
                    _entities.LocalPlayerId = (int)localId;
                }
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Kill:
                DecodeKill(frame, timestamp, fields, layout.LittleEndian);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Avoid:
                DecodeAvoid(frame, timestamp, fields, layout.LittleEndian);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Zone:
                if (TryReadString(frame, fields, "zoneName", layout.LittleEndian, out string? zone))
                {
                    CurrentZone = zone;
                }
                return Array.Empty<DamageEvent>();
            default:
                UnknownOpcodes++;
                return Array.Empty<DamageEvent>();
        }
    }

    public IReadOnlyList<(string Actor, string Skill)> DrainSkillUses()
    {
        if (_skillUses.Count == 0)
        {
            return Array.Empty<(string, string)>();
        }

        var drained = _skillUses.ToArray();
        _skillUses.Clear();
        return drained;
    }

    public IReadOnlyList<KillEvent> DrainKills() => Drain(_kills);

    public IReadOnlyList<AvoidEvent> DrainAvoids() => Drain(_avoids);

    private static IReadOnlyList<T> Drain<T>(List<T> list)
    {
        if (list.Count == 0)
        {
            return Array.Empty<T>();
        }

        var drained = list.ToArray();
        list.Clear();
        return drained;
    }

    private IEnumerable<DamageEvent> DecodeAmount(ReadOnlySpan<byte> frame, DateTime timestamp, IReadOnlyDictionary<string, FieldSpec> fields, bool isHeal, bool littleEndian)
    {
        if (!TryReadInt(frame, fields, "sourceId", littleEndian, out long sourceId)
            || !TryReadInt(frame, fields, "targetId", littleEndian, out long targetId)
            || !TryReadInt(frame, fields, "amount", littleEndian, out long amount))
        {
            SkippedShortFrames++;
            return Array.Empty<DamageEvent>();
        }

        string? skill = null;
        if (TryReadInt(frame, fields, "skillId", littleEndian, out long skillId) && skillId != 0)
        {
            skill = Aion2SkillNames.NameOf((int)skillId);
            string? actor = _entities.NameFor((int)sourceId);
            if (actor is not null)
            {
                _skillUses.Enqueue((actor, skill));
            }
        }

        bool critical = false;
        if (fields.TryGetValue("flags", out FieldSpec? flagsSpec) && flagsSpec.Mask is string mask
            && TryReadInt(frame, fields, "flags", littleEndian, out long flags))
        {
            critical = (flags & Convert.ToInt64(mask, 16)) != 0;
        }

        return new[] { new DamageEvent(timestamp, (int)sourceId, (int)targetId, amount, isHeal, skill, critical) };
    }

    /// <summary>
    /// A bundle frame: opcode | u32 LE uncompressed size | LZ4 block. The block is a run of ordinary
    /// frames in the same varint framing (verified: all of a real capture's bundles split cleanly),
    /// so each one is decoded exactly like a top-level frame. A bundle inside a bundle is not seen
    /// in practice and is not followed.
    /// </summary>
    private List<DamageEvent> DecodeBundle(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        var events = new List<DamageEvent>();
        FrameLayout layout = _protocol.FrameLayout;
        int headerEnd = layout.OpcodeOffset + layout.OpcodeSize + 4;
        if (!layout.IsVarint || frame.Length <= headerEnd)
        {
            BundleFailures++;
            return events;
        }

        uint size = BinaryPrimitives.ReadUInt32LittleEndian(frame[(layout.OpcodeOffset + layout.OpcodeSize)..]);
        if (size > Aion2Lz4.MaxOutput || !Aion2Lz4.TryDecompress(frame[headerEnd..], (int)size, out byte[] raw))
        {
            BundleFailures++;
            return events;
        }

        Bundles++;
        int p = 0;
        while (p < raw.Length)
        {
            long length = 0;
            int prefix = 0;
            bool complete = false;
            while (prefix < 5 && p + prefix < raw.Length)
            {
                byte b = raw[p + prefix];
                length |= (long)(b & 0x7f) << (7 * prefix);
                prefix++;
                if ((b & 0x80) == 0)
                {
                    complete = true;
                    break;
                }
            }

            long total = length + prefix + layout.LengthBias;
            if (!complete || total - prefix < layout.OpcodeOffset + layout.OpcodeSize || p + total > raw.Length)
            {
                BundleFailures++;
                break;
            }

            events.AddRange(DecodeFrame(raw.AsSpan(p + prefix, (int)total - prefix), timestamp, nested: true));
            p += (int)total;
        }

        return events;
    }

    private const int DodgeSkillId = 11000100;

    /// <summary>Some frames carry a 250,000,000-style placeholder instead of a real hit (seen on an
    /// NPC skill in the reference capture); nothing a player deals comes near it.</summary>
    private const long MaxPlausibleAmount = 100_000_000;

    /// <summary>
    /// The Aion 2 damage frame, as verified against a real capture and the in-game combat log
    /// (2026-09-30): opcode(2) | target id (varint) | 2 flag bytes | actor id (varint) | skill id
    /// (u32 LE, the decimal skill number) | sequence(1) | hit type(1: 2 = normal, 3 = critical) |
    /// variable block | 4-byte hit count (1..9) | 2 bytes | damage (varint) | extra-hit counters.
    /// The variable block is skipped by looking for the hit count; every frame of the reference
    /// capture (5,779 + 931 + 11,000 frames) carries one.
    /// </summary>
    /// <summary>
    /// "A monster appears": entity id (varint), three type bytes, then the monster's NPC id as a
    /// little-endian uint32 (verified on a Krao Cave run: 2300104 = Enhanced Harcon). Only boss ids
    /// are kept - see <see cref="Aion2BossCatalog"/>.
    /// </summary>
    /// <summary>
    /// "A monster disappears": opcode | entity id (varint) | 00 | reason. Reason 3 follows a monster's
    /// death (a few seconds after its hit points read 0), 7 a monster that vanished alive - the
    /// instance was closed because the group took too long (Talisra, 2026-10-07: back to full health,
    /// gone 65 s later). Only a boss entity's reason is of use (see Aion2EntityDirectory.RemovalOf).
    /// </summary>
    /// <summary>The env object ids the client names "Hidden Cube" (Tools/aion2-dat/build_hidden_cubes.py): Verteron 1200036-41, 1200667, Altgard 1301199/1301200, the Abyss ones and the field-event cubes.</summary>
    private static bool IsHiddenCube(long envId) =>
        envId is >= 1200036 and <= 1200041 or 1200667 or 1200707 or >= 1201502 and <= 1201505 or >= 1300081 and <= 1300083 or 1300754
            or 1301199 or 1301200 or >= 1301242 and <= 1301245 or 1100056 or 1100094;

    private void DecodeEnvSpawn(ReadOnlySpan<byte> frame)
    {
        int p = 2;
        if (!TryReadVarint(frame, ref p, out long entityId) || frame.Length < p + 18)
        {
            return;
        }

        long envId = BinaryPrimitives.ReadUInt32LittleEndian(frame[(p + 2)..]);
        (string Kind, string Key)? what = IsHiddenCube(envId) ? ("Cube", "Hidden Cube") : Aion2Gather.Lookup(envId);
        if (what is null)
        {
            return;
        }

        float x = BinaryPrimitives.ReadSingleLittleEndian(frame[(p + 6)..]);
        float y = BinaryPrimitives.ReadSingleLittleEndian(frame[(p + 10)..]);
        if (float.IsFinite(x) && float.IsFinite(y) && Math.Abs(x) < 1_000_000 && Math.Abs(y) < 1_000_000)
        {
            // the height follows y; the state byte is at +28 and the Unix time the object came into being (ms) at +29 (a value outside 2020 to 2052 is no time)
            float z = frame.Length >= p + 18 ? BinaryPrimitives.ReadSingleLittleEndian(frame[(p + 14)..]) : 0;
            long created = frame.Length >= p + 37 ? BinaryPrimitives.ReadInt64LittleEndian(frame[(p + 29)..]) : 0;
            _entities.NoteEnv(unchecked((int)entityId), what.Value.Kind, what.Value.Key, x, y, float.IsFinite(z) ? z : 0, created is < 1_600_000_000_000 or > 2_600_000_000_000 ? 0 : created);
        }
    }

    /// <summary>"An environment object disappears": entity id (varint), reason byte (04 = gathered or opened, 01 = left the player's range), two more bytes.
    /// The reasons are those of dr89mh/aion2-cube-radar (docs/FINDINGS.md B6), seen the same on our recordings.</summary>
    private void DecodeEnvRemove(ReadOnlySpan<byte> frame)
    {
        int p = 2;
        if (TryReadVarint(frame, ref p, out long entityId))
        {
            _entities.ForgetEnv(unchecked((int)entityId), p < frame.Length ? frame[p] : 0);
        }
    }

    /// <summary>"A player starts opening an environment object" (0x3836): object entity (varint), actor entity (varint), 01. For a Hidden Cube it is followed by 0x3a36
    /// (the same two ids, then count 1 and the key item 630900003) and the remove frame with reason 04 (dr89mh/aion2-cube-radar, FINDINGS B6).</summary>
    private void DecodeEnvOpenStart(ReadOnlySpan<byte> frame)
    {
        int p = 2;
        if (TryReadVarint(frame, ref p, out long entityId) && TryReadVarint(frame, ref p, out long actorId))
        {
            _entities.NoteEnvOpening(unchecked((int)entityId), unchecked((int)actorId));
        }
    }

    private void DecodeNpcRemove(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        int p = 2;
        if (!TryReadVarint(frame, ref p, out long entityId) || frame.Length != p + 2)
        {
            return;
        }

        _entities.NoteRemoved(unchecked((int)entityId), frame[^1], timestamp);
        _entities.Combat.NoteGone(unchecked((int)entityId), timestamp);
        _entities.Combat.ForgetEntity(unchecked((int)entityId));
    }

    private void DecodeNpcSpawn(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        int p = 2;
        if (!TryReadVarint(frame, ref p, out long entityId) || frame.Length < p + 7)
        {
            return;
        }

        _spawnedAt[unchecked((int)entityId)] = timestamp;
        _power.Remove(unchecked((int)entityId));
        _effectTargets.Remove(unchecked((int)entityId)); // the id is reused: earlier votes belong to something else

        // Two type bytes, then a flag: 1 = the entity carries a name (a summon's owner, e.g. a
        // Cleric's Divine Aura announced as "Psefon"), length-prefixed, before the NPC id.
        string? ownerName = null;
        if (frame[p + 2] == 1 && TryReadName(frame, p + 3, out string named, minLength: 2))
        {
            ownerName = named;
            p += 1 + frame[p + 3]; // the length prefix counts bytes (ë is two), not characters
            if (frame.Length < p + 7)
            {
                return;
            }
        }

        p += 3;
        int npcId = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]));
        _entities.NoteSpawned(unchecked((int)entityId));
        _entities.RegisterNpc(unchecked((int)entityId), npcId);
        _entities.HitPoints.NoteSpawn(unchecked((int)entityId), ReadSpawnHitPoints(frame, p + 4));
        if (ownerName is not null && Aion2Npcs.IsMonsterNotPlayerSummon(npcId))
        {
            ownerName = null; // a monster naming the player it targets (Blazing Totem), not that player's summon
        }

        _entities.SetSummonOwnerName(unchecked((int)entityId), ownerName);
        if (frame.Length >= p + 14)
        {
            // where the monster stands: the pet map recognises the map from the pet monsters around the player
            float mx = BinaryPrimitives.ReadSingleLittleEndian(frame[(p + 6)..]);
            float my = BinaryPrimitives.ReadSingleLittleEndian(frame[(p + 10)..]);
            if (float.IsFinite(mx) && float.IsFinite(my) && Math.Abs(mx) < 1_000_000 && Math.Abs(my) < 1_000_000)
            {
                _entities.NoteMobPosition(unchecked((int)entityId), npcId, mx, my);
                // where it appeared is the first point of its track: a monster that never moves still has a place on the replay map
                _entities.Combat.NotePosition(timestamp, unchecked((int)entityId), mx, my);
            }
        }

        // Further on: eight FF bytes, eight more bytes, then the owner's id (varint). An ordinary
        // monster names itself there; a summoned spirit names the player who summoned it (verified on
        // three Krao Cave / Urugugu captures, 2026-10-02: all 161 spirits resolved to the
        // Spiritmaster casting their "Summon:" skills, three Spiritmasters in one party kept apart).
        // A Cleric's Divine Aura names itself here, and its owner by name instead (above).
        int marker = frame[(p + 4)..].IndexOf(OwnerMarker);
        int q = marker < 0 ? -1 : p + 4 + marker + OwnerMarker.Length + 8;
        // Some monsters' spawns carry no owner block at all: a spawn is a new entity all the same, so an owner
        // from the id's previous life goes (a world boss add once took a stale owner and showed as a player).
        int? summoner = q > 0 && q < frame.Length && TryReadVarint(frame, ref q, out long owner) && owner > 0 && owner != entityId
            ? unchecked((int)owner)
            : null;
        _entities.SetSummonOwner(unchecked((int)entityId), summoner);
        if (q > 0 && q <= frame.Length && ReadSpawnLevel(frame[q..]) is int level)
        {
            _entities.NoteNpcLevel(unchecked((int)entityId), level);
        }
    }

    /// <summary>
    /// The hit points in a spawn frame: 21 bytes behind the NPC code (33 in the "extended" state) come two varints, current and maximum
    /// (current not above maximum), then exactly <c>64 00 00 00 64 00 00 00</c>. The trailer is what keeps other bytes from passing as
    /// hit points. Verified on recordings: Raur 5,220,000, Kromede's Desire 15,504,000, Ultimate Berk 123,000 (exploration, one player).
    /// </summary>
    private static (long Current, long Max)? ReadSpawnHitPoints(ReadOnlySpan<byte> frame, int afterCode)
    {
        ReadOnlySpan<byte> trailer = stackalloc byte[] { 0x64, 0, 0, 0, 0x64, 0, 0, 0 };
        foreach (int skip in stackalloc[] { 21, 33 })
        {
            int q = afterCode + skip;
            if (q >= frame.Length)
            {
                return null;
            }

            if (TryReadVarint(frame, ref q, out long current) && TryReadVarint(frame, ref q, out long max)
                && current >= 0 && max > 0 && current <= max && frame.Length >= q + 8 && frame.Slice(q, 8).SequenceEqual(trailer))
            {
                return (current, max);
            }
        }

        return null;
    }

    /// <summary>
    /// The monster's level in a spawn frame: after the owner block comes the position, then <c>01 00</c> and the level as a 4-byte number
    /// (seen on Ascension rite bosses: 45 / 50 / 55 / 60 by difficulty, and on level-45 monsters alike). Null when no such field follows.
    /// </summary>
    private static int? ReadSpawnLevel(ReadOnlySpan<byte> rest)
    {
        for (int i = 0; i + 6 <= rest.Length; i++)
        {
            if (rest[i] == 0x01 && rest[i + 1] == 0x00 && rest[i + 2] is >= 1 and <= 100 && rest[i + 3] == 0 && rest[i + 4] == 0 && rest[i + 5] == 0)
            {
                return rest[i + 2];
            }
        }

        return null;
    }

    /// <summary>
    /// A summon whose spawn names no owner, neither by id nor by name (a Sorcerer's Bittercold Wind):
    /// an entity the server announced as a monster that casts a class's skills is somebody's
    /// summon, and when exactly one member of the party plays that class, it is theirs. Remembered
    /// once found. With two players of the class nothing is guessed.
    /// <para>Only a direct hit on a monster counts. Damage-over-time frames name a class skill next
    /// to a monster too: a Sorcerer's Steel Barrier absorbing a monster's blow reads "monster X,
    /// effect Steel Barrier, on the Sorcerer" - which once made a boss's add (Phantasmal Lakshmi)
    /// the party Sorcerer's summon, its blows on the party his damage (Draupnir capture,
    /// 2026-10-02).</para>
    /// <para>Two players of the class: the summon strikes with the variant of the skill its owner
    /// cast (skill id / 10 - talents pick the variant), and its owner cast it just before it
    /// appeared. On a Draupnir run with two Sorcerers (2026-10-02 23:00), all 23 Bittercold Winds
    /// fit both: Lumy cast 15280240 and her winds hit with 15280242/3, Aurulio cast 15280030 and
    /// his hit with 15280032/3, each cast ~50 ms before the spawn. The caster of that variant
    /// closest before the spawn is the owner - by id, so it works before the players are named.</para>
    /// </summary>
    private int? GuessSummonOwner(int actor, int skillId, int target)
    {
        // The target is no player and no summon. Not IsKnownMonster: started mid-fight, the meter
        // never saw the boss spawn.
        if (!_entities.IsSpawned(actor) || _entities.IsKnownPlayer(target) || _entities.SummonOwnerOf(target) is not null
            || Aion2SkillNames.ClassOf(skillId) is not string className)
        {
            return null;
        }

        // A party member of the class who cast this variant just before (within 5 s); else any caster of
        // it within 2 s - a party member not named yet (the meter started inside a dungeon) or a player
        // outside the party, whose summon is theirs too (at a world boss the crowd is many small groups;
        // refused, each Bittercold Wind showed as a player of its own); else the party's only player of
        // the class.
        int variant = skillId / 10;
        var party = _entities.PartyMemberIdsOfClass(className).Where(id => id != actor).ToList();
        int? owner = OwnerByCast(actor, variant, TimeSpan.FromSeconds(5), id => party.Contains(id))
            ?? OwnerByCast(actor, variant, TimeSpan.FromSeconds(2), _ => true);
        if (owner is null && party.Count == 1)
        {
            owner = party[0];
        }

        if (owner is int found)
        {
            _pendingSpawned.Remove(actor);
            _entities.SetSummonOwner(actor, found);
        }
        else if (party.Count == 0 && _pendingSpawned.Count < 64)
        {
            // nobody of the class is known in the party yet (their own casts have not been seen): try again with the next hits
            _pendingSpawned[actor] = className;
        }

        return owner;
    }

    /// <summary>Spawned summons without an owner whose class had no known party member yet (a Sorcerer's wind before the Sorcerer was read): handed over once exactly one is known.</summary>
    private readonly Dictionary<int, string> _pendingSpawned = new();

    /// <summary>The caster accepted by <paramref name="eligible"/> who cast this variant of the
    /// summon's skill closest before it spawned, within <paramref name="window"/> (a Sorcerer
    /// summons a wind every ten seconds or more).</summary>
    private int? OwnerByCast(int summon, int variant, TimeSpan window, Func<int, bool> eligible)
    {
        if (!_spawnedAt.TryGetValue(summon, out DateTime spawned))
        {
            return null;
        }

        var justBefore = _lastCasts
            .Where(kv => kv.Key.Variant == variant && kv.Key.Caster != summon && eligible(kv.Key.Caster))
            .Select(kv => (Id: kv.Key.Caster, Gap: spawned - kv.Value))
            .Where(c => c.Gap >= TimeSpan.Zero && c.Gap <= window)
            .OrderBy(c => c.Gap)
            .ToList();
        return justBefore.Count > 0 ? justBefore[0].Id : null;
    }

    /// <summary>A player's cast of a class skill, remembered for <see cref="OwnerByCast"/>.</summary>
    private void NoteCast(int actor, int skillId, DateTime timestamp)
    {
        if (!_entities.IsSpawned(actor) && Aion2SkillNames.ClassOf(skillId) is not null)
        {
            _lastCasts[(actor, skillId / 10)] = timestamp;
        }
    }

    /// <summary>
    /// How a hit landed, from the bytes after its damage type (<paramref name="p"/> is the byte after the skill id): the first flag byte's low nibble 4 is a plain hit with
    /// nothing more; 5-7 add three bytes - a bit set (2 parry, 4 perfect, 8 double) and, two bytes on (after the amount restored when bit 0x20 is set), the direction (1 back,
    /// 2 front). Null when the frame is too short or of another kind.
    /// </summary>
    private static HitKinds? ReadHitKinds(ReadOnlySpan<byte> frame, int p, int flags)
    {
        int kind = flags & 0xf;
        if (kind == 4)
        {
            return HitKinds.None;
        }

        if (kind is < 5 or > 7 || p + 5 > frame.Length)
        {
            return null;
        }

        byte bits = frame[p + 2];
        // Bit 0x20 (a life-steal hit) turns the byte after the bits into a varint of 1-3 bytes (the amount restored), which pushes the direction on:
        // on 1,049 such hits of four recordings (2026-10-21 to 23) the old fixed read gave directions of 3-7, the read after the varint only 0-2.
        int at = p + 3;
        if ((bits & 0x20) != 0)
        {
            if (!TryReadVarint(frame, ref at, out _))
            {
                return null;
            }
        }
        else
        {
            at++;
        }

        if (at >= frame.Length)
        {
            return null;
        }

        byte direction = frame[at];
        HitKinds kinds = HitKinds.None;
        if ((bits & 2) != 0) { kinds |= HitKinds.Parry; }
        if ((bits & 4) != 0) { kinds |= HitKinds.Perfect; }
        if ((bits & 8) != 0) { kinds |= HitKinds.Double; }
        if (direction == 1) { kinds |= HitKinds.Back; }
        else if (direction == 2) { kinds |= HitKinds.Front; }
        return kinds;
    }

    /// <summary>
    /// A summon that appeared before the meter started has no spawn frame on record, so neither its
    /// owner field nor its owner's name nor the cast before it is known. It still casts nothing but
    /// its summon attack (a spirit's "Fire Spirit: Leaping Slam", a Cleric's "Divine Aura", a
    /// Sorcerer's "Bittercold Wind"), and when the party has exactly one player of that class, known
    /// by id, it is theirs (Canyon Urugugu, recording started mid-fight, 2026-10-03: two Divine Auras
    /// and two spirits left as Player #id). A spirit's basic attack counts too, though its id names no
    /// class (see <see cref="Aion2SkillNames.SummonAttackClass"/>): the Ancient Spirit cast nothing
    /// else once its owner was known. A named entity is a player, never a summon.
    /// </summary>
    private int? LeftoverSummonOwner(int actor, int skillId)
    {
        if (_entities.IsSpawned(actor) || _entities.HasName(actor))
        {
            return null;
        }

        // A Sorcerer's Fire Wall / Cold Storm can hit from an object whose spawn the meter never saw (Deus Research Base, 2026-10-22: three of
        // them stayed Player #id). In an instance only the own party is around, so there the party's only Sorcerer is the owner.
        string? className = Aion2SkillNames.SummonAttackClass(skillId)
            ?? (Aion2Zones.IsInstance(CurrentZoneId) && Aion2SkillNames.NameOf(skillId) is "Fire Wall" or "Cold Storm" ? Aion2SkillNames.ClassOf(skillId) : null);
        if (className is null)
        {
            return null;
        }

        var owners = _entities.PartyPlayerIdsOfClass(className).Where(id => id != actor).ToList();
        if (owners.Count > 1)
        {
            owners = OwnersByPower(actor, owners);
        }

        if (owners.Count != 1)
        {
            // the party is not read yet (the meter started a moment ago): try again with the next hits, the first ones are handed over then
            if (owners.Count == 0 && _pendingLeftovers.Count < 64)
            {
                _pendingLeftovers[actor] = className;
            }

            return null;
        }

        _pendingLeftovers.Remove(actor);
        _entities.SetSummonOwner(actor, owners[0]);
        return owners[0];
    }

    /// <summary>Summons seen before the party was known (class of their attack): their owner is the party's only player of that class, once the roster is read.</summary>
    private readonly Dictionary<int, string> _pendingLeftovers = new();

    private void ResolvePendingLeftovers()
    {
        foreach ((int actor, string className) in _pendingSpawned.ToList())
        {
            if (_entities.SummonOwnerOf(actor) is not null || _entities.HasName(actor))
            {
                _pendingSpawned.Remove(actor);
                continue;
            }

            var owners = _entities.PartyPlayerIdsOfClass(className).Where(id => id != actor).ToList();
            if (owners.Count == 1)
            {
                _pendingSpawned.Remove(actor);
                _entities.SetSummonOwner(actor, owners[0]);
            }
        }

        foreach ((int actor, string className) in _pendingLeftovers.ToList())
        {
            if (_entities.IsSpawned(actor) || _entities.HasName(actor) || _entities.SummonOwnerOf(actor) is not null)
            {
                _pendingLeftovers.Remove(actor);
                continue;
            }

            var owners = _entities.PartyPlayerIdsOfClass(className).Where(id => id != actor).ToList();
            if (owners.Count == 1)
            {
                _pendingLeftovers.Remove(actor);
                _entities.SetSummonOwner(actor, owners[0]);
            }
        }
    }

    // Per actor: the damage multipliers (power scalar, 1/100 %) and the distinct skills its hits carried.
    private readonly Dictionary<int, (HashSet<int> Scalars, HashSet<int> Skills)> _power = new();

    private void NotePower(int actor, int scalar, int skillId)
    {
        if (!_power.TryGetValue(actor, out var entry))
        {
            if (_power.Count >= 2048)
            {
                _power.Clear(); // ids are reissued with every zone: old readings are worthless anyway
            }

            _power[actor] = entry = (new HashSet<int>(), new HashSet<int>());
        }

        if (entry.Scalars.Count < 16)
        {
            entry.Scalars.Add(scalar);
        }

        if (entry.Skills.Count < 64)
        {
            entry.Skills.Add(skillId);
        }
    }

    /// <summary>
    /// A summon never announced (a Cleric's Divine Aura has no spawn record) among several players of its class: every damage record
    /// carries the actor's power scalar, a summon's is its owner's. The candidates whose scalars share a value with the summon's and
    /// who use at least three times as many skills (a player runs a rotation, a summon repeats one or two skills); the owner when
    /// exactly one remains, else the list unchanged. In the recordings of 2026-10-13/14 this was right 334 times and wrong 16 times
    /// with every same-class stranger of the zone as candidate, so it is only asked of the party's players.
    /// </summary>
    private List<int> OwnersByPower(int summon, List<int> candidates)
    {
        if (!_power.TryGetValue(summon, out var mine))
        {
            return candidates;
        }

        var fits = candidates.Where(id => _power.TryGetValue(id, out var theirs)
            && theirs.Scalars.Overlaps(mine.Scalars) && theirs.Skills.Count >= 3 * Math.Max(mine.Skills.Count, 1)).ToList();
        return fits.Count == 1 ? fits : candidates;
    }

    // Player summon -> how often each player was the target of its effects, for summons whose spawn names no owner.
    private readonly Dictionary<int, Dictionary<int, int>> _effectTargets = new();

    /// <summary>
    /// A player summon fires its friendly effects (heals, buffs) at its owner: of the recordings of 2026-10-13/14, the most-targeted
    /// player was the owner in 173 of 174 summons whose spawn names it (and filled in 90 more with no owner at all). Only NPCs of the
    /// player-summon list vote: counting every spawned entity made 445 monsters the "summon" of the player they hit. Two or more
    /// effects, a clear leader, and a target that is no monster and no spawned entity.
    /// </summary>
    private void NoteFriendlyEffect(int actor, int target)
    {
        if (actor == target || !_entities.IsSpawned(actor) || _entities.IsSpawned(target) || _entities.IsKnownMonster(target)
            || _entities.SummonOwnerOf(actor) is not null || _entities.NpcIdOf(actor) is not int npc || !Aion2Npcs.IsPlayerSummon(npc))
        {
            return;
        }

        if (!_effectTargets.TryGetValue(actor, out var votes))
        {
            if (_effectTargets.Count >= 512)
            {
                return;
            }

            _effectTargets[actor] = votes = new Dictionary<int, int>();
        }

        votes[target] = votes.GetValueOrDefault(target) + 1;
        var ranked = votes.OrderByDescending(v => v.Value).Take(2).ToList();
        if (ranked[0].Value >= 2 && (ranked.Count == 1 || ranked[0].Value > ranked[1].Value))
        {
            _entities.SetSummonOwner(actor, ranked[0].Key);
            _pendingSpawned.Remove(actor);
            _effectTargets.Remove(actor);
        }
    }

    private static ReadOnlySpan<byte> OwnerMarker => new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff };

    private IEnumerable<DamageEvent> DecodeVarintDamage(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        if (_pendingLeftovers.Count > 0 || _pendingSpawned.Count > 0)
        {
            ResolvePendingLeftovers();
        }

        int p = 2;
        if (!TryReadVarint(frame, ref p, out long target) || frame.Length < p + 2)
        {
            SkippedShortFrames++;
            return Array.Empty<DamageEvent>();
        }

        // Bit 0x04 of the first flag byte says the frame carries a damage block. Without it the frame
        // is a skill's companion notice (amount 1-4, often aimed at the caster itself), sent next to
        // nearly every real hit: verified on two Krao Cave captures (2026-10-02) against the in-game
        // combat analyzer, whose per-skill hit counts match only once these are left out.
        int flags = frame[p];
        p += 2;
        if ((flags & 0x04) == 0)
        {
            // Still a cast: a summoning skill is announced this way, just before its summon spawns.
            NoDamageFrames++;
            if (TryReadVarint(frame, ref p, out long caster) && frame.Length >= p + 4)
            {
                NoteCast(unchecked((int)caster), unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..])), timestamp);
                NoteFriendlyEffect(unchecked((int)caster), unchecked((int)target));
            }

            return Array.Empty<DamageEvent>();
        }

        if (!TryReadVarint(frame, ref p, out long actor) || frame.Length < p + 6)
        {
            SkippedShortFrames++;
            return Array.Empty<DamageEvent>();
        }

        int skillId = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]));
        p += 4;
        TakeNotice((int)actor, (int)target, skillId);
        NoteCast((int)actor, skillId, timestamp);
        NoteFriendlyEffect((int)actor, (int)target);
        if (Aion2SkillNames.IsNonDamageEffect(skillId))
        {
            return Array.Empty<DamageEvent>();
        }

        bool critical = frame[p + 1] == 3;
        HitKinds? kinds = ReadHitKinds(frame, p, flags);

        int marker = -1;
        for (int i = p + 2; i + 4 <= frame.Length; i++)
        {
            if (frame[i] is >= 1 and <= 9 && frame[i + 1] == 0 && frame[i + 2] == 0 && frame[i + 3] == 0)
            {
                marker = i;
                break;
            }
        }

        // The amount follows the marker and one more varint (a hit-kind field). Mostly that field is two
        // bytes long, but with larger values (battlefield frames: A0 9C 01, 94 A0 01) it is three, and
        // a fixed "marker + 6" then read the field's last byte as the damage: every hit read as 1.
        int hitCount = marker >= 0 ? Math.Clamp((int)frame[marker], 1, 9) : 1;
        int q = marker + 4;
        if (marker < 0 || !TryReadVarint(frame, ref q, out long powerScalar) || q >= frame.Length || !TryReadVarint(frame, ref q, out long amount) || amount <= 0 || amount > MaxPlausibleAmount)
        {
            SkippedShortFrames++;
            return Array.Empty<DamageEvent>();
        }

        NotePower((int)actor, (int)powerScalar, skillId);

        if (skillId == DodgeSkillId)
        {
            _avoids.Add(new AvoidEvent(timestamp, (int)actor, (int)target, AvoidKind.Dodge));
            return Array.Empty<DamageEvent>();
        }

        // A summoned spirit's hits are its summoner's, as in the game's own combat analyzer. The heal
        // test below still looks at the spirit itself: its spawn "heal" targets its own id.
        int source = _entities.SummonOwnerOf((int)actor) ?? GuessSummonOwner((int)actor, skillId, (int)target)
            ?? LeftoverSummonOwner((int)actor, skillId) ?? (int)actor;
        if (Aion2SkillNames.ClassOf(skillId) is string className)
        {
            _entities.NoteClass(source, className);
        }

        // No "skill used" notification for the window: its handler refreshes the row list, which must only
        // happen on the UI thread - and this runs on the capture thread. Aion 2 knows each
        // player's class from the skill ids themselves (see Aion2EntityDirectory.NoteClass).
        string skill = Aion2SkillNames.NameOf(skillId);

        // A heal-family skill is a heal unless it lands on a monster (Blood Absorption drains one).
        // "A monster" means one the server announced: a player is only known once seen casting, so a
        // Chanter's Recuperation on a member who had not cast yet used to read as damage between two
        // players - and one such hit made the resolver paint the whole party as enemies.
        bool isHeal = Aion2SkillNames.IsHealFamily(skillId) && !_entities.IsKnownMonster((int)target);
        // Not IsKnownMonster: started mid-fight, the meter never saw the boss spawn.
        if (!isHeal && _entities.IsKnownPlayer(source) && !_entities.IsKnownPlayer((int)target) && _entities.SummonOwnerOf((int)target) is null)
        {
            _entities.NoteMonsterHit(source, (int)target, skillId);
            if (_entities.IsLocalPlayer(source))
            {
                _entities.NoteLocalHit((int)target);
            }
        }

        // A heal on a summon is not healing the group: a Spiritmaster's spirit arrives with a heal of
        // its full health on itself (~56,000 per summon - 4.07 M over one Krao Cave run once spirits
        // are credited to their summoner), and topping up one's spirits is not party healing either.
        if (isHeal && _entities.SummonOwnerOf((int)target) is not null)
        {
            return Array.Empty<DamageEvent>();
        }

        _entities.Combat.NoteAim(source, (int)target, timestamp);
        if (!isHeal)
        {
            _entities.Combat.NoteHit(timestamp, source, (int)target, skillId, amount, _entities.IsKnownPlayer((int)target), _entities.IsLocalPlayer((int)target));
        }

        // A blow a shield took a share of (see DecodeVarintDot): the hit frame carries the blow's FULL amount, the
        // player lost the rest - the hit points went down by exactly blow - share on the captures of 2026-10-08
        // (Protection Circle 1,535 - 638 = 897, 4,460 - 1,080 = 3,380). A share from an earlier packet belongs to a
        // blow the shield took whole. Found in StacysView's fork, checked on our own recordings.
        long taken = amount;
        if (!isHeal && _shieldShares.Remove(((int)actor, (int)target), out var share) && share.At == timestamp)
        {
            taken -= share.Amount;
            if (taken <= 0)
            {
                return Array.Empty<DamageEvent>();
            }
        }

        return new[] { new DamageEvent(timestamp, source, (int)target, taken, isHeal, skill, critical && !isHeal, SkillId: skillId, Kinds: isHeal ? null : kinds, HitCount: hitCount, ViaSummon: !isHeal && source != (int)actor) };
    }

    /// <summary>
    /// A damage- or heal-over-time tick, sent once a second per running effect: opcode | target
    /// (varint) | flags (1) | actor (varint) | stack (varint) | effect id (u32) | amount (varint, if
    /// flags &amp; 0x02) | heal (varint, if flags &amp; 0x01) | skill id (u32 LE, if flags &amp; 0x08).
    /// Every one of a capture's 556 tick frames parses to its exact length this way.
    /// <para>Damage (flags 0x0a): the amount is the tick's damage - the 23 ticks of the local player's
    /// Jointstrike: Curse on Ultimate Berk sum to 12,180, exactly what the in-game combat analyzer
    /// adds on top of the casts' direct hits (Krao Cave capture, 2026-10-02).</para>
    /// <para>Heal (flags 0x0b): the heal field is the tick's heal and the amount what is still to
    /// come - a Chanter's Recuperation announced 334 (flags 0x09, heal only), then ticked 83 four
    /// times while the amount ran 251, 168, 85, 2. So 0x0b ticks of a class's heal-family skill, or
    /// of any skill one player keeps on another, are heals of the heal field; potions and other
    /// classless effects are not counted.</para>
    /// <para>A player's own effects have ids of ten digits that start like the skill's (Drill Dart
    /// 14050340 ticks as effect 1405000022); a monster's have nine (its skill id x 100 + 11). A tick
    /// that pairs a monster's effect with a player's class skill is the monster's effect set off by
    /// that hit, not the player's damage: the boss of 2026-10-03 23:25/23:37 cast 1604660 on itself
    /// (effect 160466011), and from then on every hit of the Ranger came with such a tick of four
    /// times the hit - Deadshot 30,697, tick 122,788, hit points 665,980 -> 758,071, up exactly
    /// 122,788 - 30,697. Counted as damage, the Ranger dealt 1.4 M to a boss of 869 K (HP check
    /// 163 %). It is the target healing itself, recorded as such (source = target).</para>
    /// </summary>
    private IEnumerable<DamageEvent> DecodeVarintDot(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        int p = 2;
        if (!TryReadVarint(frame, ref p, out long target) || p >= frame.Length)
        {
            SkippedShortFrames++;
            return Array.Empty<DamageEvent>();
        }

        int flags = frame[p++];
        if ((flags & 0x02) == 0 || (flags & 0x08) == 0)
        {
            return Array.Empty<DamageEvent>();
        }

        if (!TryReadVarint(frame, ref p, out long actor) || !TryReadVarint(frame, ref p, out _) || frame.Length < p + 4)
        {
            SkippedShortFrames++;
            return Array.Empty<DamageEvent>();
        }

        uint effectId = BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]);
        p += 4;
        long healed = 0;
        if (!TryReadVarint(frame, ref p, out long amount) || (flags & 0x01) != 0 && !TryReadVarint(frame, ref p, out healed) || frame.Length != p + 4)
        {
            SkippedShortFrames++;
            return Array.Empty<DamageEvent>();
        }

        int skillId = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]));

        // A monster's effect (nine digits) set off by a player's class skill: the target healing
        // itself by the amount (see the remarks above), never the player's damage.
        if (effectId < 1_000_000_000 && Aion2SkillNames.ClassOf(skillId) is not null)
        {
            int effectSkill = (int)(effectId / 100);
            return amount > 0 && amount <= MaxPlausibleAmount
                ? new[] { new DamageEvent(timestamp, (int)target, (int)target, amount, IsHeal: true, Aion2SkillNames.NameOf(effectSkill), IsTick: true, SkillId: effectSkill) }
                : Array.Empty<DamageEvent>();
        }

        // No summon guess here (see GuessSummonOwner): a tick's class skill can be the target's own
        // shield, the actor the monster striking it.
        int source = _entities.SummonOwnerOf((int)actor) ?? (int)actor;

        // A heal over time arrives in the damage tick's shape. Counting one as damage once made a
        // Chanter "hit" every party member once a second and painted the whole party as enemies, so
        // a heal-family skill, or any tick a player keeps on another player, is never damage; PvP
        // damage-over-time between players will need a capture of its own.
        if (Aion2SkillNames.IsHealFamily(skillId) || _entities.IsKnownPlayer(source) && _entities.IsKnownPlayer((int)target))
        {
            bool countedHeal = (flags & 0x01) != 0 && healed > 0 && healed <= MaxPlausibleAmount
                && Aion2SkillNames.ClassOf(skillId) is not null && _entities.SummonOwnerOf((int)target) is null;
            return countedHeal
                ? new[] { new DamageEvent(timestamp, source, (int)target, healed, IsHeal: true, Aion2SkillNames.NameOf(skillId), IsTick: true, SkillId: skillId) }
                : Array.Empty<DamageEvent>();
        }

        if (amount <= 0 || amount > MaxPlausibleAmount || target == actor)
        {
            return Array.Empty<DamageEvent>();
        }

        // A shield's share of a blow: a monster's tick naming the shield a player wears. The blow itself comes in the
        // very next hit frame with its full amount, so the share is kept to be taken off it (see DecodeVarintDamage).
        // Counted as a blow of its own, it inflated the damage taken (captures of 2026-10-07 to 10-09).
        if (Aion2SkillNames.ClassOf(skillId) is not null && !_entities.IsKnownPlayer(source) && _entities.IsKnownPlayer((int)target))
        {
            var key = ((int)actor, (int)target);
            long earlier = _shieldShares.TryGetValue(key, out var share) && share.At == timestamp ? share.Amount : 0;
            if (_shieldShares.Count > 512)
            {
                // Shares of blows taken whole, whose monster never struck that player again.
                foreach (var stale in _shieldShares.Where(kv => kv.Value.At != timestamp).Select(kv => kv.Key).ToList())
                {
                    _shieldShares.Remove(stale);
                }
            }

            _shieldShares[key] = (timestamp, earlier + amount);
            return Array.Empty<DamageEvent>();
        }

        return new[] { new DamageEvent(timestamp, source, (int)target, amount, IsHeal: false, Aion2SkillNames.NameOf(skillId), IsTick: true, SkillId: skillId, ViaSummon: source != (int)actor) };
    }

    /// <summary>
    /// A skill being wound up (verified on two debug recordings, 2026-10-12: 404 frames, every one reads to its end and the damage of
    /// the same skill id by the same caster follows ~0.6 s after the cast has ended): opcode | caster (varint) | 1 byte | skill id (u32 LE) |
    /// 1 byte | aim flags (1) | target (varint) | facing (f32, degrees) | if aim flags &amp; 2: x, y, z (f32) | cast time in ms (varint) | tail.
    /// </summary>
    private void DecodeWindup(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        if (!TryReadAim(frame, out int actor, out int skillId, out int target, out double facing, out float x, out float y, out float z, out bool hasPosition, out int p)
            || !TryReadVarint(frame, ref p, out long ms) || ms is < 50 or > 120_000)
        {
            return;
        }

        _entities.Combat.NoteWindup(new CastNotice(actor, skillId, target, timestamp, (int)ms, facing, x, y, z, hasPosition));
    }

    /// <summary>A skill that was cast: the same layout as the windup up to the aim, without the cast time. Remembered only as
    /// whom the caster aims at - what "the target of the target" reads.</summary>
    private void DecodeCastNotice(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        if (TryReadAim(frame, out int actor, out int skillId, out int target, out _, out _, out _, out _, out _, out _))
        {
            _entities.Combat.NoteAim(actor, target, timestamp);
            if (!_entities.IsKnownMonster(actor))
            {
                _entities.Combat.NoteSkillUse(timestamp, actor, skillId); // the rotation view
            }
        }
    }

    private static bool TryReadAim(ReadOnlySpan<byte> frame, out int actor, out int skillId, out int target, out double facing, out float x, out float y, out float z, out bool hasPosition, out int end)
    {
        actor = skillId = target = 0;
        facing = 0;
        x = y = z = 0;
        hasPosition = false;
        end = 0;
        int p = 2;
        if (!TryReadVarint(frame, ref p, out long actorId) || actorId is < 1 or > 10_000_000 || p + 8 > frame.Length)
        {
            return false;
        }

        p += 1;
        uint skill = BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]);
        p += 4;
        if (skill is < 1 or > 300_000_000)
        {
            return false;
        }

        byte aimFlags = frame[p + 1];
        p += 2;
        if (!TryReadVarint(frame, ref p, out long targetId) || targetId is < 0 or > 10_000_000 || p + 4 > frame.Length)
        {
            return false;
        }

        double angle = BinaryPrimitives.ReadSingleLittleEndian(frame[p..]);
        p += 4;
        if (!double.IsFinite(angle) || Math.Abs(angle) > 720)
        {
            return false;
        }

        if ((aimFlags & 2) != 0)
        {
            if (p + 12 > frame.Length)
            {
                return false;
            }

            x = BinaryPrimitives.ReadSingleLittleEndian(frame[p..]);
            y = BinaryPrimitives.ReadSingleLittleEndian(frame[(p + 4)..]);
            z = BinaryPrimitives.ReadSingleLittleEndian(frame[(p + 8)..]);
            hasPosition = float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z) && Math.Abs(x) < 10_000_000 && Math.Abs(y) < 10_000_000;
            p += 12;
        }

        actor = (int)actorId;
        skillId = (int)skill;
        target = (int)targetId;
        facing = angle;
        end = p;
        return true;
    }

    /// <summary>Movement frames of other entities: second byte 0x37, the first one of these. Where the position sits after the entity id differs by family (verified on three debug
    /// recordings, 2026-10-12: with these offsets the readings of one entity form paths with speeds of a few metres per second).</summary>
    private static int? PositionOffset(int first) => first switch
    {
        0x1a or 0x1b or 0x1c or 0x1e or 0x20 or 0x28 or 0x29 or 0x2a => 2,
        0x1f or 0x2f => 1,
        _ => null,
    };

    /// <summary>A position reading: opcode | entity (varint) | 1-2 bytes | x (f32) | y (f32), places in centimetres. Kept for the replay (see <see cref="Aion2CombatState"/>).</summary>
    private void DecodePosition(ReadOnlySpan<byte> frame, DateTime timestamp, int offset)
    {
        int p = 2;
        if (!TryReadVarint(frame, ref p, out long entity) || entity is < 1 or > 10_000_000 || frame.Length < p + offset + 8)
        {
            return;
        }

        float x = BinaryPrimitives.ReadSingleLittleEndian(frame[(p + offset)..]);
        float y = BinaryPrimitives.ReadSingleLittleEndian(frame[(p + offset + 4)..]);
        if (float.IsFinite(x) && float.IsFinite(y) && Math.Abs(x) is > 1 and < 200_000 && Math.Abs(y) is > 1 and < 200_000)
        {
            _entities.Combat.NotePosition(timestamp, (int)entity, x, y);
            if (!_entities.IsLocalPlayer((int)entity))
            {
                _entities.NoteAround((int)entity, x, y);
            }
        }
    }

    /// <summary>
    /// A monster's stagger bar (verified on two debug recordings, 2026-10-12: 3,699 readings and 18 breaks, every frame reads to its end; the bar
    /// is 1,200 and falls with the hits taken): opcode | entity (varint) | 03 01 | max (u32) | current (u32) | 1 byte - or | 00 03 when it breaks.
    /// </summary>
    private void DecodeStagger(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        int p = 2;
        if (!TryReadVarint(frame, ref p, out long entity) || entity is < 1 or > 10_000_000 || p + 2 > frame.Length)
        {
            return;
        }

        if (frame[p] == 3 && frame[p + 1] == 1 && frame.Length >= p + 10)
        {
            uint max = BinaryPrimitives.ReadUInt32LittleEndian(frame[(p + 2)..]);
            uint current = BinaryPrimitives.ReadUInt32LittleEndian(frame[(p + 6)..]);
            if (max is > 0 and <= 1_000_000 && current <= max)
            {
                _entities.Combat.NoteStagger(new StaggerEvent(timestamp, (int)entity, (int)current, (int)max, false));
            }
        }
        else if (frame[p] == 0 && frame[p + 1] == 3)
        {
            _entities.Combat.NoteStagger(new StaggerEvent(timestamp, (int)entity, 0, 0, true));
        }
    }

    /// <summary>
    /// An effect put on an entity (opcode 0x2a38 "on", 0x2b38 "renewed"; verified on a debug recording, 2026-10-12, every frame reads
    /// to its end): target (varint) | for "on": 1 | 1 type byte | instance (varint) | effect id (u32; the skill is the id / 10) |
    /// duration (u32 ms) | stays-on flag (u32, non-zero = no end) | 8 bytes (a server clock) | caster (varint) | ... | x, y, z (f32) at the end.
    /// </summary>
    private void DecodeBuff(ReadOnlySpan<byte> frame, DateTime timestamp, bool renewed)
    {
        int p = 2;
        if (!TryReadVarint(frame, ref p, out long target) || target is < 1 or > 10_000_000 || p + 2 > frame.Length)
        {
            return;
        }

        if (!renewed && frame[p++] != 1)
        {
            return;
        }

        p += 1; // type
        if (!TryReadVarint(frame, ref p, out long instance) || p + 12 + 8 > frame.Length)
        {
            return;
        }

        uint effect = BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]);
        uint duration = BinaryPrimitives.ReadUInt32LittleEndian(frame[(p + 4)..]);
        uint stays = BinaryPrimitives.ReadUInt32LittleEndian(frame[(p + 8)..]);
        p += 12 + 8;
        if (!TryReadVarint(frame, ref p, out long caster) || caster is < 1 or > 10_000_000 || effect is < 10 or > 2_999_999_999u)
        {
            return;
        }

        _entities.Combat.NoteBuff(new BuffNotice(timestamp, (int)target, (int)instance, (int)Math.Min(effect, int.MaxValue), (int)(effect / 10), stays != 0 ? -1 : (int)Math.Min(duration, 86_400_000u), (int)caster), renewed);

        // The last 12 bytes are the caster's own place (206,692 effect frames of a recording all end in a plausible x, y, z; where the
        // local player cast, it equals the position frame's to the centimetre), so every effect the local player puts on or renews is a free, exact position report -
        // about 130 in two minutes of fighting, against 6 of the real position frames.
        if (frame.Length - 12 > p && _entities.IsLocalPlayer((int)caster))
        {
            float x = BinaryPrimitives.ReadSingleLittleEndian(frame[^12..]);
            float y = BinaryPrimitives.ReadSingleLittleEndian(frame[^8..]);
            float z = BinaryPrimitives.ReadSingleLittleEndian(frame[^4..]);
            if (float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z) && Math.Abs(x) is > 100 and < 500_000 && Math.Abs(y) is > 100 and < 500_000 && Math.Abs(z) < 50_000)
            {
                _entities.NoteLocalPosition(x, y);
                _entities.Combat.NotePosition(timestamp, (int)caster, x, y);
            }
        }
    }

    /// <summary>Effects that ended: target (varint) | count (1) | per effect: type (1; 0 or 7) | instance (varint) | reason (1) | for type 7: by (varint), skill (u32), 4 bytes.</summary>
    private void DecodeBuffEnd(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        int p = 2;
        if (!TryReadVarint(frame, ref p, out long target) || target is < 1 or > 10_000_000 || p >= frame.Length)
        {
            return;
        }

        int count = frame[p++];
        if (count is < 1 or > 64)
        {
            return;
        }

        var ended = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            if (p >= frame.Length)
            {
                return;
            }

            int type = frame[p++];
            if (!TryReadVarint(frame, ref p, out long instance) || p >= frame.Length)
            {
                return;
            }

            p += 1; // reason
            if (type == 7)
            {
                if (!TryReadVarint(frame, ref p, out _) || p + 8 > frame.Length)
                {
                    return;
                }

                p += 8;
            }
            else if (type != 0)
            {
                return;
            }

            ended.Add((int)instance);
        }

        if (p != frame.Length)
        {
            return;
        }

        foreach (int instance in ended)
        {
            _entities.Combat.NoteBuffEnd((int)target, instance, timestamp);
        }
    }

    /// <summary>
    /// An entity's changed stats: opcode | entity (varint) | format (1) | if format &amp; 1: count (1)
    /// and that many kind (1) + u32 LE | if format &amp; 2: count (1) and that many kind (1) + i64 LE.
    /// Kind 0 of the 8-byte group is the current hit points. Verified on four captures (2026-10-02):
    /// all 4,492 frames parse to their exact length, and for every boss the hit points plus the
    /// damage decoded against it stay constant to the point (Ultimate Berk 615,000 in a party and
    /// 123,000 solo, Divine Auldor 1,125,000). The other kinds are not identified yet; 8-byte kind 7
    /// equals a boss's full health once but not a player's, so it is not taken as the maximum.
    /// </summary>
    private void DecodeHp(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        int p = 2;
        if (!TryReadVarint(frame, ref p, out long entityId) || p >= frame.Length)
        {
            SkippedShortFrames++;
            return;
        }

        int format = frame[p++];
        if ((format & 1) != 0)
        {
            // Detailed stats only ever go to the player they belong to - the local player.
            _entities.NoteDetailedStats(unchecked((int)entityId));
            if (p >= frame.Length)
            {
                SkippedShortFrames++;
                return;
            }

            p += 1 + frame[p] * 5;
        }

        long? current = null;
        long? reportedMax = null;
        if ((format & 2) != 0)
        {
            if (p >= frame.Length)
            {
                SkippedShortFrames++;
                return;
            }

            int count = frame[p++];
            for (int i = 0; i < count && p + 9 <= frame.Length; i++, p += 9)
            {
                if (frame[p] == 0)
                {
                    current = BinaryPrimitives.ReadInt64LittleEndian(frame[(p + 1)..]);
                }
                else if (frame[p] == 7)
                {
                    reportedMax = BinaryPrimitives.ReadInt64LittleEndian(frame[(p + 1)..]); // the maximum (it follows buffs that raise it)
                }
            }
        }

        if (p != frame.Length || (format & ~3) != 0)
        {
            SkippedShortFrames++;
            return;
        }

        if (reportedMax is long maxHp && maxHp > 0)
        {
            _entities.HitPoints.NoteMax(unchecked((int)entityId), maxHp);
        }

        if (current is long hp && hp >= 0)
        {
            long? before = _entities.HitPoints.Latest(unchecked((int)entityId))?.Hp;
            _entities.HitPoints.Note(unchecked((int)entityId), timestamp, hp);
            if (hp > 0 && before == 0)
            {
                _entities.Combat.NoteRevive(unchecked((int)entityId), timestamp); // back on their feet
            }

            if (hp == 0)
            {
                _entities.Combat.NoteGone(unchecked((int)entityId), timestamp);
                _entities.Combat.NoteDeath(unchecked((int)entityId), timestamp);
                _entities.ClearLocalTarget(unchecked((int)entityId)); // a dead monster is no target any more
                _entities.ForgetLiveMob(unchecked((int)entityId));
            }

            if (hp == 0 && _entities.NpcIdOf(unchecked((int)entityId)) is int died)
            {
                PetFarmLog.NoteDeath(unchecked((int)entityId), died, timestamp); // the pet farm log pairs a soul with the monsters that died just before it
            }
        }
    }

    /// <summary>
    /// The pet list of the login frame (the same frame as the species knowledge): <c>u32, varint n, n x (pet id u32, pet id u32,
    /// level u32), varint m, m x (pet id u32, progress u32)</c>. The level is 1 to 3 (3 = top, no progress any more); the progress
    /// is what the pet window shows as 32/75. Checked against the pet window (166 pets, "Schamane" level 2 at 32/75).
    /// </summary>
    private void DecodePetList(ReadOnlySpan<byte> frame)
    {
        int p = 6;
        if (!TryReadVarint(frame, ref p, out long count) || count is < 1 or > 2000 || p + count * 12 > frame.Length)
        {
            return;
        }

        var levels = new List<(int Id, int Level)>();
        for (long i = 0; i < count; i++, p += 12)
        {
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]);
            if (id != BinaryPrimitives.ReadUInt32LittleEndian(frame[(p + 4)..]) || id is < 1000 or > 99999)
            {
                return; // not the pet list
            }

            levels.Add(((int)id, (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(p + 8)..])));
        }

        var progress = new Dictionary<int, int>();
        if (TryReadVarint(frame, ref p, out long m) && m is >= 0 and <= 2000 && p + m * 8 <= frame.Length)
        {
            for (long i = 0; i < m; i++, p += 8)
            {
                progress[(int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..])] = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(p + 4)..]);
            }
        }

        // The u32 right behind the opcode is the summoned pet (0 = none); 1124 for Aahz's Zaif, 1204 / 1042 / 0 for his other
        // characters (recording 2026-10-10_12-26). Only taken from a frame that really is the pet list.
        uint active = BinaryPrimitives.ReadUInt32LittleEndian(frame[2..]);
        if (active == 0 || active is >= 1000 and <= 99999)
        {
            _entities.SetLocalActivePet((int)active);
        }

        _entities.SetLocalPetStates(levels.Select(l => new Aion2PetState(l.Id, l.Level, progress.GetValueOrDefault(l.Id))).ToList());
    }

    /// <summary>
    /// A tab of the Black Cloud shop (sent when the player opens the shop or switches the tab): <c>opcode | tab u8 | ... | offers</c>, an offer is
    /// <c>offer id u32, item id u32, quantity u64 (1), discount class u8, 0x5d, price before u64, price after u64</c>. Tab 0x65 = special, 0x68 = pets,
    /// 0x67 = skins (the tab bytes seen in one recording; the contents of each tab agree). The offers are found by their shape; only pet items count.
    /// </summary>
    private void DecodeShop(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 40)
        {
            return;
        }

        int tab = frame[2] switch { 0x65 => 0, 0x68 => 1, 0x67 => 2, _ => -1 };
        var pets = new List<Aion2ShopPet>();
        int offers = 0;
        for (int i = 3; i + 31 <= frame.Length; i++)
        {
            if (BinaryPrimitives.ReadUInt64LittleEndian(frame[(i + 4)..]) != 1 || frame[i + 13] != 0x5D)
            {
                continue;
            }

            offers++;
            if (Aion2Pets.PetOfShopItem(unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[i..]))) is int pet && pets.All(p => p.PetId != pet))
            {
                pets.Add(new Aion2ShopPet(pet, tab, unchecked((long)BinaryPrimitives.ReadUInt64LittleEndian(frame[(i + 22)..]))));
            }

            i += 30;
        }

        if (offers >= 1 && tab >= 0)
        {
            _entities.NoteShopTab(tab, pets);
        }
    }

    /// <summary>The position of the local player (see <see cref="OpcodeFamily.LocalPosition"/>): the pet map follows it.</summary>
    private void DecodeTargetChange(ReadOnlySpan<byte> frame, DateTime timestamp, bool own)
    {
        int p = 2;
        long actor, target;
        if (own)
        {
            if (frame.Length < 6 || frame[2] != 0 || frame[3] != 0)
            {
                return;
            }

            p = 4;
            if (_entities.LocalPlayerId < 0 || !TryReadVarint(frame, ref p, out target))
            {
                return;
            }

            actor = _entities.LocalPlayerId;
        }
        else
        {
            if (!TryReadVarint(frame, ref p, out actor) || p >= frame.Length || frame[p] != 0)
            {
                return;
            }

            p++;
            if (!TryReadVarint(frame, ref p, out target))
            {
                return;
            }
        }

        if (actor is < 1 or > 10_000_000 || target is < 0 or > 10_000_000)
        {
            return;
        }

        _entities.Combat.NoteTarget((int)actor, (int)target, timestamp);
    }

    private void DecodeLocalPosition(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        int p = 2;
        if (!TryReadVarint(frame, ref p, out long entityId) || frame.Length < p + 9 || frame[p] != 3)
        {
            return;
        }

        if (!_entities.IsLocalPlayer(unchecked((int)entityId)))
        {
            // somebody else's (a dash, a correction): only the replay's tracks want it
            float ox = BinaryPrimitives.ReadSingleLittleEndian(frame[(p + 1)..]), oy = BinaryPrimitives.ReadSingleLittleEndian(frame[(p + 5)..]);
            if (float.IsFinite(ox) && float.IsFinite(oy) && Math.Abs(ox) is > 1 and < 200_000 && Math.Abs(oy) is > 1 and < 200_000)
            {
                _entities.Combat.NotePosition(timestamp, unchecked((int)entityId), ox, oy);
                _entities.NoteAround(unchecked((int)entityId), ox, oy);
            }

            return;
        }

        float x = BinaryPrimitives.ReadSingleLittleEndian(frame[(p + 1)..]);
        float y = BinaryPrimitives.ReadSingleLittleEndian(frame[(p + 5)..]);
        if (float.IsFinite(x) && float.IsFinite(y) && Math.Abs(x) < 1_000_000 && Math.Abs(y) < 1_000_000)
        {
            _entities.NoteLocalPosition(x, y);
            _entities.Combat.NotePosition(timestamp, (int)entityId, x, y);
        }
    }

    /// <summary>The local player marked a monster (see <see cref="OpcodeFamily.TargetSelect"/>): remembered as the current target.</summary>
    private void DecodeTargetSelect(ReadOnlySpan<byte> frame)
    {
        int p = 2;
        if (TryReadVarint(frame, ref p, out long entityId) && frame.Length - p <= 3)
        {
            _entities.SetLocalTarget(unchecked((int)entityId));
        }
    }

    /// <summary>
    /// An inventory change of the local player: <c>opcode | 01 00 00 | 8 bytes | item id u32 | quantity u32 ...</c> (also AP and Kinah, see
    /// the resources note; bytes 5-8 are the item instance, so two souls dropped at once differ). Only a pet soul is of interest: it goes to the pet farm log with the monsters that died right before it.
    /// </summary>
    private void DecodeInventoryChange(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        if (frame.Length < 21)
        {
            return;
        }

        Aion2Stock.NoteChange(frame, timestamp);
        Aion2Loot.NoteInventory(frame, timestamp);
        int itemId = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[13..]));
        if (Aion2Pets.PetOfSoulItem(itemId) is int pet)
        {
            PetFarmLog.NoteSoul(itemId, pet, unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[17..])), timestamp, BinaryPrimitives.ReadUInt32LittleEndian(frame[5..]));
        }
    }

    /// <summary>The game's own progress message for the local player's pets: <c>opcode | n | n x (pet id u32, gained u32)</c> (seen after every
    /// soul: +1, or +2 when two dropped at once; checked against recordings of 5 level-ups).</summary>
    private void DecodePetProgress(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 3)
        {
            return;
        }

        int count = frame[2];
        for (int i = 0, p = 3; i < count && p + 8 <= frame.Length; i++, p += 8)
        {
            int pet = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]));
            int gained = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(p + 4)..]));
            if (gained is > 0 and < 1000)
            {
                _entities.NotePetProgress(pet, gained);
            }
        }
    }

    private static bool TryReadVarint(ReadOnlySpan<byte> data, ref int position, out long value)
    {
        value = 0;
        for (int i = 0; i < 5 && position < data.Length; i++)
        {
            byte b = data[position++];
            value |= (long)(b & 0x7f) << (7 * i);
            if ((b & 0x80) == 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// "Another player appeared" frame: opcode | combat id (varint) | a few bytes | length-prefixed
    /// name (UTF-8). The same id is the actor id in damage frames, so this is what turns
    /// "Assassin #3562" into "Pencilgon". The local player never gets one (verified: in a five-member
    /// party, four nickname frames, for exactly the four others).
    /// </summary>
    private void DecodeVarintNickname(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        int p = 2;
        if (!TryReadVarint(frame, ref p, out long id))
        {
            return;
        }

        for (int k = p; k < Math.Min(frame.Length - 4, p + 24); k++)
        {
            if (TryReadName(frame, k, out string name))
            {
                _entities.Register((int)id, name);
                _entities.NoteIdentity((int)id, name, 0);
                _entities.NoteMet(name, timestamp);
                ReadSeenProfile(frame, (int)id, k + 1 + frame[k]);

                // The faction: the byte after the 4 bytes that follow the name is 1 for a player of an Elyos server and 2 for one of an
                // Asmodian server (checked against 478 players whose server was known from their appearance frame: no exception).
                int factionAt = k + 1 + frame[k] + 4;
                if (factionAt < frame.Length && frame[factionAt] is 1 or 2)
                {
                    _entities.NoteFaction((int)id, frame[factionAt]);
                }

                // A player in a guild: further on, the frame carries server id (u16: 17 05 = 1303,
                // 18 05 = 1304 Kaisinel) | guild id (u32, non-zero) | 00 00 | the same server id |
                // the guild's length-prefixed name. Seen for all ten guilds of 71 nickname frames
                // (Krao Cave and Draupnir captures, 2026-10-02: HORDE, ElyosOrden, Insomnia,
                // Convèrgence, Freljord, DarkLegion...); a player without a guild has no such run,
                // and reading any "server id + name" pair there picked up garbage ("odd",
                // "jd47ddddep"). Remembered so the roster's leftover name is the player's, not the
                // guild's.
                for (int i = k + 1 + frame[k]; i + 11 < frame.Length; i++)
                {
                    int server = frame[i] | frame[i + 1] << 8;
                    if (server is >= 1000 and <= 9999 && frame[i + 8] == frame[i] && frame[i + 9] == frame[i + 1]
                        && (frame[i + 2] | frame[i + 3] | frame[i + 4] | frame[i + 5]) != 0 && frame[i + 6] == 0 && frame[i + 7] == 0
                        && TryReadName(frame, i + 10, out string other, minLength: 2) && other != name)
                    {
                        _entities.NoteNonPlayerName(other);
                        _entities.SetGuild((int)id, other);
                        break;
                    }
                }

                return;
            }
        }
    }

    /// <summary>
    /// "Player seen" frame: opcode | target id (varint) | skill id (u32) | the acting player's combat id
    /// (varint) | server id (u16) | length-prefixed name | optional length-prefixed guild. Every player
    /// who acts near you is announced this way. It used to be found by the server id 18 05 (1304,
    /// Europe - Kaisinel) in front of the name, so players of every other server were never named by
    /// it (seen: Aera of server 1303 on a Krao Cave capture, 2026-10-02); the fields are read in
    /// order now, whatever the server.
    /// </summary>
    /// <summary>The first character id of a player the game lists; below this one a "server id | id | 00 00 | server id | name" run is a legion (their ids stayed under 1,140, players' from 8,766 on).</summary>
    private const int FirstCharacterId = 5000;

    /// <summary>
    /// A frame that lists players with their permanent character id: <c>server id (u16) | character id (u32) | 00 00 | the same server id |
    /// length-prefixed name</c>, found by scanning (a legion is listed the same way with a small id and is skipped).
    /// </summary>
    private void DecodePlayerList(ReadOnlySpan<byte> frame)
    {
        for (int i = 2; i + 11 < frame.Length; i++)
        {
            int server = frame[i] | frame[i + 1] << 8;
            if (server is < 1000 or > 9999 || frame[i + 6] != 0 || frame[i + 7] != 0 || frame[i + 8] != frame[i] || frame[i + 9] != frame[i + 1])
            {
                continue;
            }

            uint characterId = BinaryPrimitives.ReadUInt32LittleEndian(frame[(i + 2)..]);
            if (characterId >= FirstCharacterId && TryReadName(frame, i + 10, out string name, minLength: 2))
            {
                _entities.NoteCharacterId(name, server, (int)characterId);
                i += 10 + frame[i + 10];
            }
        }
    }

    /// <summary>
    /// The 0xE2 frames (a name looked at, a friend or legion entry: 76e2, 77e2, 82e2 ...) carry <c>server id (u16) | length | name</c>. A player whose character
    /// window the local player opened is otherwise only known by name, so without this his server (and his faction) stayed unknown and the website
    /// left him out of the upload. The id must look like a server of the two sides (1301, 2304 ...).
    /// </summary>
    private void NoteNamedServers(ReadOnlySpan<byte> frame)
    {
        for (int i = 2; i + 5 < frame.Length; i++)
        {
            int server = frame[i] | frame[i + 1] << 8;
            if (server is < 1000 or > 2999 || server % 100 is < 1 or > 30)
            {
                continue;
            }

            if (TryReadName(frame, i + 2, out string name, minLength: 2) && char.IsLetter(name[0]))
            {
                NoteServerId(server, name);
                i += 1 + frame[i + 2];
            }
        }
    }

    /// <summary>
    /// The character list of the login: per character a length-prefixed name and, 38 bytes after the name, the length-prefixed legion (checked on
    /// the four characters of one account, 2026-10-17: Aahz, Skeeve, Chumley and Nunzio, all "Akatsuki"). The own character record carries no
    /// legion, so this is where the meter learns its own; it is only ever looked up by the exact name of the character that logged in.
    /// </summary>
    private void DecodeCharacterList(ReadOnlySpan<byte> frame)
    {
        Capture.LoginCapture.LoginStarted(); // a no-op unless the legion's login switch is on
        if (TryReadAccountKey(frame, out string accountKey))
        {
            _entities.NoteAccountKey(accountKey);
        }

        for (int i = 2; i + 40 < frame.Length; i++)
        {
            if (!TryReadName(frame, i, out string name, minLength: 2))
            {
                continue;
            }

            int guildAt = i + 1 + frame[i] + 38;
            if (TryReadName(frame, guildAt, out string guild, minLength: 2) && guild != name)
            {
                _entities.NoteAccountLegion(name, guild);
            }
        }

        // every character of the account with its basics: for the checklist's character switch and the profile upload (the rest of a character is fetched by the server)
        List<AccountCharacter> account = AccountCharacterList.Parse(frame);
        if (account.Count > 0)
        {
            _entities.NoteAccountCharacters(account);
            Aion2Tickets.NoteListedCharacters(_entities.AccountKeyHash(), account);
        }
    }

    /// <summary>
    /// The account key: a 12-character text starting with <c>$</c>, length-prefixed, at the head of the character list of the login. It is the same
    /// on every character of one game account and differs between accounts (checked on the login traffic of two accounts with four characters each, 2026-10-19).
    /// The game account belongs to one server, so the same Steam account has one key per server. Behind the key come a count byte (<c>2a</c> Elyos, <c>2c</c>
    /// Asmodian), three zero bytes, the faction (<c>01</c> Elyos, <c>02</c> Asmodian) and <c>02 01</c> (53 Elyos login dumps, two Asmodian accounts, 2026-10-20),
    /// so only the layout is checked, not the values.
    /// </summary>
    internal static bool TryReadAccountKey(ReadOnlySpan<byte> frame, out string key)
    {
        key = "";
        int limit = Math.Min(frame.Length - 20, 64);
        for (int at = 13; at < limit; at++)
        {
            if (frame[at - 13] != 12 || frame[at - 12] != (byte)'$'
                || frame[at + 1] != 0 || frame[at + 2] != 0 || frame[at + 3] != 0
                || frame[at + 4] is not (1 or 2) || frame[at + 5] != 0x02 || frame[at + 6] != 0x01)
            {
                continue;
            }

            ReadOnlySpan<byte> text = frame.Slice(at - 12, 12);
            bool printable = true;
            foreach (byte b in text)
            {
                if (b is < 0x21 or > 0x7e)
                {
                    printable = false;
                    break;
                }
            }

            if (printable)
            {
                key = System.Text.Encoding.ASCII.GetString(text);
                return true;
            }
        }

        return false;
    }

    private void DecodeAppearance(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        int p = 2;
        if (!TryReadVarint(frame, ref p, out long victim) || frame.Length < p + 4)
        {
            return;
        }

        // The same frame is the death notice: the first id is who died (its hit points fell to 0 about 50 ms before: 3,364 of 3,366 frames with a skill
        // in four recordings, 2026-10-21 to 23) and the u32 the skill that did it (a hit with that skill landed on the victim in the 1.5 s before, in all
        // 1,166 checked frames); 0 means nobody took the kill.
        int killingSkill = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]));
        if (killingSkill != 0 && victim > 0)
        {
            _entities.HitPoints.NoteKillCredit(unchecked((int)victim), killingSkill, timestamp);
        }

        p += 4;
        if (!TryReadVarint(frame, ref p, out long id) || id <= 0 || frame.Length < p + 3
            || !TryReadName(frame, p + 2, out string name, minLength: 2))
        {
            return;
        }

        _entities.Register((int)id, name);
        NoteServerId(frame[p] | frame[p + 1] << 8, name, inView: true);
        _entities.NoteIdentity((int)id, name, frame[p] | frame[p + 1] << 8);
        if (TryReadName(frame, p + 3 + frame[p + 2], out string guild, minLength: 2) && guild != name)
        {
            _entities.SetGuild((int)id, guild);
            _entities.NoteNonPlayerName(guild);
        }
    }

    /// <summary>
    /// The local player's character record (verified against four sessions; its level climbs
    /// 10 -> 12 -> 13 -> 32 -> 33 across them, matching the character): opcode | combat id (varint) |
    /// a few bytes | length-prefixed name | <c>18 05</c> | class code (u32) | <c>01</c> | level (u32) |
    /// ... a long block ... | one entry per equipped item: item id (u32), <c>00</c>, slot index, <c>00</c>.
    /// Enchant level, stones and stats are not decoded (the enchant is not in the entry bytes).
    /// </summary>
    private void DecodeCharacter(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        int p = 2;
        if (!TryReadVarint(frame, ref p, out long id))
        {
            return;
        }

        for (int k = p; k < Math.Min(frame.Length - 12, p + 16); k++)
        {
            if (!TryReadName(frame, k, out string name, minLength: 2))
            {
                continue;
            }

            int after = k + 1 + frame[k];
            // The byte at +6 is the faction: 1 Elyos, 2 Asmodian (an Asmodian character - Templar 11, Ranger 15 on NA East, issues #17/#20 - was never read while 1 was required).
            if (after + 11 > frame.Length || frame[after + 6] is not (1 or 2))
            {
                continue;
            }

            // The two bytes after the name are the character's server id (18 05 = 1304, Europe -
            // Kaisinel; 17 05 = 1303 for a character of another EU server). They used to be required to
            // be 18 05, so the own record of anyone not on Kaisinel was never read; a plausible class
            // code (4 * class + faction bit, see Aion2SkillNames.ClassFromCode) checks the match instead.
            int classCode = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(after + 2)..]));
            if (classCode < 1 || (classCode - 1) / 4 is < 1 or > 9)
            {
                continue;
            }

            int level = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(after + 7)..]));
            if (level is < 1 or > 200)
            {
                continue;
            }

            var equipment = new List<Aion2EquippedItem>();
            var seenSlots = new HashSet<int>();
            for (int q = after + 11; q + 8 <= frame.Length; q++)
            {
                int itemId = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[q..]));
                if (frame[q + 4] == 0 && frame[q + 5] is >= 1 and <= 32 && frame[q + 6] == 0
                    && Aion2ItemCatalog.Find(itemId) is not null && seenSlots.Add(frame[q + 5]))
                {
                    equipment.Add(new Aion2EquippedItem(frame[q + 5], itemId));
                }
            }

            _entities.SetLocalCharacter(new Aion2CharacterInfo((int)id, name, classCode, level, equipment, timestamp,
                ServerId: BinaryPrimitives.ReadUInt16LittleEndian(frame[after..])));
            if (TryReadWing(frame) is { } wing)
            {
                _entities.SetLocalWing(wing);
            }

            return;
        }
    }

    /// <summary>The constant block in the own character record that precedes the wing: <c>a0 86 01 00 a0 86 01 00</c>
    /// (100000 twice), then two u32, then <c>01</c> or <c>01 00 00 00 01</c>, then the wing (u32), the optional wing
    /// skin (u32, a wing of the same table: 30xxxxxx Elyos, 40xxxxxx Asmodian) and the title id. The byte before the wing is the faction (1 or 2).</summary>
    private static readonly byte[] WingAnchor = { 0xa0, 0x86, 0x01, 0x00, 0xa0, 0x86, 0x01, 0x00 };

    /// <summary>
    /// Reads the wing and its skin from the own character record (0x3336), anchored on <see cref="WingAnchor"/>. Checked
    /// 2026-10-10 against Aahz (wing 30500200, skin 30400400), Chumley (30200600, no skin) and the older recordings
    /// (30101200 with and without skin), and 20 October 2026 against an Asmodian (wing 40200100, skin 40201500). Anything that does not fit the layout - a wing
    /// outside 30000000..30599999 (Asmodian: 40000000..40599999) or inside the skin range 304xxxxx - yields null, never a guess.
    /// </summary>
    private static Aion2Wing? TryReadWing(ReadOnlySpan<byte> frame)
    {
        int anchor = frame.IndexOf(WingAnchor);
        if (anchor < 0)
        {
            return null;
        }

        int q = anchor + 16;
        int wingAt;
        // the marker byte is the faction (1 Elyos, 2 Asmodian: Kijin's record of 20 October 2026 reads 02 a4 67 65 02 = wing 40200100)
        if (q + 5 <= frame.Length && frame[q] is 1 or 2 && frame[q + 1] == 0 && frame[q + 2] == 0 && frame[q + 3] == 0 && frame[q + 4] == frame[q])
        {
            wingAt = q + 5;
        }
        else if (q < frame.Length && frame[q] is 1 or 2)
        {
            wingAt = q + 1;
        }
        else
        {
            return null;
        }

        if (wingAt + 4 > frame.Length)
        {
            return null;
        }

        // Asmodian wings carry the same numbers as the Elyos ones, 10,000,000 higher (40xxxxxx; NC's data of 1,244 Asmodians, wings.json holds both)
        uint wing = BinaryPrimitives.ReadUInt32LittleEndian(frame[wingAt..]);
        uint wingBase = wing >= 40_000_000 ? wing - 10_000_000 : wing;
        if (wingBase is < 30_000_000 or > 30_599_999 || wingBase is >= 30_400_000 and <= 30_499_999)
        {
            return null;
        }

        uint skin = wingAt + 8 <= frame.Length ? BinaryPrimitives.ReadUInt32LittleEndian(frame[(wingAt + 4)..]) : 0;
        uint skinBase = skin >= 40_000_000 ? skin - 10_000_000 : skin;
        return new Aion2Wing((int)wing, skinBase is >= 30_000_000 and <= 30_799_999 ? (int)skin : 0);
    }

    /// <summary>
    /// The stat frame of the local player: <c>opcode | 00 00 | varint n | n x (u16 id, i32 value) | 8 bytes</c>. Only the base
    /// attributes and Lord values (ids 1..17, see <see cref="Aion2Attributes"/>) go to the character; every id goes to the session's stat table
    /// (<see cref="Aion2EntityDirectory.LocalStats"/>). Checked 2026-10-10 on Aahz.
    /// </summary>
    private void DecodeAttributes(ReadOnlySpan<byte> frame)
    {
        int p = 4;
        if (frame.Length < 6 || !TryReadVarint(frame, ref p, out long count) || count is < 1 or > 1000 || p + count * 6 > frame.Length)
        {
            return;
        }

        var values = new Dictionary<int, int>();
        var all = new Dictionary<int, int>();
        for (long i = 0; i < count; i++, p += 6)
        {
            int id = BinaryPrimitives.ReadUInt16LittleEndian(frame[p..]);
            int value = BinaryPrimitives.ReadInt32LittleEndian(frame[(p + 2)..]);
            all[id] = value;
            if (id is >= Aion2Attributes.FirstId and <= Aion2Attributes.LastId)
            {
                values[id] = value;
            }
        }

        if (values.Count > 0)
        {
            _entities.SetLocalAttributes(values);
        }

        if (all.Count > 0)
        {
            _entities.SetLocalStats(all);
        }
    }

    /// <summary>
    /// The full equipment list the game sends at login (verified against the in-game window: belt
    /// +4 and amulet +3 came out exactly): one entry per item - item id (u32), <c>01 00 00 00</c>,
    /// four zero bytes, <c>0b</c>, slot index, then a block of zeros whose first non-zero byte
    /// (within 24 bytes) is the enchant level. What follows (mana stones, rolled stats) is read by
    /// <see cref="TryReadItemDetails"/>.
    /// </summary>
    private void DecodeEquipment(ReadOnlySpan<byte> frame)
    {
        var items = new List<Aion2EquippedItem>();
        var seen = new HashSet<int>();
        for (int p = 2; p + 14 < frame.Length; p++)
        {
            if (frame[p + 4] != 1 || frame[p + 5] != 0 || frame[p + 6] != 0 || frame[p + 7] != 0 || frame[p + 12] != 0x0b)
            {
                continue;
            }

            int itemId = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]));
            int slot = frame[p + 13];
            if (Aion2ItemCatalog.Find(itemId) is null || !seen.Add(slot))
            {
                continue;
            }

            // Between the slot index and the entry's size byte (the first non-zero byte from 20 bytes
            // on) the enchant level sits 14 bytes before that size byte. Some entries carry a flag
            // byte earlier (a 01 after the item id, a 24 before the enchant), which a "first non-zero
            // byte" search took for the enchant: Vakron Guard +10 read as +1, a second Clash Rune +4 as +0.
            int enchant = 0;
            Aion2ItemDetails? details = null;
            Aion2ArcanaDetails? arcana = null;
            for (int k = p + 14 + 20; k < Math.Min(frame.Length, p + 14 + 31); k++)
            {
                if (frame[k] != 0)
                {
                    enchant = frame[k - 14] <= 30 ? frame[k - 14] : 0;
                    if (slot < ArcanaSlotStart)
                    {
                        TryReadItemDetails(frame, k, out details);
                    }
                    else
                    {
                        arcana = TryReadArcana(frame, k);
                    }

                    break;
                }
            }

            items.Add(ItemWith(slot, itemId, enchant, details) with { Arcana = arcana });
        }

        if (items.Count > 0)
        {
            _entities.SetLocalEquipment(items.OrderBy(i => i.SlotIndex).ToList());
        }
    }

    /// <summary>Slots from here on are the arcana pieces, whose entries are laid out differently (no stones).</summary>
    private const int ArcanaSlotStart = 30;

    /// <summary>A list of stats in an equipment entry: count (u8), then per stat id (u16) and value (u32). Advances
    /// <paramref name="at"/> past it; false when the count or the bounds do not fit.</summary>
    private static bool TryReadStatList(ReadOnlySpan<byte> frame, ref int at, out List<Aion2RolledStat> stats)
    {
        stats = new List<Aion2RolledStat>();
        if (at >= frame.Length || frame[at] > 8 || at + 1 + frame[at] * 6 > frame.Length)
        {
            return false;
        }

        int count = frame[at];
        for (int i = 0; i < count; i++)
        {
            int o = at + 1 + i * 6;
            stats.Add(new Aion2RolledStat(frame[o] | frame[o + 1] << 8, BinaryPrimitives.ReadUInt32LittleEndian(frame[(o + 2)..])));
        }

        at += 1 + count * 6;
        return true;
    }

    /// <summary>
    /// The part of an arcana entry behind its level (login list; checked against the in-game windows of all five arcana of one
    /// character, 2026-10-26: experience 0/500/600/200/3800, god values 22/23, every skill bonus). <paramref name="sizePos"/> is
    /// the entry's size byte, 14 bytes behind the level byte: the experience collected is the u32 right behind the level
    /// (sizePos - 13). From the size byte on: size (u32), slot index, 23 bytes of zeros and counters, then <c>01 | god id | 00 |
    /// god value (u32) | 2 bytes</c> at +28, and at +37 two skill lists of the same form: count (u8), per skill id (u32) and
    /// levels (u8) - the first holds the levels the piece gives, the second (same ids) the extra levels it rolled; the tooltip
    /// shows the sum. Null when the entry does not look like this.
    /// </summary>
    private static Aion2ArcanaDetails? TryReadArcana(ReadOnlySpan<byte> frame, int sizePos)
    {
        // some entries carry one more zero byte between the size field and the slot index (seen at login 2026-10-29: the
        // piece then read as having no arcana data at all), which shifts everything behind by one
        int shift = sizePos + 5 < frame.Length && frame[sizePos + 4] == 0 ? 1 : 0;
        if (sizePos < 14 || sizePos + 38 + shift > frame.Length || frame[sizePos + 1] != 0 || frame[sizePos + 2] != 0 || frame[sizePos + 3] != 0 || frame[sizePos + 28 + shift] != 1)
        {
            return null;
        }

        int exp = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(sizePos - 13)..]));
        int godStat = frame[sizePos + 29 + shift];
        int godValue = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(sizePos + 31 + shift)..]));
        int at = sizePos + 37 + shift;
        if (!TryReadSkillLevels(frame, ref at, out var given) || !TryReadSkillLevels(frame, ref at, out var rolled))
        {
            return null;
        }

        var sum = new List<Aion2SkillBonus>(given.Count);
        foreach (Aion2SkillBonus g in given)
        {
            sum.Add(new Aion2SkillBonus(g.SkillId, g.Level + rolled.Where(r => r.SkillId == g.SkillId).Sum(r => r.Level)));
        }

        return new Aion2ArcanaDetails(exp, godStat, godValue, sum);
    }

    /// <summary>A list of skill levels: count (u8, at most 8), per entry skill id (u32) and levels (u8). Advances <paramref name="at"/>.</summary>
    private static bool TryReadSkillLevels(ReadOnlySpan<byte> frame, ref int at, out List<Aion2SkillBonus> list)
    {
        list = new List<Aion2SkillBonus>();
        if (at >= frame.Length || frame[at] > 8 || at + 1 + frame[at] * 5 > frame.Length)
        {
            return false;
        }

        int count = frame[at];
        for (int i = 0; i < count; i++)
        {
            int o = at + 1 + i * 5;
            list.Add(new Aion2SkillBonus(unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[o..])), frame[o + 4]));
        }

        at += 1 + count * 5;
        return true;
    }

    private static Aion2EquippedItem ItemWith(int slot, int itemId, int enchant, Aion2ItemDetails? d) =>
        d is null ? new Aion2EquippedItem(slot, itemId, enchant) : new Aion2EquippedItem(slot, itemId, enchant, d.Stones, d.Stats, d.GodstoneId, d.SkillBonuses);

    /// <summary>
    /// The part of an equipment entry behind the enchant level, shared by the login list and the inspect frame
    /// (checked against the in-game tooltips of two earrings: Aahz' own slot 11 and Eggsorzist's Kromede earring,
    /// 2026-10-09, and against ~180 pieces of ten other players). <paramref name="sizePos"/> is the entry's size byte
    /// (the first non-zero byte after the enchant level, 14 bytes behind it). From there: size (u32), then up to two
    /// flag bytes (0xcc/0xce/0x20 or a zero), the slot index, the stone slot count (u8), that many 7 byte stone
    /// records (a 4 byte constant of the item, stat id u16, tier u8: 1 white, 2 green, 3 blue; stat 0 / tier 0 = empty
    /// slot), the owner block of 10 bytes (soul-binding character id u32, 00 00, server id u16, bound flag, 03), 13
    /// zero bytes and the rolled stats (count u8, then stat id u16 + value u32 each; MP 96, MP regen 23, attack 24 and
    /// evasion 24 of the earring matched the tooltip exactly). The first 4 of those 13 bytes are the godstone item id
    /// (19950016 = "Aulvicars Zauber" on the weapon). Behind the stat lists come the skill bonuses ("Abwärtsschlag St. +1"):
    /// id (u32) + levels (u8) per skill. The amount a stone adds ("Block+10") is NOT in the
    /// entry: the game takes it from its stone tables. Returns false (and null lists) when the block does not look
    /// like this, e.g. for entries of other kinds.
    /// </summary>
    private static bool TryReadItemDetails(ReadOnlySpan<byte> frame, int sizePos, out Aion2ItemDetails? details)
    {
        details = null;
        if (sizePos + 8 > frame.Length || frame[sizePos + 1] != 0 || frame[sizePos + 2] != 0 || frame[sizePos + 3] != 0)
        {
            return false;
        }

        for (int r = sizePos + 4; r <= sizePos + 6 && r + 1 < frame.Length; r++)
        {
            int count = frame[r + 1];
            int first = r + 2;
            int end = first + count * 7;
            if (count is < 1 or > 6 || end + 10 > frame.Length || frame[end + 9] != 3)
            {
                continue;
            }

            bool consistent = true;
            for (int i = 0; i < count && consistent; i++)
            {
                consistent = frame[first + i * 7 + 6] <= 4 && frame.Slice(first + i * 7, 4).SequenceEqual(frame.Slice(first, 4));
            }

            if (!consistent)
            {
                continue;
            }

            var list = new List<Aion2Stone>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(new Aion2Stone(frame[first + i * 7 + 4] | frame[first + i * 7 + 5] << 8, frame[first + i * 7 + 6]));
            }

            // The 13 bytes behind the owner block start with the id of the godstone set into the piece (u32, 0 = none).
            int godstone = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(end + 10)..]));
            int at = end + 10 + 13;
            IReadOnlyList<Aion2RolledStat>? rolled = null;
            IReadOnlyList<Aion2SkillBonus>? bonuses = null;
            if (TryReadStatList(frame, ref at, out var stats))
            {
                rolled = stats;
                // a second list with the same stat ids (all values 0 so far, not used), then the skill level bonuses:
                // count (u8), per skill id (u32) + bonus levels (u8); a copy of the list with base levels follows
                if (TryReadStatList(frame, ref at, out _) && at < frame.Length && frame[at] <= 8 && at + 1 + frame[at] * 5 <= frame.Length)
                {
                    var skills = new List<Aion2SkillBonus>(frame[at]);
                    for (int i = 0; i < frame[at]; i++)
                    {
                        int o = at + 1 + i * 5;
                        skills.Add(new Aion2SkillBonus(unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[o..])), frame[o + 4]));
                    }

                    bonuses = skills;
                }
            }

            details = new Aion2ItemDetails(list, rolled, godstone, bonuses);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The activated Daevanion nodes (login): opcode | board count (u8) | per board: board id (u32),
    /// node count (u8), that many node ids (u32 each; the board's start node is among them). The ids
    /// are the same numbers the game's node table uses (board 11 -> 110001...).
    /// </summary>
    private void DecodeDaevanion(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 3)
        {
            return;
        }

        int boardCount = frame[2];
        int p = 3;
        var boards = new List<Aion2DaevanionBoard>();
        for (int b = 0; b < boardCount; b++)
        {
            if (p + 5 > frame.Length)
            {
                return;
            }

            int boardId = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]));
            int count = frame[p + 4];
            p += 5;
            if (p + count * 4 > frame.Length)
            {
                return;
            }

            var ids = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                ids.Add(unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(p + i * 4)..])));
            }

            p += count * 4;
            boards.Add(new Aion2DaevanionBoard(boardId, ids));
        }

        if (boards.Count > 0 && p == frame.Length)
        {
            _entities.SetLocalDaevanion(boards);
        }
    }

    /// <summary>
    /// The species knowledge (login, together with the pet list): five blocks with ids 2..6 (Cognia, Fera,
    /// Natura, Varia, Specia) in a row, found by their header. A block is: id (u8) twice, level (u32),
    /// progress (u64), page count (u8), then per page: page number (u8), slot count (u8) and that many
    /// 12 byte effect slots (slot u8, kind u8, stat id u16, value u64; an empty slot is all zero).
    /// The stream puts a stray byte of 0x2a or more in front of some page and slot numbers (seen as
    /// aa, fb, bb, ab, 2a), which is skipped. Stat ids resolve to names on the website.
    /// </summary>
    private void DecodeSpecies(ReadOnlySpan<byte> frame)
    {
        for (int start = 2; start + 14 < frame.Length; start++)
        {
            if (frame[start] != 2 || frame[start + 1] != 2)
            {
                continue;
            }

            var found = new List<Aion2SpeciesKnowledge>();
            int p = start;
            for (int id = 2; id <= 6; id++)
            {
                if (id > 2 && p < frame.Length && frame[p] > 9)
                {
                    p++; // a stray byte between two blocks
                }

                if (!TryReadSpeciesBlock(frame, id, ref p, out Aion2SpeciesKnowledge? block))
                {
                    break;
                }

                found.Add(block!);
            }

            if (found.Count == 5)
            {
                _entities.SetLocalSpecies(found);
                return;
            }
        }
    }

    private static bool TryReadSpeciesBlock(ReadOnlySpan<byte> frame, int id, ref int p, out Aion2SpeciesKnowledge? block)
    {
        block = null;
        if (p + 15 > frame.Length || frame[p] != id || frame[p + 1] != id)
        {
            return false;
        }

        uint level = BinaryPrimitives.ReadUInt32LittleEndian(frame[(p + 2)..]);
        ulong progress = BinaryPrimitives.ReadUInt64LittleEndian(frame[(p + 6)..]);
        int pages = frame[p + 14];
        if (level is < 1 or > 99 || pages is < 1 or > 5)
        {
            return false;
        }

        p += 15;
        var effects = new List<Aion2SpeciesEffect>();
        for (int page = 1; page <= pages; page++)
        {
            if (p < frame.Length && frame[p] > 9)
            {
                p++;
            }

            if (p + 2 > frame.Length || frame[p] != page)
            {
                return false;
            }

            int slots = frame[p + 1];
            p += 2;
            if (slots > 32)
            {
                return false;
            }

            for (int slot = 0; slot < slots; slot++)
            {
                if (p < frame.Length && frame[p] != slot && frame[p] > 9)
                {
                    p++;
                }

                if (p + 12 > frame.Length || frame[p] != slot)
                {
                    return false;
                }

                int kind = frame[p + 1];
                int stat = BinaryPrimitives.ReadUInt16LittleEndian(frame[(p + 2)..]);
                long value = unchecked((long)BinaryPrimitives.ReadUInt64LittleEndian(frame[(p + 4)..]));
                p += 12;
                if (stat != 0)
                {
                    effects.Add(new Aion2SpeciesEffect(page, slot, stat, value, kind));
                }
            }
        }

        block = new Aion2SpeciesKnowledge(id, (int)level, unchecked((long)progress), effects);
        return true;
    }

    /// <summary>
    /// One skill entry of the local player, sent at login and when a preset or a level changes (layouts in docs/protocol/skill-frames.md section 3, total = base + bonuses; Stacy's fork reads
    /// them the same way, 2026-10-23): 0x1338 = 01 | id u32 | 01 | id u32 | total level | base level | bonuses (20 bytes); 0x0F38 (a variant) = 01 | id u32 | total level | base level | bonuses
    /// (15 bytes). Updates that one skill in the directory, the rest of the list stays, so the level a player just changed is known at once.
    /// </summary>
    private void DecodeSkillEntry(ReadOnlySpan<byte> frame, bool variant)
    {
        int idAt = variant ? 3 : 8, levelAt = variant ? 7 : 12;
        if (frame.Length < levelAt + 2 || frame[2] != 1 || !variant && frame[7] != 1)
        {
            return;
        }

        int id = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[idAt..]));
        int level = frame[levelAt];
        int baseLevel = frame[levelAt + 1];
        if (id < 1_000_000 || level is < 1 or > 60 || baseLevel > level)
        {
            return;
        }

        _entities.UpdateLocalSkill(new Aion2SkillEntry(id, level, baseLevel));
    }

    /// <summary>A variant skill taken away (0x1038): opcode | id u32.</summary>
    private void DecodeSkillRemoved(ReadOnlySpan<byte> frame)
    {
        if (frame.Length != 6)
        {
            return;
        }

        int id = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[2..]));
        if (id >= 1_000_000)
        {
            _entities.RemoveLocalSkill(id);
        }
    }

    /// <summary>
    /// The skill list sent at login: per entry the skill id (u32) followed by the total level, the
    /// trained base level and bonus bytes (total = base + bonuses, e.g. 12 = 10 + 2). Only entries
    /// whose id is a known skill are taken.
    /// </summary>
    private void DecodeSkills(ReadOnlySpan<byte> frame)
    {
        var skills = new List<Aion2SkillEntry>();
        var seen = new HashSet<int>();
        IReadOnlyDictionary<int, string> names = Aion2SkillNames.Load();
        for (int p = 4; p + 8 < frame.Length; p++)
        {
            int id = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]));
            if (id < 1_000_000 || frame[p - 1] != 0x01 && frame[p - 1] != 0x05 || !names.ContainsKey(id) || !seen.Add(id))
            {
                continue;
            }

            int level = frame[p + 4];
            int baseLevel = frame[p + 5];
            if (level is < 1 or > 60 || baseLevel > level)
            {
                continue;
            }

            skills.Add(new Aion2SkillEntry(id, level, baseLevel));
        }

        if (skills.Count > 0)
        {
            _entities.SetLocalSkills(skills);
        }
    }

    /// <summary>
    /// The skills with specialisation variants (login): count (u8), then per skill its base id (u32), a
    /// variant count (u8: 3 for a normal skill, 5 for a stigma) and the variants. Verified against the
    /// in-game stigma list of one character (Lunge Stance, Zikel's Blessing, Lifestealing Blade, Rage Burst
    /// were the only ones with 5) - the stigmas are taken as the base ids with 5.
    /// </summary>
    private void DecodeStigmas(ReadOnlySpan<byte> frame)
    {
        IReadOnlyDictionary<int, string> names = Aion2SkillNames.Load();
        var stigmas = new HashSet<int>();
        var listed = new HashSet<int>();
        for (int p = 3; p + 5 < frame.Length; p++)
        {
            int id = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]));
            if (id >= 1_000_000 && id % 10000 == 0 && frame[p + 4] is 3 or 5 && names.ContainsKey(id))
            {
                listed.Add(id);
                if (frame[p + 4] == 5)
                {
                    stigmas.Add(id);
                }
            }
        }

        if (listed.Count > 0)
        {
            _entities.SetLocalStigmas(stigmas, listed);
            DecodeSpecSlots(frame, 3);
        }
    }

    /// <summary>
    /// The specialisation slots of skills: per skill its base id (u32), the slot count (u8: 3 for an active skill, 5 for a stigma)
    /// and per slot a number (u8, 1..count) and the choice (u32, 0 = empty; otherwise base id + 10 x the stone number 1-5).
    /// The login list (0x39) holds all skills, the update frame (0x0739) one skill. Read by scanning from <paramref name="start"/>,
    /// the way the stigma list is.
    /// </summary>
    private void DecodeSpecSlots(ReadOnlySpan<byte> frame, int start)
    {
        IReadOnlyDictionary<int, string> names = Aion2SkillNames.Load();
        var slots = new Dictionary<int, int[]>();
        var stigmas = new HashSet<int>();
        for (int p = start; p + 5 < frame.Length; p++)
        {
            int id = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]));
            if (id < 1_000_000 || id % 10000 != 0 || frame[p + 4] is not (3 or 5) || !names.ContainsKey(id))
            {
                continue;
            }

            int count = frame[p + 4];
            int q = p + 5;
            if (q + count * 5 > frame.Length)
            {
                continue;
            }

            var choice = new int[count];
            bool ok = true;
            for (int k = 0; k < count && ok; k++)
            {
                int at = q + k * 5;
                int value = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(at + 1)..]));
                int stone = value == 0 ? 0 : (value - id) / 10;
                ok = frame[at] == k + 1 && (value == 0 || value > id && (value - id) % 10 == 0 && stone <= 9);
                choice[k] = stone;
            }

            if (!ok)
            {
                continue;
            }

            slots[id] = choice;
            if (count == 5)
            {
                stigmas.Add(id);
            }

            p = q + count * 5 - 1;
        }

        if (slots.Count > 0)
        {
            _entities.SetLocalSpecSlots(slots, stigmas);
        }
    }

    /// <summary>
    /// The active skill preset (0x92e2, 0x90e2): in the short form <c>00 00 | count | (category, preset) pairs | 00</c>, at login the same
    /// pairs after a longer header. Category 2 is the one the skill window follows (categories 3 and 4 always matched it in
    /// the recordings), so the pairs are looked for as 02 a 03 b 04 c.
    /// </summary>
    private void DecodePresetSwitch(ReadOnlySpan<byte> frame)
    {
        // The short frame 0x90e2 (00 00, category, preset, 00 00) is what most switches send; the 0x92e2 above comes only now and then.
        if (frame.Length == 8 && frame[2] == 0 && frame[3] == 0)
        {
            if (frame[4] == 2 && frame[5] is >= 1 and <= 9)
            {
                _entities.SwitchPreset(frame[5]);
            }

            return;
        }

        for (int i = 2; i + 5 < frame.Length; i++)
        {
            if (frame[i] == 2 && frame[i + 2] == 3 && frame[i + 4] == 4 && frame[i + 1] is >= 1 and <= 9)
            {
                _entities.SwitchPreset(frame[i + 1]);
                return;
            }
        }
    }

    /// <summary>
    /// The skill bar (login): records of <c>03 | macro page (1-3) | slot | main skill id (u32) | 04 | the skill
    /// ids that can sit in that slot</c> (u32 each; variants of a skill have the same first five digits).
    /// The first macro page is what the skill window shows; its base skill ids are the equipped skills.
    /// </summary>
    private void DecodeSkillBar(ReadOnlySpan<byte> frame)
    {
        IReadOnlyDictionary<int, string> names = Aion2SkillNames.Load();
        var equipped = new HashSet<int>();
        for (int i = 2; i + 12 < frame.Length; i++)
        {
            if (frame[i] != 3 || frame[i + 1] != 1 || frame[i + 7] != 4)
            {
                continue;
            }

            int main = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(i + 3)..]));
            if (main < 1_000_000 || !names.ContainsKey(main))
            {
                continue;
            }

            for (int j = i + 8; j + 4 <= frame.Length; j += 4)
            {
                int id = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[j..]));
                if (id < 1_000_000 || !names.ContainsKey(id))
                {
                    break;
                }

                equipped.Add(id / 10000 * 10000);
            }
        }

        if (equipped.Count > 0)
        {
            // The key bound to the class's break-free skill ("Defiance", one per class) is not in the bar
            // frame; it is always on the bar (level 13 on the Space key in the checked character).
            int classPrefix = equipped.First() / 1_000_000;
            foreach ((int id, string name) in names)
            {
                if (name == "Defiance" && id % 10000 == 0 && id / 1_000_000 == classPrefix)
                {
                    equipped.Add(id);
                }
            }

            _entities.SetLocalBar(equipped);
        }
    }

    // Equipment slot numbers of the own character window, by item kind; kinds that can be worn twice take
    // the next number for the second one. (Anything unknown is numbered from 25 up.)
    private static readonly Dictionary<string, int[]> InspectSlots = new()
    {
        ["MainHand"] = new[] { 1 }, ["SubHand"] = new[] { 2 }, ["Helmet"] = new[] { 3 }, ["Shoulder"] = new[] { 4 },
        ["Torso"] = new[] { 5 }, ["Pants"] = new[] { 6 }, ["Gloves"] = new[] { 7 }, ["Boots"] = new[] { 8 },
        ["Necklace"] = new[] { 10 }, ["Earring"] = new[] { 11, 12 }, ["Ring"] = new[] { 13, 14 },
        ["Bracelet"] = new[] { 15, 16 }, ["Belt"] = new[] { 17 }, ["Cape"] = new[] { 19 }, ["Amulet"] = new[] { 22 },
        ["Rune"] = new[] { 23, 24 },
    };

    /// <summary>
    /// Another player's character window, sent when the local player opens it (verified against one
    /// window on screen: level 45, gear score 1,473, all 19 enchant levels): opcode | 00 00 07 | name length
    /// (u8) | name | class code (u32, 4 * class + faction bit) | 01 | faction | level (u32) | 4 zero bytes |
    /// gear score (u32, the "Ausr\u00fcstungswert" beside the helmet icon) | ... | server id (u16), legion name (length-prefixed) | ... then one block per worn
    /// item: item id (u32), nine zero bytes, the enchant level - or, when the block carries a marker
    /// (0x9c / 0x1c), the marker and then the enchant level. The block does not say which slot it is, so the
    /// slot comes from the kind of item (two earrings, rings, bracelets and runes are numbered in the order
    /// they appear). No object id: the window is matched by name.
    /// </summary>
    private void DecodeInspect(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        if (frame.Length < 40 || !TryReadName(frame, 5, out string name, minLength: 2))
        {
            return;
        }

        int after = 6 + frame[5];
        if (after + 18 > frame.Length)
        {
            return;
        }

        int classCode = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[after..]));
        int level = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(after + 6)..]));
        int gearScore = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(after + 14)..]));
        if (classCode < 1 || (classCode - 1) / 4 is < 1 or > 9 || level is < 1 or > 200)
        {
            return;
        }

        string? guild = null;
        for (int i = after + 18; i + 4 < Math.Min(frame.Length, after + 80); i++)
        {
            int server = frame[i] | frame[i + 1] << 8;
            if (server is >= 1000 and <= 3000 && TryReadName(frame, i + 2, out string candidate, minLength: 2) && candidate != name)
            {
                guild = candidate;
                // the id in front of the legion name is the player's own server (a legion belongs to one server): a player whose window is opened is
                // otherwise known by name only, and without a server (and its side) the website does not take him
                if (server % 100 is >= 1 and <= 30)
                {
                    NoteServerId(server, name);
                }

                break;
            }
        }

        var gear = new List<Aion2EquippedItem>();
        var used = new Dictionary<string, int>();
        int extra = 25;
        for (int q = after + 18; q + 16 <= frame.Length; q++)
        {
            int itemId = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[q..]));
            if (Aion2ItemCatalog.Find(itemId) is not { } info || !ZeroRun(frame, q + 4, 9))
            {
                continue;
            }

            int enchant = frame[q + 13];
            if (enchant is 0x9c or 0x1c)
            {
                enchant = frame[q + 14];
            }

            if (enchant > 30)
            {
                continue;
            }

            Aion2ItemDetails? details = null;
            if (info.Slot != "Arcana")
            {
                int shift = frame[q + 13] is 0x9c or 0x1c ? 1 : 0;
                for (int k = q + 14 + shift; k < Math.Min(frame.Length, q + 14 + shift + 20); k++)
                {
                    if (frame[k] != 0)
                    {
                        TryReadItemDetails(frame, k, out details);
                        break;
                    }
                }
            }

            int n = used.GetValueOrDefault(info.Slot);
            int[]? slots = InspectSlots.GetValueOrDefault(info.Slot);
            int slot = slots is not null && n < slots.Length ? slots[n] : extra++;
            used[info.Slot] = n + 1;
            gear.Add(ItemWith(slot, itemId, enchant, details));
            q += 12;
        }

        if (gear.Count > 0)
        {
            TryReadGrowth(frame, out var boardCounts, out var titles, out var pets);
            _entities.SetInspected(new Aion2InspectedPlayer(name, classCode, level, gearScore, guild, gear.OrderBy(g => g.SlotIndex).ToList(), timestamp, titles, pets, boardCounts));
        }
    }

    /// <summary>
    /// The growth overview part of an inspect frame, three blocks in a row near its end: the activated node count
    /// of each Daevanion board (count byte, then per board <c>01 | board id (u32) | nodes (u8)</c>), the worn titles
    /// (count byte, then per title <c>slot (u8) | title id (u32)</c>) and the pet circles (count byte, then per pet
    /// <c>species id | level | 00 | level-1 pairs of (quality, slot number) | the quality of the last slot</c>). Checked against
    /// three windows on screen (titles, pet circle colours and the Daevanion percentages all matched). What is not found
    /// stays null.
    /// </summary>
    private static bool TryReadGrowth(ReadOnlySpan<byte> frame, out IReadOnlyList<Aion2BoardCount>? boards, out IReadOnlyList<Aion2TitleSlot>? titles, out IReadOnlyList<Aion2Pet>? pets)
    {
        boards = null;
        titles = null;
        pets = null;
        for (int start = 40; start + 40 < frame.Length; start++)
        {
            int count = frame[start];
            if (count is < 1 or > 8 || start + 1 + count * 6 > frame.Length)
            {
                continue;
            }

            var counts = new List<Aion2BoardCount>();
            bool ok = true;
            for (int i = 0; i < count && ok; i++)
            {
                int at = start + 1 + i * 6;
                uint board = BinaryPrimitives.ReadUInt32LittleEndian(frame[(at + 1)..]);
                ok = frame[at] == 1 && board is >= 11 and <= 98 && frame[at + 5] is >= 1 and <= 250;
                counts.Add(new Aion2BoardCount((int)board, frame[at + 5]));
            }

            int p = start + 1 + count * 6;
            if (!ok || p >= frame.Length || frame[p] is < 1 or > 3)
            {
                continue;
            }

            int titleCount = frame[p++];
            var worn = new List<Aion2TitleSlot>();
            for (int i = 0; i < titleCount && ok; i++)
            {
                ok = p + 5 <= frame.Length && frame[p] is >= 1 and <= 3;
                if (!ok)
                {
                    break;
                }

                uint id = BinaryPrimitives.ReadUInt32LittleEndian(frame[(p + 1)..]);
                ok = id is >= 1_000_000 and <= 99_999_999;
                worn.Add(new Aion2TitleSlot(frame[p], (int)id));
                p += 5;
            }

            if (!ok)
            {
                continue;
            }

            boards = counts;
            titles = worn;
            pets = ReadPets(frame, p);
            return true;
        }

        return false;
    }

    private static IReadOnlyList<Aion2Pet>? ReadPets(ReadOnlySpan<byte> frame, int p)
    {
        if (p >= frame.Length || frame[p] is < 1 or > 5)
        {
            return null;
        }

        int count = frame[p++];
        var pets = new List<Aion2Pet>();
        for (int i = 0; i < count; i++)
        {
            if (p + 3 > frame.Length || frame[p] is < 2 or > 6 || frame[p + 1] is < 2 or > 30 || frame[p + 2] != 0)
            {
                return pets.Count > 0 ? pets : null;
            }

            int species = frame[p];
            int level = frame[p + 1];
            p += 3;
            var kinds = new int[level];
            if (p + (level - 1) * 2 + 1 > frame.Length)
            {
                return pets.Count > 0 ? pets : null;
            }

            for (int s = 0; s < level - 1; s++)
            {
                int slot = frame[p + 1] - 1;
                if (slot < 0 || slot >= level)
                {
                    return pets.Count > 0 ? pets : null;
                }

                kinds[slot] = frame[p];
                p += 2;
            }

            // The last slot has no number, only its quality.
            kinds[level - 1] = frame[p++];
            pets.Add(new Aion2Pet(species, level, kinds));
        }

        return pets;
    }

    /// <summary>
    /// The worn titles of the local player (login and whenever they change): a count byte (1..3) and per title the
    /// slot (u8) and the title id (u32). The frame starts with the list of every title the player owns and ends with the
    /// worn ones, so the run that ends exactly at the end of the frame wins; a run found earlier in the owned list is
    /// only a fallback (a title id starting with 02 makes <c>01 | 02 | id</c> look like a run).
    /// </summary>
    private void DecodeTitles(ReadOnlySpan<byte> frame)
    {
        List<Aion2TitleSlot>? best = null;
        bool bestAtEnd = false;
        for (int p = 2; p + 6 <= frame.Length; p++)
        {
            int count = frame[p];
            if (count is < 1 or > 3 || p + 1 + count * 5 > frame.Length)
            {
                continue;
            }

            var run = new List<Aion2TitleSlot>();
            for (int i = 0; i < count; i++)
            {
                int at = p + 1 + i * 5;
                uint id = BinaryPrimitives.ReadUInt32LittleEndian(frame[(at + 1)..]);
                if (frame[at] is < 1 or > 3 || (i > 0 && frame[at] <= run![i - 1].Slot) || id is < 1_000_000 or > 99_999_999)
                {
                    run = null;
                    break;
                }

                run.Add(new Aion2TitleSlot(frame[at], (int)id));
            }

            if (run is not null)
            {
                bool atEnd = p + 1 + count * 5 == frame.Length;
                if (atEnd || !bestAtEnd)
                {
                    best = run;
                    bestAtEnd = atEnd;
                }
            }
        }

        if (best is not null)
        {
            _entities.SetLocalTitles(best);
        }
    }

    private static bool ZeroRun(ReadOnlySpan<byte> frame, int at, int length)
    {
        for (int i = 0; i < length; i++)
        {
            if (frame[at + i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// What a "player appeared" frame says about the player after the name: a class code
    /// (<c>4 * class id + class bits</c> (the bits are no faction), see <see cref="Aion2SkillNames.ClassFromCode"/>) and the
    /// visible equipment, entries of item id (u32), <c>00</c>, slot index (1-12), <c>00</c>.
    /// </summary>
    private void ReadSeenProfile(ReadOnlySpan<byte> frame, int id, int afterName)
    {
        if (afterName + 4 > frame.Length)
        {
            return;
        }

        int code = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[afterName..]));
        int? classId = Aion2SkillNames.ClassIdOfCode(code) is >= 1 and <= 8 ? Aion2SkillNames.ClassIdOfCode(code) : null;
        int? classBits = classId is null ? null : Aion2SkillNames.ClassBitsOfCode(code);

        var gear = new List<Aion2EquippedItem>();
        var slots = new HashSet<int>();
        for (int q = afterName + 4; q + 8 <= frame.Length; q++)
        {
            int itemId = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[q..]));
            if (frame[q + 4] == 0 && frame[q + 5] is >= 1 and <= 12 && frame[q + 6] == 0
                && Aion2ItemCatalog.Find(itemId) is not null && slots.Add(frame[q + 5]))
            {
                gear.Add(new Aion2EquippedItem(frame[q + 5], itemId));
            }
        }

        (int? level, int? itemLevel) = ReadSeenLevels(frame);
        int? combatPower = ReadSeenCombatPower(frame);
        var worn = ReadWornItems(frame, afterName + 4);

        if (classId is not null || gear.Count > 0 || level is not null || worn.Count > 0)
        {
            _entities.SetSeenProfile(id, new Aion2SeenProfile(classId, classBits, gear.OrderBy(g => g.SlotIndex).ToList(), level, itemLevel, combatPower, worn));
        }
    }

    /// <summary>
    /// The level and the gear score of a "player appeared" frame: an anchor of <c>-3000 (i32) | level | 0 | gear score</c> some 390 bytes before the end
    /// (checked against the profiles of 28 players and against the character window of one, 2026-10-22). The frame is of variable length, so it is looked for
    /// from the end, and only an anchor that is followed by a level of 1 to 100 and a zero counts (some frames hold a second -3000 elsewhere).
    /// </summary>
    private static (int? Level, int? ItemLevel) ReadSeenLevels(ReadOnlySpan<byte> frame)
    {
        for (int i = frame.Length - 16; i >= 0 && frame.Length - i <= 460; i--)
        {
            if (BinaryPrimitives.ReadInt32LittleEndian(frame[i..]) != -3000)
            {
                continue;
            }

            uint level = BinaryPrimitives.ReadUInt32LittleEndian(frame[(i + 4)..]);
            if (level is >= 1 and <= 100 && BinaryPrimitives.ReadUInt32LittleEndian(frame[(i + 8)..]) == 0)
            {
                uint score = BinaryPrimitives.ReadUInt32LittleEndian(frame[(i + 12)..]);
                return ((int)level, score is > 0 and < 100_000 ? (int)score : null);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// The combat power: a 4-byte number close behind the end of the frame (nine bytes before it, ten in one case seen). Read where the number is plausible (20,000 to
    /// 4,000,000; the neighbouring positions read as a shifted copy or as a huge number). Verified against the profile of one player (Keuno 138,016) and the
    /// numbers of 28 others; in the 10 against 10 arena it is the same fixed 397228 for everybody. Null when no position fits.
    /// </summary>
    private static int? ReadSeenCombatPower(ReadOnlySpan<byte> frame)
    {
        foreach (int back in stackalloc[] { 9, 10, 8, 11 })
        {
            int at = frame.Length - back;
            if (at < 0 || at + 4 > frame.Length)
            {
                continue;
            }

            uint value = BinaryPrimitives.ReadUInt32LittleEndian(frame[at..]);
            if (value is >= 20_000 and <= 4_000_000)
            {
                return (int)value;
            }
        }

        return null;
    }

    /// <summary>
    /// The pieces of a "player appeared" frame as <c>item id (u32) | enchant level + excess level (u8, 0 = not told) | position (u8, 1-12) | 0</c> (the older frames carry a 0
    /// at the third place, so this finds those as well). Verified against the equipment of two players (11 pieces each, +10 / +15 and the excess levels as the game shows them).
    /// </summary>
    private static List<Aion2WornItem> ReadWornItems(ReadOnlySpan<byte> frame, int from)
    {
        var worn = new List<Aion2WornItem>();
        var slots = new HashSet<int>();
        for (int q = Math.Max(from, 0); q + 8 <= frame.Length; q++)
        {
            int slot = frame[q + 5];
            if (slot is < 1 or > 12 || frame[q + 6] != 0 || frame[q + 4] > 40)
            {
                continue;
            }

            int itemId = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[q..]));
            if (Aion2ItemCatalog.Find(itemId) is not null && slots.Add(slot))
            {
                worn.Add(new Aion2WornItem(slot, itemId, frame[q + 4]));
            }
        }

        return worn.OrderBy(w => w.SlotIndex).ToList();
    }

    /// <summary>
    /// The party roster (0x0297, re-sent every few seconds while in a party; 0x0197 is a list of
    /// other parties). Each member: server id (u16) | length-prefixed name | a small u32 (not the class
    /// code of the other frames: 32 for a Cleric, 24 for an Spiritmaster) | level (u32). Found by that shape rather than by the server id 18 05 (Kaisinel) it used to require,
    /// which missed every member of another server - verified on three captures (2026-10-02):
    /// Psefon 30, Boulenbouche 45, Daidai 31, ScareNight, Destinyy 30, across servers 1303 and 2301.
    /// The party list of 0x0297 also tells which players are in the local player's group.
    /// </summary>
    private void DecodeRoster(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        var members = new List<string>();
        for (int i = 2; i + 1 < frame.Length; i++)
        {
            if (!TryReadName(frame, i, out string name, minLength: 2))
            {
                continue;
            }

            // The length prefix counts bytes: "Azaëde" is 6 characters and 7 bytes, and counting
            // characters dropped every accented member from the party (Canyon Urugugu, 2026-10-03).
            int after = i + 1 + frame[i];
            if (after + 8 > frame.Length || frame[after + 1] != 0 || frame[after + 2] != 0 || frame[after + 3] != 0
                || frame[after + 5] != 0 || frame[after + 6] != 0 || frame[after + 7] != 0)
            {
                continue;
            }

            int level = frame[after + 4];
            if (frame[after] == 0 || level is < 1 or > 60)
            {
                continue;
            }

            members.Add(name);
            if (i >= 2)
            {
                NoteServerId(frame[i - 2] | frame[i - 1] << 8, name);
            }

            if (HomeServerBehindName(frame, after) is int homeServer)
            {
                NoteServerId(homeServer, name);
                _entities.NoteHomeServerOfName(name, homeServer);
            }

            _entities.NoteRosterName(name);
            if (frame[0] == 0x02 && frame[1] == 0x97 && Aion2SkillNames.ClassFromRosterCode(frame[after]) is string className)
            {
                _entities.NotePartyClass(name, className);
            }

            i = after + 7;
        }

        if (frame[0] == 0x02 && frame[1] == 0x97 && members.Count > 0)
        {
            _entities.NoteParty(members, timestamp);
        }
    }

    /// <summary>
    /// The second server id of a roster member, 12 bytes behind the end of his name (class u32 | level u32 | a u32 | server u16 | the id again or a 0x10xx value), one byte
    /// further when a marker byte (01, 02, 03, 06, 07; it changes from frame to frame) sits in front of it. Read at the marker, "marker + low byte of the id" looks like
    /// a server for some of them (Munin 2313 as 2305, 2307 or 2311, Zemurru 2315 as 2817, 2819 or 2823), so a position counts only when the two bytes after it repeat the id
    /// or are a 0x10xx value, and the id names a server of the table. Null when neither position does - then only the id in front of the name counts (it was right in
    /// every recorded member).
    /// </summary>
    private static int? HomeServerBehindName(ReadOnlySpan<byte> frame, int afterName)
    {
        foreach (int offset in stackalloc[] { 12, 13 })
        {
            if (afterName + offset + 4 > frame.Length)
            {
                continue;
            }

            int server = frame[afterName + offset] | frame[afterName + offset + 1] << 8;
            int next = frame[afterName + offset + 2] | frame[afterName + offset + 3] << 8;
            if (server is >= 1000 and <= 2999 && server % 100 is >= 1 and <= 30 && Aion2Servers.IsKnown(server) && (next == server || next >> 8 == 0x10))
            {
                return server;
            }
        }

        return null;
    }

    /// <summary>A plausible character name at <paramref name="at"/>: a length byte (3-24) followed
    /// by that many UTF-8 bytes that are all letters or digits (no spaces, no control bytes).</summary>
    private static bool TryReadName(ReadOnlySpan<byte> frame, int at, out string name, int minLength = 3)
    {
        name = "";
        if (at >= frame.Length)
        {
            return false;
        }

        int length = frame[at];
        if (length < minLength || length > 24 || at + 1 + length > frame.Length)
        {
            return false;
        }

        // Byte scans call this at every offset and almost all of them hit bytes that are no text: checked up front, no exception per miss.
        ReadOnlySpan<byte> bytes = frame.Slice(at + 1, length);
        if (!System.Text.Unicode.Utf8.IsValid(bytes))
        {
            return false;
        }

        string candidate = Encoding.UTF8.GetString(bytes);

        foreach (char c in candidate)
        {
            if (!char.IsLetterOrDigit(c))
            {
                return false;
            }
        }

        name = candidate;
        return true;
    }

    private void DecodeNickname(ReadOnlySpan<byte> frame, IReadOnlyDictionary<string, FieldSpec> fields, bool littleEndian)
    {
        if (TryReadInt(frame, fields, "objectId", littleEndian, out long objectId)
            && TryReadString(frame, fields, "name", littleEndian, out string? name) && name.Length > 0)
        {
            _entities.Register((int)objectId, name);
        }
    }

    private void DecodeKill(ReadOnlySpan<byte> frame, DateTime timestamp, IReadOnlyDictionary<string, FieldSpec> fields, bool littleEndian)
    {
        if (!TryReadInt(frame, fields, "victimId", littleEndian, out long victimId))
        {
            return;
        }

        int? killer = TryReadInt(frame, fields, "killerId", littleEndian, out long killerId) ? (int)killerId : null;
        bool victimIsPlayer = TryReadInt(frame, fields, "victimIsPlayer", littleEndian, out long flag) ? flag != 0 : victimId > 0;
        _kills.Add(new KillEvent(timestamp, killer, (int)victimId, victimIsPlayer));
    }

    private void DecodeAvoid(ReadOnlySpan<byte> frame, DateTime timestamp, IReadOnlyDictionary<string, FieldSpec> fields, bool littleEndian)
    {
        if (!TryReadInt(frame, fields, "sourceId", littleEndian, out long sourceId)
            || !TryReadInt(frame, fields, "targetId", littleEndian, out long targetId)
            || !TryReadInt(frame, fields, "kind", littleEndian, out long kind))
        {
            return;
        }

        AvoidKind avoidKind = kind switch { 1 => AvoidKind.Parry, 2 => AvoidKind.Block, 3 => AvoidKind.Resist, _ => AvoidKind.Dodge };
        _avoids.Add(new AvoidEvent(timestamp, (int)sourceId, (int)targetId, avoidKind));
    }

    private static bool TryReadInt(ReadOnlySpan<byte> frame, IReadOnlyDictionary<string, FieldSpec> fields, string name, bool littleEndian, out long value)
    {
        value = 0;
        if (!fields.TryGetValue(name, out FieldSpec? spec) || frame.Length < spec.Offset + spec.Size)
        {
            return false;
        }

        value = spec.Signed ? ReadSigned(frame, spec, littleEndian) : (long)ReadUnsigned(frame, spec, littleEndian);
        return true;
    }

    private static bool TryReadString(ReadOnlySpan<byte> frame, IReadOnlyDictionary<string, FieldSpec> fields, string name, bool littleEndian, out string value)
    {
        value = "";
        if (!fields.TryGetValue(name, out FieldSpec? spec) || frame.Length < spec.Offset + 2)
        {
            return false;
        }

        // Default: 2-byte character count followed by UTF-16LE; "utf8z" = zero-terminated UTF-8.
        if (string.Equals(spec.Encoding, "utf8z", StringComparison.OrdinalIgnoreCase))
        {
            ReadOnlySpan<byte> tail = frame[spec.Offset..];
            int end = tail.IndexOf((byte)0);
            value = Encoding.UTF8.GetString(end < 0 ? tail : tail[..end]);
            return true;
        }

        int chars = littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(frame[spec.Offset..]) : BinaryPrimitives.ReadUInt16BigEndian(frame[spec.Offset..]);
        int start = spec.Offset + 2;
        if (chars > 512 || frame.Length < start + chars * 2)
        {
            return false;
        }

        value = Encoding.Unicode.GetString(frame.Slice(start, chars * 2));
        return true;
    }

    private static ulong ReadUnsigned(ReadOnlySpan<byte> frame, FieldSpec spec, bool littleEndian)
    {
        ReadOnlySpan<byte> bytes = frame.Slice(spec.Offset, spec.Size);
        return spec.Size switch
        {
            1 => bytes[0],
            2 => littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes),
            4 => littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes),
            _ => littleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(bytes) : BinaryPrimitives.ReadUInt64BigEndian(bytes),
        };
    }

    private static long ReadSigned(ReadOnlySpan<byte> frame, FieldSpec spec, bool littleEndian)
    {
        ReadOnlySpan<byte> bytes = frame.Slice(spec.Offset, spec.Size);
        return spec.Size switch
        {
            1 => (sbyte)bytes[0],
            2 => littleEndian ? BinaryPrimitives.ReadInt16LittleEndian(bytes) : BinaryPrimitives.ReadInt16BigEndian(bytes),
            4 => littleEndian ? BinaryPrimitives.ReadInt32LittleEndian(bytes) : BinaryPrimitives.ReadInt32BigEndian(bytes),
            _ => littleEndian ? BinaryPrimitives.ReadInt64LittleEndian(bytes) : BinaryPrimitives.ReadInt64BigEndian(bytes),
        };
    }
}
