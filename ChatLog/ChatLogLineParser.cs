using System.Globalization;
using System.Text.RegularExpressions;

namespace AionSniffer.ChatLog;

/// <summary>
/// Splits one raw Chat.log line into timestamp + message text. Verified against a real
/// OriginAion Chat.log (client 4.6): lines look like
/// "2026.08.23 21:31:08 : Ulgorn Raider inflicted 1 damage on Training Dummy. " (note the
/// trailing space before CRLF -- trimmed away here).
/// </summary>
public static partial class ChatLogLineParser
{
    [GeneratedRegex(@"^(?<ts>\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2}) : (?<msg>.*)$")]
    private static partial Regex LinePattern();

    public static ChatLogEntry? TryParse(string rawLine)
    {
        var match = LinePattern().Match(rawLine.TrimEnd());
        if (!match.Success)
        {
            return null;
        }

        if (!DateTime.TryParseExact(match.Groups["ts"].Value, "yyyy.MM.dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
        {
            return null;
        }

        return new ChatLogEntry(timestamp, match.Groups["msg"].Value.TrimEnd());
    }
}
