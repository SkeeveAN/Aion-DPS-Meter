using AionDPS.Announcements;

namespace AionDPS.Combat;

/// <summary>The text of an announcement card: the server's body with its line breaks, the preview of three lines, the fallback to the one-line summary, and which addresses open in the browser.</summary>
public static class SelfCheckNews
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] announcements text:");
        var four = AnnouncementText.Prepare("Hello Daevas,\r\n\r\n\r\n\r\nLine two  \nLine three\nLine four", "ignored summary");
        bool a = four.IsLong && four.Preview == "Hello Daevas,\nLine two\nLine three" && four.Full == "Hello Daevas,\n\nLine two\nLine three\nLine four";
        var short1 = AnnouncementText.Prepare("One\nTwo", null);
        bool b = !short1.IsLong && short1.Preview == "One\nTwo";
        var fallback = AnnouncementText.Prepare("  ", "Only the teaser");
        bool c = fallback.Full == "Only the teaser" && fallback.Preview == "Only the teaser" && !fallback.IsLong;
        var longLine = AnnouncementText.Prepare(new string('x', 400), null);
        bool d = longLine.IsLong && longLine.Preview.Length == AnnouncementText.PreviewLineChars && longLine.Preview.EndsWith('…') && longLine.Full.Length == 400;
        bool e = AnnouncementText.Prepare(null, null) is { Full: "", Preview: "", IsLong: false };
        bool f = AnnouncementText.IsBrowserUrl("https://www.aion2.plaync.com/notice/1") && !AnnouncementText.IsBrowserUrl("http://x.y/z") && !AnnouncementText.IsBrowserUrl("file:///c:/x") && !AnnouncementText.IsBrowserUrl("") && !AnnouncementText.IsBrowserUrl(null);
        bool ok = a && b && c && d && e && f;
        Console.WriteLine($"  -> body {a}, short {b}, summary fallback {c}, long line {d}, empty {e}, https only {f}");
        return ok;
    }
}
