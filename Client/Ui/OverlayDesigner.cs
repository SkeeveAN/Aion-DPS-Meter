using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AionDPS.Ui;

/// <summary>
/// The overlay designer (2.0): a hotkey puts the program into a mode in which every overlay can be moved, scaled and cropped at once. The game behind is dimmed, a grid
/// is drawn, a small bar says how to leave, and a round "+" lists the overlays that are off. Right-click on an overlay sets how clear its content and its background are.
/// While the mode is on the "locked" flags of all overlays read as unlocked (<see cref="Override"/>); they are never written to the file that way (<see cref="RestoreLocks"/>).
/// </summary>
public static class OverlayDesigner
{
    private static readonly string[] LockFlags =
    {
        "AssistOverlayLocked", "NetworkOverlayLocked", "LootOverlayLocked", "CraftOverlayLocked", "DummyOverlayLocked", "CombatOverlayLocked",
        "CombatCastOverlayLocked", "CombatHpOverlayLocked", "PetFarmLocked", "PetInfoLocked", "BossOverlayLocked", "TimetableLocked", "PetListLocked", "HotkeyOverlayLocked",
    };

    private static readonly PropertyInfo[] LockProps = LockFlags.Select(n => typeof(MeterSettings).GetProperty(n)!).ToArray();
    private static object?[]? _saved;
    private static DesignerBackdrop? _backdrop;
    private static DesignerBar? _bar;
    private static DesignerPlus? _plus;
    private static OpacityPanel? _panel;
    private static Action? _refresh;

    public static bool Active { get; private set; }

    /// <summary>Switches the mode on or off. <paramref name="refresh"/> makes the overlays read the settings again (called after every change).</summary>
    public static void Toggle(Action refresh)
    {
        if (Active)
        {
            End();
        }
        else
        {
            Begin(refresh);
        }
    }

    private static void Begin(Action refresh)
    {
        var disk = MeterSettings.LoadRaw();
        _saved = LockProps.Select(p => p.GetValue(disk)).ToArray();
        _refresh = refresh;
        Active = true;
        _backdrop = new DesignerBackdrop();
        _bar = new DesignerBar(refresh);
        _plus = new DesignerPlus(refresh);
        _backdrop.Show();
        refresh();
        _bar.Show();
        _plus.Show();
        BringToFront();
    }

    private static void End()
    {
        Active = false;
        _panel?.Close();
        _panel = null;
        _bar?.Close();
        _plus?.Close();
        _backdrop?.Close();
        _bar = null;
        _plus = null;
        _backdrop = null;
        _saved = null;
        _refresh?.Invoke();
    }

    /// <summary>The overlays above the dimming, the bar and the "+" above the overlays.</summary>
    public static void BringToFront()
    {
        foreach (MovableOverlay overlay in MovableOverlay.All.ToList())
        {
            overlay.RaiseToFront();
        }

        _bar?.RaiseToFront();
        _plus?.RaiseToFront();
        _panel?.RaiseToFront();
    }

    /// <summary>Called by a settings file that was just read: while the mode is on, every overlay reads as unlocked.</summary>
    public static void Override(MeterSettings s)
    {
        if (!Active)
        {
            return;
        }

        foreach (PropertyInfo p in LockProps)
        {
            p.SetValue(s, p.PropertyType == typeof(bool?) ? (bool?)false : false);
        }
    }

    /// <summary>Called before a settings file is written: the real values of the locks go to the file, not the unlocked reading.</summary>
    public static void RestoreLocks(MeterSettings s)
    {
        if (!Active || _saved is null)
        {
            return;
        }

        for (int i = 0; i < LockProps.Length; i++)
        {
            LockProps[i].SetValue(s, _saved[i]);
        }
    }

    // ───────────────────────────── the overlays and what "off" means for each ─────────────────────────────

    /// <summary>One overlay as the "+" lists it: its name, whether it is on, how to switch it on.</summary>
    private sealed record Entry(string NameKey, Func<MeterSettings, bool> On, Action<MeterSettings> SwitchOn);

