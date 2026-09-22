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

    public IEnumerable<DamageEvent> Decode(ReadOnlySpan<byte> frame, DateTime timestamp)
    {
        FrameLayout layout = _protocol.FrameLayout;
        if (frame.Length < layout.OpcodeOffset + layout.OpcodeSize)
        {
            SkippedShortFrames++;
            return Array.Empty<DamageEvent>();
        }

        int opcode = (int)ReadUnsigned(frame, new FieldSpec(layout.OpcodeOffset, layout.OpcodeSize), layout.LittleEndian);
        OpcodeFamily family = _protocol.FamilyOf(opcode);
        IReadOnlyDictionary<string, FieldSpec> fields = _protocol.FieldsOf(family);

        switch (family)
        {
            case OpcodeFamily.Damage:
            case OpcodeFamily.Dot:
            case OpcodeFamily.Heal:
                return DecodeAmount(frame, timestamp, fields, isHeal: family == OpcodeFamily.Heal, layout.LittleEndian);
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
