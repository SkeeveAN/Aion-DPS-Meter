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

    /// <summary>
    /// Generic localized system message (chat-window notices, AP/GP gain, plain-text combat
    /// narration). See <see cref="CombatPacketParser.TryDescribeSystemMessage"/>.
    /// </summary>
    public const ushort SM_SYSTEM_MESSAGE = 0x19;

    /// <summary>NPC becomes visible: object ID, template ID (npcId), display name ID. Boss-detection candidate.</summary>
    public const ushort SM_NPC_INFO = 0x0E;

    /// <summary>Object (NPC or player) is no longer visible -- despawn/death candidate signal.</summary>
    public const ushort SM_DELETE = 0x16;

    /// <summary>
    /// Party roster update: object ID, HP/MP/FP, position, class ID, gender, level, and (for
    /// most event types) the player's name. No race field -- AION parties are same-faction only,
    /// so race is implied by the local player's own faction. Name/class/level resolution candidate.
    /// </summary>
    public const ushort SM_GROUP_MEMBER_INFO = 0x5B;
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