    private static readonly Entry[] Entries =
    {
        new("Settings.PetMap.CardMap", s => s.ShowPetMap && s.PetMapShowMap, s => { s.ShowPetMap = true; s.PetMapShowMap = true; }),
        new("Settings.PetMap.CardList", s => s.ShowPetMap && s.PetMapShowList, s => { s.ShowPetMap = true; s.PetMapShowList = true; }),
        new("Settings.Nav.Pets", s => s.ShowPetFarm, s => s.ShowPetFarm = true),
        new("Settings.Combat.CardBossHp", s => s.ShowCombatOverlay && s.ShowBossHp, s => { s.ShowCombatOverlay = true; s.ShowBossHp = true; }),
        new("Settings.Combat.CardBossCast", s => s.ShowCombatOverlay && s.CombatShowBossCast, s => { s.ShowCombatOverlay = true; s.CombatShowBossCast = true; }),
        new("Combat.Element.TargetOfTarget", s => s.ShowCombatOverlay && s.CombatShowTargetOfTarget, s => { s.ShowCombatOverlay = true; s.CombatShowTargetOfTarget = true; }),
        new("Main.MenuTools.Loot", s => s.ShowLootOverlay, s => s.ShowLootOverlay = true),
        new("Main.MenuTools.Craft", s => s.ShowCraftOverlay, s => s.ShowCraftOverlay = true),
        new("Settings.Nav.Network", s => s.ShowNetworkOverlay, s => s.ShowNetworkOverlay = true),
        new("Main.MenuTools.Assist", s => s.ShowAssistOverlay, s => s.ShowAssistOverlay = true),
        new("Main.MenuTools.DummyTest", s => s.ShowDummyOverlay, s => s.ShowDummyOverlay = true),
        new("Settings.Nav.Timetable", s => s.ShowTimetable, s => s.ShowTimetable = true),
        new("Settings.Nav.Boss", s => s.ShowBossOverlay, s => s.ShowBossOverlay = true),
        new("Settings.Hotkey.HotkeyOverlay", s => s.ShowHotkeyOverlay, s => s.ShowHotkeyOverlay = true),
    };

    /// <summary>The tools that are switched off do not appear in the "+" (their overlays cannot be on).</summary>
    private static readonly Dictionary<string, string> ToolOfName = new()
    {
        ["Settings.PetMap.CardMap"] = "petmap", ["Settings.PetMap.CardList"] = "petmap", ["Settings.Nav.Pets"] = "petmap",
        ["Settings.Combat.CardBossHp"] = "combat", ["Settings.Combat.CardBossCast"] = "combat", ["Combat.Element.TargetOfTarget"] = "combat",
        ["Main.MenuTools.Loot"] = "loot", ["Main.MenuTools.Craft"] = "craft", ["Settings.Nav.Network"] = "network", ["Main.MenuTools.Assist"] = "assist",
        ["Main.MenuTools.DummyTest"] = "dummy", ["Settings.Nav.Timetable"] = "timetable", ["Settings.Nav.Boss"] = "boss",
    };

    internal static List<(string Name, Action SwitchOn)> Off()
    {
        var s = MeterSettings.Load();
        var loc = LocalizationManager.Instance;
        var list = new List<(string, Action)>();
        foreach (Entry e in Entries)
        {
            if (e.On(s) || (ToolOfName.TryGetValue(e.NameKey, out string? tool) && !ToolSwitch.IsOn(s, tool)))
            {
                continue;
            }

            Entry entry = e;
            list.Add((loc[e.NameKey], () =>
            {
                var fresh = MeterSettings.Load();
                entry.SwitchOn(fresh);
                fresh.Save();
                _refresh?.Invoke();
                BringToFront();
            }));
        }

        return list;
    }

    // ───────────────────────────── the opacity of one overlay ─────────────────────────────

    internal static void ShowOpacityPanel(MovableOverlay overlay)
    {
        if (!Active)
        {
            return;
        }

        _panel?.Close();
        _panel = new OpacityPanel(overlay.Title, Debounced) { Left = overlay.Left + 12, Top = overlay.Top + 12 };
        _panel.Closed += (_, _) => _panel = null;
        _panel.Show();
        _panel.RaiseToFront();
    }

    private static System.Windows.Threading.DispatcherTimer? _debounce;

    /// <summary>A slider moves many times a second: the overlays read the settings again once it rests for a moment.</summary>
    private static void Debounced()
    {
        _debounce ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounce.Tick -= OnDebounce;
        _debounce.Tick += OnDebounce;
        _debounce.Stop();
        _debounce.Start();
    }

    private static void OnDebounce(object? sender, EventArgs e)
    {
        _debounce?.Stop();
        if (Active)
        {
            _refresh?.Invoke();
            BringToFront();
        }
    }

