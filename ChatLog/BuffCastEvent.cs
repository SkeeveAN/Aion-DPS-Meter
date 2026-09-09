namespace AionSniffer.ChatLog;

/// <summary>
/// One real reinforcement (buff) cast, decoded from Chat.log's "X is in the boost ... state
/// because Y used Z."/"X is in the boost ... state after using Z." lines - see ChatLogParser's own
/// remarks on BuffCast for why this is a separate, narrower event than SkillUsed (which fires for
/// any damage/heal skill instead). No target/recipient here: unlike a heal, who a buff LANDS ON
/// isn't what the web frontend's "Buffs" column asks - only who CAST it and how often.
/// </summary>
public readonly record struct BuffCastEvent(DateTime Timestamp, string Caster, string Skill);
