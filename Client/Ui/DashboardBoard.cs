using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AionDPS.Ui;

/// <summary>
/// The overview of the dashboard as a board: every widget is a frame that can be moved (by its title) and sized (by the corner), a ✕ takes it off and a + at the
/// top right puts one back. Places are kept in the settings; the contents are drawn again every second.
/// </summary>
public sealed class DashboardBoard : DockPanel
{
    private const double Cell = 10, MinW = 240, MinH = 110, Margin8 = 10, Pull = 9;
    private readonly DashboardSources _src;
    private readonly Canvas _canvas = new();
    private readonly Dictionary<string, (Border Frame, Border Body)> _frames = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Border _add = new();
    private readonly WrapPanel _tabs = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<string, UIElement> _cached = new();
    private readonly Dictionary<string, ToolPage> _pages = new();
    private List<DashboardSet> _sets;
    private int _active;
    private Dictionary<string, DashboardPlace> _places = new();

    /// <summary>A widget: a built-in one (see <see cref="HubDashboard.Widgets"/>) or a page of the app ("page:" and its id).</summary>
    private sealed record Spec(string Id, string Title, string? Opens, double W, double H, bool Cached);

    private Spec? SpecOf(string id)
    {
        if (id.StartsWith("page:", StringComparison.Ordinal))
        {
            HubPageInfo? page = _src.Pages().FirstOrDefault(p => "page:" + p.Id == id);
            return page is null ? null : new Spec(id, page.Title, null, 520, 400, true);
        }

        var w = HubDashboard.Widgets.FirstOrDefault(x => x.Id == id);
        return w is null ? null : new Spec(id, LocalizationManager.Instance[w.TitleKey], w.Opens, w.W, w.H, w.Static);
    }

