using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Schedule;

namespace AionDPS.Ui;

/// <summary>
/// The timetable overlay: what is active NOW and what starts within the next hour (the battlefields'
/// matchmaking times, see <see cref="EventSchedule"/>). A transparent, always-on-top panel in the
/// look of the compact DPS overlay, independent of the meter window: drag its header to place it,
/// the corner grip scales it, both are remembered. It takes clicks, so it is no click-through chip.
/// </summary>
public sealed class TimetableWindow : Window
{
    private TimeSpan _lookahead = TimeSpan.FromMinutes(60);
    private const double PanelWidth = 260;
    private const double MinScale = 0.7, MaxScale = 2.0;

    private readonly StackPanel _body = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Border _panel;
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public TimetableWindow()
    {
        Title = "Aion DPS Timetable";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;

        var settings = MeterSettings.Load();
        ApplyOpacity(settings.TimetableOpacity ?? settings.OverlayOpacity);
        ApplyLookahead(settings.TimetableLookaheadMinutes);
        SetScale(settings.TimetableScale);

        var header = new DockPanel { Margin = new Thickness(2, 0, 0, 4), Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
                SavePosition();
            }
        };
        var close = new Border { Padding = new Thickness(3, 2, 3, 2), CornerRadius = new CornerRadius(3), Cursor = Cursors.Hand, Background = Brushes.Transparent,
            ToolTip = LocalizationManager.Instance["Main.Overlay.Close"] };
        var closeGlyph = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        closeGlyph.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        close.Child = closeGlyph;
        close.MouseEnter += (_, _) => close.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        close.MouseLeave += (_, _) => close.Background = Brushes.Transparent;
        close.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            var s = MeterSettings.Load();
            s.ShowTimetable = false;
            s.Save();
            Hide();
        };
        DockPanel.SetDock(close, Dock.Right);
        var title = new TextBlock { FontWeight = FontWeights.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Tag = "title" };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        header.Children.Add(close);
        header.Children.Add(title);

        var grip = new Thumb
        {
            Width = 12, Height = 12, Cursor = Cursors.SizeNWSE, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Right,
            Template = GripTemplate(),
        };
        grip.DragDelta += (_, e) => SetScale((PanelWidth * _scale.ScaleX + Math.Max(e.HorizontalChange, e.VerticalChange)) / PanelWidth);
        grip.DragCompleted += (_, _) =>
        {
            var s = MeterSettings.Load();
            s.TimetableScale = _scale.ScaleX;
            s.Save();
        };

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(_body);
        stack.Children.Add(grip);
        _panel = new Border
        {
            Width = PanelWidth, CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 5, 6, 5), Child = stack,
            LayoutTransform = _scale, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };
        _panel.SetResourceReference(Border.BackgroundProperty, "Brush.OverlayBg");
        Content = _panel;

        Left = settings.TimetableLeft ?? SystemParameters.WorkArea.Right - PanelWidth - 40;
        Top = settings.TimetableTop ?? 120;
        _timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Refresh();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        };
        Closed += (_, _) => _timer.Stop();
    }

    private static ControlTemplate GripTemplate()
    {
        var template = new ControlTemplate(typeof(Thumb));
        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        var path = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        path.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M11,1 L1,11 M11,5 L5,11 M11,9 L9,11"));
        path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Brush.TextSubtle");
        path.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.0);
        grid.AppendChild(path);
        template.VisualTree = grid;
        return template;
    }

    /// <summary>How many minutes ahead an event is listed as "starts within ..." (Settings, 0 to 60; 0 lists nothing as "about to start").</summary>
    public void ApplyLookahead(int minutes) => _lookahead = TimeSpan.FromMinutes(Math.Clamp(minutes, 0, 60));

    /// <summary>The overlay's dark background at the Settings' opacity (the same as the DPS overlay's).</summary>
    public void ApplyOpacity(double opacity)
    {
        opacity = Math.Clamp(double.IsFinite(opacity) ? opacity : 0.6, 0.2, 1.0);
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), 0, 0, 0));
        brush.Freeze();
        Resources["Brush.OverlayBg"] = brush;
    }

    private void SetScale(double scale)
    {
        scale = Math.Clamp(double.IsFinite(scale) ? scale : 1.0, MinScale, MaxScale);
        _scale.ScaleX = scale;
        _scale.ScaleY = scale;
    }

    private void SavePosition()
    {
        var s = MeterSettings.Load();
        s.TimetableLeft = Left;
        s.TimetableTop = Top;
        s.Save();
    }

    /// <summary>Rebuilds the lines for the current time: what runs now with the time left, what
    /// starts within the hour with the time to go, else when the next one starts.</summary>
    public void Refresh()
    {
        var loc = LocalizationManager.Instance;
        string language = loc.Language;
        DateTime now = DateTime.Now;
        if (_panel.Child is StackPanel { Children: [DockPanel { Children: [_, TextBlock title] }, ..] })
        {
            title.Text = loc["Timetable.Title"];
        }

        _body.Children.Clear();
        var (active, soon) = EventSchedule.Evaluate(EventSchedule.Events, now, _lookahead);
        if (active.Count > 0)
        {
            AddSection(loc["Timetable.Active"], Brushes.LimeGreen);
            foreach (EventOccurrence o in active)
            {
                if (o.IsAlways)
                {
                    AddLine(Label(o, language), loc["Timetable.Always"], "");
                    continue;
                }

                AddLine(Label(o, language), string.Format(loc["Timetable.EndsIn"], Span(o.End - now)), $"{o.Start:HH:mm}-{o.End:HH:mm}");
            }
        }

        if (soon.Count > 0)
        {
            AddSection(string.Format(loc["Timetable.Soon"], (int)_lookahead.TotalMinutes), Brushes.Orange);
            foreach (EventOccurrence o in soon)
            {
                AddLine(Label(o, language), string.Format(loc["Timetable.StartsIn"], Span(o.Start - now)),
                    o.End > o.Start ? $"{o.Start:HH:mm}-{o.End:HH:mm}" : $"{o.Start:HH:mm}");
            }
        }

        if (active.All(o => o.IsAlways) && soon.Count == 0)
        {
            if (active.Count == 0)
            {
                AddNote(loc["Timetable.None"]);
            }

            EventOccurrence? next = EventSchedule.Events
                .Select(e => EventSchedule.NextAfter(e, now))
                .Where(o => o is not null)
                .OrderBy(o => o!.Start)
                .FirstOrDefault();
            if (next is not null)
            {
                string when = next.Start.Date == now.Date ? $"{next.Start:HH:mm}" : $"{next.Start:ddd HH:mm}";
                AddNote(string.Format(loc["Timetable.Next"], Label(next, language), when));
            }
        }
    }

    /// <summary>Puts the overlay back at the front of the topmost band. The main window calls this right after
    /// raising itself, once a second, so the two never take turns being in front (that made the timetable
    /// flicker through the meter). Not done from <see cref="Refresh"/>: its timer is offset from the main one.</summary>
    public void RaiseToFront()
    {
        if (IsVisible)
        {
            NativeOverlay.KeepOnTop(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        }
    }

    private static string Label(EventOccurrence o, string language) =>
        o.Event.Players is { Length: > 0 } players ? $"{o.Event.NameIn(language)} ({players})" : o.Event.NameIn(language);

    /// <summary>"1:23 h" / "42 min" / "0:30 min" - hours and minutes, seconds under ten minutes.</summary>
    private static string Span(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00} h"
            : span.TotalMinutes >= 10 ? $"{(int)span.TotalMinutes} min"
            : $"{(int)span.TotalMinutes}:{span.Seconds:00} min";
    }

    private void AddSection(string text, Brush dot)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 4, 0, 2) };
        row.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = dot, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        var label = new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        row.Children.Add(label);
        _body.Children.Add(row);
    }

    private void AddLine(string name, string time, string window)
    {
        var line = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 1, 0, 1), Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var nameText = new TextBlock { Text = name, FontWeight = FontWeights.Bold, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        nameText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        var timeText = new TextBlock { Text = time, FontSize = 11, FontWeight = FontWeights.Bold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        timeText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        var windowText = new TextBlock { Text = window, FontSize = 9, Margin = new Thickness(0, 1, 0, 0), Visibility = window.Length > 0 ? Visibility.Visible : Visibility.Collapsed };
        windowText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        Grid.SetColumn(timeText, 1);
        Grid.SetRow(windowText, 1);
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(nameText);
        grid.Children.Add(timeText);
        grid.Children.Add(windowText);
        line.Child = grid;
        _body.Children.Add(line);
    }

    private void AddNote(string text)
    {
        var note = new TextBlock { Text = text, FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 2, 2) };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        _body.Children.Add(note);
    }
}
