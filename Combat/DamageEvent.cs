namespace AionSniffer.Combat;

/// <summary>
/// One damage or heal instance, already decoded from whatever produced it. Deliberately
/// source-agnostic: the DPS/iDPS math is built and verified against synthetic events, and this
/// type is what let the meter switch from decoded packets to parsed Chat.log lines without the
/// calculator or the aggregator changing at all.
/// </summary>
public readonly record struct DamageEvent(DateTime Timestamp, int SourceObjectId, int TargetObjectId, long Amount, bool IsHeal);
