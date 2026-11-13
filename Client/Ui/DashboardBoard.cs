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
    private readonly Button _add = new();
    private readonly Dictionary<string, UIElement> _cached = new();
    private Dictionary<string, DashboardPlace> _places;

    public DashboardBoard(DashboardSources src)
    {
        _src = src;
        var loc = LocalizationManager.Instance;
        _places = MeterSettings.Load().Dashboard;
        if (_places.Count == 0)
        {
            _places = FirstLayout();
            Save();
        }
        else
        {
            // places from an earlier version may stand between the grid lines: onto the grid, so edges can be brought level
            foreach (DashboardPlace p in _places.Values)
            {
                p.X = Snap(p.X);
                p.Y = Snap(p.Y);
                p.W = Math.Max(MinW, Snap(p.W));
                p.H = Math.Max(MinH, Snap(p.H));
            }

            Save();
        }

        _add.Content = "＋";
        _add.ToolTip = loc["Hub.Dash.AddWidget"];
        _add.FontSize = 16;
        _add.Padding = new Thickness(12, 2, 12, 2);
        _add.Margin = new Thickness(0, 8, 14, 0);
        _add.HorizontalAlignment = HorizontalAlignment.Right;
        _add.Click += (_, _) => ShowAddMenu();
        SetDock(_add, Dock.Top);
        Children.Add(_add);
        _canvas.MinHeight = 600;
        _canvas.HorizontalAlignment = HorizontalAlignment.Left;
        _canvas.VerticalAlignment = VerticalAlignment.Top;
        Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _canvas });
        Build();
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

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
        s.Dashboard = _places.ToDictionary(kv => kv.Key, kv => kv.Value);
        s.Save();
    }

    private void Build()
    {
        _canvas.Children.Clear();
        _frames.Clear();
        foreach (var widget in HubDashboard.Widgets.Where(w => _places.ContainsKey(w.Id)))
        {
            AddFrame(widget);
        }

        Refresh();
        FitCanvas();
    }

    private void AddFrame(HubDashboard.Widget widget)
    {
        var loc = LocalizationManager.Instance;
        DashboardPlace place = _places[widget.Id];
        var body = new Border { Padding = new Thickness(0, 4, 0, 0), ClipToBounds = true };
        var title = new TextBlock { Text = loc[widget.TitleKey].ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        var close = new TextBlock { Text = "✕", Cursor = Cursors.Hand, FontSize = 12, Margin = new Thickness(8, 0, 0, 0), ToolTip = loc["Hub.Dash.RemoveWidget"] };
        close.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        var head = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Margin = new Thickness(0, 0, 0, 4) };
        SetDock(close, Dock.Right);
        head.Children.Add(close);
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
            // the update and the news ask the network: they are built once
            if (HubDashboard.Widgets.First(w => w.Id == id).Static)
            {
                if (!_cached.TryGetValue(id, out UIElement? kept))
                {
                    _cached[id] = kept = HubDashboard.WidgetBody(id, _src, now);
                }

                if (!ReferenceEquals(body.Child, kept))
                {
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

        _canvas.Width = right + 40;
        _canvas.Height = Math.Max(600, bottom + 40);
    }

    private void ShowAddMenu()
    {
        var loc = LocalizationManager.Instance;
        var menu = new ContextMenu();
        var free = HubDashboard.Widgets.Where(w => !_places.ContainsKey(w.Id)).ToList();
        if (free.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = loc["Hub.Dash.AddNone"], IsEnabled = false });
        }

        foreach (var widget in free)
        {
            var item = new MenuItem { Header = loc[widget.TitleKey] };
            var chosen = widget;
            item.Click += (_, _) =>
            {
                double bottom = _places.Values.Select(p => p.Y + p.H).DefaultIfEmpty(0).Max();
                _places[chosen.Id] = new DashboardPlace { X = Margin8, Y = Snap(bottom + Margin8), W = chosen.W, H = chosen.H };
                Save();
                Build();
            };
            menu.Items.Add(item);
        }

        menu.PlacementTarget = _add;
        menu.IsOpen = true;
    }
}
