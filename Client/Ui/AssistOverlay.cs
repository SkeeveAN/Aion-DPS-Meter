using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// The assist overlay: the running cooldowns and the effects on the own character (<see cref="AssistPanel"/>), placed, scaled and locked like the other
/// overlays (<see cref="MovableOverlay"/>); <see cref="AssistController"/> shows it only while the game is in front.
/// </summary>
public sealed class AssistOverlayWindow : MovableOverlay
{
    public AssistPanel Panel { get; } = new() { MinWidth = 64, MinHeight = 64 };

    public AssistOverlayWindow() : base("Aion DPS Assist", new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Left + 40, 520, LoadSize, SaveSize, OverlayEdgeMode.Crop)
    {
        Body = new Border
        {
            Background = Brushes.Transparent, Padding = new Thickness(2), Child = Panel,
        };
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.AssistOverlayWidth, s.AssistOverlayHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.AssistOverlayWidth = width;
        s.AssistOverlayHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.AssistOverlayLeft, s.AssistOverlayTop, s.AssistOverlayScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.AssistOverlayLeft = left;
        s.AssistOverlayTop = top;
        s.AssistOverlayScale = scale;
        s.Save();
    }
}

/// <summary>Runs the assist overlay: four times a second it refreshes it, and shows it only while the game is in front (or while it is unlocked, so it can be placed).</summary>
public sealed class AssistController : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Func<Aion2EntityDirectory?> _directory;
    private AssistOverlayWindow? _overlay;
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;
    private HashSet<int> _hidden = new();

    public AssistController(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void RaiseToFront() => _overlay?.RaiseToFront();

    public void Dispose()
    {
        _timer.Stop();
        _overlay?.Close();
    }

    private void Tick()
    {
        if ((DateTime.UtcNow - _settingsAt).TotalSeconds >= 1)
        {
            _settings = MeterSettings.Load();
            _settingsAt = DateTime.UtcNow;
            _hidden = _settings.AssistOverlayHidden.ToHashSet();
        }

        if (!_settings.ShowAssistOverlay)
        {
            _overlay?.ShowOverlay(false);
            return;
        }

        bool locked = _settings.AssistOverlayLocked;
        _overlay ??= new AssistOverlayWindow();
        _overlay.ApplyLockedIfChanged(locked);
        _overlay.Panel.Hidden = _hidden;
        _overlay.Panel.SortByTime = _settings.AssistSortByTime;
        _overlay.Panel.Vertical = _settings.AssistVertical;
        _overlay.Panel.FontColor = _settings.OverlayFontColors.GetValueOrDefault(OverlayPlaces.Assist);
        _overlay.Panel.Order = _settings.AssistOrder;
        _overlay.Panel.Seen = AssistKeys.Remember;
        bool inGame = GameWindow.ForegroundClientArea() is not null;
        if (locked && !inGame)
        {
            // Hidden anyway while the game is not in front: the whole panel is not rebuilt for nobody to see (four times a second).
            _overlay.ShowOverlay(false);
            return;
        }

        _overlay.Panel.Refresh(_directory());
        if (locked && !_overlay.Panel.HasContent)
        {
            _overlay.ShowOverlay(false);
            return;
        }

        _overlay.ShowOverlay(true);
    }
}
