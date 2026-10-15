using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

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

    public ToolsWindow(IReadOnlyList<IReadOnlyList<ToolEntry>> groups, Func<string, ToolPage?> build, Action<string> openOwn)
    {
        _build = build;
        _openOwn = openOwn;
        Title = LocalizationManager.Instance["Main.MenuTools"];
        Width = 1320;
        Height = 840;
        MinWidth = 900;
        MinHeight = 520;
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

        var navBorder = new Border { Width = 220, Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = nav }, BorderThickness = new Thickness(0, 0, 1, 0) };
        navBorder.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#11202b")!;
        navBorder.BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#294557")!;
        var root = new DockPanel();
        DockPanel.SetDock(navBorder, Dock.Left);
        root.Children.Add(navBorder);
        root.Children.Add(_host);
        Content = root;
        ThemedChrome.Apply(this);
        Closed += (_, _) => DisposePage();
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

    private void DisposePage()
    {
        _host.Child = null;
        _page?.Cleanup?.Invoke();
        _page = null;
    }
}
