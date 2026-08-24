namespace AionSniffer.ChatLog;

/// <summary>
/// One "You/Name have/has acquired [item:...]." (or survey-reward) line, before any filtering by
/// who's allowed to count -- see ChatLogParser.LootAcquired remarks. RawTag is the COMPLETE
/// original "[item:ID;...]" text verbatim (not just the numeric id) specifically so it can be
/// pasted back into an Aion chat box and render as a real clickable item link there, per the
/// user's request -- Aion doesn't care about the readable name for that, only the exact tag.
/// </summary>
public readonly record struct LootEvent(string? Subject, int ItemId, string RawTag, long Quantity);
