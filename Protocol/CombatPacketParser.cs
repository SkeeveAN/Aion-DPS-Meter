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

    private static int ReadI32(byte[] b, int o) => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
    private static ushort ReadU16(byte[] b, int o) => (ushort)(b[o] | (b[o + 1] << 8));
}