    public DashboardBoard(DashboardSources src)
    {
        _src = src;
        var loc = LocalizationManager.Instance;
        MeterSettings settings = MeterSettings.Load();
        _sets = settings.Dashboards;
        if (_sets.Count == 0)
        {
            // the first start (or a layout of an earlier version): the standard dashboard
            _sets.Add(new DashboardSet { Widgets = settings.Dashboard.Count > 0 ? settings.Dashboard : FirstLayout() });
        }

        _active = Math.Clamp(settings.DashboardActive, 0, _sets.Count - 1);
        foreach (DashboardSet set in _sets)
        {
            // places from an earlier version may stand between the grid lines: onto the grid, so edges can be brought level
            foreach (DashboardPlace p in set.Widgets.Values)
            {
                p.X = Snap(p.X);
                p.Y = Snap(p.Y);
                p.W = Math.Max(MinW, Snap(p.W));
                p.H = Math.Max(MinH, Snap(p.H));
            }
        }

        // the first dashboard as the earlier versions made it and never changed: the new standard takes its place
        if (SameLayout(_sets[0].Widgets, EarlierStandard()))
        {
            _sets[0].Widgets = FirstLayout();
        }

        _places = _sets[_active].Widgets;
        // the target power widget (new in 2.0.0-beta.34) is put once on the first dashboard of everybody, so they see it exists; a widget taken off again stays off
        if (!settings.TargetPowerWidgetOffered)
        {
            settings.TargetPowerWidgetOffered = true;
            if (!_sets[0].Widgets.ContainsKey("targetpower"))
            {
                var active = _places;
                _places = _sets[0].Widgets;
                _places["targetpower"] = Slot(330, 110);
                _places = active;
            }

            settings.Dashboards = _sets;
            settings.Save();
        }

        Save();

        // round, a strong ring in the accent of the theme that fades towards the middle
        Color accent = (Application.Current?.TryFindResource("Brush.Accent") as SolidColorBrush)?.Color ?? Color.FromRgb(0xff, 0xba, 0x42);
        var fill = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5), RadiusX = 0.55, RadiusY = 0.55 };
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, accent.R, accent.G, accent.B), 0.0));
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x30, accent.R, accent.G, accent.B), 0.65));
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, accent.R, accent.G, accent.B), 1.0));
        var plus = new TextBlock { Text = "＋", FontSize = 22, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) };
        plus.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        _add.Width = 44;
        _add.Height = 44;
        _add.CornerRadius = new CornerRadius(22);
        _add.BorderThickness = new Thickness(1.5);
        _add.Background = fill;
        _add.Child = plus;
        _add.ToolTip = loc["Hub.Dash.AddWidget"];
        _add.Margin = new Thickness(0, 6, 16, 0);
        _add.Cursor = Cursors.Hand;
        _add.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
        _add.MouseEnter += (_, _) => _add.Opacity = 0.85;
        _add.MouseLeave += (_, _) => _add.Opacity = 1.0;
        _add.MouseLeftButtonUp += (_, _) => ShowAddMenu();
        var top = new DockPanel { Margin = new Thickness(16, 0, 0, 0) };
        SetDock(_add, Dock.Right);
        top.Children.Add(_add);
        _tabs.Margin = new Thickness(0, 10, 0, 0);
        top.Children.Add(_tabs);
        SetDock(top, Dock.Top);
        Children.Add(top);
        _canvas.HorizontalAlignment = HorizontalAlignment.Left;
        _canvas.VerticalAlignment = VerticalAlignment.Top;
        Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _canvas });
        BuildTabs();
        Build();
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
    }

    private string NameOf(int index) => _sets[index].Name.Length > 0 ? _sets[index].Name : index == 0 ? LocalizationManager.Instance["Hub.Dash.DefaultName"] : string.Format(LocalizationManager.Instance["Hub.Dash.NewName"], index + 1);

    /// <summary>The row of dashboards: one chip each (the shown one with a pencil at its edge) and a small plus for a new one.</summary>
    private void BuildTabs()
    {
        var loc = LocalizationManager.Instance;
        _tabs.Children.Clear();
        for (int i = 0; i < _sets.Count; i++)
        {
            int index = i;
            bool on = i == _active;
            var label = new TextBlock { Text = NameOf(i), FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, on ? "Brush.Accent" : "Brush.TextMuted");
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(label);
            TextBlock? pencil = null;
            if (on)
            {
                pencil = new TextBlock { Text = "\uE70F", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, ToolTip = loc["Hub.Dash.Rename"] };
                pencil.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
                row.Children.Add(pencil);
            }

            var chip = new Border { Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = row, Background = Brushes.Transparent };
            chip.SetResourceReference(Border.BorderBrushProperty, on ? "Brush.Accent" : "Brush.Border");
            chip.MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is TextBlock { Text: "\uE70F" })
                {
                    return;
                }

                SwitchTo(index);
            };
            if (pencil is not null)
            {
                pencil.MouseLeftButtonUp += (_, e) =>
                {
                    ShowEditPopup(chip);
                    e.Handled = true;
                };
            }

            _tabs.Children.Add(chip);
        }

        var plus = new TextBlock { Text = "＋", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        plus.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        var small = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = plus, ToolTip = loc["Hub.Dash.AddDashboard"], Background = Brushes.Transparent, Margin = new Thickness(0, 0, 8, 0) };
        small.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        small.MouseLeftButtonUp += (_, _) => ShowTemplatePopup(small);
        _tabs.Children.Add(small);
    }

    private void SwitchTo(int index)
    {
        if (index == _active && _canvas.Children.Count > 0)
        {
            return;
        }

        ReleasePages();
        _active = index;
        _places = _sets[_active].Widgets;
        Save();
        BuildTabs();
        Build();
    }

    /// <summary>The inline popup of the pencil: the name of the dashboard to change, and Delete (asks once more; the last dashboard cannot be deleted).</summary>
    private void ShowEditPopup(FrameworkElement anchor)
    {
        var loc = LocalizationManager.Instance;
        var box = new TextBox { Text = _sets[_active].Name.Length > 0 ? _sets[_active].Name : NameOf(_active), MinWidth = 200, Padding = new Thickness(6, 4, 6, 4) };
        var popup = new System.Windows.Controls.Primitives.Popup { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, VerticalOffset = 4 };
        void Apply()
        {
            string name = box.Text.Trim();
            _sets[_active].Name = name.Length == 0 || (_active == 0 && name == loc["Hub.Dash.DefaultName"]) ? "" : name;
            Save();
            BuildTabs();
            popup.IsOpen = false;
        }

        var ok = new Button { Content = loc["Hub.Dash.Rename"], Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0) };
        ok.Click += (_, _) => Apply();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Apply();
            }
            else if (e.Key == Key.Escape)
            {
                popup.IsOpen = false;
            }
        };
        var nameRow = new DockPanel();
        SetDock(ok, Dock.Right);
        nameRow.Children.Add(ok);
        nameRow.Children.Add(box);
        var stack = new StackPanel { Margin = new Thickness(0) };
        stack.Children.Add(nameRow);
        if (_sets.Count > 1)
        {
            var delete = new Button { Content = loc["Hub.Dash.Delete"], Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            delete.SetResourceReference(Control.ForegroundProperty, "Brush.Danger");
            bool asked = false;
            delete.Click += (_, _) =>
            {
                if (!asked)
                {
                    asked = true;
                    delete.Content = loc["Hub.Dash.DeleteSure"];
                    return;
                }

                popup.IsOpen = false;
                ReleasePages();
                _sets.RemoveAt(_active);
                _active = Math.Max(0, _active - 1);
                _places = _sets[_active].Widgets;
                Save();
                BuildTabs();
                Build();
            };
            stack.Children.Add(delete);
        }

        var frame = new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = stack, Margin = new Thickness(0, 0, 12, 12), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.5 } };
        frame.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        frame.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
        popup.Child = frame;
        popup.IsOpen = true;
        popup.Opened += (_, _) => { };
        box.Dispatcher.BeginInvoke(new Action(() => { box.Focus(); box.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Ends the pages that stand as widgets (their timers); a dashboard that is shown again builds them again.</summary>
    private void ReleasePages()
    {
        foreach (ToolPage page in _pages.Values)
        {
            page.Cleanup?.Invoke();
        }

        _pages.Clear();
        _cached.Clear();
    }

    public void Stop()
    {
        _timer.Stop();
        ReleasePages();
    }

    /// <summary>The standard dashboard: the live meter, what is to do today, game and character, the figures and the last boss fight.</summary>
    private static Dictionary<string, DashboardPlace> FirstLayout() => new()
    {
        ["meter"] = new DashboardPlace { X = 10, Y = 10, W = 390, H = 250 },
        ["todo"] = new DashboardPlace { X = 410, Y = 10, W = 270, H = 250 },
        ["connection"] = new DashboardPlace { X = 10, Y = 270, W = 390, H = 170 },
        ["figures"] = new DashboardPlace { X = 410, Y = 270, W = 270, H = 170 },
        ["last"] = new DashboardPlace { X = 10, Y = 450, W = 390, H = 190 },
        ["targetpower"] = new DashboardPlace { X = 410, Y = 450, W = 330, H = 110 },
    };

    /// <summary>The standard dashboard of the versions before: pairs side by side, both of a pair as high as the higher one. Only used to find a dashboard that was never changed.</summary>
    private static Dictionary<string, DashboardPlace> EarlierStandard()
    {
        var result = new Dictionary<string, DashboardPlace>();
        var shown = HubDashboard.Widgets.Where(w => !w.Static && w.Id != "targetpower").ToList(); // the target power came later: the earlier standard had none
        double y = Margin8;
        for (int i = 0; i < shown.Count; i += 2)
        {
            var pair = shown.Skip(i).Take(2).ToList();
            double height = pair.Max(w => w.H);
            for (int k = 0; k < pair.Count; k++)
            {
                result[pair[k].Id] = new DashboardPlace { X = Margin8 + k * (520 + Margin8), Y = y, W = 520, H = height };
            }

            y += height + Margin8;
        }

        return result;
    }

    private static bool SameLayout(Dictionary<string, DashboardPlace> a, Dictionary<string, DashboardPlace> b) =>
        a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out DashboardPlace? o) && p.Value.X == o.X && p.Value.Y == o.Y && p.Value.W == o.W && p.Value.H == o.H);

    private void Save()
    {
        var s = MeterSettings.Load();
        s.Dashboards = _sets;
        s.DashboardActive = _active;
        s.Save();
    }

    private void Build()
    {
        _canvas.Children.Clear();
        _frames.Clear();
        foreach (string id in _places.Keys.ToList())
        {
            if (SpecOf(id) is { } spec)
            {
                AddFrame(spec);
            }
            else
            {
                _places.Remove(id); // a page that is not there any more
            }
        }

        Refresh();
        FitCanvas();
    }

    private void AddFrame(Spec widget)
    {
        var loc = LocalizationManager.Instance;
        DashboardPlace place = _places[widget.Id];
        var body = new Border { Padding = new Thickness(0, 4, 0, 0), ClipToBounds = true };
        var title = new TextBlock { Text = widget.Title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        var close = new TextBlock { Text = "✕", Cursor = Cursors.Hand, FontSize = 12, Margin = new Thickness(8, 0, 0, 0), ToolTip = loc["Hub.Dash.RemoveWidget"] };
        close.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        var head = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Margin = new Thickness(0, 0, 0, 4) };
        SetDock(close, Dock.Right);
        head.Children.Add(close);
        // every widget can also stand over the game as an overlay, or in a small window of its own
        var popout = new TextBlock { Text = "\u29C9", Cursor = Cursors.Hand, FontSize = 14, Margin = new Thickness(8, 0, 0, 0), ToolTip = loc["Hub.Dash.Popout"] };
        popout.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        popout.MouseLeftButtonDown += (_, e) => e.Handled = true;
        popout.MouseLeftButtonUp += (_, _) => WidgetPopoutWindow.Show(widget.Id, _src);
        SetDock(popout, Dock.Right);
        head.Children.Add(popout);
        var asOverlay = new TextBlock { Text = "\u25EB", Cursor = Cursors.Hand, FontSize = 14, Margin = new Thickness(8, 0, 0, 0) };
        void ShowOverlayState()
        {
            bool on = WidgetOverlayWindow.IsOn(MeterSettings.Load(), widget.Id);
            asOverlay.ToolTip = loc[on ? "Hub.Dash.OverlayOff" : "Hub.Dash.OverlayOn"];
            asOverlay.SetResourceReference(TextBlock.ForegroundProperty, on ? "Brush.Accent" : "Brush.TextMuted");
        }

        ShowOverlayState();
        asOverlay.MouseLeftButtonDown += (_, e) => e.Handled = true;
        asOverlay.MouseLeftButtonUp += (_, _) =>
        {
            WidgetOverlayWindow.SetOn(widget.Id, !WidgetOverlayWindow.IsOn(MeterSettings.Load(), widget.Id));
            ShowOverlayState();
        };
        SetDock(asOverlay, Dock.Right);
        head.Children.Add(asOverlay);
        if (widget.Opens is { } opens)
        {
            var link = new TextBlock { Text = "↗", Cursor = Cursors.Hand, FontSize = 14, Margin = new Thickness(8, 0, 0, 0) };
            link.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            link.MouseLeftButtonDown += (_, e) => e.Handled = true;
            link.MouseLeftButtonUp += (_, _) => (opens == "sessions" ? _src.OpenSessions : (Action)(() => _src.OpenTool(opens)))();
            SetDock(link, Dock.Right);
            head.Children.Add(link);
        }

        head.Children.Add(title);
        var inner = new DockPanel();
        SetDock(head, Dock.Top);
        inner.Children.Add(head);
        inner.Children.Add(body);
        var grip = new Border { Width = 16, Height = 16, Cursor = Cursors.SizeNWSE, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Child = new TextBlock { Text = "◢", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom } };
        ((TextBlock)grip.Child).SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        // the corner to pull stands at the very edge of the frame, not inside the content
        inner.Margin = new Thickness(14, 10, 14, 16);
        grip.Margin = new Thickness(0, 0, -1, 0);
        ((TextBlock)grip.Child).Margin = new Thickness(0, 0, 0, -3);
        var cell = new Grid();
        cell.Children.Add(inner);
        cell.Children.Add(grip);
        var frame = new Border { Width = place.W, Height = place.H, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = cell };
        frame.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        frame.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        Canvas.SetLeft(frame, place.X);
        Canvas.SetTop(frame, place.Y);

        // move by the title
        Point start = default;
        double x0 = 0, y0 = 0;
        head.MouseLeftButtonDown += (_, e) =>
        {
            start = e.GetPosition(_canvas);
            x0 = Canvas.GetLeft(frame);
            y0 = Canvas.GetTop(frame);
            head.CaptureMouse();
            Panel.SetZIndex(frame, 10);
        };
        head.MouseMove += (_, e) =>
        {
            if (!head.IsMouseCaptured)
            {
                return;
            }

            Point now = e.GetPosition(_canvas);
            double nx = x0 + now.X - start.X, ny = y0 + now.Y - start.Y;
            var others = Others(frame).ToList();
            // left edge to another's left or right+gap; right edge to another's right or left-gap (the same for top and bottom)
            double left = Pull_(nx, others.SelectMany(o => new[] { L(o), L(o) + o.Width + Margin8 }));
            double rightFit = Pull_(nx + frame.Width, others.SelectMany(o => new[] { L(o) + o.Width, L(o) - Margin8 })) - frame.Width;
            double top = Pull_(ny, others.SelectMany(o => new[] { T(o), T(o) + o.Height + Margin8 }));
            double bottomFit = Pull_(ny + frame.Height, others.SelectMany(o => new[] { T(o) + o.Height, T(o) - Margin8 })) - frame.Height;
            Canvas.SetLeft(frame, Math.Max(0, Math.Abs(left - nx) <= Math.Abs(rightFit - nx) ? left : rightFit));
            Canvas.SetTop(frame, Math.Max(0, Math.Abs(top - ny) <= Math.Abs(bottomFit - ny) ? top : bottomFit));
        };
        head.MouseLeftButtonUp += (_, _) =>
        {
            if (!head.IsMouseCaptured)
            {
                return;
            }

            head.ReleaseMouseCapture();
            Panel.SetZIndex(frame, 0);
            place.X = Canvas.GetLeft(frame);
            place.Y = Canvas.GetTop(frame);
            Save();
            FitCanvas();
        };

        // size by the corner
        Point s0 = default;
        double w0 = 0, h0 = 0;
        grip.MouseLeftButtonDown += (_, e) =>
        {
            s0 = e.GetPosition(_canvas);
            w0 = frame.Width;
            h0 = frame.Height;
            grip.CaptureMouse();
            e.Handled = true;
        };
        grip.MouseMove += (_, e) =>
        {
            if (!grip.IsMouseCaptured)
            {
                return;
            }

            Point now = e.GetPosition(_canvas);
            var others = Others(frame).ToList();
            double right = Pull_(L(frame) + w0 + now.X - s0.X, others.SelectMany(o => new[] { L(o) + o.Width, L(o) - Margin8 }));
            double bottom = Pull_(T(frame) + h0 + now.Y - s0.Y, others.SelectMany(o => new[] { T(o) + o.Height, T(o) - Margin8 }));
            frame.Width = Math.Max(MinW, right - L(frame));
            frame.Height = Math.Max(MinH, bottom - T(frame));
        };
        grip.MouseLeftButtonUp += (_, _) =>
        {
            if (!grip.IsMouseCaptured)
            {
                return;
            }

            grip.ReleaseMouseCapture();
            place.W = frame.Width;
            place.H = frame.Height;
            Save();
            FitCanvas();
        };

        close.MouseLeftButtonDown += (_, e) => e.Handled = true;
        close.MouseLeftButtonUp += (_, _) =>
        {
            _places.Remove(widget.Id);
            Compact();
            Save();
            Build();
        };
        _canvas.Children.Add(frame);
        _frames[widget.Id] = (frame, body);
    }

    private static double Snap(double v) => Math.Round(v / Cell) * Cell;

    /// <summary>Whether a rectangle lies clear of every widget of the dashboard (with the gap between widgets).</summary>
    private bool Free(double x, double y, double w, double h, string? except = null)
    {
        foreach (var (id, o) in _places)
        {
            if (id != except && x < o.X + o.W + Margin8 && x + w + Margin8 > o.X && y < o.Y + o.H + Margin8 && y + h + Margin8 > o.Y)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The first free place for a new widget, from the top left, row by row, within the width of the window: a gap is used before the board grows downwards.</summary>
    private DashboardPlace Slot(double w, double h)
    {
        double limit = Math.Max(2 * 520 + 3 * Margin8, ActualWidth - 40);
        double bottom = _places.Values.Select(p => p.Y + p.H).DefaultIfEmpty(0).Max();
        for (double y = Margin8; y <= bottom + Margin8; y += Cell)
        {
            for (double x = Margin8; x + w <= limit || x == Margin8; x += Cell)
            {
                if (Free(x, y, w, h))
                {
                    return new DashboardPlace { X = x, Y = y, W = w, H = h };
                }
            }
        }

        return new DashboardPlace { X = Margin8, Y = Snap(bottom + Margin8), W = w, H = h };
    }

    /// <summary>After a widget was taken off: the ones below move up into the free place, as far as nothing stands in the way.</summary>
    private void Compact()
    {
        foreach (var pair in _places.OrderBy(p => p.Value.Y).ThenBy(p => p.Value.X).ToList())
        {
            DashboardPlace p = pair.Value;
            while (p.Y > Margin8 && Free(p.X, p.Y - Cell, p.W, p.H, pair.Key))
            {
                p.Y -= Cell;
            }
        }
    }

    /// <summary>The widgets of a layout for a new dashboard.</summary>
    private static string[] TemplateIds(string key) => key switch
    {
        "combat" => new[] { "meter", "last", "figures", "page:best", "bosses", "soon" },
        "character" => new[] { "connection", "todo", "page:profile", "page:attributes", "page:stats", "page:skills" },
        "stream" => new[] { "meter", "last", "figures", "drops", "page:stream" },
        _ => Array.Empty<string>(),
    };

    private void NewFromTemplate(string key)
    {
        var loc = LocalizationManager.Instance;
        var set = new DashboardSet { Name = key switch { "combat" => loc["Hub.Dash.TplCombat"], "character" => loc["Hub.Dash.TplCharacter"], "stream" => loc["Hub.Dash.TplStream"], "standard" => loc["Hub.Dash.TplStandard"], _ => "" } };
        _sets.Add(set);
        _places = set.Widgets;
        if (key == "standard")
        {
            foreach (var pair in FirstLayout())
            {
                _places[pair.Key] = pair.Value;
            }
        }

        foreach (string id in TemplateIds(key))
        {
            if (SpecOf(id) is { } spec)
            {
                _places[id] = Slot(spec.W, spec.H);
            }
        }

        Save();
        SwitchTo(_sets.Count - 1);
    }

    /// <summary>The small plus of the row of dashboards: a new dashboard empty or from a layout.</summary>
    private void ShowTemplatePopup(FrameworkElement anchor)
    {
        var loc = LocalizationManager.Instance;
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, VerticalOffset = 4 };
        var stack = new StackPanel { MinWidth = 220 };
        var head = new TextBlock { Text = loc["Hub.Dash.NewFrom"], FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 0, 8) };
        head.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        stack.Children.Add(head);
        foreach (var (key, name) in new[] { ("standard", loc["Hub.Dash.TplStandard"]), ("combat", loc["Hub.Dash.TplCombat"]), ("character", loc["Hub.Dash.TplCharacter"]), ("stream", loc["Hub.Dash.TplStream"]), ("empty", loc["Hub.Dash.TplEmpty"]) })
        {
            var text = new TextBlock { Text = name, FontSize = 13 };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            var row = new Border { Child = text, Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "Brush.ControlHoverStrong");
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            string chosen = key;
            row.MouseLeftButtonUp += (_, _) =>
            {
                popup.IsOpen = false;
                NewFromTemplate(chosen);
            };
            stack.Children.Add(row);
        }

        var frame = new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = stack, Margin = new Thickness(0, 0, 12, 12), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.5 } };
        frame.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        frame.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
        popup.Child = frame;
        popup.IsOpen = true;
    }

    /// <summary>Pulls a value to the nearest edge of another widget (or the gap beside it) when it is within a few pixels, else to the grid.</summary>
    private double Pull_(double v, IEnumerable<double> candidates)
    {
        double best = double.NaN, bestDistance = Pull + 0.01;
        foreach (double c in candidates)
        {
            double d = Math.Abs(v - c);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = c;
            }
        }

        return double.IsNaN(best) ? Snap(v) : best;
    }

    private IEnumerable<Border> Others(Border frame) => _frames.Values.Select(f => f.Frame).Where(f => !ReferenceEquals(f, frame));

    private static double L(Border f) => Canvas.GetLeft(f);
    private static double T(Border f) => Canvas.GetTop(f);

    private void Refresh()
    {
        DateTime now = DateTime.Now;
        foreach (var (id, (_, body)) in _frames)
        {
            // the update and the news ask the network, a page of the app has its own life: they are built once
            if (SpecOf(id) is { Cached: true })
            {
                if (!_cached.TryGetValue(id, out UIElement? kept))
                {
                    if (id.StartsWith("page:", StringComparison.Ordinal))
                    {
                        ToolPage? page = _src.BuildPage(id["page:".Length..]);
                        if (page is not null)
                        {
                            _pages[id] = page;
                        }

                        kept = page?.Content ?? new TextBlock { Text = "–" };
                    }
                    else
                    {
                        kept = HubDashboard.WidgetBody(id, _src, now);
                    }

                    _cached[id] = kept;
                }

                if (!ReferenceEquals(body.Child, kept))
                {
                    if (kept is FrameworkElement element && element.Parent is Decorator oldHost)
                    {
                        oldHost.Child = null;
                    }

                    body.Child = kept;
                }

                continue;
            }

            body.Child = HubDashboard.WidgetBody(id, _src, now);
        }
    }

    private void FitCanvas()
    {
        double right = 0, bottom = 0;
        foreach (var p in _places.Values)
        {
            right = Math.Max(right, p.X + p.W);
            bottom = Math.Max(bottom, p.Y + p.H);
        }

        _canvas.Width = right + Margin8;
        _canvas.Height = bottom + Margin8;
    }

    private const double SheetWidth = 500, ThumbWidth = 132, ThumbHeight = 80, Crop = 340;

    // the pictures of the panels in the sheet, taken once from the real panel (per language and accent)
    private static readonly Dictionary<string, ImageSource> Snapshots = new();

    /// <summary>The sheet of the plus: every panel that can stand on the dashboard in groups, each with a picture of the real panel, a line about it and an add button (one that is on the board is dimmed with a green tick). It closes with the add, a click beside it or Esc.</summary>
    private void ShowAddMenu()
    {
        var popup = new Popup { PlacementTarget = _add, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, HorizontalOffset = _add.ActualWidth + 16 - SheetWidth - 12 };
        popup.Child = BuildSheet(popup, false);
        popup.Opened += (_, _) => popup.Child.Focus();
        popup.IsOpen = true;
    }

    /// <summary>The sheet as a picture (render-tool with AIONDPS_DASHMOCK=1): the board with the open sheet over it, without a window of its own.</summary>
    internal UIElement WithSheetOpen()
    {
        var grid = new Grid();
        grid.Children.Add(this);
        var sheet = BuildSheet(new Popup(), true);
        ((ScrollViewer)sheet.Child).MaxHeight = 760;
        sheet.HorizontalAlignment = HorizontalAlignment.Right;
        sheet.VerticalAlignment = VerticalAlignment.Top;
        sheet.Margin = new Thickness(0, 6, 4, 0);
        grid.Children.Add(sheet);
        return grid;
    }

    /// <summary>A picture of the real panel (its page for a page of the app) as it would stand on the board; null when there is none or it already stands somewhere.</summary>
    private ImageSource? Snapshot(Spec spec)
    {
        ToolPage? page = null;
        UIElement? content;
        if (spec.Id.StartsWith("page:", StringComparison.Ordinal))
        {
            page = _src.BuildPage(spec.Id["page:".Length..]);
            content = page?.Content;
        }
        else
        {
            content = HubDashboard.WidgetBody(spec.Id, _src, DateTime.Now);
        }

        try
        {
            if (content is null || (content is FrameworkElement { Parent: not null }))
            {
                return null;
            }

            // the upper left corner of the panel at its own size (so the writing stays readable in the small picture)
            double h = Math.Round(Crop * ThumbHeight / ThumbWidth);
            var element = (FrameworkElement)content;
            element.Width = spec.W;
            element.Height = spec.H;
            var host = new Canvas { Width = Crop, Height = h, ClipToBounds = true };
            host.SetResourceReference(Panel.BackgroundProperty, "Brush.Panel");
            var inset = new Border { Padding = new Thickness(14, 12, 14, 0), Width = spec.W, Height = spec.H, Child = element };
            host.Children.Add(inset);
            host.Measure(new Size(Crop, h));
            host.Arrange(new Rect(0, 0, Crop, h));
            inset.Measure(new Size(spec.W, spec.H));
            inset.Arrange(new Rect(0, 0, spec.W, spec.H));
            host.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Crop, (int)h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);
            bitmap.Freeze();
            inset.Child = null;
            return bitmap;
        }
        finally
        {
            page?.Cleanup?.Invoke();
        }
    }

    private Border BuildSheet(Popup popup, bool allAtOnce)
    {
        var loc = LocalizationManager.Instance;
        var green = new SolidColorBrush(Color.FromRgb(0x3d, 0xd6, 0x8c));
        green.Freeze();
        string cachePrefix = loc.In("Hub.Dash.Add", "en") + "|" + (Application.Current?.TryFindResource("Brush.Accent") as SolidColorBrush)?.Color + "|" + loc["Hub.Dash.Add"] + "|";
        var pending = new List<(Spec Spec, Image Image, UIElement Bars, string Key)>();

        TextBlock Label(string text, double size, string brush, FontWeight? weight = null)
        {
            var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return t;
        }

        void Place(Spec spec)
        {
            _places[spec.Id] = Slot(spec.W, spec.H);
            popup.IsOpen = false;
            Save();
            Build();
        }

        // the picture of the real panel; until it is taken a few bars stand in; dimmed with a tick when the panel is already on the board
        UIElement Thumb(Spec spec, bool added)
        {
            int seed = spec.Id.Aggregate(7, (h, c) => h * 31 + c) & 0xffff;
            var grid = new Grid { Width = ThumbWidth, Height = ThumbHeight };
            var frame = new Border { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromRgb(0x0b, 0x14, 0x1e)) };
            frame.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            grid.Children.Add(frame);
            var bars = new StackPanel { Margin = new Thickness(10, 10, 10, 6) };
            for (int i = 0; i < 4; i++)
            {
                var bar = new System.Windows.Shapes.Rectangle { Height = 5, Width = i == 0 ? 40 : 22 + (seed >> (i * 3)) % 60, RadiusX = 2, RadiusY = 2, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 5) };
                bar.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, i == 0 ? "Brush.Accent" : "Brush.TextMuted");
                bars.Children.Add(bar);
            }

            grid.Children.Add(bars);
            var image = new Image { Stretch = Stretch.Fill, Margin = new Thickness(1) };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            grid.Children.Add(new Border { Child = image, CornerRadius = new CornerRadius(5), ClipToBounds = true });
            string key = cachePrefix + spec.Id;
            if (Snapshots.TryGetValue(key, out ImageSource? known))
            {
                image.Source = known;
                bars.Visibility = Visibility.Collapsed;
            }
            else
            {
                pending.Add((spec, image, bars, key));
            }

            if (added)
            {
                image.Opacity = 0.55;
                bars.Opacity = 0.25;
                grid.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x60, 0x07, 0x0d, 0x14)), CornerRadius = new CornerRadius(5) });
                grid.Children.Add(new TextBlock { Text = "\u2714", FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 24, Foreground = green, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            }

            return grid;
        }

        UIElement Entry(Spec spec)
        {
            bool added = _places.ContainsKey(spec.Id);
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), Opacity = added ? 0.7 : 1, Background = Brushes.Transparent, Cursor = added ? Cursors.Arrow : Cursors.Hand };
            var thumb = Thumb(spec, added);
            SetDock(thumb, Dock.Left);
            row.Children.Add(thumb);
            var button = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(12, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            if (added)
            {
                button.BorderBrush = green;
                button.Child = new TextBlock { Text = loc["Hub.Dash.OnBoard"], FontSize = 11, Foreground = green };
            }
            else
            {
                button.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
                button.Child = Label(loc["Hub.Dash.Add"], 11, "Brush.Accent", FontWeights.SemiBold);
                row.MouseLeftButtonUp += (_, _) => Place(spec);
            }

            SetDock(button, Dock.Right);
            row.Children.Add(button);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            texts.Children.Add(Label(spec.Title, 13, "Brush.Text", FontWeights.SemiBold));
            string descKey = "Hub.Dash.Desc." + (spec.Id.StartsWith("page:", StringComparison.Ordinal) ? spec.Id["page:".Length..] : spec.Id);
            string desc = loc[descKey];
            if (desc != descKey)
            {
                var line = Label(desc, 11, "Brush.TextMuted");
                line.TextWrapping = TextWrapping.Wrap;
                line.TextTrimming = TextTrimming.None;
                line.Margin = new Thickness(0, 2, 0, 0);
                texts.Children.Add(line);
            }

            row.Children.Add(texts);
            return row;
        }

        UIElement Group(string heading)
        {
            var line = new Border { Height = 1, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            line.SetResourceReference(Border.BackgroundProperty, "Brush.Border");
            var row = new DockPanel { Margin = new Thickness(0, 12, 0, 10) };
            var head = Label(heading.ToUpperInvariant(), 11, "Brush.Accent", FontWeights.SemiBold);
            SetDock(head, Dock.Left);
            row.Children.Add(head);
            row.Children.Add(line);
            return row;
        }

        var list = new StackPanel();
        var title = new DockPanel { Margin = new Thickness(0, 0, 14, 0) };
        var reset = Label(loc["Hub.Dash.Reset"], 11, "Brush.TextMuted");
        reset.Cursor = Cursors.Hand;
        bool sure = false;
        reset.MouseLeftButtonUp += (_, _) =>
        {
            if (!sure)
            {
                sure = true;
                reset.Text = loc["Hub.Dash.ResetSure"];
                reset.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
                return;
            }

            _places.Clear();
            foreach (var pair in FirstLayout())
            {
                _places[pair.Key] = pair.Value;
            }

            popup.IsOpen = false;
            Save();
            Build();
        };
        SetDock(reset, Dock.Right);
        title.Children.Add(reset);
        title.Children.Add(Label(loc["Hub.Dash.Extend"], 15, "Brush.Text", FontWeights.SemiBold));
        list.Children.Add(title);

        list.Children.Add(Group(loc["Hub.Dash.GroupOverview"]));
        foreach (var widget in HubDashboard.Widgets)
        {
            list.Children.Add(Entry(SpecOf(widget.Id)!));
        }

        // every window of the app can stand on a dashboard: the pages of the tabs, grouped as the tabs are
        // the checklist page is not offered: it would bring the whole window and bypass the on/off switches of the checklist widget ("todo")
        foreach (var group in _src.Pages().Where(p => p.Id != "checklist").GroupBy(p => p.Group))
        {
            list.Children.Add(Group(group.Key));
            foreach (HubPageInfo page in group)
            {
                list.Children.Add(Entry(SpecOf("page:" + page.Id)!));
            }
        }

        // the tabs of the app as links
        list.Children.Add(Group(loc["Hub.Dash.GoTo"]));
        var links = new WrapPanel();
        foreach (var (id, name) in new[] { ("dashboard", loc["Hub.Tab.Dashboard"]), ("combat", loc["Hub.Tab.Combat"]), ("character", loc["Hub.Tab.Character"]), ("tools", loc["Main.MenuTools"]) })
        {
            var text = new StackPanel { Orientation = Orientation.Horizontal };
            text.Children.Add(Label("\u203A", 12, "Brush.Accent"));
            var label = Label(name, 12, "Brush.Text");
            label.Margin = new Thickness(6, 0, 0, 0);
            text.Children.Add(label);
            var pill = new Border { Child = text, CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(1), Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            pill.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            string tab = id;
            pill.MouseLeftButtonUp += (_, _) =>
            {
                popup.IsOpen = false;
                _src.OpenTool("tab:" + tab);
            };
            links.Children.Add(pill);
        }

        list.Children.Add(links);

        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(300, ActualHeight - 40) };
        var sheet = new Border { Child = scroll, Width = SheetWidth, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(18, 16, 10, 8), Margin = new Thickness(12), Focusable = true, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.6 } };
        sheet.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        sheet.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
        sheet.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                popup.IsOpen = false;
                e.Handled = true;
            }
        };

        // the pictures are taken one after the other while the sheet is open, so it appears at once and fills
        void TakeNext()
        {
            while (pending.Count > 0)
            {
                if (!allAtOnce && !popup.IsOpen)
                {
                    return;
                }

                var (spec, image, bars, key) = pending[0];
                pending.RemoveAt(0);
                try
                {
                    if (Snapshot(spec) is { } picture)
                    {
                        Snapshots[key] = picture;
                        image.Source = picture;
                        bars.Visibility = Visibility.Collapsed;
                    }
                }
                catch (Exception)
                {
                    // a panel that cannot be drawn stays with its bars
                }

                if (!allAtOnce)
                {
                    sheet.Dispatcher.BeginInvoke(new Action(TakeNext), System.Windows.Threading.DispatcherPriority.Background);
                    return;
                }
            }
        }

        if (allAtOnce)
        {
            TakeNext();
        }
        else
        {
            popup.Opened += (_, _) => sheet.Dispatcher.BeginInvoke(new Action(TakeNext), System.Windows.Threading.DispatcherPriority.Background);
        }

        return sheet;
    }
}
