namespace AionSniffer.Protocol;

/// <summary>
/// Candidate opcodes for the packets we care about, taken from the Aion-Lightning emulator
/// source for its 4.5/4.6-era build (Rydiik/Aion-Server-4.6, ServerPacketsOpcodes.java).
/// UNVERIFIED against the actual OriginAion 4.6 server -- treat as a starting hypothesis to
/// confirm with CalibrationMode, not as ground truth. A private server can renumber opcodes.
/// </summary>
public static class Opcodes
{
    /// <summary>Damage/heal/regen "tick" applied to a creature (HP/MP/FP change + reason).</summary>
    public const ushort SM_ATTACK_STATUS = 0x05;

    /// <summary>Direct attack/skill result: attacker vs target, one or more hit results with damage.</summary>
    public const ushort SM_ATTACK = 0x36;
}

/// <summary>
/// Mirrors SM_ATTACK_STATUS.TYPE from the emulator: what kind of stat the value applies to.
/// </summary>
public enum AttackStatusType
{
    NaturalHp = 3,
    UsedHp = 4,
    Regular = 5,
    AbsorbedHp = 6,
    Damage = 7, // same numeric value as HP in the source; damage is inferred from a negative value
    ProtectDmg = 8,
    DelayDamage = 10,
    FallDamage = 17,
    HealMp = 19,
    AbsorbedMp = 20,
    Mp = 21,
    NaturalMp = 22,
    FpRings = 23,
    Fp = 25,
    NaturalFp = 26,
}
