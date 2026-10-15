using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// The floor of the combat overlay: every element sits on it at its own place. It wants the size the overlay frame was given (so a locked,
/// shrink-wrapped frame keeps it), the elements are placed by <see cref="Canvas.LeftProperty"/> and <see cref="Canvas.TopProperty"/>.
/// </summary>
public sealed class CombatStage : Canvas
{
    public Size Wanted { get; set; } = new(460, 240);

    /// <summary>A stage inside a block takes the block's whole size instead of a wanted size of its own.</summary>
    public bool FillAvailable { get; set; }

    protected override Size MeasureOverride(Size constraint)
    {
        base.MeasureOverride(constraint);
        if (FillAvailable && !double.IsInfinity(constraint.Width) && !double.IsInfinity(constraint.Height))
        {
            return constraint;
        }

        return new Size(
            double.IsInfinity(constraint.Width) ? Wanted.Width : Math.Min(Wanted.Width, constraint.Width),
            double.IsInfinity(constraint.Height) ? Wanted.Height : Math.Min(Wanted.Height, constraint.Height));
    }
}

/// <summary>
/// One element of the combat overlay. Locked, it is only its content. Unlocked (the edit mode) it has an outline and can be moved by its body, pulled larger or
/// smaller at each of its four edges, and scaled at its four corners - the overlay's own handles then scale all of them together.
/// Place, size and scale are kept in the element's <see cref="CombatElementLayout"/> in the stage's units, and saved when a drag ends.
/// </summary>
public sealed class CombatElementHost : Border
{
    private const double MinWidth_ = 36, MinHeight_ = 22, MinScale_ = 0.5, MaxScale_ = 3.0, EdgeThickness = 7;
    private static readonly Brush EditBorder = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xBA, 0x42)));
    private static readonly Brush EditFill = Freeze(new SolidColorBrush(Color.FromArgb(0x38, 0, 0, 0)));

    private readonly CombatStage _stage;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Action<string, CombatElementLayout> _save;
    private readonly Thumb _left = Edge(Cursors.SizeWE), _right = Edge(Cursors.SizeWE), _top = Edge(Cursors.SizeNS), _bottom = Edge(Cursors.SizeNS);
    private readonly Thumb[] _corners = new Thumb[4];
    private readonly TextBlock _caption;
    private bool _editing;
    private bool _dragging;
    private Point _dragStart;
    private double _startLeft, _startTop;

    public CombatElementHost(string id, CombatStage stage, UIElement content, CombatElementLayout layout, string caption, Action<string, CombatElementLayout> save)
    {
        Id = id;
        _stage = stage;
        Layout = layout;
        _save = save;
        BorderThickness = new Thickness(1);
        BorderBrush = Brushes.Transparent;
        LayoutTransform = _scale;
        ClipToBounds = true;

        _caption = new TextBlock
        {
            Text = caption, FontSize = 9.5, Foreground = EditBorder, Margin = new Thickness(0, 0, 18, 1), IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed,
        };
        _left.HorizontalAlignment = HorizontalAlignment.Left;
        _left.Width = EdgeThickness;
        _right.HorizontalAlignment = HorizontalAlignment.Right;
        _right.Width = EdgeThickness;
        _top.VerticalAlignment = VerticalAlignment.Top;
        _top.Height = EdgeThickness;
        _bottom.VerticalAlignment = VerticalAlignment.Bottom;
        _bottom.Height = EdgeThickness;

        var grid = new Grid();
        grid.Children.Add(content);
        foreach (UIElement part in new UIElement[] { _caption, _left, _right, _top, _bottom })
        {
            grid.Children.Add(part);
        }

        Child = grid;
        Wire(_left, (m, s) =>
        {
            double dx = m.X - s.Mouse.X;
            double right = s.Left + s.Width * s.Scale;
            double left = Math.Clamp(s.Left + dx, 0, right - MinWidth_ * s.Scale);
            Layout.Left = left;
            Layout.Width = (right - left) / s.Scale;
        });
        Wire(_right, (m, s) => Layout.Width = Math.Max(MinWidth_, s.Width + (m.X - s.Mouse.X) / s.Scale));
        Wire(_top, (m, s) =>
        {
            double dy = m.Y - s.Mouse.Y;
            double bottom = s.Top + s.Height * s.Scale;
            double top = Math.Clamp(s.Top + dy, 0, bottom - MinHeight_ * s.Scale);
            Layout.Top = top;
            Layout.Height = (bottom - top) / s.Scale;
        });
        Wire(_bottom, (m, s) => Layout.Height = Math.Max(MinHeight_, s.Height + (m.Y - s.Mouse.Y) / s.Scale));
        // a corner scales the element: the opposite corner stays where it is, the mouse sets the distance from it
        int index = 0;
        foreach (var (isLeft, isTop) in new[] { (true, true), (false, true), (true, false), (false, false) })
        {
            var corner = new Thumb
            {
                Width = 14, Height = 14, Cursor = isLeft == isTop ? Cursors.SizeNWSE : Cursors.SizeNESW,
                HorizontalAlignment = isLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = isTop ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                Template = CornerTemplate(), Visibility = Visibility.Collapsed,
            };
            bool l = isLeft, t = isTop;
            Wire(corner, (m, s) =>
            {
                double ax = s.Left + (l ? s.Width * s.Scale : 0), ay = s.Top + (t ? s.Height * s.Scale : 0);
                double d0 = Math.Max(12, Math.Sqrt((s.Mouse.X - ax) * (s.Mouse.X - ax) + (s.Mouse.Y - ay) * (s.Mouse.Y - ay)));
                double d1 = Math.Sqrt((m.X - ax) * (m.X - ax) + (m.Y - ay) * (m.Y - ay));
                Layout.Scale = Math.Clamp(s.Scale * d1 / d0, MinScale_, MaxScale_);
                Layout.Left = Math.Max(0, l ? ax - s.Width * Layout.Scale : ax);
                Layout.Top = Math.Max(0, t ? ay - s.Height * Layout.Scale : ay);
            });
            _corners[index++] = corner;
            grid.Children.Add(corner);
        }

        MouseLeftButtonDown += OnBodyDown;
        MouseMove += OnBodyMove;
        MouseLeftButtonUp += OnBodyUp;
        Apply();
    }

    public string Id { get; }

    public CombatElementLayout Layout { get; }

    /// <summary>The element is the only one in its window and fills it: the window's own frame is its outline and handles, so it shows none of its own.</summary>
    public bool Chromeless { get; set; }

    /// <summary>Puts the layout on the element: size, scale and place.</summary>
    public void Apply()
    {
        Layout.Scale = Math.Clamp(double.IsFinite(Layout.Scale) ? Layout.Scale : 1.0, MinScale_, MaxScale_);
        Layout.Width = Math.Max(MinWidth_, Layout.Width);
        Layout.Height = Math.Max(MinHeight_, Layout.Height);
        Width = Layout.Width;
        Height = Layout.Height;
        _scale.ScaleX = _scale.ScaleY = Layout.Scale;
        Canvas.SetLeft(this, Layout.Left);
        Canvas.SetTop(this, Layout.Top);
    }

    /// <summary>Edit mode on (unlocked) or off (locked: no outline, no handles, clicks go through the whole overlay anyway).</summary>
    public void SetEditing(bool editing)
    {
        if (Chromeless)
        {
            return;
        }

        _editing = editing;
        BorderBrush = editing ? EditBorder : Brushes.Transparent;
        Background = editing ? EditFill : null;
        Visibility visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        _caption.Visibility = visibility;
        foreach (Thumb corner in _corners)
        {
            corner.Visibility = visibility;
        }

        foreach (Thumb edge in new[] { _left, _right, _top, _bottom })
        {
            edge.Visibility = visibility;
        }

        Cursor = editing ? Cursors.SizeAll : Cursors.Arrow;
    }

    private static Thumb Edge(Cursor cursor) => new() { Cursor = cursor, Background = Brushes.Transparent, Visibility = Visibility.Collapsed, Template = EdgeTemplate() };

    private static ControlTemplate EdgeTemplate()
    {
        var template = new ControlTemplate(typeof(Thumb));
        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.SetValue(Panel.BackgroundProperty, Brushes.Transparent); // transparent but hit-testable
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
        square.SetValue(Border.BackgroundProperty, EditBorder);
        square.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        grid.AppendChild(square);
        template.VisualTree = grid;
        return template;
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private readonly record struct Start(Point Mouse, double Left, double Top, double Width, double Height, double Scale);

    /// <summary>A handle: remembers where the drag began and lets <paramref name="move"/> change the layout from the mouse's place on the stage
    /// (the stage's own units, so the overlay's scaling does not matter).</summary>
    private void Wire(Thumb thumb, Action<Point, Start> move)
    {
        Start start = default;
        thumb.DragStarted += (_, e) =>
        {
            start = new Start(Mouse.GetPosition(_stage), Layout.Left, Layout.Top, Layout.Width, Layout.Height, Layout.Scale);
        };
        thumb.DragDelta += (_, e) =>
        {
            move(Mouse.GetPosition(_stage), start);
            Apply();
            e.Handled = true;
        };
        thumb.DragCompleted += (_, _) => _save(Id, Layout);
    }

    private void OnBodyDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editing || e.OriginalSource is Thumb)
        {
            return;
        }

        _dragging = true;
        _dragStart = Mouse.GetPosition(_stage);
        _startLeft = Layout.Left;
        _startTop = Layout.Top;
        CaptureMouse();
        e.Handled = true; // the overlay frame must not start moving the whole window
    }

    private void OnBodyMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        Point now = Mouse.GetPosition(_stage);
        Layout.Left = Math.Max(0, _startLeft + now.X - _dragStart.X);
        Layout.Top = Math.Max(0, _startTop + now.Y - _dragStart.Y);
        Apply();
    }

    private void OnBodyUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
        _save(Id, Layout);
        e.Handled = true;
    }
}
