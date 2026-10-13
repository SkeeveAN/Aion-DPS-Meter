using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.History;

namespace AionDPS.Ui;

/// <summary>
/// The stays as a strip on a clock: a window of a few hours that is pushed along with the mouse, the arrows or the wheel, and below it the
/// whole recorded range as a thin overview with a frame around what the strip shows. A click on a stay picks it. Drawn by hand (one control,
/// no child per stay), so a week of stays costs nothing.
/// </summary>
public sealed class SessionTimeline : FrameworkElement
{
    private const double StripTop = 18;
    private const double StripHeight = 40;
    private const double AxisTop = 62;
    private const double OverviewTop = 84;
    private const double OverviewHeight = 12;
    private const double DragThreshold = 4;

    private static readonly TimeSpan[] SpanSteps =
    {
        TimeSpan.FromHours(1), TimeSpan.FromHours(6), TimeSpan.FromHours(12), TimeSpan.FromHours(24),
    };

    private IReadOnlyList<SessionInfo> _sessions = Array.Empty<SessionInfo>();
    private DateTime _viewStart;
    private TimeSpan _span = TimeSpan.FromHours(12);
    private bool _follow = true;
    private Point _down;
    private bool _pressed;
    private bool _dragging;
    private bool _overviewDrag;
    private DateTime _dragViewStart;
    private SessionInfo? _hover;

    public SessionTimeline()
    {
        Height = OverviewTop + OverviewHeight + 4;
        ClipToBounds = true;
        Focusable = false;
        Cursor = Cursors.Arrow;
        _viewStart = DateTime.Now - _span;
    }

    public SessionInfo? Selected { get; private set; }

    /// <summary>Raised when a click picks a stay.</summary>
    public event Action<SessionInfo>? SessionPicked;

    public TimeSpan Span => _span;

    public bool Following => _follow;

    /// <summary>Hands the control the stays to draw (oldest first) and what is picked; keeps the view where it is, or on "now" while following.</summary>
    public void SetSessions(IReadOnlyList<SessionInfo> sessions, SessionInfo? selected)
    {
        _sessions = sessions;
        Selected = selected;
        Follow();
        InvalidateVisual();
    }

    /// <summary>Keeps the right edge on the present when the view is following it.</summary>
    public void Follow()
    {
        if (_follow)
        {
            _viewStart = DateTime.Now - _span + TimeSpan.FromMinutes(Math.Max(1, _span.TotalMinutes * 0.03));
        }

        InvalidateVisual();
    }

    public void SetSpan(TimeSpan span)
    {
        // Zooming keeps the middle of the strip where it is; a following view stays on the present.
        DateTime middle = _viewStart + _span / 2;
        _span = span;
        if (_follow)
        {
            Follow();
        }
        else
        {
            _viewStart = middle - _span / 2;
            Clamp();
        }

        InvalidateVisual();
    }

    /// <summary>Moves the strip by a share of its width: negative is earlier.</summary>
    public void Pan(double share)
    {
        _follow = false;
        _viewStart += TimeSpan.FromTicks((long)(_span.Ticks * share));
        Clamp();
        InvalidateVisual();
    }

    public void JumpToNow()
    {
        _follow = true;
        Follow();
    }

    /// <summary>Brings a stay into the strip (picked in the list): shows it whole, or its start when it is longer than the strip.</summary>
    public void Reveal(SessionInfo session)
    {
        DateTime end = session.End ?? DateTime.Now;
        if (session.Start >= _viewStart && end <= _viewStart + _span)
        {
            return;
        }

        _follow = false;
        TimeSpan length = end - session.Start;
        _viewStart = length >= _span ? session.Start - _span / 20 : session.Start + length / 2 - _span / 2;
        Clamp();
        InvalidateVisual();
    }

    private DateTime DataStart => _sessions.Count > 0 ? _sessions.Min(s => s.Start) : DateTime.Now.AddHours(-1);

    private void Clamp()
    {
        DateTime earliest = DataStart - _span / 4;
        DateTime latest = DateTime.Now + _span / 8 - _span;
        if (_viewStart < earliest)
        {
            _viewStart = earliest;
        }

        if (_viewStart > latest)
        {
            _viewStart = latest;
            _follow = true;
        }
    }

    private double PixelsPerTick => Math.Max(1, ActualWidth) / _span.Ticks;

    private double XOf(DateTime time) => (time - _viewStart).Ticks * PixelsPerTick;

    private DateTime TimeAt(double x) => _viewStart + TimeSpan.FromTicks((long)(x / PixelsPerTick));

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        if (width < 20)
        {
            return;
        }

