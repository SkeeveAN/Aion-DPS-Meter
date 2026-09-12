namespace AionSniffer.ChatLog;

/// <summary>
/// One real reinforcement (buff) cast, decoded from Chat.log's "X is in the boost ... state
/// because Y used Z."/"X is in the boost ... state after using Z." lines - see ChatLogParser's own
/// remarks on BuffCast for why this is a separate, narrower event than SkillUsed (which fires for
/// any damage/heal skill instead). Both Caster and Recipient are kept - a self-buff has the same
/// name in both, but a Cleric/Chanter group buff names every affected party member as a separate
/// Recipient (one line per recipient in Chat.log), and the web frontend's "Buffs" column is keyed
/// off Recipient so a group buff shows up on every member who actually received it, not only on
/// whoever cast it (see MainWindow.BuildEncounterUpload).
/// </summary>
public readonly record struct BuffCastEvent(DateTime Timestamp, string Caster, string Recipient, string Skill);
