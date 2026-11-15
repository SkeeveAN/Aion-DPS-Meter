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
    private Border? _side;
    private bool _collapsed;
    private bool _menuRebuilding;
    private readonly StackPanel _info = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
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
        WidgetKind.Pages = PageList;
        _openSettings = openSettings;
        _status = status;
        _sections = BuildSections();
        _section = _sections[0];
        LocalizationManager loc = LocalizationManager.Instance;
        Title = "Daevas Power Suite";
        Width = 1500;
        Height = 900;
        MinWidth = 1000;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontSize = 13;
        SetResourceReference(ForegroundProperty, "Brush.Text");

        var root = new DockPanel();
        var footer = new Border { Height = 32, Background = Navy, BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(18, 0, 18, 0), Child = FooterContent() };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var bar = new Border { Height = 64, Background = NavyBar, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(18, 0, 18, 0) };
        var barGrid = new Grid();
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition());
        UIElement burger = NavToggle();
        Grid.SetColumn(burger, 0);
        barGrid.Children.Add(burger);
        Grid.SetColumn(_tabs, 1);
        _tabs.Margin = new Thickness(8, 0, 0, 0);
        barGrid.Children.Add(_tabs);
        Grid.SetColumn(_right, 2);
        _right.HorizontalAlignment = HorizontalAlignment.Right;
        barGrid.Children.Add(_right);
        bar.Child = barGrid;
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        var side = _side = new Border { Width = 250, Background = Navy, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _menu } };
        DockPanel.SetDock(side, Dock.Left);
        root.Children.Add(side);
        root.Children.Add(_host);
        Content = root;
        ThemedChrome.Apply(this);

        _collapsed = MeterSettings.Load().HubNavCollapsed;
        BuildTabs();
        ShowSection(_sections[0]);
        ApplyNav();
        RefreshStatus();
        _refresh.Tick += (_, _) => RefreshStatus();
        _refresh.Start();
        Closed += (_, _) =>
        {
            _refresh.Stop();
            _page?.Cleanup?.Invoke();
        };
    }

    /// <summary>The live meter's list as it is known from the meter overlay (class colour bars, DPS ...), bound to the meter's rows; set by the main window.</summary>
    public Func<UIElement?>? MeterView { get; set; }

    /// <summary>The sources a widget needs, for the widgets that stand over the game.</summary>
    public DashboardSources OverlaySources() => Sources();

    private DashboardSources Sources() => new(_status, OpenToolFromDashboard, _sessions, () => SelectForRender("combat", "sessions"), _version, PageList, BuildPage, MeterView);

    /// <summary>Every page of the app a dashboard can take as a widget (all but the overview itself).</summary>
    private IReadOnlyList<HubPageInfo> PageList() =>
        _sections.SelectMany(sec => sec.Groups.SelectMany(g => g.Entries.Where(e => e.Id != "overview" && e.Kind is "tool" or "overview").Select(e => new HubPageInfo(e.Id, sec.Title, e.Title)))).ToList();

    /// <summary>The page of an entry as its own content (for a widget of a dashboard); null when there is none.</summary>
    private ToolPage? BuildPage(string entryId)
    {
        Entry? entry = _sections.SelectMany(sec => sec.Groups).SelectMany(g => g.Entries).FirstOrDefault(e => e.Id == entryId);
        return entry switch
        {
            null => null,
            { Kind: "overview" } => HubDashboard.Build(entry.Target, Sources()),
            { Kind: "tool" } => _build(entry.Target),
            _ => null,
        };
    }

    /// <summary>The history got new entries: a dashboard page that shows them is drawn again.</summary>
    public void RefreshHistory()
    {
        if (_entry is { Kind: "overview" } shown)
        {
            ShowEntry(shown);
        }
    }

    /// <summary>What the dashboard pages read: the recorded stays and the version.</summary>
    public void SetDashboardSources(Func<IReadOnlyList<AionDPS.History.SessionInfo>> sessions, string version)
    {
        _sessions = sessions;
        _version = version;
        Title = version.Length > 0 ? "Daevas Power Suite " + version : "Daevas Power Suite";
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
            new("replay", loc["Hub.Replay"], "tool", "hub.replay"),
        };
        combat.AddRange(Tools("dummy"));
        var character = new List<Entry>
        {
            new("profile", loc["Hub.Profile"], "tool", "hub.char.character"),
            new("skills", loc["Hub.Skills"], "tool", "hub.char.skills"),
            new("arcana", loc["Hub.Arcana"], "tool", "hub.char.arcana"),
            new("board", loc["Hub.Board"], "tool", "hub.char.board"),
            new("species", loc["Hub.Species"], "tool", "hub.char.species"),
            new("stats", loc["Hub.Stats.Title"], "overview", "stats"),
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
                    new Entry("designer", loc["Designer.Title"], "action", "designer"),
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
                new Group(loc["Hub.Group.Client"], new[]
                {
                    new Entry("client.general", loc["Settings.Nav.General"], "tool", "hub.client.NavGeneral"),
                    new Entry("client.meter", loc["Settings.Nav.Meter"], "tool", "hub.client.NavMeter"),
                    new Entry("client.install", loc["Settings.Nav.Capture"], "tool", "hub.client.NavInstall"),
                    new Entry("client.hotkeys", loc["Settings.Hotkeys"], "tool", "hub.client.NavHotkeys"),
                    new Entry("client.updates", loc["Settings.Nav.System"], "tool", "hub.client.NavUpdates"),
                }),
                new Group(loc["Hub.Group.World"], Tools("loot", "craft", "petmap", "timetable", "boss")),
                new Group(loc["Hub.Group.Combat"], Tools("combat", "assist", "stream", "network", "theme")),
            }),
        };
    }

    /// <summary>The three lines at the very left of the header: they take the names off the tabs and the menu (only the symbols stay) and bring them back.</summary>
    private UIElement NavToggle()
    {
        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        for (int i = 0; i < 3; i++)
        {
            var line = new Border { Width = 20, Height = 2.5, CornerRadius = new CornerRadius(1.25), Margin = new Thickness(0, i == 0 ? 0 : 4, 0, 0) };
            line.SetResourceReference(Border.BackgroundProperty, "Brush.Text");
            lines.Children.Add(line);
        }

        var button = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = lines, ToolTip = LocalizationManager.Instance["Hub.Nav.Toggle"], Margin = new Thickness(-8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        button.MouseEnter += (_, _) => button.SetResourceReference(Border.BackgroundProperty, "Brush.ControlHoverStrong");
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        button.MouseLeftButtonUp += (_, _) => SetNavCollapsed(!_collapsed, true);
        return button;
    }

    /// <summary>Collapsed: the tabs and the menu show their symbols only, the menu is narrow.</summary>
    internal void SetNavCollapsed(bool collapsed, bool remember)
    {
        _collapsed = collapsed;
        if (remember)
        {
            MeterSettings s = MeterSettings.Load();
            s.HubNavCollapsed = collapsed;
            s.Save();
        }

        ApplyNav();
    }

    private void ApplyNav()
    {
        if (_side is not null)
        {
            _side.Width = _collapsed ? 76 : 250;
        }

        _tabs.Children.Clear();
        BuildTabs();
        ShowSection(_section, _entry);
    }

    private static readonly Dictionary<string, string> TabSymbols = new() { ["dashboard"] = "\u2302", ["combat"] = "\u2694", ["character"] = "\u263A", ["tools"] = "\u2699" };

    /// <summary>The name of a top tab with its symbol in front.</summary>
    private object TabContent(Section section)
    {
        if (!TabSymbols.TryGetValue(section.Id, out string? symbol))
        {
            return section.Title;
        }

        // the texts take the colour and weight the tab's template gives its content (the chosen tab is accent and bold)
        TextBlock Follow(TextBlock text)
        {
            var source = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor) { AncestorType = typeof(ContentPresenter) };
            text.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding { Path = new PropertyPath(TextElement.ForegroundProperty), RelativeSource = source });
            text.SetBinding(TextBlock.FontWeightProperty, new System.Windows.Data.Binding { Path = new PropertyPath(TextElement.FontWeightProperty), RelativeSource = source });
            return text;
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Follow(new TextBlock { Text = symbol, FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = _collapsed ? 20 : 17, Margin = new Thickness(0, 0, _collapsed ? 0 : 8, 0), VerticalAlignment = VerticalAlignment.Center }));
        if (!_collapsed)
        {
            row.Children.Add(Follow(new TextBlock { Text = section.Title, VerticalAlignment = VerticalAlignment.Center }));
        }

        return row;
    }

    private void BuildTabs()
    {
        foreach (Section section in _sections)
        {
            Section s = section;
            var tab = new RadioButton { GroupName = "HubTabs", Foreground = Muted, Cursor = Cursors.Hand, Tag = s, IsChecked = s == _section, Template = TabTemplate(), Content = TabContent(s), ToolTip = _collapsed ? s.Title : null };
            tab.Checked += (_, _) => ShowSection(s);
            _tabs.Children.Add(tab);
        }
    }

    /// <summary>The template of a top tab, in the colours of the chosen theme: a raised field, the text colour, and for the chosen one a stronger field, the accent line and bold accent text.</summary>
    private static ControlTemplate TabTemplate() => (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='RadioButton'>" +
        "<Border x:Name='Bd' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Padding='18,0,18,0' Margin='0,12,6,0' CornerRadius='8,8,0,0' BorderThickness='0,0,0,3' BorderBrush='Transparent' Background='{DynamicResource Brush.ControlRaised}'>" +
        "<ContentPresenter x:Name='Cp' VerticalAlignment='Center' TextElement.FontSize='15' TextElement.Foreground='{DynamicResource Brush.Text}'/></Border>" +
        "<ControlTemplate.Triggers>" +
        "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bd' Property='Background' Value='{DynamicResource Brush.ControlHoverStrong}'/></Trigger>" +
        "<Trigger Property='IsChecked' Value='True'><Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Brush.Accent}'/><Setter TargetName='Bd' Property='Background' Value='{DynamicResource Brush.ControlHoverStrong}'/>" +
        "<Setter TargetName='Cp' Property='TextElement.FontWeight' Value='SemiBold'/><Setter TargetName='Cp' Property='TextElement.Foreground' Value='{DynamicResource Brush.Accent}'/></Trigger>" +
        "</ControlTemplate.Triggers></ControlTemplate>");

    private Border Pill(UIElement child) => new()
    {
        Height = 44, CornerRadius = new CornerRadius(22), Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(8, 0, 0, 0),
        BorderBrush = Frozen("#2f4a60"), BorderThickness = new Thickness(1), Background = Frozen("#0c1823"), Child = child,
    };

    private Border? _pin;
    private TextBlock? _updateText;
    private Velopack.UpdateInfo? _footerUpdate;

    /// <summary>The footer: the state of the game on the left; on the right the update check and the pin "always on top" (with a green tick while it is on).</summary>
    private UIElement FooterContent()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(_footer);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(right, 1);
        right.Children.Add(_info); // who plays: the state of the game as a dot, the character and the server

        // icons only, the words are the tooltips; after a click the answer stands beside the icons for a moment
        _updateText = new TextBlock { Text = "", Foreground = Muted, FontSize = 12, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_updateText);
        var refresh = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M16.5,6.5 A7,7 0 1 0 17,11 M16.5,2 L16.5,6.5 L12,6.5"),
            Stroke = Muted, StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = 16, Height = 16, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center,
        };
        var update = new Border { Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(5), Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = refresh, ToolTip = loc["Hub.Dash.CheckNow"] };
        update.MouseEnter += (_, _) =>
        {
            update.Background = Hover;
            refresh.Stroke = Light;
        };
        update.MouseLeave += (_, _) =>
        {
            update.Background = Brushes.Transparent;
            refresh.Stroke = Muted;
        };
        update.MouseLeftButtonUp += (_, _) => FooterUpdateClick();
        right.Children.Add(update);

        // two windows over each other, the front one filled: "this one stays in front"
        var back = new System.Windows.Shapes.Rectangle { Width = 13, Height = 10, Stroke = Muted, StrokeThickness = 1.5, RadiusX = 1.5, RadiusY = 1.5, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var front = new System.Windows.Shapes.Rectangle { Width = 13, Height = 10, Stroke = Muted, Fill = Navy, StrokeThickness = 1.5, RadiusX = 1.5, RadiusY = 1.5, Margin = new Thickness(7, 7, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var pinIcon = new Grid { Width = 22, Height = 19 };
        pinIcon.Children.Add(back);
        pinIcon.Children.Add(front);
        var tick = new TextBlock { Text = "✓", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Frozen("#3ddc84"), Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var pinRow = new StackPanel { Orientation = Orientation.Horizontal };
        pinRow.Children.Add(pinIcon);
        pinRow.Children.Add(tick);
        _pin = new Border { Padding = new Thickness(10, 3, 10, 3), CornerRadius = new CornerRadius(5), Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = pinRow, ToolTip = loc["Hub.Footer.OnTop"] };
        void ShowPin()
        {
            Brush ink = Topmost ? Light : Muted;
            back.Stroke = ink;
            front.Stroke = ink;
            front.Fill = Topmost ? Light : Navy;
            tick.Visibility = Topmost ? Visibility.Visible : Visibility.Collapsed;
        }

        _pin.MouseEnter += (_, _) => _pin.Background = Hover;
        _pin.MouseLeave += (_, _) => _pin.Background = Brushes.Transparent;
        _pin.MouseLeftButtonUp += (_, _) =>
        {
            Topmost = !Topmost;
            MeterSettings s = MeterSettings.Load();
            s.HubAlwaysOnTop = Topmost;
            s.Save();
            ShowPin();
        };
        Topmost = MeterSettings.Load().HubAlwaysOnTop;
        ShowPin();
        right.Children.Add(_pin);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>The first click looks for an update; with one found the same button downloads it and restarts.</summary>
    private async void FooterUpdateClick()
    {
        // the icon stands for the check; the words are the tooltip
        LocalizationManager loc = LocalizationManager.Instance;
        if (_updateText is null)
        {
            return;
        }

        if (!AionDPS.Update.UpdateService.CanUpdate)
        {
            _updateText.Text = loc["Hub.Dash.NotInstalled"];
            return;
        }

        try
        {
            if (_footerUpdate is { } found)
            {
                await AionDPS.Update.UpdateService.DownloadAsync(found, percent => Dispatcher.Invoke(() => _updateText.Text = string.Format(loc["Hub.Dash.Downloading"], percent)));
                AionDPS.Update.UpdateService.ApplyAndRestart(found);
                return;
            }

            _updateText.Text = loc["Hub.Dash.Checking"];
            _footerUpdate = await AionDPS.Update.UpdateService.CheckAsync();
            _updateText.Text = _footerUpdate is null ? "✓  " + loc["Hub.Dash.UpToDate"] : loc["Hub.Dash.Version"] + " " + _footerUpdate.TargetFullRelease.Version + " – " + loc["Hub.Dash.InstallRestart"];
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _footerUpdate = null;
            _updateText.Text = loc["Hub.Dash.CheckFailed"];
        }
    }

    private Velopack.UpdateInfo? _updateInfo;
    private bool _updating;
    private string? _updateVersion;
    private bool _dropsActive, _dropsNew, _announcementsNew;
    private long _dropsKey, _announcementsKey;
    private DateTime _newsAskedAt = DateTime.MinValue;

    /// <summary>A round button of the header (like the pills beside it) with a drawn icon, a tip and, when something is new, the "New" tag beside the icon.</summary>
    private Border HeaderButton(UIElement icon, string tip, string? newTag, Action click)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(icon);
        if (newTag is not null)
        {
            row.Children.Add(new Border
            {
                Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 1, 6, 1), CornerRadius = new CornerRadius(8), Background = Frozen("#FFD24A"), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = newTag, FontSize = 11, FontWeight = FontWeights.ExtraBold, Foreground = Frozen("#1A1200") },
            });
        }

        Border button = Pill(row);
        button.Padding = new Thickness(12, 0, 12, 0);
        button.Cursor = Cursors.Hand;
        button.ToolTip = tip;
        button.MouseLeftButtonUp += (_, _) => click();
        return button;
    }

    /// <summary>The click on the Twitch or the announcements symbol: what is shown now counts as seen.</summary>
    private void MarkSeen(bool drops)
    {
        MeterSettings s = MeterSettings.Load();
        if (drops)
        {
            s.TwitchDropsSeen = Math.Max(s.TwitchDropsSeen, _dropsKey);
            _dropsNew = false;
        }
        else
        {
            s.AnnouncementsSeen = Math.Max(s.AnnouncementsSeen, _announcementsKey);
            _announcementsNew = false;
        }

        s.Save();
        RefreshStatus();
    }

    /// <summary>Asks the update, the announcements and the Twitch drops now and then (every five minutes); the header marks what is new.</summary>
    private async void AskNews()
    {
        if (DateTime.UtcNow - _newsAskedAt < TimeSpan.FromMinutes(5))
        {
            return;
        }

        _newsAskedAt = DateTime.UtcNow;
        try
        {
            if (AionDPS.Update.UpdateService.CanUpdate)
            {
                var info = await AionDPS.Update.UpdateService.CheckAsync();
                _updateInfo = info;
                _updateVersion = info?.TargetFullRelease.Version.ToString();
            }

            MeterSettings seen = MeterSettings.Load();
            long? latest = await Announcements.AnnouncementsClient.GetLatestAsync();
            _announcementsKey = latest ?? 0;
            _announcementsNew = Announcements.AnnouncementsClient.IsNew(latest, seen.AnnouncementsSeen);
            // drops: running now, or starting within a week
            var drops = await Announcements.TwitchDropsClient.GetStatusAsync();
            long soon = await Announcements.TwitchDropsClient.NextStartAsync(TimeSpan.FromDays(7));
            _dropsActive = drops is { Running: true } || soon > 0;
            _dropsKey = Math.Max(drops?.Latest ?? 0, soon);
            _dropsNew = _dropsActive && _dropsKey > seen.TwitchDropsSeen;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // offline: the header shows what it knew
        }
    }

    /// <summary>The update symbol of the header: download the new version and install it (the program restarts), no page in between.</summary>
    private async void InstallUpdate()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        if (_updateInfo is not { } info || _updating)
        {
            return;
        }

        _updating = true;
        try
        {
            await AionDPS.Update.UpdateService.DownloadAsync(info, percent => Dispatcher.Invoke(() => _footer.Text = string.Format(loc["Hub.Dash.Downloading"], percent)));
            AionDPS.Update.UpdateService.ApplyAndRestart(info);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _footer.Text = loc["Hub.Dash.CheckFailed"];
            _updating = false;
        }
    }

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

        AskNews();
        _right.Children.Clear();
        // the update symbol only while there is an update (in the orange of the theme); the Twitch symbol while drops run or come soon; the announcements always - "New" until clicked
        if (_updateVersion is not null)
        {
            var arrow = new TextBlock { Text = "\uE896", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 18, VerticalAlignment = VerticalAlignment.Center };
            arrow.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            Border update = HeaderButton(arrow, loc["Hub.Dash.InstallRestart"] + " – " + _updateVersion, null, InstallUpdate);
            update.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
            _right.Children.Add(update);
        }

        if (_dropsActive)
        {
            var twitch = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("F1 M11.571 4.714h1.715v5.143H11.57zm4.715 0H18v5.143h-1.714zM6 0L1.714 4.286v15.428h5.143V24l4.286-4.286h3.428L22.286 12V0zm14.571 11.143l-3.428 3.428h-3.429l-3 3v-3H6.857V1.714h13.714Z"),
                Fill = Frozen("#A970FF"), Width = 16, Height = 16, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center,
            };
            Border drops = HeaderButton(twitch, loc["Hub.Dash.Drops"], _dropsNew ? loc["Main.TwitchDrops.New"] : null, () =>
            {
                MarkSeen(drops: true);
                NewsWindow.ShowDrops();
            });
            drops.BorderBrush = Frozen("#A970FF");
            _right.Children.Add(drops);
        }

        var horn = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M3,9 L3,15 L7,15 L14,20 L14,4 L7,9 Z M17,8 C18.5,9.5 18.5,14.5 17,16 M19.5,5.5 C22.5,8.5 22.5,15.5 19.5,18.5"),
            Stroke = Light, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = 18, Height = 15, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center,
        };
        _right.Children.Add(HeaderButton(horn, loc["Hub.Dash.Announcements"], _announcementsNew ? loc["Main.Announcements.New"] : null, () =>
        {
            MarkSeen(drops: false);
            NewsWindow.ShowAnnouncements();
        }));
        // the footer: the state of the game as a dot, the class symbol with the name of the character, the server
        Color c = status.GameOn ? Color.FromRgb(0x3d, 0xdc, 0x84) : Color.FromRgb(0xff, 0x4d, 0x4d);
        _info.Children.Clear();
        _info.Children.Add(new Ellipse
        {
            Width = 11, Height = 11, Fill = new SolidColorBrush(c), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = loc[status.GameOn ? "Hub.GameOn" : "Hub.GameOff"],
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = c, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 },
        });
        var who = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ToolTip = status.ClassName };
        if (status.ClassName is { Length: > 0 } cn && Picture(Aion2Artwork.ClassEmblemPath(cn)) is { } icon)
        {
            who.Children.Add(new Image { Source = icon, Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0) });
        }

        who.Children.Add(new TextBlock { Text = status.CharacterName ?? loc["Hub.NoCharacter"], FontSize = 12, Foreground = Light, VerticalAlignment = VerticalAlignment.Center });
        _info.Children.Add(who);
        if (status.Server is { Length: > 0 } sv)
        {
            _info.Children.Add(new TextBlock { Text = "·", FontSize = 14, Foreground = Muted, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            _info.Children.Add(new TextBlock { Text = sv, FontSize = 12, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
        }

        if (!_updating)
        {
            _footer.Text = ""; // the dot says whether the game runs; the text stands here only while an update is downloaded
        }
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

    private void ShowSection(Section section, Entry? keep = null)
    {
        _section = section;
        _menu.Children.Clear();
        _menu.Margin = _collapsed ? new Thickness(10, 16, 10, 10) : new Thickness(10, 16, 10, 10);
        bool first = true;
        foreach (Group group in section.Groups.Where(g => g.Entries.Length > 0))
        {
            if (_collapsed)
            {
                // no heading over the symbols, a thin line between the groups
                if (!first)
                {
                    _menu.Children.Add(new Border { Height = 1, Background = Line, Margin = new Thickness(8, 8, 8, 12) });
                }
            }
            else
            {
                _menu.Children.Add(new TextBlock
                {
                    Text = (group.Title ?? "").ToUpperInvariant(), FontSize = 11, Foreground = Frozen("#62788a"), FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(12, first ? 0 : 22, 0, 8),
                });
            }

            first = false;
            foreach (Entry entry in group.Entries)
            {
                Entry e = entry;
                var item = new RadioButton { GroupName = "HubMenu", Foreground = Muted, Cursor = Cursors.Hand, Tag = e, Template = MenuTemplate(), Content = MenuContent(section.Id, e, _collapsed), Margin = new Thickness(0, 0, 0, 3) };
                if (e.Id == "sessions")
                {
                    item.ToolTip = LocalizationManager.Instance["Hub.Sessions.Tip"];
                }
                else if (e.Kind == "action")
                {
                    item.ToolTip = LocalizationManager.Instance["Hub.Designer.Tip"];
                }
                else if (_collapsed)
                {
                    item.ToolTip = e.Title;
                }

                if (e.Kind == "action")
                {
                    // a button, not a page: it is never the chosen entry
                    item.PreviewMouseLeftButtonDown += (_, ev) =>
                    {
                        ev.Handled = true;
                        ToggleDesigner?.Invoke();
                    };
                }
                else
                {
                    item.Checked += (_, _) =>
                    {
                        if (!_menuRebuilding)
                        {
                            ShowEntry(e);
                        }
                    };
                }

                _menu.Children.Add(item);
            }
        }

        // a rebuilt menu keeps the chosen entry without showing its page again; a new section starts with its first
        var chosen = keep is not null ? _menu.Children.OfType<RadioButton>().FirstOrDefault(r => ReferenceEquals(r.Tag, keep)) : null;
        if (chosen is not null)
        {
            _menuRebuilding = true;
            chosen.IsChecked = true;
            _menuRebuilding = false;
            return;
        }

        var first1 = _menu.Children.OfType<RadioButton>().FirstOrDefault(r => ((Entry)r.Tag).Kind != "action");
        if (first1 is not null)
        {
            first1.IsChecked = true;
        }
    }

    /// <summary>The overlay designer on or off (set by the main window).</summary>
    public Action? ToggleDesigner { get; set; }

    /// <summary>A symbol before the name of a tool in the menu (the Tools tab), in the colour of the text, so it also turns accent when the entry is chosen.</summary>
    private static readonly Dictionary<string, string> MenuSymbols = new()
    {
        ["client.general"] = "\u2699", ["client.meter"] = "\u2694", ["client.install"] = "\u2B06", ["client.hotkeys"] = "\u2328", ["client.updates"] = "\u2318",
        ["loot"] = "\u25C6", ["craft"] = "\u2692", ["petmap"] = "\u2316", ["timetable"] = "\u25F7", ["boss"] = "\u265C",
        ["combat"] = "\u265B", ["assist"] = "\u2726", ["stream"] = "\u25C9", ["network"] = "\u21C5", ["theme"] = "\u270E",
        // the pages of the other tabs: Dashboard, Combat & Analysis, Character
        ["overview"] = "\u2302", ["fights"] = "\u2694", ["best"] = "\u2605", ["updates"] = "\u2B06", ["news"] = "\u2709",
        ["live"] = "\u26A1", ["sessions"] = "\u25A4", ["compare"] = "\u21C4", ["replay"] = "\u25B6", ["dummy"] = "\u25CE",
        ["profile"] = "\u263A", ["skills"] = "\u2737", ["arcana"] = "\u2740", ["board"] = "\u2B22", ["species"] = "\u273F", ["stats"] = "\u2211",
        ["checklist"] = "\u2611", ["attributes"] = "\u2630", ["statcalc"] = "\u2295", ["designer"] = "\u25A3",
    };

    private static object MenuContent(string sectionId, Entry entry, bool iconsOnly = false)
    {
        string text = entry.Title + (entry.Kind == "own" ? "  ↗" : "");
        if (!MenuSymbols.TryGetValue(entry.Id, out string? symbol))
        {
            return iconsOnly ? new TextBlock { Text = entry.Title.Length > 0 ? entry.Title[..1].ToUpperInvariant() : "·", FontSize = 16, FontWeight = FontWeights.SemiBold, Width = 28, TextAlignment = TextAlignment.Center } : text;
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = symbol, FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 18, Width = 28, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        if (!iconsOnly)
        {
            row.Children.Add(new TextBlock { Text = text, FontSize = 14.5, VerticalAlignment = VerticalAlignment.Center });
        }

        return row;
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

    /// <summary>The page of a tool under two tabs: the page itself, and its settings (built when the tab is first chosen; they end with the page).</summary>
    private RadioButton? _toolSettingsTab;

    /// <summary>For the test picture: the Settings tab of the tool page that is shown.</summary>
    private readonly Dictionary<string, RadioButton> _toolTabs = new();

    /// <summary>For the test picture: one of the extra tabs of the tool page that is shown ("petmap.pets", "petmap.gather").</summary>
    internal void SelectToolTabForRender(string id)
    {
        if (_toolTabs.TryGetValue(id, out RadioButton? tab))
        {
            tab.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
        }
    }

    internal void SelectToolSettingsForRender() => _toolSettingsTab?.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);

    private UIElement WithSettingsTab(string tool, UIElement view, ToolPage? ownSettings = null)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        ToolPage? settings = null;
        var shown = new Border { Child = view };
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 8, 0, 0) };
        var line = new Border { BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = strip };
        string group = "ToolTab" + Guid.NewGuid();
        RadioButton Tab(string text, bool on, Action choose)
        {
            var tab = new RadioButton { GroupName = group, IsChecked = on, Content = text, Cursor = Cursors.Hand, Template = ModulePage.TabTemplate() };
            tab.Checked += (_, _) => choose();
            return tab;
        }

        strip.Children.Add(Tab(loc["Hub.Tab.View"], true, () => shown.Child = view));
        // the interactive map has its pets and its gathering as tabs of their own: View | Pets | Gathering | Settings
        var extraPages = new List<ToolPage>();
        if (tool == "petmap")
        {
            foreach (var (label, page) in new[] { (loc["Settings.Nav.Pets"], "petmap.pets"), (loc["Settings.Nav.Gather"], "petmap.gather") })
            {
                ToolPage? built = null;
                string id = page;
                RadioButton extraTab = Tab(label, false, () =>
                {
                    if (built is null && _build("hub.cfg." + id) is { } made)
                    {
                        built = made;
                        extraPages.Add(made);
                    }

                    shown.Child = built?.Content ?? new TextBlock { Text = "–" };
                });
                _toolTabs[id] = extraTab;
                strip.Children.Add(extraTab);
            }
        }

        settings = ownSettings;
        var settingsTab = Tab(loc["Hub.Tab.Options"], false, () =>
        {
            settings ??= _build("hub.cfg." + tool);
            shown.Child = settings?.Content ?? new TextBlock { Text = "–" };
        });
        _toolSettingsTab = settingsTab;
        strip.Children.Add(settingsTab);
        var inner = ownSettings is null ? _page : null;
        _page = new ToolPage { Content = view, CurrentTab = inner?.CurrentTab, Cleanup = () => { inner?.Cleanup?.Invoke(); settings?.Cleanup?.Invoke(); foreach (ToolPage extra in extraPages) { extra.Cleanup?.Invoke(); } } };
        var dock = new DockPanel();
        DockPanel.SetDock(line, Dock.Top);
        dock.Children.Add(line);
        dock.Children.Add(shown);
        return dock;
    }

    /// <summary>The View tab of the DPS meter: the list the meter overlay draws, with the button that opens it in a window of its own.</summary>
    private UIElement MeterPageView()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var popout = new TextBlock { Text = "\u29C9", Cursor = Cursors.Hand, FontSize = 16, Margin = new Thickness(0, 0, 4, 0), ToolTip = loc["Hub.Dash.Popout"], HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        popout.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        popout.MouseLeftButtonUp += (_, _) => WidgetPopoutWindow.Show("meter", Sources());
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var title = new TextBlock { Text = loc["Settings.Nav.Meter"], FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        DockPanel.SetDock(popout, Dock.Right);
        head.Children.Add(popout);
        head.Children.Add(title);
        var dock = new DockPanel { Margin = new Thickness(14, 10, 14, 14) };
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        dock.Children.Add(MeterView?.Invoke() ?? new TextBlock { Text = "–" });
        return dock;
    }

    private static readonly HashSet<string> LiveTools = new() { "loot", "craft", "petmap", "boss", "combat", "assist", "network", "dummy", "hub.live" };

    private bool GameIsOn()
    {
        try
        {
            return _status().GameOn;
        }
        catch (Exception)
        {
            return false;
        }
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
                _host.Child = Message(entry.Title, loc["Hub.Soon"], null);
                break;
            case "overview":
                _page = HubDashboard.Build(entry.Target, Sources());
                _host.Child = new Grid { Children = { _page.Content } };
                break;
            case "settings":
                _host.Child = Message(entry.Title, null, (loc["Hub.OpenSettings"], _openSettings));
                break;
            case "tool":
                _page = _build(entry.Target);
                if (_page?.Content is not UIElement content0)
                {
                    _host.Child = Message(entry.Title, "–", null);
                    break;
                }

                UIElement content = content0;
                // every tool has two tabs: what it shows, and its settings (the switch that takes it out of the menus and its own cards)
                if (entry.Id == "client.meter" && MeterView is not null)
                {
                    // the DPS meter: its list as the live widget shows it (with the window button) and the settings of the meter as the second tab
                    content = WithSettingsTab("client.meter", MeterPageView(), _page);
                }
                else if (ToolSwitch.All.Any(t => t.Id == entry.Target) && entry.Target != "analyzer")
                {
                    content = WithSettingsTab(entry.Target, content);
                }

                // tools that live on what the game sends say so while the game is off: what they show is a sample or the last thing they saw
                if (LiveTools.Contains(entry.Target) && !GameIsOn())
                {
                    var note = new Border { Padding = new Thickness(16, 7, 16, 7), Background = Frozen("#3a2a10"), BorderBrush = Accent, BorderThickness = new Thickness(0, 0, 0, 1), Child = new TextBlock { Text = loc["Hub.MockNote"], Foreground = Frozen("#ffd27a"), TextWrapping = TextWrapping.Wrap, FontSize = 12 } };
                    var dock = new DockPanel();
                    DockPanel.SetDock(note, Dock.Top);
                    dock.Children.Add(note);
                    dock.Children.Add(content);
                    _host.Child = dock;
                }
                else
                {
                    _host.Child = new Grid { Children = { content } };
                }

                break;
                break;
            default:
                _host.Child = Message(entry.Title, loc["Hub.Soon"], null);
                break;
        }
    }

    /// <summary>The replay the Replay page shows (set by <see cref="ShowReplay"/>).</summary>
    internal static Combat.ReplayData? PendingReplay { get; private set; }

    /// <summary>A player's details asked for the replay of a fight: the Replay page of the Combat &amp; Analysis tab shows it.</summary>
    internal void ShowReplay(Combat.ReplayData data)
    {
        PendingReplay = data;
        Entry? shown = _entry;
        GoTo("combat", "replay");
        if (shown is { Id: "replay" })
        {
            ShowEntry(shown); // already on the page: it is built again for the new data
        }
    }

    /// <summary>Shows a page of the interface (for a page that sends the player on to another one).</summary>
    public void GoTo(string sectionId, string entryId) => SelectForRender(sectionId, entryId);

    /// <summary>A shortcut of the dashboard: the tools tab with that tool's page.</summary>
    private void OpenToolFromDashboard(string tool)
    {
        if (tool.StartsWith("tab:", StringComparison.Ordinal))
        {
            // a tab of the app (the links of the dashboard's sheet)
            if (_sections.FirstOrDefault(s => s.Id == tool["tab:".Length..]) is { } target)
            {
                foreach (RadioButton tab in _tabs.Children.OfType<RadioButton>())
                {
                    tab.IsChecked = (Section)tab.Tag == target;
                }

                ShowSection(target);
            }

            return;
        }

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
