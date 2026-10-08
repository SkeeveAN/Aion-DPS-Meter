using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// Resizing an unlocked overlay by its four edges: a left or right edge changes only the width, a top or bottom edge only the height (the content
/// keeps its scale and is cut off at the frame); the opposite edge stays where it is. The mouse is measured on the screen, because the window
/// itself moves while the left or top edge is dragged. Only the corner grip scales the content.
/// </summary>
internal sealed class OverlayEdgeResize
{
    private readonly Window _window;
    private readonly FrameworkElement _sized;
    private readonly Func<double> _getScale;
    private readonly Action _begin, _end;
    private readonly List<Thumb> _edges = new();
    private double _startW, _startH, _startLeft, _startTop;
    private Point _startMouse;

    public OverlayEdgeResize(Window window, FrameworkElement sized, Func<double> getScale, Action begin, Action end)
    {
        _window = window;
        _sized = sized;
        _getScale = getScale;
        _begin = begin;
        _end = end;
    }

    /// <summary>The content with the four edge handles laid over it (shown by <see cref="Show"/>).</summary>
    public Grid Wrap(UIElement content)
    {
        var grid = new Grid();
        grid.Children.Add(content);
        foreach (var edge in new[] { Edge.Left, Edge.Right, Edge.Top, Edge.Bottom })
        {
            bool vertical = edge is Edge.Left or Edge.Right;
            var thumb = new Thumb
            {
                Opacity = 0.01,
                Background = Brushes.Transparent,
                Template = HitTemplate(),
                Cursor = vertical ? Cursors.SizeWE : Cursors.SizeNS,
                Width = vertical ? 7 : double.NaN,
                Height = vertical ? double.NaN : 7,
                HorizontalAlignment = edge == Edge.Left ? HorizontalAlignment.Left : edge == Edge.Right ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
                VerticalAlignment = edge == Edge.Top ? VerticalAlignment.Top : edge == Edge.Bottom ? VerticalAlignment.Bottom : VerticalAlignment.Stretch,
                Visibility = Visibility.Collapsed,
            };
            Edge e = edge;
            thumb.DragStarted += (_, _) => Start(e);
            thumb.DragDelta += (_, _) => Move(e);
            thumb.DragCompleted += (_, _) => _end();
            _edges.Add(thumb);
            grid.Children.Add(thumb);
        }

        return grid;
    }

    public void Show(bool visible)
    {
        foreach (Thumb t in _edges)
        {
            t.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private enum Edge { Left, Right, Top, Bottom }

    private static ControlTemplate HitTemplate()
    {
        var template = new ControlTemplate(typeof(Thumb));
        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        template.VisualTree = grid;
        return template;
    }

    private Point MouseOnScreen()
    {
        Point device = _window.PointToScreen(Mouse.GetPosition(_window));
        var source = PresentationSource.FromVisual(_window);
        return source?.CompositionTarget is { } target ? target.TransformFromDevice.Transform(device) : device;
    }

    private void Start(Edge edge)
    {
        _begin();
        _startW = Math.Max(1, _window.ActualWidth);
        _startH = Math.Max(1, _window.ActualHeight);
        _startLeft = _window.Left;
        _startTop = _window.Top;
        _startMouse = MouseOnScreen();
    }

    private void Move(Edge edge)
    {
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
    }
}
