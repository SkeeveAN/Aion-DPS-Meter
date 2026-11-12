using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>What the header of the new interface shows about the game and the character.</summary>
public sealed record HubStatus(bool GameOn, string? CharacterName, string? ClassName, string? Server);

/// <summary>
/// The new interface (alpha): one big window with five main tabs on top (Dashboard, Combat &amp; Analysis, Character, Tools, Settings), a menu on the left that
/// changes with the tab, and the page on the right. The pages are the ones the tools window already knows (the same builder), so every page here works with real data;
/// pages that are not redrawn for the new interface yet say so. Built step by step - the meter window and the tools window stay as they are.
/// </summary>
public sealed class HubWindow : Window
{
    private sealed record Entry(string Id, string Title, string Kind, string Target);

    private sealed record Group(string? Title, Entry[] Entries);

    private sealed record Section(string Id, string Title, Group[] Groups);

    private static readonly Brush Navy = Frozen("#08121b");
    private static readonly Brush NavyBar = Frozen("#09121b");
    private static readonly Brush Line = Frozen("#294557");
    private static readonly Brush Accent = Frozen("#ffba42");
    private static readonly Brush Muted = Frozen("#9db3c2");
    private static readonly Brush Light = Frozen("#eaf2f8");
    private static readonly Brush Selected = Frozen("#26405a");
    private static readonly Brush Hover = Frozen("#1a2b38");

    private readonly Func<string, ToolPage?> _build;
    private readonly Action<string> _openOwn;
    private readonly Action _openMeter;
    private readonly Action _openSettings;
    private readonly Func<HubStatus> _status;
    private Func<IReadOnlyList<AionDPS.History.SessionInfo>> _sessions = () => Array.Empty<AionDPS.History.SessionInfo>();
    private string _version = "";
    private readonly Section[] _sections;
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _right = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _menu = new() { Margin = new Thickness(10, 16, 10, 10) };
    private readonly Border _host = new();
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly TextBlock _footer = new() { Foreground = Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private Section _section;
    private Entry? _entry;
    private ToolPage? _page;
    private readonly Dictionary<string, BitmapImage?> _images = new();

    public HubWindow(Func<string, ToolPage?> build, Action<string> openOwn, Action openMeter, Action openSettings, Func<HubStatus> status)
    {
        _build = build;
        _openOwn = openOwn;
        _openMeter = openMeter;
        _openSettings = openSettings;
        _status = status;
        _sections = BuildSections();
        _section = _sections[0];
        LocalizationManager loc = LocalizationManager.Instance;
        Title = loc["Hub.Title"];
        Width = 1500;
        Height = 900;
        MinWidth = 1000;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontSize = 13;
        SetResourceReference(ForegroundProperty, "Brush.Text");

        var root = new DockPanel();
        var footer = new Border { Height = 32, Background = Navy, BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(18, 0, 18, 0), Child = _footer };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var bar = new Border { Height = 64, Background = NavyBar, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(18, 0, 18, 0) };
        var barGrid = new Grid();
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition());
        barGrid.Children.Add(Brand());
        Grid.SetColumn(_tabs, 1);
        barGrid.Children.Add(_tabs);
        Grid.SetColumn(_right, 2);
        _right.HorizontalAlignment = HorizontalAlignment.Right;
        barGrid.Children.Add(_right);
        bar.Child = barGrid;
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        var side = new Border { Width = 250, Background = Navy, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _menu } };
        DockPanel.SetDock(side, Dock.Left);
        root.Children.Add(side);
        root.Children.Add(_host);
        Content = root;
        ThemedChrome.Apply(this);