    /// <summary>The background opacity of an overlay as it is stored (a few overlays keep it in a field of their own).</summary>
    internal static double GetBackground(MeterSettings s, string title) => title switch
    {
        OverlayPlaces.BossHp => s.BossHpOpacity,
        OverlayPlaces.BossCast => s.CombatCastOpacity,
        OverlayPlaces.TargetOfTarget => s.CombatTargetOpacity,
        OverlayPlaces.PetMap => s.PetMapOpacity,
        OverlayPlaces.Timetable => s.TimetableOpacity ?? s.OverlayOpacity,
        _ => s.OverlayOpacities.GetValueOrDefault(title, 0.6),
    };

    internal static void SetBackground(MeterSettings s, string title, double value)
    {
        switch (title)
        {
            case OverlayPlaces.BossHp: s.BossHpOpacity = value; break;
            case OverlayPlaces.BossCast: s.CombatCastOpacity = value; break;
            case OverlayPlaces.TargetOfTarget: s.CombatTargetOpacity = value; break;
            case OverlayPlaces.PetMap: s.PetMapOpacity = value; break;
            case OverlayPlaces.Timetable: s.TimetableOpacity = value; break;
            default: s.OverlayOpacities[title] = value; break;
        }
    }

    /// <summary>Only the overlays that fade as a whole (the boss fight, the map, the timetable) have a single opacity; the others split content and background.</summary>
    internal static bool HasSplit(string title) => title is not (OverlayPlaces.BossHp or OverlayPlaces.BossCast or OverlayPlaces.TargetOfTarget or OverlayPlaces.PetMap or OverlayPlaces.Timetable);

    internal static readonly Brush Navy = Frozen(0x0f, 0x1c, 0x27, 0xee), Line = Frozen(0x29, 0x45, 0x57, 0xff), Orange = Frozen(0xff, 0xba, 0x42, 0xff), Ink = Frozen(0x1a, 0x12, 0x05, 0xff),
        Text = Frozen(0xea, 0xf2, 0xf8, 0xff), Muted = Frozen(0x9d, 0xb3, 0xc2, 0xff), Panel = Frozen(0x1b, 0x31, 0x42, 0xff);

    private static Brush Frozen(byte r, byte g, byte b, byte a)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    internal static Button Chip(string text, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(11, 4, 11, 4),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            Foreground = primary ? Ink : Text,
            Background = primary ? Orange : Panel,
            BorderBrush = primary ? Orange : Line,
            BorderThickness = new Thickness(1),
            FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
        };
        button.Template = ButtonTemplate();
        return button;
    }

    private static ControlTemplate ButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    internal static Window BaseWindow()
    {
        var w = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            SizeToContent = SizeToContent.WidthAndHeight,
        };
        AltTabHidden.Apply(w);
        return w;
    }
}

/// <summary>The dimming and the grid over the whole first monitor; clicks go through it to the game.</summary>
internal sealed class DesignerBackdrop : Window
{
    public DesignerBackdrop()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AltTabHidden.Apply(this);
        Rect area = WindowPlacement.Primary();
        Left = area.Left;
        Top = area.Top;
        Width = area.Width;
        Height = area.Height;
        Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x05, 0x0A, 0x10));
        var settings = MeterSettings.Load();
        double step = settings.OverlayGridSnap ? Math.Max(10, settings.OverlayGridSize) : 60;
        var grid = new Canvas { IsHitTestVisible = false, SnapsToDevicePixels = true, UseLayoutRounding = true };
        Brush lineBrush = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xBA, 0x42));
        for (double x = 0; x < area.Width; x += step)
        {
            grid.Children.Add(new Rectangle { Width = 1, Height = area.Height, Fill = lineBrush, SnapsToDevicePixels = true, Margin = new Thickness(Math.Round(x), 0, 0, 0) });
        }

        for (double y = 0; y < area.Height; y += step)
        {
            grid.Children.Add(new Rectangle { Width = area.Width, Height = 1, Fill = lineBrush, SnapsToDevicePixels = true, Margin = new Thickness(0, Math.Round(y), 0, 0) });
        }

        Content = grid;
        SourceInitialized += (_, _) =>
        {
            using var native = new NativeOverlay(this);
            native.SetClickThrough(true);
        };
    }
}

/// <summary>The bar at the bottom: how to leave, the grid switch, "Done". It can be dragged by its grip and folded to a small button.</summary>
internal sealed class DesignerBar : Window
{
    private readonly StackPanel _full = new() { Orientation = Orientation.Horizontal };

