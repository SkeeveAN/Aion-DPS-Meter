using System.Buffers.Binary;
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

    public Aion2FrameDecoder(Aion2Protocol protocol, Aion2EntityDirectory entities)
    {
        _protocol = protocol;
        _entities = entities;
    }

    public string CurrentZone { get; private set; } = "";

    public int SkippedShortFrames { get; private set; }
    public int UnknownOpcodes { get; private set; }

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

        OpcodeFamily family = _protocol.FamilyOf(opcode);
        IReadOnlyDictionary<string, FieldSpec> fields = _protocol.FieldsOf(family);

        switch (family)
        {
            case OpcodeFamily.Damage when string.Equals(_protocol.DamageLayout, "varint-v1", StringComparison.Ordinal):
                return DecodeVarintDamage(frame, timestamp);
            case OpcodeFamily.Damage:
            case OpcodeFamily.Dot:
            case OpcodeFamily.Heal:
                return DecodeAmount(frame, timestamp, fields, isHeal: family == OpcodeFamily.Heal, layout.LittleEndian);
            case OpcodeFamily.Nickname when string.Equals(_protocol.NicknameLayout, "varint-v1", StringComparison.Ordinal):
                DecodeVarintNickname(frame);
                return Array.Empty<DamageEvent>();
            case OpcodeFamily.Roster:
                DecodeRoster(frame);
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
                DecodeNpcSpawn(frame);
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
    private void DecodeNpcSpawn(ReadOnlySpan<byte> frame)
    {
        int p = 2;
        if (!TryReadVarint(frame, ref p, out long entityId) || frame.Length < p + 7)
        {
            return;
        }

        p += 3;
        int npcId = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]));
        _entities.RegisterNpc(unchecked((int)entityId), npcId);
    }

    private IEnumerable<DamageEvent> DecodeVarintDamage(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        int p = 2;
        if (!TryReadVarint(frame, ref p, out long target) || frame.Length < p + 2)
        {
            SkippedShortFrames++;
            return Array.Empty<DamageEvent>();
        }

        p += 2;
        if (!TryReadVarint(frame, ref p, out long actor) || frame.Length < p + 6)
        {
            SkippedShortFrames++;
            return Array.Empty<DamageEvent>();
        }

        int skillId = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]));
        p += 4;
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

        if (Aion2SkillNames.ClassOf(skillId) is string className)
        {
            _entities.NoteClass((int)actor, className);
        }

        // No "skill used" notification for the window: its handler (Chat.log's way of finding the
        // active character and other players' classes) refreshes the row list, which must only
        // happen on the UI thread - and this runs on the capture thread. Aion 2 knows each
        // player's class from the skill ids themselves (see Aion2EntityDirectory.NoteClass).
        string skill = Aion2SkillNames.NameOf(skillId);

        // A heal-family skill aimed at its caster (Blood Absorption) or at another known player is a
        // heal; the same skill aimed at anything else (a mob) stays damage.
        bool isHeal = Aion2SkillNames.IsHealFamily(skillId) && (target == actor || _entities.IsKnownPlayer((int)target));
        return new[] { new DamageEvent(timestamp, (int)actor, (int)target, amount, isHeal, skill, critical && !isHeal) };
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

                // The rest of the frame repeats the guild name behind the same 18 05 marker the
                // roster uses; remember it so the roster's leftover name is the player's, not it.
                for (int i = k + 1 + name.Length; i + 4 < frame.Length; i++)
                {
                    if (frame[i] == 0x18 && frame[i + 1] == 0x05 && TryReadName(frame, i + 2, out string other) && other != name)
                    {
                        _entities.NoteNonPlayerName(other);
                        _entities.SetGuild((int)id, other);
                    }
                }

                return;
            }
        }
    }

    /// <summary>
    /// "Player seen" frame (verified against captures from the open world and a dungeon): opcode |
    /// target id (varint) | skill id (u32) | the acting player's combat id (varint) | <c>18 05</c> |
    /// length-prefixed name | optional length-prefixed guild. Every player who acts near you is
    /// announced this way, which names everyone in the open world - including the local player,
    /// who is the same kind of entry as anyone else.
    /// </summary>
    private void DecodeAppearance(ReadOnlySpan<byte> frame)
    {
        for (int k = 3; k + 3 < frame.Length; k++)
        {
            if (frame[k] != 0x18 || frame[k + 1] != 0x05 || !TryReadName(frame, k + 2, out string name, minLength: 2))
            {
                continue;
            }

            // The combat id is the varint that ends right before the marker.
            int end = k - 1;
            if ((frame[end] & 0x80) != 0)
            {
                return;
            }

            int start = end;
            while (start > 2 && (frame[start - 1] & 0x80) != 0 && end - start < 4)
            {
                start--;
            }

            long id = 0;
            for (int i = start, shift = 0; i <= end; i++, shift += 7)
            {
                id |= (long)(frame[i] & 0x7f) << shift;
            }

            _entities.Register((int)id, name);
            if (TryReadName(frame, k + 3 + name.Length, out string guild, minLength: 2) && guild != name)
            {
                _entities.SetGuild((int)id, guild);
                _entities.NoteNonPlayerName(guild);
            }

            return;
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
            if (after + 11 > frame.Length || frame[after] != 0x18 || frame[after + 1] != 0x05 || frame[after + 6] != 1)
            {
                continue;
            }

            // The two bytes after the name (18 05 = 1304 on Europe - Kaisinel) are the character's server id:
            // the same value sits before every Kaisinel member in the legion list, and Aion 2 characters of
            // other servers show up in the same group with other values.
            int classCode = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(after + 2)..]));
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

    /// <summary>The party roster: every member's name follows a <c>18 05</c> marker as a
    /// length-prefixed string. Carries no combat ids - it only tells which names belong to the party
    /// (see <see cref="Aion2EntityDirectory.NoteRosterName"/>).</summary>
    private void DecodeRoster(ReadOnlySpan<byte> frame)
    {
        for (int i = 0; i + 4 < frame.Length; i++)
        {
            if (frame[i] == 0x18 && frame[i + 1] == 0x05 && TryReadName(frame, i + 2, out string name))
            {
                // A member entry continues with level(u32) and 0x1e(u32); that rules out the
                // look-alike byte runs a random stretch of data can contain (seen: "Coh").
                int after = i + 3 + name.Length;
                if (after + 8 <= frame.Length && frame[after + 1] == 0 && frame[after + 2] == 0 && frame[after + 3] == 0
                    && frame[after + 4] == 0x1e && frame[after + 5] == 0 && frame[after + 6] == 0 && frame[after + 7] == 0)
                {
                    _entities.NoteRosterName(name);
                }
            }
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
