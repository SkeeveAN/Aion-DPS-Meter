using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>What pulling an edge of an unlocked overlay does. A corner always scales everything (width, height and text) in proportion.</summary>
public enum OverlayEdgeMode
{
    /// <summary>A left or right edge changes only the width, a top or bottom edge only the height; the content keeps its scale and is cut off at the frame (lists).</summary>
    Crop,

    /// <summary>Like <see cref="Crop"/>, but the content stays where it is on the screen: the frame cuts away from the side that is pulled (the map, whose centre is the player).</summary>
    CropAnchored,

    /// <summary>Only the left and right edges, they change the width; the height follows the content (lists that grow downwards).</summary>
    Width,

    /// <summary>Every handle scales everything evenly around the middle.</summary>
    Uniform,
}

/// <summary>
/// Resizing an unlocked overlay: four corner handles scale the content (and with it the width and the height) in proportion, the opposite corner stays where it is;
/// the four edge handles act as <see cref="OverlayEdgeMode"/> says. The mouse is measured on the screen, because the window itself moves while the left or top edge is dragged.
/// The handles are small orange marks, so the user sees where to take hold.
/// </summary>
internal sealed class OverlayEdgeResize
{
    private const double MinScale = 0.5, MaxScale = 3.0;
    private readonly Window _window;
    private readonly FrameworkElement _sized;
    private readonly Func<double> _getScale;
    private readonly Action _begin, _end;
    private readonly Action<double>? _setScale;
    private readonly OverlayEdgeMode _mode;
    private readonly Action<double, double>? _shiftContent;
    private readonly List<Thumb> _handles = new();
    private double _startW, _startH, _startLeft, _startTop, _startScale;
    private Point _startMouse;

    public OverlayEdgeResize(Window window, FrameworkElement sized, Func<double> getScale, Action begin, Action end,
        Action<double>? setScale = null, OverlayEdgeMode mode = OverlayEdgeMode.Crop, Action<double, double>? shiftContent = null)
    {
        _window = window;
        _sized = sized;
        _getScale = getScale;
        _begin = begin;
        _end = end;
        _setScale = setScale;
        _mode = mode;
        _shiftContent = shiftContent;
    }

    /// <summary>The content with the handles laid over it (shown by <see cref="Show"/>).</summary>
    public Grid Wrap(UIElement content)
    {
        var grid = new Grid();
        grid.Children.Add(content);
        var edges = _mode == OverlayEdgeMode.Width ? new[] { Edge.Left, Edge.Right } : new[] { Edge.Left, Edge.Right, Edge.Top, Edge.Bottom };
        foreach (Edge edge in edges)
        {
            bool vertical = edge is Edge.Left or Edge.Right;
            var thumb = new Thumb
            {
                Background = Brushes.Transparent,
                Template = EdgeTemplate(vertical),
                Cursor = vertical ? Cursors.SizeWE : Cursors.SizeNS,
                Width = vertical ? 12 : double.NaN,
                Height = vertical ? double.NaN : 12,
                Margin = new Thickness(vertical ? 0 : 16, vertical ? 16 : 0, vertical ? 0 : 16, vertical ? 16 : 0),
                HorizontalAlignment = edge == Edge.Left ? HorizontalAlignment.Left : edge == Edge.Right ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
                VerticalAlignment = edge == Edge.Top ? VerticalAlignment.Top : edge == Edge.Bottom ? VerticalAlignment.Bottom : VerticalAlignment.Stretch,
                Visibility = Visibility.Collapsed,
            };
            Edge e = edge;
            thumb.DragStarted += (_, _) => Start();
            thumb.DragDelta += (_, _) => MoveEdge(e);
            thumb.DragCompleted += (_, _) => _end();
            _handles.Add(thumb);
            grid.Children.Add(thumb);
        }

        if (_setScale is not null)
        {
            foreach (var (left, top) in new[] { (true, true), (false, true), (true, false), (false, false) })
            {
                var thumb = new Thumb
                {
                    Background = Brushes.Transparent,
                    Template = CornerTemplate(),
                    Cursor = left == top ? Cursors.SizeNWSE : Cursors.SizeNESW,
                    Width = 14,
                    Height = 14,
                    HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                    VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                    Visibility = Visibility.Collapsed,
                };
                bool l = left, t = top;
                thumb.DragStarted += (_, _) => Start();
                thumb.DragDelta += (_, _) => ScaleBy(l, t);
                thumb.DragCompleted += (_, _) => _end();
                _handles.Add(thumb);
                grid.Children.Add(thumb);
            }
        }

        return grid;
    }

