using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;

namespace AionDPS.Ui;

/// <summary>One tool of the tools window: its entry in the list on the left.</summary>
public sealed record ToolEntry(string Id, string Title, bool OwnWindow = false);

/// <summary>What the tools window shows for a tool: the page, and what has to happen when the page goes away (stop timers, close the hosted window).</summary>
public sealed class ToolPage
{
    public required UIElement Content { get; init; }

    public Action? Cleanup { get; init; }
}

/// <summary>
/// The window of all tools: on the left the tools in the order of the Tools menu (a line between the groups of the menu), on the right the page of the chosen tool.
/// A tool that is a large window of its own (the combat analyzer) is marked and opens that window instead.
/// </summary>
public sealed class ToolsWindow : Window
{
    private readonly Func<string, ToolPage?> _build;
    private readonly Action<string> _openOwn;
    private readonly Dictionary<string, RadioButton> _buttons = new();
    private readonly Dictionary<string, ToolEntry> _entries = new();
    private readonly Border _host = new();
    private ToolPage? _page;
    private string? _current;
    private bool _selecting;
    private bool _applyingWidth;
    private readonly DispatcherTimer _saveWidth = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private Border _navBorder = null!;
    private StackPanel _navList = null!;
    private Button _navToggle = null!;

    /// <summary>The width a tool's page starts with when the player has not sized it yet: a checklist is narrow, the design editor wide.</summary>
    private static double DefaultWidth(string id) => id switch
    {
        "checklist" => 660,
        "assist" => 640,
        "timetable" => 640,
        "stream" => 700,
        "dummy" => 1300,
        "theme" => 1700,
        _ => 860,
    };

    public ToolsWindow(IReadOnlyList<IReadOnlyList<ToolEntry>> groups, Func<string, ToolPage?> build, Action<string> openOwn)
    {
        _build = build;
        _openOwn = openOwn;
        Title = LocalizationManager.Instance["Main.MenuTools"];
        Width = 1320;
        Height = 840;
        MinWidth = 300;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var nav = new StackPanel { Margin = new Thickness(6) };
        var template = (ControlTemplate)XamlReader.Parse(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='RadioButton'>" +
            "<Border x:Name='Bd' Background='Transparent' CornerRadius='7' Padding='12,8'><ContentPresenter/></Border>" +
            "<ControlTemplate.Triggers>" +
            "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bd' Property='Background' Value='#1a2b38'/></Trigger>" +
            "<Trigger Property='IsChecked' Value='True'><Setter TargetName='Bd' Property='Background' Value='#26405a'/></Trigger>" +
            "</ControlTemplate.Triggers></ControlTemplate>");
        bool first = true;
        foreach (var group in groups)
        {
            if (!first)
            {
                nav.Children.Add(new Border { Height = 1, Margin = new Thickness(6, 5, 6, 5), Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#294557")! });
            }

            first = false;
            foreach (ToolEntry entry in group)
            {
                var button = new RadioButton
                {
                    GroupName = "Tools", Template = template, Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(0, 0, 0, 2),
                    Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#9db3c2")!,
                    Content = new TextBlock { Text = entry.OwnWindow ? entry.Title + "  ↗" : entry.Title, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis },
                };
                button.Checked += (_, _) =>
                {
                    button.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#ffba42")!;
                    if (!_selecting)
                    {
                        Select(entry.Id);
                    }
                };
                button.Unchecked += (_, _) => button.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#9db3c2")!;
                _entries[entry.Id] = entry;
                _buttons[entry.Id] = button;
                nav.Children.Add(button);
            }
        }

        _navList = nav;
        _navToggle = new Button { Content = "«", Width = 28, Height = 26, Padding = new Thickness(0), Margin = new Thickness(6, 6, 6, 0), HorizontalAlignment = HorizontalAlignment.Right, ToolTip = LocalizationManager.Instance["Tools.NavFold"] };
        _navToggle.Click += (_, _) =>
        {
            MeterSettings s = MeterSettings.Load();
            s.ToolsNavCollapsed = !s.ToolsNavCollapsed;
            s.Save();
            ApplyNav(s.ToolsNavCollapsed);
        };
        var navBody = new DockPanel();
        DockPanel.SetDock(_navToggle, Dock.Top);
        navBody.Children.Add(_navToggle);
        navBody.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = nav });
        var navBorder = new Border { Width = 220, Child = navBody, BorderThickness = new Thickness(0, 0, 1, 0) };
        _navBorder = navBorder;
        navBorder.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#11202b")!;
        navBorder.BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#294557")!;
        var root = new DockPanel();
        DockPanel.SetDock(navBorder, Dock.Left);
        root.Children.Add(navBorder);
        root.Children.Add(_host);
        Content = root;
        ThemedChrome.Apply(this);
        ApplyNav(MeterSettings.Load().ToolsNavCollapsed);
        _saveWidth.Tick += (_, _) =>
        {
            _saveWidth.Stop();
            if (_current is not null && WindowState == WindowState.Normal)
            {
                MeterSettings s = MeterSettings.Load();
                s.ToolWidths[_current] = Math.Round(ActualWidth);
                s.Save();
            }
        };
        // the player sizes the window for the tool that is shown: the width is kept for that tool
        SizeChanged += (_, e) =>
        {
            if (e.WidthChanged && !_applyingWidth && _page is not null)
            {
                _saveWidth.Stop();
                _saveWidth.Start();
            }
        };
        Closed += (_, _) =>
        {
            _saveWidth.Stop();
            DisposePage();
        };
    }

    /// <summary>Shows the page of a tool (or opens its own window).</summary>
    public void Select(string id)
    {
        if (!_buttons.TryGetValue(id, out RadioButton? button))
        {
            return;
        }

        if (_entries[id].OwnWindow)
        {
            // opens its own window and keeps the page that is shown: a click, not a choice
            _openOwn(id);
            if (_current is not null && _buttons.TryGetValue(_current, out RadioButton? shown))
            {
                _selecting = true;
                shown.IsChecked = true;
                _selecting = false;
            }
            else
            {
                button.IsChecked = false;
            }

            return;
        }

        if (_current == id && _page is not null)
        {
            return;
        }

        DisposePage();
        _current = id;
        _selecting = true;
        button.IsChecked = true;
        _selecting = false;

        _page = _build(id);
        ApplyWidth(id);
        if (_page is not null)
        {
            _host.Child = _page.Content;
        }
        else
        {
            var none = new TextBlock { Text = "–", Margin = new Thickness(20), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            none.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            _host.Child = none;
        }
    }

    private void ApplyNav(bool collapsed)
    {
        _navList.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        _navBorder.Width = collapsed ? 40 : 220;
        _navToggle.Content = collapsed ? "»" : "«";
        _navToggle.HorizontalAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        _navToggle.Margin = collapsed ? new Thickness(0, 6, 0, 0) : new Thickness(6, 6, 6, 0);
    }

    /// <summary>The window takes the width this tool has (kept from the last time, else its start width), within the screen.</summary>
    private void ApplyWidth(string id)
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        MeterSettings s = MeterSettings.Load();
        double area = SystemParameters.WorkArea.Width;
        double width = Math.Clamp(s.ToolWidths.TryGetValue(id, out double saved) ? saved : DefaultWidth(id), MinWidth, area);
        _applyingWidth = true;
        Width = width;
        if (Left + width > SystemParameters.WorkArea.Right)
        {
            Left = Math.Max(SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Right - width);
        }

        _applyingWidth = false;
    }

    private void DisposePage()
    {
        _host.Child = null;
        _page?.Cleanup?.Invoke();
        _page = null;
    }
}