        Brush lane = Resource("Brush.Window", Brushes.Black);
        Brush border = Resource("Brush.Border", Brushes.Gray);
        Brush muted = Resource("Brush.TextMuted", Brushes.Gray);
        Brush text = Resource("Brush.Text", Brushes.White);
        Brush accent = Resource("Brush.Accent", Brushes.Goldenrod);
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // The strip: an empty lane, the stays on it, then the clock under it.
        dc.DrawRoundedRectangle(lane, new Pen(border, 1), new Rect(0.5, StripTop + 0.5, width - 1, StripHeight - 1), 5, 5);
        dc.PushClip(new RectangleGeometry(new Rect(1, StripTop + 1, width - 2, StripHeight - 2)));
        DateTime viewEnd = _viewStart + _span;
        foreach (SessionInfo session in _sessions)
        {
            DateTime end = session.End ?? DateTime.Now;
            if (end < _viewStart || session.Start > viewEnd)
            {
                continue;
            }

            double x1 = Math.Max(0, XOf(session.Start));
            double x2 = Math.Min(width, XOf(end));
            var rect = new Rect(x1 + 0.5, StripTop + 3, Math.Max(2, x2 - x1 - 1), StripHeight - 6);
            Color color = SessionLabels.ColorOf(session.Kind);
            Brush fill = new SolidColorBrush(color) { Opacity = ReferenceEquals(session, Selected) ? 1.0 : 0.78 };
            Pen? outline = ReferenceEquals(session, Selected) ? new Pen(text, 2) : ReferenceEquals(session, _hover) ? new Pen(muted, 1) : null;
            dc.DrawRoundedRectangle(fill, outline, rect, 4, 4);
            if (rect.Width > 56)
            {
                string label = SessionLabels.NameOf(session);
                var formatted = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 12,
                    new SolidColorBrush(Color.FromRgb(0x0C, 0x14, 0x1A)), dip) { MaxTextWidth = Math.Max(1, rect.Width - 12), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                dc.DrawText(formatted, new Point(rect.X + 6, rect.Y + (rect.Height - formatted.Height) / 2));
            }
        }

        dc.Pop();

        // Marks above the strip: a star for a kept stay, a flame for one with a boss that raged.
        foreach (SessionInfo session in _sessions)
        {
            DateTime end = session.End ?? DateTime.Now;
            double x = XOf(session.Start) + 3;
            if (end < _viewStart || session.Start > viewEnd || x < 0 || x > width - 14)
            {
                continue;
            }

            string marks = (session.IsSaved ? "★" : "") + (session.CachedAnalysis is { Result.HasEnrage: true } ? "▲" : "");
            if (marks.Length > 0)
            {
                dc.DrawText(new FormattedText(marks, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Symbol"), 11,
                    session.IsSaved ? accent : muted, dip), new Point(Math.Max(2, x), 2));
            }
        }

        DrawAxis(dc, width, muted, border, dip);
        DrawOverview(dc, width, lane, border, accent);

