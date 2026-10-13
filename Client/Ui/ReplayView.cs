using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>
/// The fight from above: the group as class-coloured dots, the bosses as large ones with the way they face, the areas of the skills a boss is winding up (amber while it winds up,
/// red as it lands), a red ring on whoever it hit, the dead crossed out, and the last few seconds of each walk as a trail. Positions are the game's own readings (it sends them with
/// movements and skills, not on a regular beat), drawn straight between two of them. Drag to move the map, the wheel zooms, a double click fits it again.
/// </summary>
public sealed class ReplayView : FrameworkElement
{
    private const double TrailSeconds = 6, FlashSeconds = 0.6, LandSeconds = 0.45;

    private ReplayData? _data;
    private double _time;
    private bool _showNames = true;
    private double _scale = 0.02; // pixels per centimetre
    private Point _origin; // the world point at the middle of the view
    private bool _fitted, _panning;
    private Point _panStart, _panOrigin;

    public ReplayView()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = Cursors.Hand;
    }

    public void Show(ReplayData data)
    {
        _data = data;
        _fitted = false;
        InvalidateVisual();
    }

    public double Time
    {
        get => _time;
        set
        {
            _time = value;
            InvalidateVisual();
        }
    }

    public bool ShowNames
    {
        get => _showNames;
        set
        {
            _showNames = value;
            InvalidateVisual();
        }
    }

    /// <summary>Fits the map to everything that was drawn, with a margin.</summary>
    public void Fit()
    {
        _fitted = false;
        InvalidateVisual();
    }

    private void FitNow()
    {
        if (_data is null || ActualWidth < 10 || ActualHeight < 10)
        {
            return;
        }

        var xs = new List<double>();
        var ys = new List<double>();
        foreach (ReplayEntity e in _data.Entities)
        {
            // the middle 96 % of each walk: one wrong reading must not shrink the whole map
            var px = e.Track.X.OrderBy(v => v).ToArray();
            var py = e.Track.Y.OrderBy(v => v).ToArray();
            xs.Add(px[(int)(px.Length * 0.02)]);
            xs.Add(px[Math.Min(px.Length - 1, (int)(px.Length * 0.98))]);
            ys.Add(py[(int)(py.Length * 0.02)]);
            ys.Add(py[Math.Min(py.Length - 1, (int)(py.Length * 0.98))]);
        }

        foreach (ReplayArea a in _data.Areas)
        {
            xs.Add(a.X - a.Radius);
            xs.Add(a.X + a.Radius);
            ys.Add(a.Y - a.Radius);
            ys.Add(a.Y + a.Radius);
        }

        if (xs.Count == 0)
        {
            return;
        }

        double minX = xs.Min(), maxX = xs.Max(), minY = ys.Min(), maxY = ys.Max();
        double w = Math.Max(maxX - minX, 800), h = Math.Max(maxY - minY, 800);
        _scale = Math.Min((ActualWidth - 60) / w, (ActualHeight - 60) / h);
        _origin = new Point((minX + maxX) / 2, (minY + maxY) / 2);
        _fitted = true;
    }

    private Point ToScreen(double x, double y) => new(ActualWidth / 2 + (x - _origin.X) * _scale, ActualHeight / 2 - (y - _origin.Y) * _scale);

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        var back = (Brush)(TryFindResource("Brush.Panel") ?? Brushes.Black);
        dc.DrawRectangle(back, null, new Rect(0, 0, width, height));
        if (_data is null || !_data.HasData)
        {
            return;
        }

        if (!_fitted)
        {
            FitNow();
        }

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var muted = (Brush)(TryFindResource("Brush.TextMuted") ?? Brushes.Gray);
        var text = (Brush)(TryFindResource("Brush.Text") ?? Brushes.White);
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0x9D, 0xB3, 0xC2)), 1);

        // a grid of ten metres, to show the distances
        double stepCm = 1000;
        double left = _origin.X - width / 2 / _scale, right = _origin.X + width / 2 / _scale, bottom = _origin.Y - height / 2 / _scale, top = _origin.Y + height / 2 / _scale;
        if (_scale * stepCm >= 14)
        {
            for (double gx = Math.Floor(left / stepCm) * stepCm; gx <= right; gx += stepCm)
            {
                dc.DrawLine(gridPen, ToScreen(gx, bottom), ToScreen(gx, top));
            }

            for (double gy = Math.Floor(bottom / stepCm) * stepCm; gy <= top; gy += stepCm)
            {
                dc.DrawLine(gridPen, ToScreen(left, gy), ToScreen(right, gy));
            }
        }

        // the areas
        foreach (ReplayArea area in _data.Areas)
        {
            if (_time < area.Start || _time > area.End + LandSeconds)
            {
                continue;
            }

            Point c = ToScreen(area.X, area.Y);
            double r = area.Radius * _scale;
            bool landing = _time > area.End;
            double progress = Math.Clamp((_time - area.Start) / Math.Max(0.05, area.End - area.Start), 0, 1);
            Color fill = landing ? Color.FromArgb((byte)(0x80 * (1 - (_time - area.End) / LandSeconds)), 0xFF, 0x4D, 0x4D) : Color.FromArgb((byte)(0x16 + 0x40 * progress), 0xFF, 0xBA, 0x42);
            var pen = new Pen(new SolidColorBrush(landing ? Color.FromArgb(0xC0, 0xFF, 0x4D, 0x4D) : Color.FromArgb(0xC0, 0xFF, 0xBA, 0x42)), 1.5);
            if (area.RadiusCm is null)
            {
                pen.DashStyle = DashStyles.Dash; // nobody has measured this one: its size is a guess
            }

            dc.DrawEllipse(new SolidColorBrush(fill), pen, c, r, r);
            if (!landing && r > 8)
            {
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xBA, 0x42)), 1), c, r * progress, r * progress);
            }
        }

        // trails, then the entities
        foreach (ReplayEntity e in _data.Entities.OrderBy(e => e.Role == ReplayRole.Boss ? 1 : 0))
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), 1);
            Point? previous = null;
            for (double t = Math.Max(0, _time - TrailSeconds); t <= _time; t += 0.5)
            {
                if (e.At(_data.Start, t) is { } p)
                {
                    Point s = ToScreen(p.X, p.Y);
                    if (previous is Point q)
                    {
                        dc.DrawLine(pen, q, s);
                    }

                    previous = s;
                }
            }

            if (e.At(_data.Start, _time) is not { } now)
            {
                continue;
            }

            Point at = ToScreen(now.X, now.Y);
            ReplayDeath? death = _data.Deaths.LastOrDefault(d => d.Entity == e.Id && d.At <= _time);
            bool dead = death is not null && e.Role == ReplayRole.Player;
            if (e.Role == ReplayRole.Boss)
            {
                double rb = Math.Max(9, 160 * _scale);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xC0, 0x45, 0x2E)), new Pen(Brushes.White, 1.5), at, rb, rb);
                ReplayArea? facing = _data.Areas.LastOrDefault(a => a.Actor == e.Id && a.Start <= _time && a.Start >= _time - 6);
                if (facing is not null)
                {
                    double ang = facing.Facing * Math.PI / 180;
                    dc.DrawLine(new Pen(Brushes.White, 2), at, new Point(at.X + Math.Cos(ang) * rb * 1.8, at.Y - Math.Sin(ang) * rb * 1.8));
                }

                var label = new FormattedText(e.Name, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, text, dpi);
                dc.DrawText(label, new Point(at.X - label.Width / 2, at.Y + rb + 2));
            }
            else
            {
                Brush colour = dead ? Brushes.DimGray : ClassBrushes.Solid(e.ClassName);
                dc.DrawEllipse(colour, new Pen(Brushes.Black, 1), at, 6, 6);
                if (dead)
                {
                    var cross = new Pen(Brushes.OrangeRed, 2);
                    dc.DrawLine(cross, new Point(at.X - 6, at.Y - 6), new Point(at.X + 6, at.Y + 6));
                    dc.DrawLine(cross, new Point(at.X - 6, at.Y + 6), new Point(at.X + 6, at.Y - 6));
                }

                // the hit it just took: a red ring
                double flash = _data.Hits.Where(h => h.Victim == e.Id && h.At <= _time && _time - h.At <= FlashSeconds).Select(h => 1 - (_time - h.At) / FlashSeconds).DefaultIfEmpty(0).Max();
                if (flash > 0)
                {
                    dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(255 * flash), 0xFF, 0x3B, 0x3B)), 3), at, 6 + 10 * (1 - flash), 6 + 10 * (1 - flash));
                }

                if (_showNames)
                {
                    var label = new FormattedText(e.Name, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, dead ? muted : text, dpi);
                    dc.DrawText(label, new Point(at.X - label.Width / 2, at.Y + 8));
                }
            }
        }

        // the time and what the boss is doing
        var status = new List<string> { TimeSpan.FromSeconds(_time).ToString(@"m\:ss") };
        ReplayHp? hp = _data.Hp.FirstOrDefault();
        if (hp is not null)
        {
            int i = Array.FindLastIndex(hp.Seconds, s => s <= _time);
            if (i >= 0)
            {
                status.Add($"{hp.Fraction[i] * 100:0}%");
            }
        }

        ReplayArea? current = _data.Areas.LastOrDefault(a => a.Start <= _time && a.End + LandSeconds >= _time);
        if (current is not null)
        {
            status.Add(current.SkillName);
        }

        if (_data.Staggers.Any(s => s.Start <= _time && s.End >= _time))
        {
            status.Add(LocalizationManager.Instance["Replay.Staggered"]);
        }

        var header = new FormattedText(string.Join("  ·  ", status), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13, text, dpi);
        dc.DrawText(header, new Point(10, 8));
        double bar = 10 * _scale * 100; // ten metres
        if (bar >= 20)
        {
            var scalePen = new Pen(muted, 2);
            dc.DrawLine(scalePen, new Point(10, height - 14), new Point(10 + bar, height - 14));
            var meters = new FormattedText("10 m", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, muted, dpi);
            dc.DrawText(meters, new Point(14 + bar, height - 22));
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Point mouse = e.GetPosition(this);
        double worldX = _origin.X + (mouse.X - ActualWidth / 2) / _scale, worldY = _origin.Y - (mouse.Y - ActualHeight / 2) / _scale;
        _scale = Math.Clamp(_scale * (e.Delta > 0 ? 1.2 : 1 / 1.2), 0.002, 1.0);
        _origin = new Point(worldX - (mouse.X - ActualWidth / 2) / _scale, worldY + (mouse.Y - ActualHeight / 2) / _scale);
        _fitted = true;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2)
        {
            Fit();
            return;
        }

        _panning = true;
        _panStart = e.GetPosition(this);
        _panOrigin = _origin;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_panning)
        {
            Point now = e.GetPosition(this);
            _origin = new Point(_panOrigin.X - (now.X - _panStart.X) / _scale, _panOrigin.Y + (now.Y - _panStart.Y) / _scale);
            _fitted = true;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _panning = false;
        ReleaseMouseCapture();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _fitted = false;
    }
}

