using System.ComponentModel;
using AionDPS.Aion2.Protocol;
using AionDPS.History;

namespace AionDPS.Ui;

/// <summary>What the session menu says about a session: its name in the interface language and its time span.</summary>
public static class SessionLabels
{
    /// <summary>Muted on purpose: the menu should read as calm as the rest of the window. A map is steel blue, an instance sand.</summary>
    public static System.Windows.Media.Color ColorOf(SessionKind kind) => kind == SessionKind.Instance
        ? System.Windows.Media.Color.FromRgb(0xB3, 0x9A, 0x62)
        : System.Windows.Media.Color.FromRgb(0x62, 0x86, 0xA3);

    /// <summary>The colours of the check results, as quiet as the rest: a soft green, sand and brick.</summary>
    public static System.Windows.Media.Brush LevelBrush(FindingLevel level)
    {
        var brush = new System.Windows.Media.SolidColorBrush(level switch
        {
            FindingLevel.Ok => System.Windows.Media.Color.FromRgb(0x6F, 0xA8, 0x8A),
            FindingLevel.Warn => System.Windows.Media.Color.FromRgb(0xC4, 0xA2, 0x5E),
            FindingLevel.Bad => System.Windows.Media.Color.FromRgb(0xC0, 0x6A, 0x62),
            _ => System.Windows.Media.Color.FromRgb(0x8A, 0x9B, 0xA6),
        });
        brush.Freeze();
        return brush;
    }

    public static string IconOf(FindingLevel level) => level switch
    {
        FindingLevel.Ok => "\u2713",
        FindingLevel.Warn => "\u26A0",
        FindingLevel.Bad => "\u2717",
        _ => "\u2022",
    };

    public static string NameOf(SessionInfo session)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        if (session.ZoneId == 0)
        {
            return loc["Sessions.UnknownMap"];
        }

        return Aion2Zones.NameOf(session.ZoneId, loc.Language)
            ?? string.Format(loc[session.Kind == SessionKind.Instance ? "Sessions.UnknownInstance" : "Sessions.UnknownZone"], session.ZoneId);
    }

    /// <summary>"14:05 - 14:20", or "14:20 - now" while the player is still there. A stay from an earlier day carries its date.</summary>
    public static string TimeSpanOf(SessionInfo session)
    {
        DateTime start = session.Start;
        string from = start.Date == DateTime.Today ? start.ToString("HH:mm") : start.ToString("dd.MM. HH:mm");
        string to = session.End is DateTime end ? end.ToString("HH:mm") : LocalizationManager.Instance["Sessions.Now"];
        return $"{from} – {to}";
    }
}

/// <summary>Which upload entries the App menu shows. A part of a control template has no field in the code-behind, so the window
/// keeps these flags in AppMenu.Tag and the template binds to them.</summary>
public sealed class UploadMenuFlags : INotifyPropertyChanged
{
    private bool _showBoss;
    private bool _showRun;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool ShowBoss
    {
        get => _showBoss;
        set => Set(ref _showBoss, value, nameof(ShowBoss));
    }

    public bool ShowRun
    {
        get => _showRun;
        set => Set(ref _showRun, value, nameof(ShowRun));
    }

    public bool ShowAny => _showBoss || _showRun;

    private void Set(ref bool field, bool value, string name)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowAny)));
    }
}
