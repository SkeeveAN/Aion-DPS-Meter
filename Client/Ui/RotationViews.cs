using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>One line of "Rotation in short", with its icon.</summary>
public sealed record RotationLine(string Name, ImageSource? Icon, int Uses, string PerMinute, string AverageGap, string FirstAt, string Share);

/// <summary>One line of the effects table.</summary>
public sealed record BuffLine(string Name, ImageSource? Icon, string Uptime, string FromYou, int Applications, string Givers);

/// <summary>
/// The rotation on a timeline: every skill the player used at the moment they used it, support skills in the lane on top and damage skills below, a skill woven in
/// right after another one row lower, so a run of weaves reads as a staircase. Behind it the player's DPS (the average of the last five seconds) and the moments a monster
/// was staggered. Hover an icon for the use; drag the picture to move along the fight, Ctrl + wheel to zoom (the scroll viewer around it does the scrolling).
/// </summary>
public sealed class RotationTimelineView : FrameworkElement
{
    private const double Margin = 16, AxisHeight = 18, SupportLane = 30, RowHeight = 24, IconSize = 21;

    private RotationReport? _report;
    private double[] _dps = Array.Empty<double>();
    private double _pixelsPerSecond = 14;
    private bool _showPassives = true, _showDps = true, _showStagger = true;
    private readonly List<(Rect Box, RotationUse Use)> _boxes = new();
    private RotationUse? _hover;
    private bool _panning;
    private double _panStartX, _panStartOffset;

    public RotationTimelineView()
    {
        ClipToBounds = true;
        Cursor = Cursors.Hand;
    }

    public double PixelsPerSecond => _pixelsPerSecond;

    public void Show(RotationReport report, double[] dpsPerSecond)
    {
        _report = report;
        _dps = dpsPerSecond;
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void Configure(bool showPassives, bool showDps, bool showStagger)
    {
        _showPassives = showPassives;
        _showDps = showDps;
        _showStagger = showStagger;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = _report is null ? 200 : Margin * 2 + _report.DurationSeconds * _pixelsPerSecond;
        return new Size(Math.Max(width, 200), AxisHeight + SupportLane + RowHeight * 4 + 14);
    }

    private double X(double seconds) => Margin + seconds * _pixelsPerSecond;

    protected override void OnRender(DrawingContext dc)
    {
        _boxes.Clear();
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_report is null || !_report.HasData)
        {
            return;
        }

        var text = (Brush)(TryFindResource("Brush.TextMuted") ?? Brushes.Gray);
        var line = new Pen((Brush)(TryFindResource("Brush.Border") ?? Brushes.DimGray), 1);
        var accent = (Brush)(TryFindResource("Brush.Accent") ?? Brushes.Orange);
        double height = ActualHeight;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // seconds axis
        double step = _pixelsPerSecond >= 40 ? 1 : _pixelsPerSecond >= 18 ? 5 : _pixelsPerSecond >= 8 ? 10 : 30;
        for (double t = 0; t <= _report.DurationSeconds; t += step)
        {
            double x = X(t);
            dc.DrawLine(line, new Point(x, AxisHeight), new Point(x, height));
            var label = new FormattedText(TimeSpan.FromSeconds(t).ToString(t >= 3600 ? @"h\:mm\:ss" : @"m\:ss"), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 10, text, dpi);
            dc.DrawText(label, new Point(x + 2, 1));
        }

        // stagger phases
        if (_showStagger)
        {
            var band = new SolidColorBrush(Color.FromArgb(0x38, 0xB0, 0x6B, 0xFF));
            foreach (StaggerPhase phase in _report.Staggers)
            {
                dc.DrawRectangle(band, null, new Rect(X(phase.Start), AxisHeight, Math.Max(2, (phase.End - phase.Start) * _pixelsPerSecond), height - AxisHeight));
            }
        }

        // DPS behind the skills
        if (_showDps && _dps.Length > 1)
        {
            double max = Math.Max(1, _dps.Max());
            double baseline = height - 4, usable = height - AxisHeight - 10;
            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(X(0), baseline), true, true);
                for (int i = 0; i < _dps.Length; i++)
                {
                    ctx.LineTo(new Point(X(i), baseline - usable * _dps[i] / max), true, false);
                }

                ctx.LineTo(new Point(X(_dps.Length - 1), baseline), true, false);
            }

