using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// The crafting overlay: the materials the marked recipes still lack, the ones that lack most first, each with what the player has of what is needed ("3 / 50").
/// Placed, scaled and locked like the other overlays (<see cref="MovableOverlay"/>); <see cref="CraftOverlayController"/> shows it only while the game is in front
/// (or while it is unlocked, so it can be placed).
/// </summary>
public sealed class CraftOverlayWindow : MovableOverlay
{
    private readonly StackPanel _rows = new() { MinWidth = 250, MinHeight = 30 };
    private string _shown = "";

    public CraftOverlayWindow() : base(OverlayPlaces.Craft, new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Left + 60, 420, LoadSize, SaveSize, OverlayEdgeMode.Width)
    {
        Body = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x0E, 0x18, 0x20)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x29, 0x45, 0x57)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), Child = _rows,
        };
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.CraftOverlayWidth, s.CraftOverlayHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.CraftOverlayWidth = width;
        s.CraftOverlayHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.CraftOverlayLeft, s.CraftOverlayTop, s.CraftOverlayScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.CraftOverlayLeft = left;
        s.CraftOverlayTop = top;
        s.CraftOverlayScale = scale;
        s.Save();
    }

    public bool HasContent => _rows.Children.Count > 0;

    /// <summary>The lines: name, colour, "have / need"; a last line (text only) sums up what does not fit.</summary>
    public void Render(string caption, IReadOnlyList<(string Name, Color Color, string Count)> lines, string? more)
    {
        string key = caption + "\n" + string.Join("\n", lines.Select(l => l.Name + l.Color + l.Count)) + "\n" + more;
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        _rows.Children.Clear();
        if (lines.Count == 0)
        {
            return;
        }

        _rows.Children.Add(new TextBlock { Text = caption, FontWeight = FontWeights.Bold, FontSize = 12.5, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 3) });
        foreach (var (name, color, count) in lines)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = name, FontSize = 12, Foreground = new SolidColorBrush(color), TextTrimming = TextTrimming.CharacterEllipsis });
            var right = new TextBlock { Text = count, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x9D, 0xB3, 0xC2)), Margin = new Thickness(16, 0, 0, 0), TextAlignment = TextAlignment.Right };
            Grid.SetColumn(right, 1);
            row.Children.Add(right);
            _rows.Children.Add(row);
        }

        if (more is not null)
        {
            _rows.Children.Add(new TextBlock { Text = more, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0x87, 0x97)), Margin = new Thickness(0, 1, 0, 0) });
        }
    }
}

/// <summary>Runs the crafting overlay: twice a second it reads the settings, the plan and the stock, and shows the overlay only while the game is in front (or while it is unlocked so it can be placed).</summary>
public sealed class CraftOverlayController : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Func<Aion2EntityDirectory?> _directory;
    private CraftOverlayWindow? _overlay;
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;

    public CraftOverlayController(Func<Aion2EntityDirectory?> directory)
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

    /// <summary>The lines of the overlay for a plan (also used to draw the sample picture of the tool).</summary>
    public static (string Caption, List<(string Name, Color Color, string Count)> Lines, string? More) OverlayLines(IReadOnlyList<CraftResult> results, MeterSettings settings, bool sample)
    {
        var loc = LocalizationManager.Instance;
        List<CraftLine> missing = CraftCalc.Total(results).Where(l => l.Missing > 0).ToList();
        int max = Math.Clamp(settings.CraftOverlayLines, 3, 30);
        var lines = missing.Take(max).Select(l => (CraftText.ItemName(l.Item), CraftText.GradeColor(l.Item), CraftText.HaveOfNeed(l))).ToList();
        string? more = missing.Count > max ? string.Format(loc["Craft.More"], missing.Count - max) : null;
        if (lines.Count == 0 && sample)
        {
            Color grade = CharacterWindow.GradeColors[2];
            lines = new() { (loc["Craft.Sample.1"], CharacterWindow.GradeColors[1], "3 / 50"), (loc["Craft.Sample.2"], grade, "0 / 20"), (loc["Craft.Sample.3"], CharacterWindow.GradeColors[3], "0 / 6") };
        }

        return (loc["Craft.Missing"], lines, more);
    }

    private void Tick()
    {
        if ((DateTime.UtcNow - _settingsAt).TotalSeconds >= 1)
        {
            _settings = MeterSettings.Load();
            _settingsAt = DateTime.UtcNow;
        }

        if (!_settings.ShowCraftOverlay)
        {
            _overlay?.ShowOverlay(false);
            return;
        }

        bool locked = _settings.CraftOverlayLocked;
        _overlay ??= new CraftOverlayWindow();
        _overlay.ApplyLockedIfChanged(locked);
        var (caption, lines, more) = OverlayLines(CraftText.Results(_directory(), _settings), _settings, sample: !locked);
        _overlay.Render(caption, lines, more);
        if (locked && (GameWindow.ForegroundClientArea() is null || !_overlay.HasContent))
        {
            _overlay.ShowOverlay(false);
            return;
        }

        _overlay.ShowOverlay(true);
    }
}
