using System.Text;
using AionSniffer.Data;

namespace AionSniffer.Protocol;

/// <summary>
/// Best-effort field layout for the two packets a DMG meter actually needs, taken from the
/// emulator's SM_ATTACK_STATUS / SM_ATTACK writeImpl(). UNVERIFIED against the real server --
/// this is what to check first once <see cref="Opcodes"/> are confirmed to line up.
/// </summary>
public static class CombatPacketParser
{
    /// <summary>
    /// SM_ATTACK_STATUS body (14 bytes): creatureObjId:i32, value:i32 (negative = damage),
    /// type:u8, hpPercent:u8, skillId:u16, logId:u16.
    /// </summary>
    public static string? TryDescribeAttackStatus(byte[] body)
    {
        if (body.Length < 14)
        {
            return null;
        }

        int creatureObjId = ReadI32(body, 0);
        int value = ReadI32(body, 4);
        byte type = body[8];
        byte hpPercent = body[9];
        ushort skillId = ReadU16(body, 10);
        ushort logId = ReadU16(body, 12);

        string kind = value < 0 ? "DAMAGE" : "GAIN";
        int amount = Math.Abs(value);
        string skillName = skillId == 0 ? "(none)" : SkillDatabase.DisplayName(skillId);

        return $"ATTACK_STATUS target=0x{creatureObjId:X8} {kind}={amount} hp%={hpPercent} type={type} skill={skillId} \"{skillName}\" log={logId}";
    }

    /// <summary>One hit result inside an SM_ATTACK packet's hit list.</summary>
    public readonly record struct AttackHit(long Damage, byte AttackStatusId, byte ShieldType);

    /// <summary>A fully parsed SM_ATTACK packet: attacker/target plus every hit, not just the first.</summary>
    public sealed record AttackPacket(
        int AttackerObjectId,
        int TargetObjectId,
        byte TargetHpPercent,
        byte AttackerHpPercent,
        IReadOnlyList<AttackHit> Hits);

    /// <summary>
    /// SM_ATTACK fixed header (20 bytes): attacker:i32, attackNo:u8, time:u16,
    /// simpleAttackType:u8, type:u8, target:i32, targetHp%:u8, attackerHp%:u8, counterFlag:i32,
    /// hitCount:u8. Then `hitCount` hit records, each: damage:i32, attackStatusId:u8,
    /// shieldType:u8, 16 reserved bytes, then a shieldType-dependent extra block (see
    /// SM_ATTACK.java writeImpl): 0 bytes for shieldType 0/2, 12 bytes for 8/10 (protector
    /// id/damage/skill), 28 bytes for 16 or anything else (reflect/protect fields). A trailing
    /// list-size byte (always written as 0 in the source) follows the hits.
    /// </summary>
    public static AttackPacket? TryParseAttack(byte[] body)
    {
        const int headerLen = 4 + 1 + 2 + 1 + 1 + 4 + 1 + 1 + 4 + 1; // = 20
        if (body.Length < headerLen)
        {
            return null;
        }

        int attackerObjId = ReadI32(body, 0);
        int offset = 5 + 2 + 1 + 1; // skip attackNo:u8, time:u16, simpleAttackType:u8, type:u8
        int targetObjId = ReadI32(body, offset);
        offset += 4;
        byte targetHp = body[offset++];
        byte attackerHp = body[offset++];
        offset += 4; // counter flag (u32)
        byte hitCount = body[offset++];

        var hits = new List<AttackHit>(hitCount);
        for (int i = 0; i < hitCount; i++)
        {
            if (offset + 4 + 1 + 1 + 16 > body.Length)
            {
                break; // truncated/desynced packet -- return what parsed cleanly so far
            }

            long damage = ReadI32(body, offset);
            byte attackStatusId = body[offset + 4];
            byte shieldType = body[offset + 5];
            offset += 4 + 1 + 1 + 16;

            // The "_ => 28" catch-all is deliberate, not a shortcut: in the emulator source itself,
            // shieldType 16 AND its unlabeled "default" branch (its own comment admits shieldType 1
            // and 4 are real, seen-in-the-wild values it never bothered to name -- "TODO find out 4")
            // both write exactly 28 bytes. So "unknown shieldType" is not the same as "corrupt data"
            // here; rejecting anything outside {0,2,8,10,16} would misparse legitimate traffic
            // (types 1, 4, ...) just as often as it would catch a real desync.
            //
            // What this does NOT protect against: the frame this body came from was already
            // length- and checksum-validated by AionSession using the wire's own length prefix, so
            // a wrong extraLen guess here cannot corrupt framing or bleed into the next packet --
            // but within *this* packet, if there's enough slack in the body for a wrong guess to
            // avoid an outright bounds failure, it will silently produce a plausible-looking but
            // wrong AttackPacket rather than an error. There is no separate downstream check that
            // catches that; it's an accepted risk until 0x36 is confirmed to really be SM_ATTACK
            // with this exact layout (see calibration status in README.md).
            int extraLen = shieldType switch
            {
                0 or 2 => 0,
                8 or 10 => 12,
                _ => 28,
            };

            if (offset + extraLen > body.Length)
            {
                hits.Add(new AttackHit(damage, attackStatusId, shieldType));
                break;
            }

            offset += extraLen;
            hits.Add(new AttackHit(damage, attackStatusId, shieldType));
        }

        return new AttackPacket(attackerObjId, targetObjId, targetHp, attackerHp, hits);
    }

