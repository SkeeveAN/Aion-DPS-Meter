namespace AionDPS.Announcements;

/// <summary>What the announcements window shows of one notice: the full text (the server's `body` with its line breaks, else the one-line `summary`), the short preview of a closed card and whether there is more to open.</summary>
public sealed record AnnouncementTexts(string Full, string Preview, bool IsLong);

/// <summary>Prepares the text of an announcement for its card; pure, so it can be tested.</summary>
public static class AnnouncementText
{
    /// <summary>Lines shown on a closed card.</summary>
    public const int PreviewLines = 3;

    /// <summary>A longer line is cut in the preview (the open card shows it whole).</summary>
    public const int PreviewLineChars = 170;

    public static AnnouncementTexts Prepare(string? body, string? summary)
    {
        string full = Clean(!string.IsNullOrWhiteSpace(body) ? body : summary);
        if (full.Length == 0)
        {
            return new AnnouncementTexts("", "", false);
        }

        string[] lines = full.Split('\n').Where(l => l.Length > 0).ToArray();
        bool cut = false;
        var shown = new List<string>();
        foreach (string line in lines.Take(PreviewLines))
        {
            if (line.Length > PreviewLineChars)
            {
                shown.Add(line[..(PreviewLineChars - 1)].TrimEnd() + "…");
                cut = true;
            }
            else
            {
                shown.Add(line);
            }
        }

        return new AnnouncementTexts(full, string.Join('\n', shown), cut || lines.Length > PreviewLines);
    }

    /// <summary>Line ends unified, spaces at the line ends gone, no more than one empty line in a row, nothing around the whole.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd()).ToArray();
        var result = new List<string>();
        int empty = 0;
        foreach (string line in lines)
        {
            if (line.Length == 0)
            {
                empty++;
                if (empty > 1)
                {
                    continue;
                }
            }
            else
            {
                empty = 0;
            }

            result.Add(line);
        }

        return string.Join('\n', result).Trim('\n', ' ');
    }

    /// <summary>The "open in the browser" button shows only for an https address (the notice on NC's own page).</summary>
    public static bool IsBrowserUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length > 0;
}
