namespace AionSniffer.ChatLog;

/// <summary>Which running total a ChatLogParser.PersonalStatChanged event is about -- see its
/// remarks for why this is a separate signal from DamageEvent (only ever "You", never per-row).</summary>
public enum PersonalStatKind
{
    AbyssPoints,
    Kinah,
    Experience,
    GloryPoints,
}
