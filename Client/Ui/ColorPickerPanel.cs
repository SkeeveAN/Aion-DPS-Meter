using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AionDPS.Ui;

/// <summary>
/// A colour picker: a square for saturation (across) and brightness (down) of the chosen hue, and a bar with all hues below it.
/// Click or drag in either; <see cref="Picked"/> fires with the colour while the user moves, never when the colour is set from outside.
/// </summary>
public sealed class ColorPickerPanel : StackPanel
{
    private const double Width = 230, SquareHeight = 150, BarHeight = 18;

    private readonly GradientStop _hueEnd = new(Colors.Red, 1);
    private readonly Ellipse _squareMarker = new() { Width = 14, Height = 14, Stroke = Brushes.White, StrokeThickness = 2, Fill = Brushes.Transparent, IsHitTestVisible = false };
    private readonly Rectangle _barMarker = new() { Width = 4, Height = BarHeight + 4, Stroke = Brushes.Black, StrokeThickness = 1, Fill = Brushes.White, IsHitTestVisible = false };
    private readonly Canvas _squareCanvas = new() { Width = Width, Height = SquareHeight, Background = Brushes.Transparent, Cursor = Cursors.Cross };
    private readonly Canvas _barCanvas = new() { Width = Width, Height = BarHeight, Background = Brushes.Transparent, Cursor = Cursors.Hand };
    private double _hue, _saturation, _value = 1;
    private bool _setting;

    public event Action<Color>? Picked;

    public ColorPickerPanel()
    {
        var across = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        across.GradientStops.Add(new GradientStop(Colors.White, 0));
        across.GradientStops.Add(_hueEnd);
        var down = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        down.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        down.GradientStops.Add(new GradientStop(Colors.Black, 1));
        var square = new Grid { Width = Width, Height = SquareHeight };
        square.Children.Add(new Rectangle { Fill = across });
        square.Children.Add(new Rectangle { Fill = down });
        _squareCanvas.Children.Add(_squareMarker);
        square.Children.Add(_squareCanvas);

        var hues = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (int i = 0; i <= 6; i++)
        {
            hues.GradientStops.Add(new GradientStop(FromHsv(i * 60, 1, 1), i / 6.0));
        }

        var bar = new Grid { Width = Width, Height = BarHeight, Margin = new Thickness(0, 8, 0, 0) };
        bar.Children.Add(new Rectangle { Fill = hues, RadiusX = 3, RadiusY = 3 });
        _barCanvas.Children.Add(_barMarker);
        bar.Children.Add(_barCanvas);

        Children.Add(square);
        Children.Add(bar);
        Drag(_squareCanvas, point =>
        {
            _saturation = Math.Clamp(point.X / Width, 0, 1);
            _value = 1 - Math.Clamp(point.Y / SquareHeight, 0, 1);
        });
        Drag(_barCanvas, point => _hue = Math.Clamp(point.X / Width, 0, 1) * 360);
        Place();
    }

    /// <summary>Shows a colour (from the field's box or its default); raises nothing.</summary>
    public void SetColor(Color color)
    {
        _setting = true;
        ToHsv(color, out double h, out double s, out double v);
        if (s > 0.001 && v > 0.001)
        {
            _hue = h; // grey and black have no hue: the bar keeps where it was
        }

        _saturation = s;
        _value = v;
        Place();
        _setting = false;
    }

    private void Drag(Canvas canvas, Action<Point> apply)
    {
        void Move(MouseEventArgs e)
        {
            apply(e.GetPosition(canvas));
            Place();
            if (!_setting)
            {
                Picked?.Invoke(FromHsv(_hue, _saturation, _value));
            }
        }

        canvas.MouseLeftButtonDown += (_, e) => { canvas.CaptureMouse(); Move(e); };
        canvas.MouseMove += (_, e) => { if (canvas.IsMouseCaptured) { Move(e); } };
        canvas.MouseLeftButtonUp += (_, _) => canvas.ReleaseMouseCapture();
    }

    private void Place()
    {
        _hueEnd.Color = FromHsv(_hue, 1, 1);
        Canvas.SetLeft(_squareMarker, _saturation * Width - _squareMarker.Width / 2);
        Canvas.SetTop(_squareMarker, (1 - _value) * SquareHeight - _squareMarker.Height / 2);
        Canvas.SetLeft(_barMarker, _hue / 360 * Width - _barMarker.Width / 2);
        Canvas.SetTop(_barMarker, -2);
    }

    public static Color FromHsv(double hue, double saturation, double value)
    {
        hue = (hue % 360 + 360) % 360;
        double c = value * saturation;
        double x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        double m = value - c;
        (double r, double g, double b) = (int)(hue / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    public static void ToHsv(Color color, out double hue, out double saturation, out double value)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        value = max;
        saturation = max <= 0 ? 0 : delta / max;
        if (delta <= 0)
        {
            hue = 0;
        }
        else if (max == r)
        {
            hue = 60 * (((g - b) / delta % 6 + 6) % 6);
        }
        else if (max == g)
        {
            hue = 60 * ((b - r) / delta + 2);
        }
        else
        {
            hue = 60 * ((r - g) / delta + 4);
        }
    }
}
