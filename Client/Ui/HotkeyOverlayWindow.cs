using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// The hotkey overlay: a movable, transparent list of the hotkeys the player picked in the settings (name, then the key combination).
/// Placed, scaled and locked like the pet map's list (<see cref="MovableOverlay"/>); MainWindow shows it only while the game is in front.
/// </summary>
public sealed class HotkeyOverlayWindow : MovableOverlay
{
    private readonly StackPanel _rows = new() { MinWidth = 200, MinHeight = 40 };
    private string _shown = "";

    public HotkeyOverlayWindow() : base("Aion DPS Hotkeys", new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Left + 40, 120, LoadSize, SaveSize)
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
        return (s.HotkeyOverlayWidth, s.HotkeyOverlayHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.HotkeyOverlayWidth = width;
        s.HotkeyOverlayHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.HotkeyOverlayLeft, s.HotkeyOverlayTop, s.HotkeyOverlayScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.HotkeyOverlayLeft = left;
        s.HotkeyOverlayTop = top;
        s.HotkeyOverlayScale = scale;
        s.Save();
    }

    /// <summary>The hotkeys the settings list, as (name, key combination); only the ones whose switch is on and that have a key.</summary>
    public static List<(string Name, string Keys)> RowsOf(MeterSettings s)
    {
        var loc = LocalizationManager.Instance;
        var all = new (string Id, string NameKey, string Keys)[]
        {
            ("HideUi", "Settings.Hotkey.HideUi", s.HotkeyHideUi),
            ("Pause", "Settings.Hotkey.Pause", s.HotkeyPause),
            ("CopyDamage", "Settings.Hotkey.CopyDamage", s.HotkeyCopyDamage),
            ("Clear", "Settings.Hotkey.Clear", s.HotkeyClear),
            ("UploadBoss", "Settings.Hotkey.UploadBoss", s.HotkeyUploadBoss),
            ("Mode", "Settings.Hotkey.Mode", s.HotkeyMode),
            ("Timetable", "Settings.Hotkey.Timetable", s.HotkeyTimetable),
            ("PetMap", "Settings.Hotkey.PetMap", s.HotkeyPetMap),
        };
        return all.Where(h => !s.HotkeyOverlayHidden.Contains(h.Id))
            .Select(h => (Name: loc[h.NameKey], Keys: HotkeyBinding.Parse(h.Keys).ToString()))
            .Where(h => h.Keys.Length > 0)
            .ToList();
    }

    public void Render(IReadOnlyList<(string Name, string Keys)> rows)
    {
        string key = string.Join("\n", rows.Select(r => r.Name + "|" + r.Keys));
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        _rows.Children.Clear();
        foreach (var (name, keys) in rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var n = new TextBlock { Text = name, FontWeight = FontWeights.Bold, FontSize = 12.5, Foreground = Brushes.White, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
            var k = new TextBlock { Text = keys, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x7A)), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(k, 1);
            grid.Children.Add(n);
            grid.Children.Add(k);
            _rows.Children.Add(grid);
        }
    }
}
