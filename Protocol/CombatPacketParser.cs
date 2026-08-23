using System.Text;

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

        return $"ATTACK_STATUS target=0x{creatureObjId:X8} {kind}={amount} hp%={hpPercent} type={type} skill={skillId} log={logId}";
    }

    /// <summary>
    /// SM_ATTACK fixed header (24 bytes) + first hit's fixed fields (6 bytes: damage:i32,
    /// attackStatus:u8, shieldType:u8). Multi-hit lists and non-zero shieldType extra fields
    /// are NOT parsed here yet (variable-length; add once opcode/layout is confirmed).
    /// </summary>
    public static string? TryDescribeAttack(byte[] body)
    {
        const int headerLen = 4 + 1 + 2 + 1 + 1 + 4 + 1 + 1 + 4 + 1; // = 20
        if (body.Length < headerLen + 6)
        {
            return null;
        }

        int attackerObjId = ReadI32(body, 0);
        byte attackNo = body[4];
        int offset = 5 + 2 + 1 + 1; // skip time:u16, simpleAttackType:u8, type:u8
        int targetObjId = ReadI32(body, offset);
        offset += 4;
        byte targetHp = body[offset++];
        byte attackerHp = body[offset++];
        offset += 4; // counter flag (u32)
        byte hitCount = body[offset++];

        int firstDamage = ReadI32(body, offset);
        byte attackStatusId = body[offset + 4];
        byte shieldType = body[offset + 5];

        return $"ATTACK attacker=0x{attackerObjId:X8} target=0x{targetObjId:X8} hits={hitCount} " +
               $"firstHit.damage={firstDamage} status={attackStatusId} shield={shieldType} " +
               $"targetHp%={targetHp} attackerHp%={attackerHp} attackNo={attackNo}";
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
