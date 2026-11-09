using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AionDPS.Ui;

/// <summary>
/// The navigation of the settings window (2.0): search, "Program" (its pages are tabs), "Tools" (the tiles) and one page per tool.
/// The pages themselves are the panels of SettingsWindow.xaml; this part only decides which of them is shown and builds the frame around them.
/// </summary>
public partial class SettingsWindow
{
    private enum SettingsView { Program, Tools, Tool }

    /// <summary>One tab of a tool's page: a whole panel, or only some of the cards of a panel (the index in the panel's children).</summary>
    private sealed record ToolTab(string TitleKey, string Panel, int[]? Cards = null);

    private static readonly string[] AllPanels =
    {
        "PanelGeneral", "PanelMeter", "PanelCapture", "PanelHotkeys", "PanelSystem",
        "PanelNetwork", "PanelLoot", "PanelCombat", "PanelTimetable", "PanelBoss", "PanelPetMap", "PanelPets", "PanelGather", "PanelStream",
        "PanelTools", "PanelToolInfo", "PanelSearch",
    };

    /// <summary>The page each tab of the "Program" page shows (the settings proper: only what belongs to no tool).</summary>
    private static readonly Dictionary<string, string> PageOfNav = new()
    {
        ["NavGeneral"] = "PanelGeneral", ["NavMeter"] = "PanelMeter", ["NavInstall"] = "PanelCapture", ["NavHotkeys"] = "PanelHotkeys", ["NavUpdates"] = "PanelSystem",
    };

    /// <summary>The tabs of the tools that have settings in this window; a tool that is not here has no settings page of its own (its page says so).
    /// The boss fight keeps its main switch card (child 0) on every tab.</summary>
    private static readonly Dictionary<string, ToolTab[]> ToolTabsOf = new()
    {
        ["network"] = new[] { new ToolTab("Settings.Nav.Network", "PanelNetwork") },
        ["loot"] = new[] { new ToolTab("Main.MenuTools.Loot", "PanelLoot") },
        ["combat"] = new[]
        {
            new ToolTab("Settings.Combat.CardBossHp", "PanelCombat", new[] { 0, 1 }),
            new ToolTab("Settings.Combat.CardBossCast", "PanelCombat", new[] { 0, 2 }),
            new ToolTab("Combat.Element.TargetOfTarget", "PanelCombat", new[] { 0, 3 }),
        },
        ["timetable"] = new[] { new ToolTab("Settings.Nav.Timetable", "PanelTimetable") },
        ["boss"] = new[] { new ToolTab("Settings.Nav.Boss", "PanelBoss") },
        ["petmap"] = new[]
        {
            new ToolTab("Settings.Tab.Map", "PanelPetMap"),
            new ToolTab("Settings.Nav.Pets", "PanelPets"),
            new ToolTab("Settings.Nav.Gather", "PanelGather"),
        },
        ["stream"] = new[] { new ToolTab("Settings.Nav.Stream", "PanelStream") },
    };

    private SettingsView _view = SettingsView.Program;
    private string? _module;
    private string _page = "NavGeneral";
    private int _tab;
    private HashSet<string> _disabled = new();

    /// <summary>The user asked to open a tool (the "Open" button of a tool page).</summary>
    public event Action<string>? OpenToolRequested;

    private readonly Dictionary<string, (Border Item, Ellipse? Dot)> _navItems = new();

    private static readonly Brush DotOn = Frozen(0x56, 0xd3, 0x8b);
    private static readonly Brush DotOff = Frozen(0x6b, 0x7f, 0x8f);
    private static readonly Brush ItemSelected = Frozen(0x1f, 0x34, 0x45);
    private static readonly Brush ItemHover = Frozen(0x17, 0x28, 0x35);
    private static readonly Brush Accent = Frozen(0xff, 0xba, 0x42);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void InitNav(MeterSettings settings)
    {
        _disabled = new HashSet<string>(settings.DisabledTools);
        BuildSideNav();
    }

    private readonly Dictionary<string, OpacityField> _contentFields = new();