    public DesignerBar(Action refresh)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        AltTabHidden.Apply(this);
        var loc = LocalizationManager.Instance;
        var settings = MeterSettings.Load();
        var grip = new TextBlock { Text = "⋮⋮", Foreground = OverlayDesigner.Muted, FontSize = 18, Margin = new Thickness(2, 0, 10, 0), Cursor = Cursors.SizeAll, VerticalAlignment = VerticalAlignment.Center };
        grip.MouseLeftButtonDown += (_, _) => DragMove();
        var title = new TextBlock { Text = loc["Designer.Title"], Foreground = OverlayDesigner.Text, FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        string key = HotkeyBinding.Parse(settings.HotkeyDesigner).ToString();
        var hint = new TextBlock
        {
            Text = loc["Designer.Hint"] + "   ·   " + string.Format(loc["Designer.Exit"], key),
            Foreground = OverlayDesigner.Muted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
        };
        var gridButton = OverlayDesigner.Chip(loc["Designer.Grid"]);
        gridButton.Click += (_, _) =>
        {
            var s = MeterSettings.Load();
            s.OverlayGridSnap = !s.OverlayGridSnap;
            s.Save();
            gridButton.Background = s.OverlayGridSnap ? OverlayDesigner.Orange : OverlayDesigner.Panel;
            gridButton.Foreground = s.OverlayGridSnap ? OverlayDesigner.Ink : OverlayDesigner.Text;
            refresh();
        };
        if (settings.OverlayGridSnap)
        {
            gridButton.Background = OverlayDesigner.Orange;
            gridButton.Foreground = OverlayDesigner.Ink;
        }

        var done = OverlayDesigner.Chip(loc["Designer.Done"], primary: true);
        done.Margin = new Thickness(0);
        done.Click += (_, _) => OverlayDesigner.Toggle(refresh);
        _full.Children.Add(title);
        _full.Children.Add(hint);
        _full.Children.Add(gridButton);
        _full.Children.Add(done);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(grip);
        row.Children.Add(_full);
        var fold = new TextBlock { Text = "▾", Foreground = OverlayDesigner.Muted, FontSize = 16, Margin = new Thickness(12, 0, 2, 0), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        fold.MouseLeftButtonUp += (_, _) =>
        {
            _full.Visibility = _full.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            fold.Text = _full.Visibility == Visibility.Visible ? "▾" : "▴";
        };
        row.Children.Add(fold);
        Content = new Border
        {
            Child = row,
            Background = OverlayDesigner.Navy,
            BorderBrush = OverlayDesigner.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 8, 10, 8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.5 },
        };
        Rect area = WindowPlacement.Primary();
        Loaded += (_, _) =>
        {
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Bottom - ActualHeight - 24;
        };
    }

    public void RaiseToFront() => NativeOverlay.KeepOnTop(new System.Windows.Interop.WindowInteropHelper(this).Handle);
}

/// <summary>The round "+" at the bottom right and the list of the overlays that are off.</summary>
internal sealed class DesignerPlus : Window
{
    private readonly StackPanel _list = new();
    private readonly Border _palette;
    private readonly TextBlock _fab;
    private readonly Action _refresh;
    private Point _anchor;

