using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Combat;
using AionDPS.Combat.Sources;
using AionDPS.History;

namespace AionDPS.Ui;

/// <summary>What the dummy test overlay shows at one moment.</summary>
public sealed record DummyOverlayData(string Header, string Clock, Color ClockColor, string Sub, string Stats, double Fraction = 1.0);

/// <summary>
/// The dummy test overlay: the time of the run, counting down as minutes:seconds:hundredths, and after it "Finished". Placed, scaled and locked like the network
/// overlay (<see cref="MovableOverlay"/>); <see cref="DummyTestController"/> shows it only while a test is waiting, running or just over (or while it is unlocked).
/// </summary>
public sealed class DummyTestOverlayWindow : MovableOverlay
{
    private readonly TextBlock _header = new() { FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x9D, 0xB3, 0xC2)), HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _clock = new() { FontSize = 44, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas"), HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _sub = new() { FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xE6, 0xEC)), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _stats = new() { FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };

    private readonly Border _barFill = new() { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Height = 6 };
    private readonly Border _bar = new() { Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 2, 0, 4), Background = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)) };
    private double _fraction = 1;

    public DummyTestOverlayWindow() : base("Aion DPS Dummy Test", new Border(), Load, Save, SystemParameters.WorkArea.Left + 40, 360, LoadSize, SaveSize, OverlayEdgeMode.Uniform)
    {
        var stack = new StackPanel { MinWidth = 250 };
        stack.Children.Add(_header);
        stack.Children.Add(_clock);
        // the time left as a bar that slowly runs empty
        _bar.Child = _barFill;
        _bar.SizeChanged += (_, _) => _barFill.Width = Math.Max(0, _bar.ActualWidth * _fraction);
        stack.Children.Add(_bar);
        stack.Children.Add(_sub);
        stack.Children.Add(_stats);
        Body = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x0E, 0x18, 0x20)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x29, 0x45, 0x57)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 6, 14, 8), Child = stack,
        };
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.DummyOverlayWidth, s.DummyOverlayHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.DummyOverlayWidth = width;
        s.DummyOverlayHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.DummyOverlayLeft, s.DummyOverlayTop, s.DummyOverlayScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.DummyOverlayLeft = left;
        s.DummyOverlayTop = top;
        s.DummyOverlayScale = scale;
        s.Save();
    }

    public void Render(DummyOverlayData data)
    {
        _header.Text = data.Header;
        _clock.Text = data.Clock;
        _clock.Foreground = new SolidColorBrush(data.ClockColor);
        _barFill.Background = new SolidColorBrush(data.ClockColor);
        _fraction = Math.Clamp(data.Fraction, 0, 1);
        _barFill.Width = Math.Max(0, _bar.ActualWidth * _fraction);
        _sub.Text = data.Sub;
        _sub.Visibility = data.Sub.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _stats.Text = data.Stats;
        _stats.Visibility = data.Stats.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// <summary>
/// Runs the dummy test: owns the model (<see cref="DummyTest"/>), feeds it the capture's damage and the player's skills and effects, keeps its runs in a file
/// and shows the overlay. The window of the test (<see cref="DummyTestWindow"/>) reads and starts it through here.
/// </summary>
public sealed class DummyTestController : IDisposable
{
    private static readonly Color Accent = Color.FromRgb(0xFF, 0xBA, 0x42);
    private static readonly Color Calm = Color.FromRgb(0xF2, 0xF5, 0xF7);
    private static readonly Color Done = Color.FromRgb(0x6F, 0xD6, 0x9B);

    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Func<Aion2EntityDirectory?> _directory;
    private DummyTestOverlayWindow? _overlay;
    private MeterSettings _settings = MeterSettings.Load();
    private int _ticks;
    private IEntityDirectory? _entities;

    public DummyTest Model { get; } = new();

    public DummyTestController(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        Model.Load(DummyStore.Load(DummyStore.DefaultPath));
        Model.Capture = CaptureRun;
        Model.Changed += () => TrySave();
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void RaiseToFront() => _overlay?.RaiseToFront();

    public void Dispose()
    {
        _timer.Stop();
        _overlay?.Close();
    }

    /// <summary>The meter's poll: the player's own damage starts the clock and fills the run. <paramref name="entities"/> says who the player is.</summary>
    public void Feed(CombatBatch batch, IEntityDirectory? entities)
    {
        _entities = entities;
        if (Model.State is DummyState.Armed or DummyState.Running or DummyState.Finished && batch.Damage.Count > 0 && entities is not null)
        {
            Model.Feed(batch.Damage, entities.IsLocalPlayer);
        }
    }

    /// <summary>Starts a test run (see <see cref="DummyTest.Arm"/>).</summary>
    public void Start(TimeSpan planned, string note)
    {
        Model.Arm(planned, note, DateTime.Now);
        var settings = MeterSettings.Load();
        settings.DummyTestSeconds = (int)planned.TotalSeconds;
        settings.Save();
    }

    private void TrySave()
    {
        try
        {
            DummyStore.Save(DummyStore.DefaultPath, Model.Runs);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // the runs stay in memory; the next change tries again
        }
    }

    /// <summary>Reads what the capture logged for the player while the run lasted (the skills used and the effects on the player).</summary>
    private void CaptureRun(DummyRun run)
    {
        if (_directory() is not { } dir)
        {
            return;
        }

        int player = run.PlayerId;
        FightExtras extras = dir.Combat.Slice(run.Start, run.End, id => dir.NameFor(id) ?? dir.MonsterNameFor(id));
        run.Uses.AddRange(extras.Uses.Where(u => u.Actor == player));
        run.Buffs.AddRange(extras.Buffs.Where(b => b.Target == player));
        foreach (var (id, name) in extras.Names)
        {
            if (id == player)
            {
                run.Names[id] = name;
            }
        }
    }

    private void Tick()
    {
        DateTime now = DateTime.Now;
        if (_ticks++ % 10 == 0)
        {
            _settings = MeterSettings.Load();
        }

        // a skill or an effect starts the clock as well as a hit does (the cast comes before the hit); for a few seconds into the run it still moves the start back
        if (Model.State == DummyState.Armed || Model.State == DummyState.Running && Model.Current is { } run && now - run.Start < TimeSpan.FromSeconds(3))
        {
            if (_directory() is { } dir && dir.LocalPlayerId is >= 0 and var player && dir.Combat.FirstActivity(player, Model.ArmedAt) is DateTime at)
            {
                Model.NoteActivity(at, player);
            }
        }

        Model.Tick(now);
        UpdateOverlay(now);
    }

    private void UpdateOverlay(DateTime now)
    {
        bool locked = _settings.DummyOverlayLocked;
        bool wanted = _settings.ShowDummyOverlay && (Model.State != DummyState.Idle || !locked);
        if (!wanted)
        {
            _overlay?.ShowOverlay(false);
            return;
        }

        _overlay ??= new DummyTestOverlayWindow();
        _overlay.ApplyLockedIfChanged(locked);
        if (locked && GameWindow.ForegroundClientArea() is null)
        {
            _overlay.ShowOverlay(false);
            return;
        }

        _overlay.Render(DataFor(now));
        _overlay.ShowOverlay(true);
    }

    /// <summary>The countdown as the overlay writes it: minutes, seconds and hundredths.</summary>
    public static string Clock(TimeSpan left) => $"{(int)left.TotalMinutes}:{left.Seconds:00}:{left.Milliseconds / 10:00}";

    private DummyOverlayData DataFor(DateTime now)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string header = loc["Dummy.Title"] + (Model.Current is { } current && Model.State != DummyState.Armed ? $" · {string.Format(loc["Dummy.RunNumber"], current.Number)}" : "");
        switch (Model.State)
        {
            case DummyState.Armed:
                return new DummyOverlayData(header, Clock(Model.Planned), Calm, loc["Dummy.Waiting"], "");
            case DummyState.Running:
                return new DummyOverlayData(header, Clock(Model.Remaining(now)), Accent, loc["Dummy.RunningHint"], Stats(Model.Current, now), Model.Planned.TotalSeconds > 0 ? Model.Remaining(now).TotalSeconds / Model.Planned.TotalSeconds : 0);
            case DummyState.Finished:
                return new DummyOverlayData(header, loc["Dummy.Finished"], Done, loc["Dummy.FinishedHint"], Stats(Model.Current, now), 0);
            default:
                return new DummyOverlayData(loc["Dummy.Title"], Clock(TimeSpan.FromSeconds(_settings.DummyTestSeconds)), Calm, loc["Dummy.OverlaySample"], "");
        }
    }

    private static string Stats(DummyRun? run, DateTime now)
    {
        if (run is null || run.Events.Count == 0)
        {
            return "";
        }

        double seconds = Math.Max(1, Math.Min((now - run.Start).TotalSeconds, run.Length.TotalSeconds));
        long total = run.Events.Sum(e => e.Amount);
        LocalizationManager loc = LocalizationManager.Instance;
        return $"{loc["Dummy.Dps"]} {Compact(total / seconds)}   ·   {loc["Dummy.Damage"]} {Compact(total)}";
    }

    /// <summary>12 345 678 as 12.3M, 48 210 as 48.2k.</summary>
    public static string Compact(double value) => value >= 1_000_000 ? (value / 1_000_000).ToString("0.0#") + "M"
        : value >= 10_000 ? (value / 1000).ToString("0.0") + "k"
        : value.ToString("N0");
}
