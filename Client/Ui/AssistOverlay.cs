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
    public AssistPanel Panel { get; } = new(true) { MinWidth = 220, MinHeight = 40 };

    public AssistOverlayWindow() : base("Aion DPS Assist", new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Left + 40, 520, LoadSize, SaveSize)
    {
        Body = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x0E, 0x18, 0x20)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x29, 0x45, 0x57)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), Child = Panel,
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
        _overlay.Panel.Refresh(_directory());
        bool inGame = GameWindow.ForegroundClientArea() is not null;
        if (locked && (!inGame || !_overlay.Panel.HasContent))
        {
            _overlay.ShowOverlay(false);
            return;
        }

        _overlay.ShowOverlay(true);
    }
}
