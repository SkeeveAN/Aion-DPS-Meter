using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>How the loot tool names and orders what was picked up: the same lines in the overlay and in the tool's page.</summary>
public static class LootText
{
    public static string ItemName(int itemId)
    {
        string language = LocalizationManager.Instance.Language;
        return Aion2ItemNames.NameOf(itemId, language) ?? Aion2ItemCatalog.Find(itemId)?.Name ?? "#" + itemId;
    }

    public static Color GradeColor(int itemId)
    {
        int grade = Aion2ItemNames.GradeOf(itemId);
        if (grade == 0 && Aion2ItemCatalog.Find(itemId) is { } info)
        {
            grade = info.Grade;
        }

        return CharacterWindow.GradeColors.GetValueOrDefault(grade, CharacterWindow.GradeColors[1]);
    }

    public static bool IsCommon(int itemId) => Aion2ItemNames.GradeOf(itemId) is 0 or 1;

    public static string MapName(int zone)
    {
        var loc = LocalizationManager.Instance;
        return zone > 0 ? Aion2Zones.NameOf(zone, loc.Language) ?? loc["Loot.Map"] + " " + zone : loc["Loot.UnknownMap"];
    }

    /// <summary>Items of the same kind summed over the maps, the one picked up last first.</summary>
    public static List<LootItem> Merge(IEnumerable<LootMap> maps) =>
        maps.SelectMany(m => m.Items).GroupBy(i => i.ItemId)
            .Select(g => new LootItem { ItemId = g.Key, Count = g.Sum(i => i.Count), Picks = g.Sum(i => i.Picks), First = g.Min(i => i.First), Last = g.Max(i => i.Last) })
            .OrderByDescending(i => i.Last).ToList();

    /// <summary>What the overlay lists for the running session: the maps the settings ask for, the lines the player allows, a last line for the rest.</summary>
    public static (string Caption, List<(string Text, Color Color)> Lines) OverlayLines(LootSession? session, int currentZone, MeterSettings settings, bool sample)
    {
        var loc = LocalizationManager.Instance;
        if (session is null)
        {
            return sample ? Sample(settings) : (loc["Main.MenuTools.Loot"], new());
        }

        IEnumerable<LootMap> maps = settings.LootOverlayWholeSession ? session.Maps : session.Maps.Where(m => m.Zone == currentZone);
        List<LootItem> items = Merge(maps);
        if (settings.LootOverlayHideCommon)
        {
            items = items.Where(i => !IsCommon(i.ItemId)).ToList();
        }

        string caption = settings.LootOverlayWholeSession ? loc["Loot.Session"] : MapName(currentZone);
        int max = Math.Clamp(settings.LootOverlayLines, 3, 30);
        var lines = items.Take(max).Select(i => (i.Count > 1 ? $"{ItemName(i.ItemId)}  ×{i.Count:N0}" : ItemName(i.ItemId), GradeColor(i.ItemId))).ToList();
        if (items.Count > max)
        {
            lines.Add((string.Format(loc["Loot.More"], items.Count - max), Color.FromRgb(0x9D, 0xB3, 0xC2)));
        }

        return lines.Count == 0 && sample ? Sample(settings) : (caption, lines);
    }

    /// <summary>Lines as they look in a game session, to place the overlay in its real size while nothing was picked up yet.</summary>
    private static (string, List<(string, Color)>) Sample(MeterSettings settings)
    {
        var loc = LocalizationManager.Instance;
        var lines = new List<(string, Color)>();
        foreach ((string text, int grade) in new[] { (loc["Loot.Sample.1"] + "  ×3", 4), (loc["Loot.Sample.2"], 3), (loc["Loot.Sample.3"] + "  ×12", 2), (loc["Loot.Sample.4"] + "  ×25", 1) })
        {
            lines.Add((text, CharacterWindow.GradeColors[grade]));
        }

        return (settings.LootOverlayWholeSession ? loc["Loot.Session"] : loc["Loot.Sample.Map"], lines);
    }
}

/// <summary>
/// The loot overlay: the items picked up on the map the player is on (or in the whole session), the one picked up last on top. Placed, scaled and locked like the other
/// overlays (<see cref="MovableOverlay"/>); <see cref="LootOverlayController"/> shows it only while the game is in front (or while it is unlocked, so it can be placed).
/// </summary>
public sealed class LootOverlayWindow : MovableOverlay
{
    private readonly StackPanel _rows = new() { MinWidth = 230, MinHeight = 30 };

    public LootOverlayWindow() : base(OverlayPlaces.Loot, new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Left + 40, 360, LoadSize, SaveSize, OverlayEdgeMode.Width)
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
        return (s.LootOverlayWidth, s.LootOverlayHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.LootOverlayWidth = width;
        s.LootOverlayHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.LootOverlayLeft, s.LootOverlayTop, s.LootOverlayScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.LootOverlayLeft = left;
        s.LootOverlayTop = top;
        s.LootOverlayScale = scale;
        s.Save();
    }

    private string _shown = "";

    public bool HasContent => _rows.Children.Count > 0;

    public void Render(string caption, IReadOnlyList<(string Text, Color Color)> lines)
    {
        string key = caption + "\n" + string.Join("\n", lines.Select(l => l.Text + l.Color));
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

        _rows.Children.Add(new TextBlock
        {
            Text = caption, FontWeight = FontWeights.Bold, FontSize = 12.5, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 3), TextTrimming = TextTrimming.CharacterEllipsis,
        });
        foreach (var (text, color) in lines)
        {
            _rows.Children.Add(new TextBlock { Text = text, FontSize = 12, Foreground = new SolidColorBrush(color), Margin = new Thickness(0, 1, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis });
        }
    }
}

/// <summary>Runs the loot overlay: twice a second it reads the settings and the loot of the running session, and shows the overlay only while the game is in front
/// (or while it is unlocked so it can be placed).</summary>
public sealed class LootOverlayController : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private LootOverlayWindow? _overlay;
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;

    public LootOverlayController()
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

        if (!_settings.ShowLootOverlay)
        {
            _overlay?.ShowOverlay(false);
            return;
        }

        bool locked = _settings.LootOverlayLocked;
        _overlay ??= new LootOverlayWindow();
        _overlay.ApplyLockedIfChanged(locked);
        var (caption, lines) = LootText.OverlayLines(Aion2Loot.Current, Aion2Loot.CurrentZone, _settings, sample: !locked);
        _overlay.Render(caption, lines);
        if (locked && (GameWindow.ForegroundClientArea() is null || !_overlay.HasContent))
        {
            _overlay.ShowOverlay(false);
            return;
        }

        _overlay.ShowOverlay(true);
    }
}
