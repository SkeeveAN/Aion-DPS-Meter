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

    public string CurrentZone { get; private set; } = "";

    public int SkippedShortFrames { get; private set; }
    public int UnknownOpcodes { get; private set; }

    /// <summary>Damage-opcode frames without a damage block (see <see cref="DecodeVarintDamage"/>).</summary>
    public int NoDamageFrames { get; private set; }

    /// <summary>Each server id seen in front of a player's name (appearance and party frames), with the
    /// names it came with - for working out which id is which server. Diagnostic.</summary>
    public Dictionary<int, SortedSet<string>> ServerIdsSeen { get; } = new();

    private void NoteServerId(int serverId, string name)
    {
        if (!ServerIdsSeen.TryGetValue(serverId, out var names))
        {
            ServerIdsSeen[serverId] = names = new SortedSet<string>(StringComparer.Ordinal);
        }

        if (names.Count < 12)
        {
            names.Add(name);
        }
    }

    public int Bundles { get; private set; }
    public int BundleFailures { get; private set; }

    public IEnumerable<DamageEvent> Decode(ReadOnlySpan<byte> frame, DateTime timestamp) => DecodeFrame(frame, timestamp, nested: false);

    private IEnumerable<DamageEvent> DecodeFrame(ReadOnlySpan<byte> frame, DateTime timestamp, bool nested)
    {
        FrameLayout layout = _protocol.FrameLayout;
        if (frame.Length < layout.OpcodeOffset + layout.OpcodeSize)
        {
            SkippedShortFrames++;
            return Array.Empty<DamageEvent>();
        }

        int opcode = (int)ReadUnsigned(frame, new FieldSpec(layout.OpcodeOffset, layout.OpcodeSize), layout.LittleEndian && !layout.OpcodeBigEndian);
        if (_protocol.BundleOpcode is int bundleOpcode && opcode == bundleOpcode && !nested)
        {
            return DecodeBundle(frame, timestamp);
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
                DecodeVarintNickname(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Roster:
                DecodeRoster(frame, timestamp);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Equipment:
                DecodeEquipment(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Daevanion:
                DecodeDaevanion(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Skills:
                DecodeSkills(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Character:
                DecodeCharacter(frame, timestamp);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Appearance:
                DecodeAppearance(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.NpcSpawn:
                DecodeNpcSpawn(frame, timestamp);
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
    private void DecodeNpcSpawn(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        int p = 2;
        if (!TryReadVarint(frame, ref p, out long entityId) || frame.Length < p + 7)
        {
            return;
        }

        _spawnedAt[unchecked((int)entityId)] = timestamp;

        // Two type bytes, then a flag: 1 = the entity carries a name (a summon's owner, e.g. a
        // Cleric's Divine Aura announced as "Psefon"), length-prefixed, before the NPC id.
        string? ownerName = null;
        if (frame[p + 2] == 1 && TryReadName(frame, p + 3, out string named, minLength: 2))
        {
            ownerName = named;
            p += 1 + named.Length;
            if (frame.Length < p + 7)
            {
                return;
            }
        }

        p += 3;
        int npcId = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]));
        _entities.NoteSpawned(unchecked((int)entityId));
        _entities.RegisterNpc(unchecked((int)entityId), npcId);
        _entities.SetSummonOwnerName(unchecked((int)entityId), ownerName);

        // Further on: eight FF bytes, eight more bytes, then the owner's id (varint). An ordinary
        // monster names itself there; a summoned spirit names the player who summoned it (verified on
        // three Krao Cave / Urugugu captures, 2026-10-02: all 161 spirits resolved to the
        // Spiritmaster casting their "Summon:" skills, three Spiritmasters in one party kept apart).
        // A Cleric's Divine Aura names itself here, and its owner by name instead (above).
        int marker = frame[(p + 4)..].IndexOf(OwnerMarker);
        int q = marker < 0 ? -1 : p + 4 + marker + OwnerMarker.Length + 8;
        if (q > 0 && q < frame.Length && TryReadVarint(frame, ref q, out long owner) && owner > 0)
        {
            _entities.SetSummonOwner(unchecked((int)entityId), owner == entityId ? null : unchecked((int)owner));
        }
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
    /// his hit with 15280032/3, each cast ~50 ms before the spawn. The variant decides; with the
    /// same talents, the cast closest before the spawn (within two seconds) does.</para>
    /// </summary>
    private int? GuessSummonOwner(int actor, int skillId, int target)
    {
        if (!_entities.IsSpawned(actor) || !_entities.IsKnownMonster(target) || Aion2SkillNames.ClassOf(skillId) is not string className)
        {
            return null;
        }

        var owners = _entities.PartyMemberIdsOfClass(className).Where(id => id != actor).ToList();
        int? owner = owners.Count switch
        {
            0 => null,
            1 => owners[0],
            _ => OwnerByCast(actor, skillId / 10, owners),
        };
        if (owner is int found)
        {
            _entities.SetSummonOwner(actor, found);
        }

        return owner;
    }

    private int? OwnerByCast(int summon, int variant, List<int> owners)
    {
        var casters = owners.Where(id => _lastCasts.ContainsKey((id, variant))).ToList();
        if (casters.Count == 1)
        {
            return casters[0];
        }

        if (casters.Count == 0 || !_spawnedAt.TryGetValue(summon, out DateTime spawned))
        {
            return null;
        }

        var justBefore = casters
            .Select(id => (Id: id, Gap: spawned - _lastCasts[(id, variant)]))
            .Where(c => c.Gap >= TimeSpan.Zero && c.Gap <= TimeSpan.FromSeconds(2))
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

    private static ReadOnlySpan<byte> OwnerMarker => new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff };

    private IEnumerable<DamageEvent> DecodeVarintDamage(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
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
        NoteCast((int)actor, skillId, timestamp);
        if (Aion2SkillNames.IsNonDamageEffect(skillId))
        {
            return Array.Empty<DamageEvent>();
        }

        bool critical = frame[p + 1] == 3;

        int marker = -1;
        for (int i = p + 2; i + 4 <= frame.Length; i++)
        {
            if (frame[i] is >= 1 and <= 9 && frame[i + 1] == 0 && frame[i + 2] == 0 && frame[i + 3] == 0)
            {
                marker = i;
                break;
            }
        }

        int q = marker + 6;
        if (marker < 0 || q >= frame.Length || !TryReadVarint(frame, ref q, out long amount) || amount <= 0 || amount > MaxPlausibleAmount)
        {
            SkippedShortFrames++;
            return Array.Empty<DamageEvent>();
        }

        if (skillId == DodgeSkillId)
        {
            _avoids.Add(new AvoidEvent(timestamp, (int)actor, (int)target, AvoidKind.Dodge));
            return Array.Empty<DamageEvent>();
        }

        // A summoned spirit's hits are its summoner's, as in the game's own combat analyzer. The heal
        // test below still looks at the spirit itself: its spawn "heal" targets its own id.
        int source = _entities.SummonOwnerOf((int)actor) ?? GuessSummonOwner((int)actor, skillId, (int)target) ?? (int)actor;
        if (Aion2SkillNames.ClassOf(skillId) is string className)
        {
            _entities.NoteClass(source, className);
        }

        // No "skill used" notification for the window: its handler (Chat.log's way of finding the
        // active character and other players' classes) refreshes the row list, which must only
        // happen on the UI thread - and this runs on the capture thread. Aion 2 knows each
        // player's class from the skill ids themselves (see Aion2EntityDirectory.NoteClass).
        string skill = Aion2SkillNames.NameOf(skillId);

        // A heal-family skill is a heal unless it lands on a monster (Blood Absorption drains one).
        // "A monster" means one the server announced: a player is only known once seen casting, so a
        // Chanter's Recuperation on a member who had not cast yet used to read as damage between two
        // players - and one such hit made the resolver paint the whole party as enemies.
        bool isHeal = Aion2SkillNames.IsHealFamily(skillId) && !_entities.IsKnownMonster((int)target);

        // A heal on a summon is not healing the group: a Spiritmaster's spirit arrives with a heal of
        // its full health on itself (~56,000 per summon - 4.07 M over one Krao Cave run once spirits
        // are credited to their summoner), and topping up one's spirits is not party healing either.
        if (isHeal && _entities.SummonOwnerOf((int)target) is not null)
        {
            return Array.Empty<DamageEvent>();
        }

        return new[] { new DamageEvent(timestamp, source, (int)target, amount, isHeal, skill, critical && !isHeal) };
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

        p += 4;
        long healed = 0;
        if (!TryReadVarint(frame, ref p, out long amount) || (flags & 0x01) != 0 && !TryReadVarint(frame, ref p, out healed) || frame.Length != p + 4)
        {
            SkippedShortFrames++;
            return Array.Empty<DamageEvent>();
        }

        int skillId = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]));

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
                ? new[] { new DamageEvent(timestamp, source, (int)target, healed, IsHeal: true, Aion2SkillNames.NameOf(skillId), IsTick: true) }
                : Array.Empty<DamageEvent>();
        }

        if (amount <= 0 || amount > MaxPlausibleAmount || target == actor)
        {
            return Array.Empty<DamageEvent>();
        }

        return new[] { new DamageEvent(timestamp, source, (int)target, amount, IsHeal: false, Aion2SkillNames.NameOf(skillId), IsTick: true) };
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
            }
        }

        if (p != frame.Length || (format & ~3) != 0)
        {
            SkippedShortFrames++;
            return;
        }

        if (current is long hp && hp >= 0)
        {
            _entities.HitPoints.Note(unchecked((int)entityId), timestamp, hp);
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
    private void DecodeVarintNickname(ReadOnlySpan<byte> frame)
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
                ReadSeenProfile(frame, (int)id, k + 1 + name.Length);

                // A player in a guild: further on, the frame carries server id (u16: 17 05 = 1303,
                // 18 05 = 1304 Kaisinel) | guild id (u32, non-zero) | 00 00 | the same server id |
                // the guild's length-prefixed name. Seen for all ten guilds of 71 nickname frames
                // (Krao Cave and Draupnir captures, 2026-10-02: HORDE, ElyosOrden, Insomnia,
                // Convèrgence, Freljord, DarkLegion...); a player without a guild has no such run,
                // and reading any "server id + name" pair there picked up garbage ("odd",
                // "jd47ddddep"). Remembered so the roster's leftover name is the player's, not the
                // guild's.
                for (int i = k + 1 + name.Length; i + 11 < frame.Length; i++)
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
    private void DecodeAppearance(ReadOnlySpan<byte> frame)
    {
        int p = 2;
        if (!TryReadVarint(frame, ref p, out _) || frame.Length < p + 4)
        {
            return;
        }

        p += 4;
        if (!TryReadVarint(frame, ref p, out long id) || id <= 0 || frame.Length < p + 3
            || !TryReadName(frame, p + 2, out string name, minLength: 2))
        {
            return;
        }

        _entities.Register((int)id, name);
        NoteServerId(frame[p] | frame[p + 1] << 8, name);
        if (TryReadName(frame, p + 3 + name.Length, out string guild, minLength: 2) && guild != name)
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

            int after = k + 1 + name.Length;
            if (after + 11 > frame.Length || frame[after + 6] != 1)
            {
                continue;
            }

            // The two bytes after the name are the character's server id (18 05 = 1304, Europe -
            // Kaisinel; 17 05 = 1303 for a character of another EU server). They used to be required to
            // be 18 05, so the own record of anyone not on Kaisinel was never read; a plausible class
            // code (4 * class + faction bit, see Aion2SkillNames.ClassFromCode) checks the match instead.
            int classCode = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(after + 2)..]));
            if (classCode % 4 is not (1 or 2) || classCode / 4 is < 1 or > 9)
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
            return;
        }
    }

    /// <summary>
    /// The full equipment list the game sends at login (verified against the in-game window: belt
    /// +4 and amulet +3 came out exactly): one entry per item - item id (u32), <c>01 00 00 00</c>,
    /// four zero bytes, <c>0b</c>, slot index, then a block of zeros whose first non-zero byte
    /// (within 24 bytes) is the enchant level. Entries carry more (stones, rolled stats) that is not
    /// decoded.
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

            int enchant = 0;
            // The enchant sits 22-23 bytes after the slot index; the next non-zero byte of a plain
            // item only starts at 36 (an unrelated value), so the window stops before that.
            for (int k = p + 14; k < Math.Min(frame.Length, p + 14 + 24); k++)
            {
                if (frame[k] != 0)
                {
                    enchant = frame[k] <= 30 ? frame[k] : 0;
                    break;
                }
            }

            items.Add(new Aion2EquippedItem(slot, itemId, enchant));
        }

        if (items.Count > 0)
        {
            _entities.SetLocalEquipment(items.OrderBy(i => i.SlotIndex).ToList());
        }
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
    /// What a "player appeared" frame says about the player after the name: a class code
    /// (<c>4 * class id + faction bit</c>, see <see cref="Aion2SkillNames.ClassFromCode"/>) and the
    /// visible equipment, entries of item id (u32), <c>00</c>, slot index (1-12), <c>00</c>.
    /// </summary>
    private void ReadSeenProfile(ReadOnlySpan<byte> frame, int id, int afterName)
    {
        if (afterName + 4 > frame.Length)
        {
            return;
        }

        int code = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[afterName..]));
        int? classId = code % 4 is 1 or 2 && code / 4 is >= 1 and <= 8 ? code / 4 : null;
        int? faction = classId is null ? null : code % 4;

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

        if (classId is not null || gear.Count > 0)
        {
            _entities.SetSeenProfile(id, new Aion2SeenProfile(classId, faction, gear.OrderBy(g => g.SlotIndex).ToList()));
        }
    }

    /// <summary>
    /// The party roster (0x0297, re-sent every few seconds while in a party; 0x0197 is a list of
    /// other parties). Each member: server id (u16) | length-prefixed name | a small u32 (not the class
    /// code of the other frames: 32 for a Cleric, 24 for an Elementalist) | level (u32). Found by that shape rather than by the server id 18 05 (Kaisinel) it used to require,
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

            int after = i + 1 + name.Length;
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

            _entities.NoteRosterName(name);
            i = after + 7;
        }

        if (frame[0] == 0x02 && frame[1] == 0x97 && members.Count > 0)
        {
            _entities.NoteParty(members, timestamp);
        }
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

        string candidate;
        try
        {
            candidate = new UTF8Encoding(false, true).GetString(frame.Slice(at + 1, length));
        }
        catch (ArgumentException)
        {
            return false;
        }

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
