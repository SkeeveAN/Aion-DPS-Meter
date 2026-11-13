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
        Grid.SetColumn(_tabs, 0);
        _tabs.Margin = new Thickness(-8, 0, 0, 0); // the first tab stands at the very left
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

    private DashboardSources Sources() => new(_status, OpenToolFromDashboard, _sessions, () => SelectForRender("combat", "sessions"), _version, PageList, BuildPage);

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
        if (!_updating)
        {
            _footer.Text = loc[status.GameOn ? "Hub.GameOn" : "Hub.GameOff"];
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

    /// <summary>The page of a tool under two tabs: the page itself, and its settings (built when the tab is first chosen; they end with the page).</summary>
    private RadioButton? _toolSettingsTab;

    /// <summary>For the test picture: the Settings tab of the tool page that is shown.</summary>
    internal void SelectToolSettingsForRender() => _toolSettingsTab?.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);

    private UIElement WithSettingsTab(string tool, UIElement view)
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
        var settingsTab = Tab(loc["Hub.Tab.Options"], false, () =>
        {
            settings ??= _build("hub.cfg." + tool);
            shown.Child = settings?.Content ?? new TextBlock { Text = "–" };
        });
        _toolSettingsTab = settingsTab;
        strip.Children.Add(settingsTab);
        var inner = _page;
        _page = new ToolPage { Content = view, CurrentTab = inner?.CurrentTab, Cleanup = () => { inner?.Cleanup?.Invoke(); settings?.Cleanup?.Invoke(); } };
        var dock = new DockPanel();
        DockPanel.SetDock(line, Dock.Top);
        dock.Children.Add(line);
        dock.Children.Add(shown);
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
                _host.Child = Message(entry.Title, loc["Hub.Soon"], (loc["Hub.OpenMeter"], _openMeter));
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
                if (ToolSwitch.All.Any(t => t.Id == entry.Target) && entry.Target != "analyzer")
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
