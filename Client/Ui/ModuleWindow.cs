using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
    private readonly Button _pop = new();
    private Action? _popOut;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _syncing;
    private readonly List<RadioButton> _tabButtons = new();

    public ModulePage(string title, UIElement body, Func<MeterSettings, bool> getOn, Action<MeterSettings, bool> setOn, Func<MeterSettings, string>? getKey,
        Action openHotkeys, Action? openSettings, Action<MeterSettings> applied, Action? tick = null, UIElement? left = null, UIElement? config = null, IReadOnlyList<(string Title, UIElement Content)>? tabList = null)
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
        _key.Margin = new Thickness(0, 0, 8, 0);
        _key.Padding = new Thickness(10, 2, 10, 2);
        _key.MinHeight = 26;
        _key.ToolTip = loc["Settings.Hotkey.ChipTip"];
        _key.Click += (_, _) => openHotkeys();
        var cogLabel = new StackPanel { Orientation = Orientation.Horizontal };
        var cogIcon = new TextBlock { Text = "\uE713", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        var cogText = new TextBlock { Text = loc["Settings.Configuration"], Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        cogIcon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        cogText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        cogLabel.Children.Add(cogIcon);
        cogLabel.Children.Add(cogText);
        var cog = new Button
        {
            Content = cogLabel, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 2, 10, 2), MinHeight = 26, ToolTip = loc["Settings.WindowTitle"],
        };
        cog.SetResourceReference(Control.BorderBrushProperty, "Brush.Accent");
        cog.Click += (_, _) => openSettings?.Invoke();
        right.Children.Add(_key);
        if (openSettings is not null && config is null)
        {
            right.Children.Add(cog);
        }

        // the overlay switch is gone from the page: the overlay designer (Ctrl+Alt+F) adds, hides and places the overlays
        // right of the overlay switch: the tool in a window of its own, to put it anywhere (only where the owner of the page has set PopOut)
        var popIcon = new TextBlock { Text = "\uE8A7", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        popIcon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        _pop.Content = popIcon;
        _pop.Margin = new Thickness(12, 0, 0, 0);
        _pop.Padding = new Thickness(8, 2, 8, 2);
        _pop.MinHeight = 26;
        _pop.ToolTip = loc["Tools.PopOut.Tip"];
        _pop.Visibility = Visibility.Collapsed;
        _pop.SetResourceReference(Control.BorderBrushProperty, "Brush.Accent");
        _pop.Click += (_, _) => PopOut?.Invoke();
        right.Children.Add(_pop);
        var name = new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        header.Children.Add(right);
        header.Children.Add(left ?? name);

        SetDock(header, Dock.Top);
        Children.Add(header);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
        // tabs under the title: the display and the configuration, or the list the owner gave (the pet map has four)
        var pages = tabList ?? (config is null ? null : new[] { (loc["Hub.Tab.View"], body), (loc["Hub.Tab.Config"], config) });
        if (pages is not null)
        {
            var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            var border = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = tabs };
            border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            bool first = true;
            foreach (var (title2, content) in pages)
            {
                UIElement shownContent = content;
                bool isView = first && ReferenceEquals(content, body);
                var tab = new RadioButton { GroupName = "ModuleTab" + GetHashCode(), IsChecked = first, Content = title2, Cursor = System.Windows.Input.Cursors.Hand, Template = TabTemplate() };
                tab.Checked += (_, _) =>
                {
                    // the configuration wraps its texts to the width of the page; the view keeps the scroll bars it always had
                    scroll.HorizontalScrollBarVisibility = isView ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
                    scroll.Content = shownContent;
                };
                _tabButtons.Add(tab);
                tabs.Children.Add(tab);
                first = false;
            }

            SetDock(border, Dock.Top);
            Children.Add(border);
        }

        Children.Add(scroll);

        Sync();
        _timer.Tick += (_, _) =>
        {
            Sync();
            tick?.Invoke();
        };
        _timer.Start();
    }

    /// <summary>For the test picture: chooses the tab with this number (0 = display, 1 = configuration).</summary>
    internal void SelectTabForRender(int index)
    {
        if (index >= 0 && index < _tabButtons.Count)
        {
            _tabButtons[index].IsChecked = true;
        }
    }

    private static ControlTemplate TabTemplate()
    {
        var bd = new FrameworkElementFactory(typeof(Border)) { Name = "Bd" };
        bd.SetValue(Border.PaddingProperty, new Thickness(18, 8, 18, 8));
        bd.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 3));
        bd.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(TextElement.FontWeightProperty, FontWeights.SemiBold);
        bd.AppendChild(content);
        var template = new ControlTemplate(typeof(RadioButton)) { VisualTree = bd };
        var on = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true };
        on.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Brush.Accent"), "Bd"));
        on.Setters.Add(new Setter(TextElement.ForegroundProperty, new DynamicResourceExtension("Brush.Accent")));
        template.Triggers.Add(on);
        var off = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = false };
        off.Setters.Add(new Setter(TextElement.ForegroundProperty, new DynamicResourceExtension("Brush.TextMuted")));
        template.Triggers.Add(off);
        return template;
    }

    /// <summary>Shows the tool in a window of its own; the button for it (right of the overlay switch) is there only while this is set.</summary>
    public Action? PopOut
    {
        get => _popOut;
        set
        {
            _popOut = value;
            _pop.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
        }
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

    /// <summary>A grey note under a tool's header.</summary>
    public static TextBlock Hint(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        return block;
    }

    /// <summary>A caption with a switch at its end ("show as overlay"): the choice is kept the moment it is flipped, no Save needed.</summary>
    public static (DockPanel Row, CheckBox Switch) SwitchCaption(string text, string switchText)
    {
        var row = new DockPanel { Margin = new Thickness(0, 10, 0, 4) };
        var box = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
        if (Application.Current?.TryFindResource("SettingsSwitch") is Style style)
        {
            box.Style = style;
        }

        var label = new TextBlock { Text = switchText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), FontSize = 12 };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(label);
        right.Children.Add(box);
        DockPanel.SetDock(right, Dock.Right);
        var caption = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        row.Children.Add(right);
        row.Children.Add(caption);
        return (row, box);
    }
}