    /// <summary>Human-readable summary of <see cref="TryParseAttack"/>, for the calibration dump.</summary>
    public static string? TryDescribeAttack(byte[] body)
    {
        var attack = TryParseAttack(body);
        if (attack is null)
        {
            return null;
        }

        string hitsDesc = string.Join(", ", attack.Hits.Select(h => $"{h.Damage}(status={h.AttackStatusId},shield={h.ShieldType})"));
        return $"ATTACK attacker=0x{attack.AttackerObjectId:X8} target=0x{attack.TargetObjectId:X8} " +
               $"hits=[{hitsDesc}] targetHp%={attack.TargetHpPercent} attackerHp%={attack.AttackerHpPercent}";
    }

    /// <summary>
    /// SM_SYSTEM_MESSAGE body: textColorId:u8, unk:u8, npcObjId:i32, msgCode:i32, paramCount:u8,
    /// then paramCount entries that are either a DescriptionId (fixed 8 bytes, starts with the
    /// marker short 0x0024) or a null-terminated UTF-16LE string. This is the same generic packet
    /// AION uses for AP/GP gain notifications ("You have earned %num0 Abyss Points") and for
    /// plain-text combat narration ("You inflicted %num1 damage on %0") -- useful as a
    /// human-readable cross-check against the binary SM_ATTACK/SM_ATTACK_STATUS decode, and as
    /// the (probable) source for AP/GP tracking once the real msgCode values are confirmed.
    /// </summary>
    public static string? TryDescribeSystemMessage(byte[] body)
    {
        if (body.Length < 11)
        {
            return null;
        }

        int npcObjId = ReadI32(body, 2);
        int code = ReadI32(body, 6);
        byte paramCount = body[10];
        int offset = 11;
        var parts = new List<string>();

        for (int i = 0; i < paramCount && offset + 2 <= body.Length; i++)
        {
            ushort marker = ReadU16(body, offset);
            if (marker == 0x0024 && offset + 8 <= body.Length)
            {
                int descId = ReadI32(body, offset + 2);
                parts.Add($"#{descId}");
                offset += 8;
            }
            else
            {
                int start = offset;
                while (offset + 2 <= body.Length && ReadU16(body, offset) != 0)
                {
                    offset += 2;
                }

                parts.Add(Encoding.Unicode.GetString(body, start, offset - start));
                offset += 2; // null terminator
            }
        }

        string tag = code is 1320000 or 1300965 or 1402081 or 1402219
            ? " [AP/GP CANDIDATE -- unconfirmed msgCode]"
            : "";

        return $"SYSTEM_MESSAGE code={code} npcObjId=0x{npcObjId:X8} params=[{string.Join(", ", parts)}]{tag}";
    }

    /// <summary>
    /// SM_NPC_INFO fixed header (36 bytes; equipment/buffs/etc. follow at a variable offset and
    /// are not parsed): x,y,z:f32*3, objectId:i32, npcId:i32 (repeated field skipped), typeId:u8,
    /// state:u16, heading:u8, nameId:i32, titleId:i32. `npcId` is the monster template ID -- the
    /// field a boss-detection allowlist would key on, once real boss template IDs are observed.
    /// </summary>
    public static string? TryDescribeNpcInfo(byte[] body)
    {
        if (body.Length < 36)
        {
            return null;
        }

        int objectId = ReadI32(body, 12);
        int npcId = ReadI32(body, 16);
        byte typeId = body[24];
        int nameId = ReadI32(body, 28);
        int titleId = ReadI32(body, 32);

        return $"NPC_INFO objectId=0x{objectId:X8} npcId={npcId} typeId={typeId} nameId={nameId} titleId={titleId}";
    }

    /// <summary>SM_DELETE (5 bytes): objectId:i32, animSpeed:u8. Despawn/death candidate signal.</summary>
    public static string? TryDescribeDelete(byte[] body)
    {
        if (body.Length < 5)
        {
            return null;
        }

        int objectId = ReadI32(body, 0);
        return $"DELETE objectId=0x{objectId:X8}";
    }

    /// <summary>
    /// SM_GROUP_MEMBER_INFO fixed header (63 bytes), see writeImpl in the emulator source:
    /// groupId:i32, objectId:i32, maxHp/curHp/maxMp/curMp/maxFp/curFp:i32*6, unk:i32,
    /// mapId:i32*2, x/y/z:f32*3, classId:u8, genderId:u8, level:u8, eventId:u8, channel:u16,
    /// mentor:u8. For most event types a UTF-16LE name string follows -- attempted opportunistically.
    /// No race field: AION groups are single-faction, race is whatever the local player's is.
    /// </summary>
    public static string? TryDescribeGroupMemberInfo(byte[] body)
    {
        const int headerLen = 4 + 4 + 4 * 6 + 4 + 4 * 2 + 4 * 3 + 1 + 1 + 1 + 1 + 2 + 1; // = 63
        if (body.Length < headerLen)
        {
            return null;
        }

        int objectId = ReadI32(body, 4);
        byte classId = body[56];
        byte genderId = body[57];
        byte level = body[58];
        byte eventId = body[59];

        string name = "";
        if (body.Length > headerLen)
        {
            int offset = headerLen;
            int start = offset;
            while (offset + 2 <= body.Length && ReadU16(body, offset) != 0)
            {
                offset += 2;
            }

            if (offset > start)
            {
                name = Encoding.Unicode.GetString(body, start, offset - start);
            }
        }

        return $"GROUP_MEMBER objectId=0x{objectId:X8} name='{name}' classId={classId} gender={genderId} level={level} event={eventId}";
    }

    private static int ReadI32(byte[] b, int o) => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
    private static ushort ReadU16(byte[] b, int o) => (ushort)(b[o] | (b[o + 1] << 8));
}