        // "now", when it is inside the strip.
        double nowX = XOf(DateTime.Now);
        if (nowX > 0 && nowX < width)
        {
            var nowPen = new Pen(new SolidColorBrush(Color.FromRgb(0xC0, 0x6A, 0x62)), 1.5);
            dc.DrawLine(nowPen, new Point(nowX, StripTop - 4), new Point(nowX, StripTop + StripHeight + 4));
        }
    }

    private void DrawAxis(DrawingContext dc, double width, Brush muted, Brush border, double dip)
    {
        TimeSpan step = AxisStep();
        DateTime first = Floor(_viewStart, step);
        var pen = new Pen(border, 1);
        for (DateTime tick = first; tick <= _viewStart + _span; tick += step)
        {
            double x = XOf(tick);
            if (x < 0 || x > width)
            {
                continue;
            }

            dc.DrawLine(pen, new Point(x, StripTop + StripHeight), new Point(x, StripTop + StripHeight + 4));
            string label = tick.TimeOfDay == TimeSpan.Zero
                ? tick.ToString("ddd d.M.", CultureInfo.CurrentCulture)
                : tick.ToString("HH:mm", CultureInfo.InvariantCulture);
            var formatted = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, muted, dip);
            dc.DrawText(formatted, new Point(Math.Min(Math.Max(0, x - formatted.Width / 2), width - formatted.Width), AxisTop));
        }
    }

    /// <summary>About one label per 90 pixels, on a round time.</summary>
    private TimeSpan AxisStep()
    {
        TimeSpan wanted = TimeSpan.FromTicks((long)(_span.Ticks * 90 / Math.Max(180, ActualWidth)));
        TimeSpan[] steps =
        {
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1),
            TimeSpan.FromHours(2), TimeSpan.FromHours(3), TimeSpan.FromHours(6), TimeSpan.FromHours(12), TimeSpan.FromDays(1),
        };
        return steps.FirstOrDefault(s => s >= wanted) is { Ticks: > 0 } found ? found : TimeSpan.FromDays(1);
    }

    private static DateTime Floor(DateTime time, TimeSpan step) => new(time.Ticks - time.Ticks % step.Ticks, time.Kind);

    private (DateTime From, DateTime To) OverviewRange()
    {
        DateTime from = DataStart;
        DateTime to = DateTime.Now;
        // Never narrower than the strip itself, or the frame would fill it.
        if (to - from < _span)
        {
            from = to - _span;
        }

        return (from, to);
    }

    private void DrawOverview(DrawingContext dc, double width, Brush lane, Brush border, Brush accent)
    {
        (DateTime from, DateTime to) = OverviewRange();
        double perTick = (width - 2) / (to - from).Ticks;
        double X(DateTime time) => 1 + (time - from).Ticks * perTick;
        dc.DrawRoundedRectangle(lane, new Pen(border, 1), new Rect(0.5, OverviewTop + 0.5, width - 1, OverviewHeight - 1), 3, 3);
        foreach (SessionInfo session in _sessions)
        {
            double x1 = X(session.Start);
            double x2 = X(session.End ?? to);
            Brush fill = new SolidColorBrush(SessionLabels.ColorOf(session.Kind)) { Opacity = 0.7 };
            dc.DrawRectangle(fill, null, new Rect(x1, OverviewTop + 3, Math.Max(1.5, x2 - x1), OverviewHeight - 6));
        }

        double fx1 = Math.Max(1, X(_viewStart));
        double fx2 = Math.Min(width - 1, X(_viewStart + _span));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x22, 0xC9, 0xAD, 0x6A)), new Pen(accent, 1.5),
            new Rect(fx1, OverviewTop + 0.5, Math.Max(4, fx2 - fx1), OverviewHeight - 1), 3, 3);
    }

    private Brush Resource(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private SessionInfo? SessionAt(double x)
    {
        DateTime time = TimeAt(x);
        return _sessions.LastOrDefault(s => s.Start <= time && time <= (s.End ?? DateTime.Now));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _down = e.GetPosition(this);
        _pressed = true;
        _dragging = false;
        _dragViewStart = _viewStart;
        _overviewDrag = _down.Y >= OverviewTop - 2;
        if (_overviewDrag)
        {
            MoveFromOverview(_down.X);
        }

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Point at = e.GetPosition(this);
        if (_pressed)
        {
            if (_overviewDrag)
            {
                MoveFromOverview(at.X);
                return;
            }

            if (!_dragging && Math.Abs(at.X - _down.X) > DragThreshold)
            {
                _dragging = true;
                Cursor = Cursors.SizeWE;
            }

            if (_dragging)
            {
                _follow = false;
                _viewStart = _dragViewStart - TimeSpan.FromTicks((long)((at.X - _down.X) / PixelsPerTick));
                Clamp();
                InvalidateVisual();
            }

            return;
        }

        SessionInfo? over = at.Y is >= StripTop and <= StripTop + StripHeight ? SessionAt(at.X) : null;
        if (!ReferenceEquals(over, _hover))
        {
            _hover = over;
            ToolTip = over is null ? null : TooltipOf(over);
            InvalidateVisual();
        }
    }

    private static string TooltipOf(SessionInfo session)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string text = $"{SessionLabels.NameOf(session)}\n{SessionLabels.TimeSpanOf(session)}";
        if (session.IsSaved)
        {
            text += $"\n\u2605 {loc["Sessions.FilterSaved"]}";
        }

        if (session.CachedAnalysis is { Result.HasEnrage: true })
        {
            text += $"\n\u25B2 {loc["Sessions.Col.Enrage"]}";
        }

        return text;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_pressed && !_dragging && !_overviewDrag && SessionAt(_down.X) is { } picked && _down.Y is >= StripTop and <= StripTop + StripHeight)
        {
            Selected = picked;
            SessionPicked?.Invoke(picked);
            InvalidateVisual();
        }

        _pressed = false;
        _dragging = false;
        _overviewDrag = false;
        Cursor = Cursors.Arrow;
        ReleaseMouseCapture();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hover is not null && !_pressed)
        {
            _hover = null;
            ToolTip = null;
            InvalidateVisual();
        }
    }

    /// <summary>The wheel pushes the strip; with Ctrl it zooms to the next span.</summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            int at = Array.FindIndex(SpanSteps, s => s >= _span);
            if (at < 0)
            {
                at = SpanSteps.Length - 1;
            }

            SetSpan(SpanSteps[Math.Clamp(at + (e.Delta > 0 ? -1 : 1), 0, SpanSteps.Length - 1)]);
            SpanChanged?.Invoke();
        }
        else
        {
            Pan(e.Delta > 0 ? -0.15 : 0.15);
        }

        e.Handled = true;
    }

    /// <summary>Raised when the wheel changed the span, so the zoom buttons can follow.</summary>
    public event Action? SpanChanged;

    /// <summary>A press or drag in the overview puts the middle of the frame under the pointer.</summary>
    private void MoveFromOverview(double x)
    {
        (DateTime from, DateTime to) = OverviewRange();
        double perTick = (ActualWidth - 2) / (to - from).Ticks;
        DateTime middle = from + TimeSpan.FromTicks((long)((x - 1) / perTick));
        _follow = false;
        _viewStart = middle - _span / 2;
        Clamp();
        InvalidateVisual();
    }
}
