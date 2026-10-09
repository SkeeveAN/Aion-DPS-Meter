using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>How a boss's state reads: "now", or the clock time it is back with the time left in hours, minutes and seconds. Shared by the settings page and the overlay.</summary>
public static class WorldBossText
{
    public static string Duration(TimeSpan span)
    {
        int seconds = (int)Math.Ceiling(Math.Max(0, span.TotalSeconds));
        int h = seconds / 3600, m = seconds % 3600 / 60, s = seconds % 60;
        return h > 0 ? $"{h} h {m:00} min {s:00} s" : m > 0 ? $"{m} min {s:00} s" : $"{s} s";
    }

    /// <summary>The clock time of a moment; with the weekday when it is not today.</summary>
    public static string Clock(DateTime at, DateTime now)
    {
        if (at.Date == now.Date)
        {
            return at.ToString("HH:mm:ss");
        }

        System.Globalization.CultureInfo culture;
        try
        {
            culture = System.Globalization.CultureInfo.GetCultureInfo(LocalizationManager.Instance.Language);
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            culture = System.Globalization.CultureInfo.InvariantCulture;
        }

        return $"{at.ToString("ddd", culture)} {at:HH:mm}";
    }

    public static string Of(BossStatus status, DateTime now)
    {
        var loc = LocalizationManager.Instance;
        switch (status.Phase)
        {
            case BossPhase.Now:
                return loc["Boss.Now"];
            case BossPhase.Respawn when status.At is { } back:
                return $"{Clock(back, now)} · {string.Format(loc["Boss.In"], Duration(back - now))}";
            case BossPhase.Fixed when status.At is { } next:
                return $"{Clock(next, now)} · {string.Format(loc["Boss.In"], Duration(next - now))}";
            default:
                return loc["Boss.Unknown"];
        }
    }
}

/// <summary>
/// The world boss overlay: the bosses switched on in the World boss settings, each with when it is there (now, or the time left). Placed, scaled and
/// locked like the pet map's list (<see cref="MovableOverlay"/>); <see cref="WorldBossController"/> shows it only while the game is in front.
/// </summary>
public sealed class WorldBossOverlayWindow : MovableOverlay
{
    private readonly StackPanel _rows = new() { MinWidth = 260, MinHeight = 40 };

    public WorldBossOverlayWindow() : base("Aion DPS World Boss", new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Left + 40, 360, LoadSize, SaveSize)
    {
        Body = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x0E, 0x18, 0x20)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x29, 0x45, 0x57)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), Child = _rows,
        };
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.BossOverlayWidth, s.BossOverlayHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.BossOverlayWidth = width;
        s.BossOverlayHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.BossOverlayLeft, s.BossOverlayTop, s.BossOverlayScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.BossOverlayLeft = left;
        s.BossOverlayTop = top;
        s.BossOverlayScale = scale;
        s.Save();
    }

    /// <summary>Rows: the boss, its state text, and whether it is there now.</summary>
    public void Render(IReadOnlyList<(string Name, string Text, bool Now)> rows)
    {
        _rows.Children.Clear();
        foreach (var (name, text, now) in rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var n = new TextBlock { Text = name, FontWeight = FontWeights.Bold, FontSize = 12.5, Foreground = Brushes.White, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            var t = new TextBlock
            {
                Text = text, FontSize = 11.5, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(now ? Color.FromRgb(0x5B, 0xD0, 0x7A) : Color.FromRgb(0xFF, 0xD2, 0x7A)),
                FontWeight = now ? FontWeights.Bold : FontWeights.Normal,
            };
            Grid.SetColumn(t, 1);
            grid.Children.Add(n);
            grid.Children.Add(t);
            _rows.Children.Add(grid);
        }
    }
}

/// <summary>
/// Runs the world boss overlay and the boss sounds: once a second it reads the settings, shows the overlay (only while the game is in front, or
/// while the overlays are unlocked so it can be placed) and plays the sound of a watched boss that has just come back.
/// </summary>
public sealed class WorldBossController : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<int, BossPhase> _phases = new();
    private WorldBossOverlayWindow? _overlay;
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;

    public WorldBossController()
    {
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
        }

        var settings = _settings;
        DateTime now = DateTime.Now;
        string language = LocalizationManager.Instance.Language;
        var watched = new List<(FieldBossMap Map, FieldBoss Boss, BossAlertSetting Alert, BossStatus Status)>();
        foreach (var map in Aion2FieldBosses.Maps)
        {
            foreach (var boss in map.Bosses)
            {
                if (settings.BossAlerts.TryGetValue(boss.Npc.ToString(), out var alert) && alert.Enabled)
                {
                    watched.Add((map, boss, alert, Aion2FieldBosses.StatusOf(boss, now)));
                }
            }
        }

        // the sound: a watched boss that was away and is back now
        foreach (var (_, boss, alert, status) in watched)
        {
            bool wasAway = _phases.TryGetValue(boss.Npc, out var before) && before is BossPhase.Respawn or BossPhase.Fixed;
            _phases[boss.Npc] = status.Phase;
            if (wasAway && status.Phase == BossPhase.Now && settings.BossNotify && alert.Sound.Length > 0 && alert.Volume > 0)
            {
                NotifySounds.Play(alert.Sound, alert.Volume);
            }
        }

        if (!settings.ShowBossOverlay)
        {
            _overlay?.ShowOverlay(false);
            return;
        }

        bool locked = settings.BossOverlayLocked;
        bool inGame = GameWindow.ForegroundClientArea() is not null;
        _overlay ??= new WorldBossOverlayWindow();
        _overlay.ApplyLockedIfChanged(locked);
        if (locked && (!inGame || watched.Count == 0))
        {
            _overlay.ShowOverlay(false);
            return;
        }

        // the bosses that are there first, then the next to come
        var rows = watched
            .OrderBy(w => w.Status.Phase == BossPhase.Now ? 0 : w.Status.Phase == BossPhase.Unknown ? 2 : 1)
            .ThenBy(w => w.Status.At ?? DateTime.MaxValue)
            .Select(w => (Aion2FieldBosses.DisplayName(w.Map, w.Boss, language), WorldBossText.Of(w.Status, now), w.Status.Phase == BossPhase.Now))
            .ToList();
        if (rows.Count == 0)
        {
            rows.Add((LocalizationManager.Instance["Boss.OverlayEmpty"], "", false));
        }

        _overlay.Render(rows);
        _overlay.ShowOverlay(true);
    }
}
