using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AionDPS.Ui;

/// <summary>
/// The page of a tool in the tools window: the tool's name, its overlay switch (the master of its overlays), its hotkey (a click opens the hotkey page of the settings)
/// and a cog for its settings, above a live view of what the tool shows. The overlay can be on at the same time; the page and the overlay do not depend on each other.
/// </summary>
public sealed class ModulePage : DockPanel
{
    private readonly Func<MeterSettings, bool> _getOn;
    private readonly Func<MeterSettings, string>? _getKey;
    private readonly CheckBox _master = new();
    private readonly Button _key = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _syncing;

    public ModulePage(string title, UIElement body, Func<MeterSettings, bool> getOn, Action<MeterSettings, bool> setOn, Func<MeterSettings, string>? getKey,
        Action openHotkeys, Action openSettings, Action<MeterSettings> applied, Action? tick = null)
    {
        _getOn = getOn;
        _getKey = getKey;
        var loc = LocalizationManager.Instance;
        Margin = new Thickness(14, 10, 14, 14);

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(right, Dock.Right);
        var masterText = new TextBlock { Text = loc["Settings.Overlay.Master"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        masterText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        _master.VerticalAlignment = VerticalAlignment.Center;
        if (Application.Current?.TryFindResource("SettingsSwitch") is Style style)
        {
            _master.Style = style;
        }

        _master.Click += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }

            MeterSettings s = MeterSettings.Load();
            setOn(s, _master.IsChecked == true);
            s.Save();
            applied(s);
        };
        _key.Margin = new Thickness(12, 0, 0, 0);
        _key.Padding = new Thickness(10, 2, 10, 2);
        _key.MinHeight = 26;
        _key.ToolTip = loc["Settings.Hotkey.ChipTip"];
        _key.Click += (_, _) => openHotkeys();
        var cog = new Button
        {
            Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14 },
            Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2), MinHeight = 26, ToolTip = loc["Settings.WindowTitle"],
        };
        cog.Click += (_, _) => openSettings();
        right.Children.Add(masterText);
        right.Children.Add(_master);
        right.Children.Add(_key);
        right.Children.Add(cog);
        var name = new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        header.Children.Add(right);
        header.Children.Add(name);

        SetDock(header, Dock.Top);
        Children.Add(header);
        Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body });

        Sync();
        _timer.Tick += (_, _) =>
        {
            Sync();
            tick?.Invoke();
        };
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    /// <summary>The switch and the hotkey follow the settings (a hotkey may have flipped the switch).</summary>
    private void Sync()
    {
        MeterSettings s = MeterSettings.Load();
        _syncing = true;
        _master.IsChecked = _getOn(s);
        _syncing = false;
        string key = _getKey is null ? "" : HotkeyBinding.Parse(_getKey(s)).ToString();
        _key.Visibility = _getKey is null ? Visibility.Collapsed : Visibility.Visible;
        _key.Content = key.Length > 0 ? key : LocalizationManager.Instance["Settings.Hotkey.NoKey"];
    }

    /// <summary>A small caption above one view of a tool that has several (the three overlays of the boss fight).</summary>
    public static TextBlock Caption(string text)
    {
        var block = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        return block;
    }
}
