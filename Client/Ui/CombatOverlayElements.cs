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
/// smaller at each of its four edges, and scaled with the grip at its bottom right corner - the overlay's own grip then scales all of them together.
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
    private readonly Thumb _grip;
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
        _grip = new Thumb
        {
            Width = 12, Height = 12, Cursor = Cursors.SizeNWSE, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Template = GripTemplate(), Visibility = Visibility.Collapsed,
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
        foreach (UIElement part in new UIElement[] { _caption, _left, _right, _top, _bottom, _grip })
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
        Wire(_grip, (m, s) =>
        {
            double rx = (m.X - s.Left) / Math.Max(1, s.Width * s.Scale);
            double ry = (m.Y - s.Top) / Math.Max(1, s.Height * s.Scale);
            Layout.Scale = Math.Clamp(s.Scale * Math.Max(0.1, (rx + ry) / 2), MinScale_, MaxScale_);
        });

        MouseLeftButtonDown += OnBodyDown;
        MouseMove += OnBodyMove;
        MouseLeftButtonUp += OnBodyUp;
        Apply();
    }

    public string Id { get; }

    public CombatElementLayout Layout { get; }

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
        _editing = editing;
        BorderBrush = editing ? EditBorder : Brushes.Transparent;
        Background = editing ? EditFill : null;
        Visibility visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        _caption.Visibility = visibility;
        _grip.Visibility = visibility;
        foreach (Thumb edge in new[] { _left, _right, _top, _bottom })
        {
            edge.Visibility = visibility;
        }

        Cursor = editing ? Cursors.SizeAll : Cursors.Arrow;
    }

    private static Thumb Edge(Cursor cursor) => new() { Cursor = cursor, Background = Brushes.Transparent, Opacity = 0.01, Visibility = Visibility.Collapsed, Template = EdgeTemplate() };

    private static ControlTemplate EdgeTemplate()
    {
        var template = new ControlTemplate(typeof(Thumb));
        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.SetValue(Panel.BackgroundProperty, Brushes.Transparent); // transparent but hit-testable
        template.VisualTree = grid;
        return template;
    }

    private static ControlTemplate GripTemplate()
    {
        var template = new ControlTemplate(typeof(Thumb));
        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        var path = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        path.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M11,1 L1,11 M11,5 L5,11 M11,9 L9,11"));
        path.SetValue(System.Windows.Shapes.Shape.StrokeProperty, EditBorder);
        path.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.2);
        grid.AppendChild(path);
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
