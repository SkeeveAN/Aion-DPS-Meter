namespace AionSniffer.Combat;

/// <summary>
/// One damage or heal instance, already decoded from whatever packet produced it
/// (SM_ATTACK, SM_ATTACK_STATUS, ...). Deliberately protocol-agnostic so the DPS/iDPS
/// math can be built and verified against synthetic data before real opcodes are confirmed.
/// </summary>
public readonly record struct DamageEvent(DateTime Timestamp, int SourceObjectId, int TargetObjectId, long Amount, bool IsHeal);