/// <summary>The strip under the map: the whole fight with its big attacks (amber), the hits on the group (red), deaths (x), staggers (shaded) and the boss's health as a line; click or
/// drag on it to go to that moment.</summary>
public sealed class ReplayTimeline : FrameworkElement
{
    private ReplayData? _data;
    private double _time;
    private bool _dragging;

    public event Action<double>? Seek;

    public ReplayTimeline()
    {
        Cursor = Cursors.Hand;
        Height = 54;
    }

    public void Show(ReplayData data)
    {
        _data = data;
        InvalidateVisual();
    }

    public double Time
    {
        set
        {
            _time = value;
            InvalidateVisual();
        }
    }

    private double X(double seconds) => 8 + seconds / Math.Max(1, _data?.DurationSeconds ?? 1) * Math.Max(1, ActualWidth - 16);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (_data is null)
        {
            return;
        }

        var border = (Brush)(TryFindResource("Brush.Border") ?? Brushes.DimGray);
        dc.DrawLine(new Pen(border, 1), new Point(8, h - 10), new Point(w - 8, h - 10));
        foreach (StaggerPhase s in _data.Staggers)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x40, 0xB0, 0x6B, 0xFF)), null, new Rect(X(s.Start), 2, Math.Max(2, X(s.End) - X(s.Start)), h - 12));
        }

        if (_data.Hp.FirstOrDefault() is { } hp)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0x5B, 0xD0, 0x7A)), 1.2);
            for (int i = 1; i < hp.Seconds.Length; i++)
            {
                dc.DrawLine(pen, new Point(X(hp.Seconds[i - 1]), h - 12 - (h - 24) * hp.Fraction[i - 1]), new Point(X(hp.Seconds[i]), h - 12 - (h - 24) * hp.Fraction[i]));
            }
        }

        var amber = new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xBA, 0x42)), 1);
        foreach (ReplayArea a in _data.Areas)
        {
            dc.DrawLine(amber, new Point(X(a.Start), 4), new Point(X(a.Start), 16));
        }

        var red = new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0x4D, 0x4D)), 1);
        foreach (ReplayHit hit in _data.Hits)
        {
            dc.DrawLine(red, new Point(X(hit.At), 18), new Point(X(hit.At), 26));
        }

        var cross = new Pen(Brushes.OrangeRed, 1.5);
        foreach (ReplayDeath d in _data.Deaths)
        {
            double x = X(d.At);
            dc.DrawLine(cross, new Point(x - 4, 30), new Point(x + 4, 38));
            dc.DrawLine(cross, new Point(x - 4, 38), new Point(x + 4, 30));
        }

        var accent = (Brush)(TryFindResource("Brush.Accent") ?? Brushes.Orange);
        dc.DrawLine(new Pen(accent, 2), new Point(X(_time), 0), new Point(X(_time), h - 4));
    }

    private void SeekTo(Point p)
    {
        if (_data is null)
        {
            return;
        }

        Seek?.Invoke(Math.Clamp((p.X - 8) / Math.Max(1, ActualWidth - 16), 0, 1) * _data.DurationSeconds);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragging = true;
        CaptureMouse();
        SeekTo(e.GetPosition(this));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            SeekTo(e.GetPosition(this));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        ReleaseMouseCapture();
    }
}
