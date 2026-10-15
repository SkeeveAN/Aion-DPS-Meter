using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// The assist window (Tools menu): two switches (show the assist as an overlay in the game, lock that overlay) above the <see cref="AssistPanel"/> with the
/// local player's cooldowns and effects. Built in code, in the look of the other windows.
/// </summary>
public sealed class AssistWindow : Window
{
    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly AssistPanel _panel = new(false);
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public AssistWindow(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        LocalizationManager loc = LocalizationManager.Instance;
        Title = loc["Assist.Title"];
        Width = 420;
        Height = 640;
        MinWidth = 360;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        MeterSettings settings = MeterSettings.Load();
        var root = new DockPanel { Margin = new Thickness(12, 10, 12, 12) };
        var switches = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(switches, Dock.Top);
        switches.Children.Add(Switch(loc["Assist.ShowOverlay"], settings.ShowAssistOverlay, on => Change(s => s.ShowAssistOverlay = on)));
        switches.Children.Add(Switch(loc["Assist.LockOverlay"], settings.AssistOverlayLocked, on => Change(s => s.AssistOverlayLocked = on)));
        switches.Children.Add(Switch(loc["Assist.SortByTime"], settings.AssistSortByTime, on =>
        {
            _panel.SortByTime = on;
            Change(s => s.AssistSortByTime = on);
        }));
        _panel.SortByTime = settings.AssistSortByTime;
        root.Children.Add(switches);

        // which skills and effects the overlay shows: the boxes in the list below; unticked ones are kept in the settings
        _panel.Hidden = settings.AssistOverlayHidden.ToHashSet();
        _panel.ShowInOverlayChanged = (id, shown) => Change(s => { s.AssistOverlayHidden.Remove(id); if (!shown) { s.AssistOverlayHidden.Add(id); } });
        var pickBar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(pickBar, Dock.Top);
        var showAll = new Button { Content = loc["Assist.ShowAll"], Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0) };
        showAll.Click += (_, _) =>
        {
            _panel.Hidden.Clear();
            Change(s => s.AssistOverlayHidden.Clear());
            _panel.Refresh(_directory());
        };
        DockPanel.SetDock(showAll, Dock.Right);
        var pickHint = new TextBlock { Text = loc["Assist.PickHint"], TextWrapping = TextWrapping.Wrap, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        pickHint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        pickBar.Children.Add(showAll);
        pickBar.Children.Add(pickHint);
        root.Children.Add(pickBar);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _panel });
        Content = root;
        ThemedChrome.Apply(this);

        // the list is rebuilt four times a second: not while a box is being clicked, or the click would land on a box that is gone
        _tick.Tick += (_, _) =>
        {
            if (System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed && _panel.IsMouseOver)
            {
                return;
            }

            _panel.Refresh(_directory());
        };
        _tick.Start();
        Closed += (_, _) => _tick.Stop();
        _panel.Refresh(_directory());
    }

    private static CheckBox Switch(string text, bool on, Action<bool> changed)
    {
        var box = new CheckBox { Content = text, IsChecked = on };
        if (Application.Current?.TryFindResource("SettingsSwitch") is Style style)
        {
            box.Style = style;
        }

        box.Click += (_, _) => changed(box.IsChecked == true);
        return box;
    }

    /// <summary>Changes one setting in the file; the overlay's controller reads it within a second.</summary>
    private static void Change(Action<MeterSettings> apply)
    {
        MeterSettings settings = MeterSettings.Load();
        apply(settings);
        settings.Save();
    }
}