    public void Show(bool visible)
    {
        foreach (Thumb t in _handles)
        {
            t.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private enum Edge { Left, Right, Top, Bottom }

    private static readonly Brush Mark = new SolidColorBrush(Color.FromRgb(0xFF, 0xBA, 0x42));
    private static readonly Brush MarkBorder = new SolidColorBrush(Color.FromRgb(0x0E, 0x18, 0x20));

    private static ControlTemplate EdgeTemplate(bool vertical)
    {
        var template = new ControlTemplate(typeof(Thumb));
        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        var bar = new FrameworkElementFactory(typeof(Border));
        bar.SetValue(FrameworkElement.WidthProperty, vertical ? 4.0 : 28.0);
        bar.SetValue(FrameworkElement.HeightProperty, vertical ? 28.0 : 4.0);
        bar.SetValue(Border.BackgroundProperty, Mark);
        bar.SetValue(Border.BorderBrushProperty, MarkBorder);
        bar.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        bar.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        bar.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        bar.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        grid.AppendChild(bar);
        template.VisualTree = grid;
        return template;
    }

    private static ControlTemplate CornerTemplate()
    {
        var template = new ControlTemplate(typeof(Thumb));
        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        var square = new FrameworkElementFactory(typeof(Border));
        square.SetValue(FrameworkElement.WidthProperty, 10.0);
        square.SetValue(FrameworkElement.HeightProperty, 10.0);
        square.SetValue(Border.BackgroundProperty, Mark);
        square.SetValue(Border.BorderBrushProperty, MarkBorder);
        square.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        square.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        grid.AppendChild(square);
        template.VisualTree = grid;
        return template;
    }

    private Point MouseOnScreen()
    {
        Point device = _window.PointToScreen(Mouse.GetPosition(_window));
        var source = PresentationSource.FromVisual(_window);
        return source?.CompositionTarget is { } target ? target.TransformFromDevice.Transform(device) : device;
    }

    private void Start()
    {
        _begin();
        _startW = Math.Max(1, _window.ActualWidth);
        _startH = Math.Max(1, _window.ActualHeight);
        _startLeft = _window.Left;
        _startTop = _window.Top;
        _startScale = _getScale();
        _startMouse = MouseOnScreen();
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>A corner scales the content: the opposite corner stays where it is, the mouse sets the distance from it.</summary>
    private void ScaleBy(bool left, bool top)
    {
        double ax = _startLeft + (left ? _startW : 0), ay = _startTop + (top ? _startH : 0);
        ScaleAround(new Point(ax, ay), (nw, nh) => (left ? ax - nw : ax, top ? ay - nh : ay));
    }

    /// <summary>Every handle of an <see cref="OverlayEdgeMode.Uniform"/> overlay: the middle stays where it is.</summary>
    private void ScaleAboutMiddle()
    {
        double cx = _startLeft + _startW / 2, cy = _startTop + _startH / 2;
        ScaleAround(new Point(cx, cy), (nw, nh) => (cx - nw / 2, cy - nh / 2));
    }

    private void ScaleAround(Point anchor, Func<double, double, (double Left, double Top)> place)
    {
        if (_setScale is null)
        {
            return;
        }

        double d0 = Math.Max(12, Distance(_startMouse, anchor));
        double d1 = Distance(MouseOnScreen(), anchor);
        _setScale(Math.Clamp(_startScale * d1 / d0, MinScale, MaxScale));
        double k = _getScale() / Math.Max(0.01, _startScale);
        var (l, t) = place(_startW * k, _startH * k);
        _window.Left = l;
        _window.Top = t;
    }

    private void MoveEdge(Edge edge)
    {
        if (_mode == OverlayEdgeMode.Uniform)
        {
            ScaleAboutMiddle();
            return;
        }

        Point m = MouseOnScreen();
        double scale = Math.Max(0.01, _getScale());
        const double min = 40; // screen size at least
        switch (edge)
        {
            case Edge.Right:
            case Edge.Left:
            {
                double screenW = Math.Max(min, edge == Edge.Right ? _startW + (m.X - _startMouse.X) : _startW - (m.X - _startMouse.X));
                _sized.Width = screenW / scale;
                if (edge == Edge.Left)
                {
                    _window.Left = _startLeft + _startW - screenW;
                }

                break;
            }

            default:
            {
                double screenH = Math.Max(min, edge == Edge.Bottom ? _startH + (m.Y - _startMouse.Y) : _startH - (m.Y - _startMouse.Y));
                _sized.Height = screenH / scale;
                if (edge == Edge.Top)
                {
                    _window.Top = _startTop + _startH - screenH;
                }

                break;
            }
        }

        if (_mode == OverlayEdgeMode.CropAnchored && _shiftContent is not null)
        {
            // the frame's left/top edge moved with the window: the content moves back by the same distance, so it stays where it was on the screen
            _shiftContent(-(_window.Left - _startLeft) / scale, -(_window.Top - _startTop) / scale);
        }
    }
}
