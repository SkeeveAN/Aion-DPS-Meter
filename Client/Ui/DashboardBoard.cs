using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        _places = _sets[_active].Widgets;
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
        small.MouseLeftButtonUp += (_, _) =>
        {
            _sets.Add(new DashboardSet());
            Save();
            SwitchTo(_sets.Count - 1);
        };
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

    private static Dictionary<string, DashboardPlace> FirstLayout()
    {
        // pairs side by side, both of a pair as high as the higher one, all on the grid
        var result = new Dictionary<string, DashboardPlace>();
        var shown = HubDashboard.Widgets.Where(w => !w.Static).ToList();
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
        var cell = new Grid();
        cell.Children.Add(inner);
        cell.Children.Add(grip);
        var frame = new Border { Width = place.W, Height = place.H, Padding = new Thickness(14, 10, 14, 12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = cell };
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
            Save();
            Build();
        };
        _canvas.Children.Add(frame);
        _frames[widget.Id] = (frame, body);
    }

    private static double Snap(double v) => Math.Round(v / Cell) * Cell;

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

    private void ShowAddMenu()
    {
        var loc = LocalizationManager.Instance;
        var menu = new ContextMenu();
        void Place(Spec spec)
        {
            double bottom = _places.Values.Select(p => p.Y + p.H).DefaultIfEmpty(0).Max();
            _places[spec.Id] = new DashboardPlace { X = Margin8, Y = Snap(bottom + Margin8), W = spec.W, H = spec.H };
            Save();
            Build();
        }

        foreach (var widget in HubDashboard.Widgets.Where(w => !_places.ContainsKey(w.Id)))
        {
            Spec spec = SpecOf(widget.Id)!;
            var item = new MenuItem { Header = spec.Title };
            item.Click += (_, _) => Place(spec);
            menu.Items.Add(item);
        }

        // every window of the app can stand on a dashboard: the pages of the tabs, grouped as the tabs are
        menu.Items.Add(new Separator());
        foreach (var group in _src.Pages().GroupBy(p => p.Group))
        {
            var sub = new MenuItem { Header = group.Key };
            foreach (HubPageInfo page in group.Where(p => !_places.ContainsKey("page:" + p.Id)))
            {
                Spec spec = SpecOf("page:" + page.Id)!;
                var item = new MenuItem { Header = page.Title };
                item.Click += (_, _) => Place(spec);
                sub.Items.Add(item);
            }

            if (sub.Items.Count > 0)
            {
                menu.Items.Add(sub);
            }
        }

        menu.PlacementTarget = _add;
        menu.IsOpen = true;
    }
}