    /// <summary>Beside each overlay's opacity slider a second one: how clear the content (text, symbols) is. The first one is then the background's.</summary>
    private void AddContentOpacityFields(MeterSettings settings)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        foreach (var (title, field) in new[]
        {
            (OverlayPlaces.Hotkeys, HotkeyOpacity), (OverlayPlaces.Network, NetworkOpacity), (OverlayPlaces.Loot, LootOpacity),
            (OverlayPlaces.WorldBoss, BossOpacity), (OverlayPlaces.PetList, PetListOpacity), (OverlayPlaces.PetInfo, PetInfoOpacity),
        })
        {
            if (field.Parent is not Panel parent)
            {
                continue;
            }

            field.Label = loc["Designer.Background"];
            var content = new OpacityField { Label = loc["Designer.Content"], Margin = new Thickness(field.Margin.Left, 2, field.Margin.Right, field.Margin.Bottom) };
            content.Fraction = settings.OverlayForegrounds.GetValueOrDefault(title, 1.0);
            parent.Children.Insert(parent.Children.IndexOf(field) + 1, content);
            _contentFields[title] = content;
        }
    }

    private static string NameOf(string tool)
    {
        foreach (var (id, nameKey, _) in ToolSwitch.All)
        {
            if (id == tool)
            {
                return LocalizationManager.Instance[nameKey];
            }
        }

        return tool;
    }

    // ───────────────────────────── left navigation ─────────────────────────────

    private void BuildSideNav()
    {
        SideNav.Children.Clear();
        _navItems.Clear();
        LocalizationManager loc = LocalizationManager.Instance;
        AddNavItem("program", loc["Settings.Nav.Program"], big: true, dot: false);
        AddNavItem("tools", loc["Main.MenuTools"], big: true, dot: false);
        foreach (var (id, nameKey, _) in ToolSwitch.All)
        {
            AddNavItem(id, loc[nameKey], big: false, dot: true);
        }

        RefreshSideNav();
    }

    private void AddNavItem(string tag, string text, bool big, bool dot)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = big ? 15 : 13,
            FontWeight = big ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Tag = big,
        };
        grid.Children.Add(label);
        Ellipse? dotShape = null;
        if (dot)
        {
            dotShape = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0) };
            Grid.SetColumn(dotShape, 1);
            grid.Children.Add(dotShape);
        }

        var item = new Border
        {
            Child = grid,
            Tag = tag,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(2, 0, 0, 0),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            // a tool is indented under "Tools"; the rows have air between them and are not taller than their text needs
            Padding = big ? new Thickness(10, 7, 8, 7) : new Thickness(22, 5, 8, 5),
            Margin = big ? new Thickness(0, 2, 0, 2) : new Thickness(0, 3, 0, 3),
            Cursor = Cursors.Hand,
        };
        item.MouseLeftButtonUp += OnNavItemClicked;
        item.MouseEnter += (_, _) => { if (!IsCurrent(tag)) { item.Background = ItemHover; } };
        item.MouseLeave += (_, _) => { if (!IsCurrent(tag)) { item.Background = Brushes.Transparent; } };
        SideNav.Children.Add(item);
        _navItems[tag] = (item, dotShape);
    }

    private bool IsCurrent(string tag)
    {
        if (SearchActive)
        {
            return false;
        }

        return tag switch
        {
            "program" => _view == SettingsView.Program,
            "tools" => _view == SettingsView.Tools,
            _ => _view == SettingsView.Tool && _module == tag,
        };
    }

    private void RefreshSideNav()
    {
        foreach (var (tag, (item, dot)) in _navItems)
        {
            bool current = IsCurrent(tag);
            item.Background = current ? ItemSelected : Brushes.Transparent;
            item.BorderBrush = current ? Accent : Brushes.Transparent;
            var label = (TextBlock)((Grid)item.Child).Children[0];
            bool big = (bool)label.Tag;
            bool off = dot is not null && _disabled.Contains(tag);
            label.Foreground = current ? (Brush)Frozen(0xea, 0xf2, 0xf8) : off ? (Brush)Frozen(0x6e, 0x82, 0x92) : big ? (Brush)Frozen(0xea, 0xf2, 0xf8) : (Brush)Frozen(0x9d, 0xb3, 0xc2);
            if (dot is not null)
            {
                dot.Fill = off ? DotOff : DotOn;
            }
        }
    }

    private void OnNavItemClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string tag })
        {
            return;
        }

        ClearSearch();
        switch (tag)
        {
            case "program":
                _view = SettingsView.Program;
                break;
            case "tools":
                _view = SettingsView.Tools;
                break;
            default:
                OpenTool(tag, 0);
                return;
        }

        ApplyView();
    }

    private void OpenTool(string tool, int tab)
    {
        _view = SettingsView.Tool;
        _module = tool;
        _tab = tab;
        ApplyView();
    }

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Name: { Length: > 0 } name })
        {
            _page = name;
            _view = SettingsView.Program;
            ApplyView();
        }
    }

    /// <summary>Shows one tool's page (null: the "Program" page). The window keeps what was typed, so switching does not lose changes.</summary>
    public void ShowModule(string? module)
    {
        ClearSearch();
        if (module is null)
        {
            _view = SettingsView.Program;
            ApplyView();
            return;
        }

        // "petmap.pets" / "petmap.gather" are tabs of the pet map
        int dot = module.IndexOf('.');
        string tool = dot < 0 ? module : module[..dot];
        int tab = module switch { "petmap.pets" => 1, "petmap.gather" => 2, _ => 0 };
        OpenTool(tool, tab);
    }

    /// <summary>The hotkey page of the program settings.</summary>
    public void ShowHotkeysPage()
    {
        ClearSearch();
        _view = SettingsView.Program;
        _page = "NavHotkeys";
        NavHotkeys.IsChecked = true;
        ApplyView();
    }

    // ───────────────────────────── the page ─────────────────────────────

    private bool SearchActive => SearchBox.Text.Trim().Length > 0;

    private static ToolTab[] TabsOf(string? tool) => tool is not null && ToolTabsOf.TryGetValue(tool, out ToolTab[]? tabs) ? tabs : Array.Empty<ToolTab>();

    private void ApplyView()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string[] visible;
        string? headerTool = null;
        if (SearchActive)
        {
            NavBar.Visibility = Visibility.Collapsed;
            visible = new[] { "PanelSearch" };
            Title = loc["Settings.WindowTitle"];
        }
        else if (_view == SettingsView.Tools)
        {
            NavBar.Visibility = Visibility.Collapsed;
            visible = new[] { "PanelTools" };
            Title = loc["Settings.WindowTitle"];
            BuildToolTiles();
        }
        else if (_view == SettingsView.Tool && _module is not null)
        {
            NavBar.Visibility = Visibility.Collapsed;
            headerTool = _module;
            ToolTab[] tabs = TabsOf(_module);
            if (tabs.Length == 0)
            {
                visible = new[] { "PanelToolInfo" };
                ToolInfoText.Text = loc["Settings.Tools.OwnSettings"];
                ToolOpenButton.Tag = _module;
            }
            else
            {
                _tab = Math.Clamp(_tab, 0, tabs.Length - 1);
                visible = new[] { tabs[_tab].Panel };
                ShowCardsOfTab(tabs, _tab);
            }

            Title = NameOf(_module);
        }
        else
        {
            NavBar.Visibility = Visibility.Visible;
            visible = new[] { PageOfNav.GetValueOrDefault(_page, "PanelGeneral") };
            Title = loc["Settings.WindowTitle"];
        }

        ShowModuleHeader(headerTool);
        foreach (string name in AllPanels)
        {
            if (FindName(name) is UIElement panel)
            {
                panel.Visibility = visible.Contains(name) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        RefreshSideNav();
        if (visible.Contains("PanelBoss"))
        {
            RefreshBossTimes(force: true);
        }
    }

    /// <summary>Of a panel that is split into several tabs (the boss fight), only the cards of the chosen tab are shown.</summary>
    private void ShowCardsOfTab(ToolTab[] tabs, int chosen)
    {
        foreach (ToolTab tab in tabs.Where(t => t.Cards is not null))
        {
            if (FindName(tab.Panel) is Panel panel)
            {
                for (int i = 0; i < panel.Children.Count; i++)
                {
                    panel.Children[i].Visibility = tabs[chosen].Cards!.Contains(i) ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
    }

    // ───────────────────────────── header of a tool page ─────────────────────────────

    private (CheckBox? Master, TextBox? Hotkey) MasterOf(string module) => module switch
    {
        "network" => (ShowNetworkOverlayBox, HotkeyNetworkBox),
        "loot" => (ShowLootOverlayBox, HotkeyLootBox),
        "combat" => (ShowCombatOverlayBox, HotkeyCombatBox),
        "timetable" => (ShowTimetableBox, HotkeyTimetableBox),
        "boss" => (ShowBossOverlayBox, HotkeyWorldBossBox),
        "petmap" => (ShowPetMapBox, HotkeyPetMapBox),
        "stream" => (StreamOverlayOnBox, null),
        "assist" => (null, HotkeyAssistBox),
        "craft" => (null, HotkeyCraftBox),
        _ => (null, null),
    };

    // the hotkey chip sits beside the switch it controls: it is put next to the tool's own "show overlay" switch while the page is open
    private Button? _chip;
    private Panel? _chipParent;
    private DockPanel? _chipDock;
    private CheckBox? _chipMaster;
    private Thickness _chipMasterMargin;

    private void DetachChip()
    {
        if (_chipDock is not null && _chipParent is not null && _chipMaster is not null)
        {
            int index = _chipParent.Children.IndexOf(_chipDock);
            _chipDock.Children.Clear();
            _chipMaster.Margin = _chipMasterMargin;
            if (index >= 0)
            {
                _chipParent.Children[index] = _chipMaster;
            }
        }

        _chip = null;
        _chipParent = null;
        _chipDock = null;
        _chipMaster = null;
    }

    private void AttachChip(CheckBox master, TextBox hotkey)
    {
        if (master.Parent is not Panel parent)
        {
            return;
        }

        int index = parent.Children.IndexOf(master);
        _chipMasterMargin = master.Margin;
        var dock = new DockPanel { LastChildFill = true, Margin = master.Margin };
        _chip = new Button
        {
            Padding = new Thickness(10, 1, 10, 1),
            MinHeight = 24,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = LocalizationManager.Instance["Settings.Hotkey.ChipTip"],
        };
        string key = hotkey.Text;
        _chip.Content = key.Length > 0 ? key : LocalizationManager.Instance["Settings.Hotkey.NoKey"];
        _chip.Click += (_, _) => ShowHotkeysPage();
        DockPanel.SetDock(_chip, Dock.Right);
        master.Margin = new Thickness(0);
        parent.Children.RemoveAt(index);
        dock.Children.Add(_chip);
        dock.Children.Add(master);
        parent.Children.Insert(index, dock);
        _chipParent = parent;
        _chipDock = dock;
        _chipMaster = master;
    }

    private void ShowModuleHeader(string? module)
    {
        DetachChip();
        ModuleHeader.Visibility = module is null ? Visibility.Collapsed : Visibility.Visible;
        if (module is null)
        {
            return;
        }

        LocalizationManager loc = LocalizationManager.Instance;
        ModuleTitleText.Text = NameOf(module);
        string descKey = "Tool.Desc." + module;
        ModuleDescText.Text = loc[descKey];
        UpdateActiveText(module);
        ModuleActiveBox.IsChecked = !_disabled.Contains(module);
        var (master, hotkey) = MasterOf(module);
        if (master is not null && hotkey is not null)
        {
            AttachChip(master, hotkey);
        }

        BuildToolTabs(module);
    }

    private void UpdateActiveText(string module)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        bool on = !_disabled.Contains(module);
        ModuleActiveText.Text = loc[on ? "Tool.Active" : "Tool.Inactive"];
        var (master, hotkey) = MasterOf(module);
        bool overlay = master is not null || module is "assist" or "dummy" or "craft";
        bool key = hotkey is not null;
        ModuleActiveHint.Text = loc[overlay && key ? "Tool.InactiveHint.OverlayKey" : overlay ? "Tool.InactiveHint.Overlay" : "Tool.InactiveHint.None"];
    }

    private void OnModuleActiveClicked(object sender, RoutedEventArgs e)
    {
        if (_module is not { } module)
        {
            return;
        }

        SetToolActive(module, ModuleActiveBox.IsChecked == true);
        UpdateActiveText(module);
    }

    private void SetToolActive(string tool, bool active)
    {
        if (active)
        {
            _disabled.Remove(tool);
        }
        else
        {
            _disabled.Add(tool);
            // a switched-off tool shows no overlay: its own switch follows (the window's other boxes are read on Save)
            if (MasterOf(tool).Master is { } master)
            {
                master.IsChecked = false;
            }
        }

        RefreshSideNav();
    }

    private void BuildToolTabs(string module)
    {
        ToolTabs.Children.Clear();
        ToolTab[] tabs = TabsOf(module);
        ToolTabBar.Visibility = tabs.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (tabs.Length <= 1)
        {
            return;
        }

        for (int i = 0; i < tabs.Length; i++)
        {
            int index = i;
            var tab = new System.Windows.Controls.Primitives.ToggleButton
            {
                Content = LocalizationManager.Instance[tabs[i].TitleKey],
                Style = (Style)FindResource("PetTab"),
                IsChecked = i == _tab,
            };
            tab.Click += (_, _) =>
            {
                _tab = index;
                ApplyView();
            };
            ToolTabs.Children.Add(tab);
        }
    }

    private void OnToolOpenClicked(object sender, RoutedEventArgs e)
    {
        if (ToolOpenButton.Tag is string tool)
        {
            OpenToolRequested?.Invoke(tool);
        }
    }

    // ───────────────────────────── the tiles of "Tools" ─────────────────────────────

    private void BuildToolTiles()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        ToolTiles.Children.Clear();
        ToolsCount.Text = string.Format(loc["Settings.Tools.Count"], ToolSwitch.All.Length - _disabled.Count, ToolSwitch.All.Length);
        foreach (var (id, nameKey, descKey) in ToolSwitch.All)
        {
            string tool = id;
            var title = new TextBlock { Text = loc[nameKey], FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            var toggle = new CheckBox { IsChecked = !_disabled.Contains(tool), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            toggle.Click += (_, _) =>
            {
                SetToolActive(tool, toggle.IsChecked == true);
                BuildToolTiles();
            };
            var head = new DockPanel();
            DockPanel.SetDock(toggle, Dock.Right);
            head.Children.Add(toggle);
            head.Children.Add(title);
            var desc = new TextBlock { Text = loc[descKey], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8), FontSize = 12 };
            desc.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            var open = new Button { Content = loc["Settings.Tools.Open"] + " ›", Style = (Style)FindResource("GatherLink"), HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand };
            open.Click += (_, _) => OpenTool(tool, 0);
            var stack = new StackPanel();
            stack.Children.Add(head);
            stack.Children.Add(desc);
            stack.Children.Add(open);
            var tile = new Border { Style = (Style)FindResource("SettingsCard"), Width = 226, Margin = new Thickness(0, 0, 10, 10), Child = stack, MinHeight = 104 };
            if (_disabled.Contains(tool))
            {
                tile.Opacity = 0.6;
            }

            ToolTiles.Children.Add(tile);
        }
    }

    private void OnEnableAllToolsClicked(object sender, RoutedEventArgs e)
    {
        foreach (var (id, _, _) in ToolSwitch.All)
        {
            SetToolActive(id, true);
        }

        BuildToolTiles();
    }

    private void OnDisableAllToolsClicked(object sender, RoutedEventArgs e)
    {
        foreach (var (id, _, _) in ToolSwitch.All)
        {
            SetToolActive(id, false);
        }

        BuildToolTiles();
    }

    // ───────────────────────────── search ─────────────────────────────

    private sealed record Hit(string Text, FrameworkElement Element, string Panel);

    private List<Hit>? _index;

    private void ClearSearch()
    {
        if (SearchBox.Text.Length > 0)
        {
            SearchBox.Text = "";
        }
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ClearSearch();
            e.Handled = true;
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        if (SearchActive)
        {
            RunSearch(SearchBox.Text.Trim());
        }

        ApplyView();
    }

    /// <summary>The texts of the panels (labels of switches, headings, hints, buttons), collected once.</summary>
    private List<Hit> Index()
    {
        if (_index is not null)
        {
            return _index;
        }

        _index = new List<Hit>();
        foreach (string name in AllPanels.Where(n => n is not "PanelTools" and not "PanelToolInfo" and not "PanelSearch"))
        {
            if (FindName(name) is DependencyObject panel)
            {
                Collect(panel, name);
            }
        }

        return _index;
    }

    private void Collect(DependencyObject node, string panel)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(node))
        {
            if (child is not DependencyObject element)
            {
                continue;
            }

            switch (element)
            {
                case TextBlock { Text.Length: > 0 } text when text.Name != "SearchTitle":
                    _index!.Add(new Hit(text.Text, text, panel));
                    break;
                case ContentControl { Content: string content, Name: not "ToolOpenButton" } control when content.Length > 0:
                    _index!.Add(new Hit(content, control, panel));
                    break;
            }

            Collect(element, panel);
        }
    }

    /// <summary>Where a text is found: the view and the tool/tab it belongs to, written as a path ("Tools › Boss fight › Boss cast").</summary>
    private (string Crumb, Action Go) Locate(Hit hit)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        foreach (var (nav, panel) in PageOfNav)
        {
            if (panel == hit.Panel)
            {
                string tab = loc[TabKeyOfNav(nav)];
                return (loc["Settings.Nav.Program"] + " › " + tab, () =>
                {
                    _view = SettingsView.Program;
                    _page = nav;
                    ((RadioButton)FindName(nav)).IsChecked = true;
                });
            }
        }

        int card = CardIndex(hit);
        foreach (var (tool, tabs) in ToolTabsOf)
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                if (tabs[i].Panel == hit.Panel && (tabs[i].Cards is null || card < 0 || tabs[i].Cards!.Contains(card)))
                {
                    // card 0 of the boss fight is on every tab: the first one is shown
                    int tabIndex = i;
                    string crumb = loc["Main.MenuTools"] + " › " + NameOf(tool) + (tabs.Length > 1 ? " › " + loc[tabs[i].TitleKey] : "");
                    return (crumb, () => OpenTool(tool, tabIndex));
                }
            }
        }

        return (loc["Settings.WindowTitle"], () => { });
    }

    private static string TabKeyOfNav(string nav) => nav switch
    {
        "NavGeneral" => "Settings.Nav.General",
        "NavMeter" => "Settings.Nav.Meter",
        "NavInstall" => "Settings.Nav.Capture",
        "NavHotkeys" => "Settings.Hotkeys",
        _ => "Settings.Nav.System",
    };

    private int CardIndex(Hit hit)
    {
        if (FindName(hit.Panel) is not Panel panel)
        {
            return -1;
        }

        DependencyObject? node = hit.Element;
        while (node is not null)
        {
            DependencyObject? parent = LogicalTreeHelper.GetParent(node);
            if (parent == panel)
            {
                return panel.Children.IndexOf((UIElement)node);
            }

            node = parent;
        }

        return -1;
    }

    private void RunSearch(string query)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        SearchResults.Children.Clear();
        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var shown = new HashSet<string>();
        int count = 0;
        foreach (Hit hit in Index())
        {
            if (!words.All(w => hit.Text.Contains(w, StringComparison.CurrentCultureIgnoreCase)))
            {
                continue;
            }

            var (crumb, go) = Locate(hit);
            string line = crumb + "\n" + hit.Text;
            if (!shown.Add(line))
            {
                continue;
            }

            count++;
            if (count > 60)
            {
                break;
            }

            FrameworkElement target = hit.Element;
            var crumbText = new TextBlock { Text = crumb, FontSize = 11 };
            crumbText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            var label = new TextBlock { Text = hit.Text, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 40 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            var stack = new StackPanel();
            stack.Children.Add(crumbText);
            stack.Children.Add(label);
            var row = new Border { Child = stack, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            row.MouseEnter += (_, _) => row.Background = ItemHover;
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (_, _) =>
            {
                ClearSearch();
                go();
                ApplyView();
                Dispatcher.BeginInvoke(new Action(() => target.BringIntoView()), System.Windows.Threading.DispatcherPriority.Loaded);
            };
            SearchResults.Children.Add(row);
        }

        SearchTitle.Text = count == 0 ? string.Format(loc["Settings.Search.None"], query) : string.Format(loc["Settings.Search.Results"], count, query);
    }
}