        BuildTabs();
        ShowSection(_sections[0]);
        RefreshStatus();
        _refresh.Tick += (_, _) => RefreshStatus();
        _refresh.Start();
        Closed += (_, _) =>
        {
            _refresh.Stop();
            _page?.Cleanup?.Invoke();
        };
    }

    /// <summary>What the dashboard pages read: the recorded stays and the version.</summary>
    public void SetDashboardSources(Func<IReadOnlyList<AionDPS.History.SessionInfo>> sessions, string version)
    {
        _sessions = sessions;
        _version = version;
        if (_entry is { Kind: "overview" } shown)
        {
            ShowEntry(shown);
        }
    }

    /// <summary>For the test picture: chooses a tab and a page of it without a click.</summary>
    internal void SelectForRender(string sectionId, string entryId)
    {
        Section? section = _sections.FirstOrDefault(s => s.Id == sectionId);
        if (section is null)
        {
            return;
        }

        foreach (RadioButton tab in _tabs.Children.OfType<RadioButton>())
        {
            tab.IsChecked = (Section)tab.Tag == section;
        }

        ShowSection(section);
        RadioButton? item = _menu.Children.OfType<RadioButton>().FirstOrDefault(r => ((Entry)r.Tag).Id == entryId);
        if (item is not null)
        {
            item.IsChecked = true;
        }
    }

    private static Brush Frozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private Section[] BuildSections()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var tools = ToolsWindow.Groups().SelectMany(g => g).ToDictionary(t => t.Id, t => t.Title);
        Entry? Tool(string id) => tools.TryGetValue(id, out string? title) ? new Entry(id, title, "tool", id) : null;
        Entry[] Tools(params string[] ids) => ids.Select(Tool).Where(e => e is not null).Select(e => e!).ToArray();
        string settings = loc["Main.MenuSettings"];
        var combat = new List<Entry>
        {
            new("live", loc["Hub.LiveMeter"], "tool", "hub.live"),
            new("sessions", loc["Hub.Sessions"], "tool", "hub.sessions"),
            new("compare", loc["Hub.Compare"], "tool", "hub.compare"),
        };
        combat.AddRange(Tools("dummy"));
        var character = new List<Entry>
        {
            new("profile", loc["Hub.Profile"], "tool", "hub.char.character"),
            new("skills", loc["Hub.Skills"], "tool", "hub.char.skills"),
            new("arcana", loc["Hub.Arcana"], "tool", "hub.char.arcana"),
            new("board", loc["Hub.Board"], "tool", "hub.char.board"),
            new("species", loc["Hub.Species"], "tool", "hub.char.species"),
        };
        return new[]
        {
            new Section("dashboard", loc["Hub.Tab.Dashboard"], new[]
            {
                new Group(loc["Hub.Tab.Dashboard"], new[]
                {
                    new Entry("overview", loc["Hub.Overview"], "overview", "overview"),
                    new Entry("fights", loc["Hub.Dash.Fights"], "overview", "fights"),
                    new Entry("best", loc["Hub.Dash.Best"], "overview", "best"),
                    new Entry("updates", loc["Hub.Dash.Update"], "overview", "updates"),
                    new Entry("news", loc["Hub.Dash.News"], "overview", "news"),
                }),
            }),
            new Section("combat", loc["Hub.Tab.Combat"], new[] { new Group(loc["Hub.Tab.Combat"], combat.ToArray()) }),
            new Section("character", loc["Hub.Tab.Character"], new[]
            {
                new Group(loc["Hub.Tab.Character"], character.ToArray()),
                new Group(loc["Hub.Group.Planning"], Tools("checklist", "attributes", "statcalc")),
            }),
            new Section("tools", loc["Main.MenuTools"], new[]
            {
                new Group(loc["Hub.Group.World"], Tools("loot", "craft", "petmap", "timetable", "boss")),
                new Group(loc["Hub.Group.Combat"], Tools("combat", "assist", "stream", "network", "theme")),
            }),
            new Section("settings", settings, new[] { new Group(settings, new[] { new Entry("settings", settings, "tool", "hub.settings") }) }),
        };
    }

    // ───────────────────────────── header ─────────────────────────────

    private FrameworkElement Brand()
    {
        var logo = new Border
        {
            Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 12, 0),
            Background = new LinearGradientBrush(Color.FromRgb(0xff, 0xba, 0x42), Color.FromRgb(0xb8, 0x66, 0x1a), 45),
            Child = new TextBlock { Text = "D", FontWeight = FontWeights.ExtraBold, Foreground = Frozen("#2a1700"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = "DAEVAS POWER SUITE", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Light });
        texts.Children.Add(new TextBlock { Text = "AION 2 Companion · Alpha", FontSize = 11, Foreground = Frozen("#62788a") });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 28, 0), VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(logo);
        brand.Children.Add(texts);
        return brand;
    }

    private void BuildTabs()
    {
        foreach (Section section in _sections)
        {
            Section s = section;
            var tab = new RadioButton { GroupName = "HubTabs", Foreground = Muted, Cursor = Cursors.Hand, Tag = s, IsChecked = s == _section, Template = TabTemplate(), Content = s.Title };
            tab.Checked += (_, _) => ShowSection(s);
            _tabs.Children.Add(tab);
        }
    }

    private static ControlTemplate TabTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.PaddingProperty, new Thickness(18, 0, 18, 0));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 3));
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetValue(TextElement.FontSizeProperty, 15.0);
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(RadioButton)) { VisualTree = border };
        var checkedTrigger = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true };
        checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "Bd"));
        checkedTrigger.Setters.Add(new Setter(TextElement.ForegroundProperty, Accent));
        template.Triggers.Add(checkedTrigger);
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Frozen("#0c1823"), "Bd"));
        template.Triggers.Add(hover);
        return template;
    }

    private Border Pill(UIElement child) => new()
    {
        Height = 44, CornerRadius = new CornerRadius(22), Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(8, 0, 0, 0),
        BorderBrush = Frozen("#2f4a60"), BorderThickness = new Thickness(1), Background = Frozen("#0c1823"), Child = child,
    };

    private void RefreshStatus()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        HubStatus status;
        try
        {
            status = _status();
        }
        catch (Exception)
        {
            status = new HubStatus(false, null, null, null);
        }

        _right.Children.Clear();
        Color c = status.GameOn ? Color.FromRgb(0x3d, 0xdc, 0x84) : Color.FromRgb(0xff, 0x4d, 0x4d);
        var dot = new Ellipse
        {
            Width = 16, Height = 16, Fill = new SolidColorBrush(c), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = c, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 },
        };
        var game = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        game.Children.Add(dot);
        game.Children.Add(new TextBlock { Text = "Aion 2", FontWeight = FontWeights.SemiBold, Foreground = Light, VerticalAlignment = VerticalAlignment.Center });
        Border gamePill = Pill(game);
        gamePill.ToolTip = loc[status.GameOn ? "Hub.GameOn" : "Hub.GameOff"];
        Border? server = status.Server is { Length: > 0 } sv ? Pill(new TextBlock { Text = sv, Foreground = Light, VerticalAlignment = VerticalAlignment.Center }) : null;
        var who = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (status.ClassName is { Length: > 0 } cn && Picture(Aion2Artwork.ClassEmblemPath(cn)) is { } icon)
        {
            who.Children.Add(new Image { Source = icon, Width = 26, Height = 26, Margin = new Thickness(0, 0, 10, 0) });
        }

        who.Children.Add(new TextBlock { Text = status.CharacterName ?? loc["Hub.NoCharacter"], Foreground = Light, VerticalAlignment = VerticalAlignment.Center });
        if (server is not null)
        {
            _right.Children.Add(Pill(who));
            _right.Children.Add(server);
        }
        else
        {
            _right.Children.Add(Pill(who));
        }

        _right.Children.Add(gamePill);
        _footer.Text = loc[status.GameOn ? "Hub.GameOn" : "Hub.GameOff"];
    }

    private BitmapImage? Picture(string? path)
    {
        if (path is null)
        {
            return null;
        }

        if (_images.TryGetValue(path, out BitmapImage? cached))
        {
            return cached;
        }

        BitmapImage? image = null;
        try
        {
            image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
        }
        catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException or UriFormatException or InvalidOperationException)
        {
            image = null;
        }

        _images[path] = image;
        return image;
    }

    // ───────────────────────────── menu and page ─────────────────────────────

    private void ShowSection(Section section)
    {
        _section = section;
        _menu.Children.Clear();
        bool first = true;
        foreach (Group group in section.Groups.Where(g => g.Entries.Length > 0))
        {
            _menu.Children.Add(new TextBlock
            {
                Text = (group.Title ?? "").ToUpperInvariant(), FontSize = 11, Foreground = Frozen("#62788a"), FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(12, first ? 0 : 22, 0, 8),
            });
            first = false;
            foreach (Entry entry in group.Entries)
            {
                Entry e = entry;
                var item = new RadioButton { GroupName = "HubMenu", Foreground = Muted, Cursor = Cursors.Hand, Tag = e, Template = MenuTemplate(), Content = e.Title + (e.Kind == "own" ? "  ↗" : ""), Margin = new Thickness(0, 0, 0, 3) };
                item.Checked += (_, _) => ShowEntry(e);
                _menu.Children.Add(item);
            }
        }

        Entry? start = section.Groups.SelectMany(g => g.Entries).FirstOrDefault();
        if (start is not null)
        {
            var first1 = _menu.Children.OfType<RadioButton>().FirstOrDefault();
            if (first1 is not null)
            {
                first1.IsChecked = true;
            }
        }
    }

    private static ControlTemplate MenuTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.PaddingProperty, new Thickness(14, 10, 12, 10));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(2, 0, 0, 0));
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(TextElement.FontSizeProperty, 14.5);
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(RadioButton)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Hover, "Bd"));
        template.Triggers.Add(hover);
        var on = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true };
        on.Setters.Add(new Setter(Border.BackgroundProperty, Selected, "Bd"));
        on.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "Bd"));
        on.Setters.Add(new Setter(TextElement.ForegroundProperty, Accent));
        template.Triggers.Add(on);
        return template;
    }

    private void ShowEntry(Entry entry)
    {
        _entry = entry;
        LocalizationManager loc = LocalizationManager.Instance;
        _page?.Cleanup?.Invoke();
        _page = null;
        switch (entry.Kind)
        {
            case "own":
                _openOwn(entry.Target);
                _host.Child = Message(entry.Title + "  ↗", null, null);
                break;
            case "meter":
                _host.Child = Message(entry.Title, loc["Hub.Soon"], (loc["Hub.OpenMeter"], _openMeter));
                break;
            case "overview":
                _page = HubDashboard.Build(entry.Target, new DashboardSources(_status, OpenToolFromDashboard, _sessions, () => SelectForRender("combat", "sessions"), _version));
                _host.Child = new Grid { Children = { _page.Content } };
                break;
            case "settings":
                _host.Child = Message(entry.Title, null, (loc["Hub.OpenSettings"], _openSettings));
                break;
            case "tool":
                _page = _build(entry.Target);
                _host.Child = _page?.Content is UIElement content ? new Grid { Children = { content } } : Message(entry.Title, "–", null);
                break;
            default:
                _host.Child = Message(entry.Title, loc["Hub.Soon"], null);
                break;
        }
    }

    /// <summary>Shows a page of the interface (for a page that sends the player on to another one).</summary>
    public void GoTo(string sectionId, string entryId) => SelectForRender(sectionId, entryId);

    /// <summary>A shortcut of the dashboard: the tools tab with that tool's page.</summary>
    private void OpenToolFromDashboard(string tool)
    {
        SelectForRender(tool is "checklist" or "attributes" or "statcalc" ? "character" : "tools", tool);
    }

    private static UIElement Message(string title, string? text, (string Label, Action Click)? button)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 520 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Light, HorizontalAlignment = HorizontalAlignment.Center });
        if (text is not null)
        {
            panel.Children.Add(new TextBlock { Text = text, Foreground = Muted, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
        }

        if (button is { } b)
        {
            var click = new Button { Content = b.Label, Padding = new Thickness(18, 4, 18, 4), Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            click.Click += (_, _) => b.Click();
            panel.Children.Add(click);
        }

        return panel;
    }
}