    public DesignerPlus(Action refresh)
    {
        _refresh = refresh;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        AltTabHidden.Apply(this);
        var loc = LocalizationManager.Instance;
        var title = new TextBlock { Text = loc["Designer.Add"], Foreground = OverlayDesigner.Text, FontWeight = FontWeights.SemiBold, FontSize = 16 };
        var hint = new TextBlock { Text = loc["Designer.AddHint"], Foreground = OverlayDesigner.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8), MaxWidth = 330 };
        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(hint);
        stack.Children.Add(_list);
        _palette = new Border
        {
            Child = stack,
            Background = OverlayDesigner.Navy,
            BorderBrush = OverlayDesigner.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10),
            Width = 360,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _fab = new TextBlock { Text = "+", FontSize = 34, FontWeight = FontWeights.SemiBold, Foreground = OverlayDesigner.Ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -4, 0, 0) };
        var fab = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(28),
            Background = OverlayDesigner.Orange,
            Child = _fab,
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.55 },
        };
        fab.MouseLeftButtonUp += (_, _) => TogglePalette();
        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(_palette);
        root.Children.Add(fab);
        Content = root;
        Rect area = WindowPlacement.Primary();
        _anchor = new Point(area.Right - 24, area.Bottom - 24);
        Loaded += (_, _) => Place();
        SizeChanged += (_, _) => Place();
    }

    private void Place()
    {
        Left = _anchor.X - ActualWidth;
        Top = _anchor.Y - ActualHeight;
    }

    private void TogglePalette()
    {
        bool open = _palette.Visibility != Visibility.Visible;
        _palette.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        _fab.Text = open ? "×" : "+";
        if (open)
        {
            Fill();
        }
    }

    private void Fill()
    {
        var loc = LocalizationManager.Instance;
        _list.Children.Clear();
        var off = OverlayDesigner.Off();
        if (off.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = loc["Designer.AddNone"], Foreground = OverlayDesigner.Muted, Margin = new Thickness(0, 4, 0, 4) });
            return;
        }

        foreach (var (name, switchOn) in off)
        {
            var row = new DockPanel();
            var add = new TextBlock { Text = loc["Designer.AddButton"], Foreground = OverlayDesigner.Orange, FontWeight = FontWeights.SemiBold };
            DockPanel.SetDock(add, Dock.Right);
            row.Children.Add(add);
            row.Children.Add(new TextBlock { Text = name, Foreground = OverlayDesigner.Text });
            var item = new Border { Child = row, Background = OverlayDesigner.Panel, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 6), Cursor = Cursors.Hand };
            item.MouseLeftButtonUp += (_, _) =>
            {
                switchOn();
                Fill();
            };
            _list.Children.Add(item);
        }
    }

    public void RaiseToFront() => NativeOverlay.KeepOnTop(new System.Windows.Interop.WindowInteropHelper(this).Handle);
}

/// <summary>The little window of a right-click on an overlay: how clear its content and its background are, reset, hide.</summary>
internal sealed class OpacityPanel : Window
{
    public OpacityPanel(string title, Action refresh)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        AltTabHidden.Apply(this);
        var loc = LocalizationManager.Instance;
        var settings = MeterSettings.Load();
        bool split = OverlayDesigner.HasSplit(title);
        var stack = new StackPanel { MinWidth = 300 };
        stack.Children.Add(new TextBlock { Text = loc["Designer.Opacity"], Foreground = OverlayDesigner.Text, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });
        if (split)
        {
            stack.Children.Add(Row(loc["Designer.Content"], settings.OverlayForegrounds.GetValueOrDefault(title, 1.0), v =>
            {
                var s = MeterSettings.Load();
                s.OverlayForegrounds[title] = v;
                s.Save();
                refresh();
            }));
        }

        stack.Children.Add(Row(split ? loc["Designer.Background"] : loc["Designer.Opacity"], OverlayDesigner.GetBackground(settings, title), v =>
        {
            var s = MeterSettings.Load();
            OverlayDesigner.SetBackground(s, title, v);
            s.Save();
            refresh();
        }));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var reset = OverlayDesigner.Chip(loc["Designer.Reset"]);
        reset.Click += (_, _) =>
        {
            OverlayPlaces.Reset(title);
            var s = MeterSettings.Load();
            s.OverlayForegrounds.Remove(title);
            s.Save();
            refresh();
            Close();
        };
        var close = OverlayDesigner.Chip(loc["Designer.Done"], primary: true);
        close.Margin = new Thickness(0);
        close.Click += (_, _) => Close();
        buttons.Children.Add(reset);
        buttons.Children.Add(close);
        stack.Children.Add(buttons);
        Content = new Border
        {
            Child = stack,
            Background = OverlayDesigner.Navy,
            BorderBrush = OverlayDesigner.Orange,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.5 },
        };
    }

    private static UIElement Row(string label, double value, Action<double> changed)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        var name = new TextBlock { Text = label, Foreground = OverlayDesigner.Text, VerticalAlignment = VerticalAlignment.Center };
        var percent = new TextBlock { Foreground = OverlayDesigner.Text, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Text = Math.Round(value * 100) + " %" };
        var slider = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, Value = Math.Round(value * 100), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        if (Application.Current?.TryFindResource("ThemedSlider") is Style style)
        {
            slider.Style = style;
        }

        slider.ValueChanged += (_, e) =>
        {
            percent.Text = Math.Round(e.NewValue) + " %";
            changed(e.NewValue / 100.0);
        };
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(percent, 2);
        grid.Children.Add(name);
        grid.Children.Add(slider);
        grid.Children.Add(percent);
        return grid;
    }

    public void RaiseToFront() => NativeOverlay.KeepOnTop(new System.Windows.Interop.WindowInteropHelper(this).Handle);
}
