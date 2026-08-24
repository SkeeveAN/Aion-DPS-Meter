namespace AionSniffer.ChatLog;

/// <summary>One raw, timestamped line from Chat.log, before any combat-specific interpretation.</summary>
public readonly record struct ChatLogEntry(DateTime Timestamp, string Message);