            geometry.Freeze();
            var fill = accent.Clone();
            fill.Opacity = 0.16;
            dc.DrawGeometry(fill, null, geometry);
        }

        // the skills
        foreach (RotationUse use in _report.Uses)
        {
            if (use.Passive && !_showPassives)
            {
                continue;
            }

            double x = X(use.At) - IconSize / 2;
            double y = use.Damaging ? AxisHeight + SupportLane + use.Row * RowHeight : AxisHeight + 4;
            var box = new Rect(x, y, IconSize, IconSize);
            _boxes.Add((box, use));
            ImageSource? icon = OverlayPictures.Load(Aion2SkillIcons.PathFor(use.SkillId));
            if (icon is not null)
            {
                dc.DrawImage(icon, box);
            }
            else
            {
                var fillBrush = new SolidColorBrush(Color.FromRgb((byte)(60 + use.SkillId * 7 % 120), (byte)(60 + use.SkillId * 13 % 120), (byte)(70 + use.SkillId * 29 % 120)));
                dc.DrawRoundedRectangle(fillBrush, null, box, 3, 3);
                var letter = new FormattedText(use.Name.Length > 0 ? use.Name[..1] : "?", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.White, dpi);
                dc.DrawText(letter, new Point(box.X + (IconSize - letter.Width) / 2, box.Y + (IconSize - letter.Height) / 2));
            }

            if (use.Passive)
            {
                dc.DrawRectangle(null, new Pen(Brushes.Gray, 1) { DashStyle = DashStyles.Dot }, box);
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point p = e.GetPosition(this);
        if (_panning && FindScroll() is ScrollViewer scroll)
        {
            Point inScroll = e.GetPosition(scroll);
            scroll.ScrollToHorizontalOffset(_panStartOffset - (inScroll.X - _panStartX));
            return;
        }

        RotationUse? hit = null;
        for (int i = _boxes.Count - 1; i >= 0; i--)
        {
            if (_boxes[i].Box.Contains(p))
            {
                hit = _boxes[i].Use;
                break;
            }
        }

        if (!Equals(hit, _hover))
        {
            _hover = hit;
            ToolTip = hit is null ? null : Describe(hit);
        }
    }

    private string Describe(RotationUse use)
    {
        var loc = LocalizationManager.Instance;
        var lines = new List<string>
        {
            Aion2SkillNames.Display(use.Name),
            TimeSpan.FromSeconds(use.At).ToString(@"m\:ss\.f") + " · " + string.Format(loc["Rotation.UseOf"], use.Number, use.OfTotal),
        };
        if (use.GapSeconds > 0)
        {
            lines.Add(string.Format(loc["Rotation.SincePrevious"], use.GapSeconds.ToString("0.0")));
        }

        int second = (int)Math.Clamp(use.At, 0, Math.Max(0, _dps.Length - 1));
        if (_dps.Length > 0)
        {
            lines.Add(string.Format(loc["Rotation.DpsThen"], _dps[second].ToString("N0")));
        }

        return string.Join("\n", lines);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (FindScroll() is ScrollViewer scroll)
        {
            _panning = true;
            _panStartX = e.GetPosition(scroll).X;
            _panStartOffset = scroll.HorizontalOffset;
            CaptureMouse();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _panning = false;
        ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return; // the page scrolls
        }

        double factor = e.Delta > 0 ? 1.25 : 0.8;
        double before = e.GetPosition(this).X;
        double seconds = (before - Margin) / _pixelsPerSecond;
        _pixelsPerSecond = Math.Clamp(_pixelsPerSecond * factor, 3, 80);
        InvalidateMeasure();
        InvalidateVisual();
        if (FindScroll() is ScrollViewer scroll)
        {
            // keep the second under the mouse where it is
            Dispatcher.BeginInvoke(() => scroll.ScrollToHorizontalOffset(Math.Max(0, Margin + seconds * _pixelsPerSecond - (before - scroll.HorizontalOffset))), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        e.Handled = true;
    }

    private ScrollViewer? FindScroll()
    {
        DependencyObject? parent = VisualTreeHelper.GetParent(this);
        while (parent is not null and not ScrollViewer)
        {
            parent = VisualTreeHelper.GetParent(parent);
        }

        return parent as ScrollViewer;
    }
}
