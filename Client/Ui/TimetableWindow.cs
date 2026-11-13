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
public sealed class TimetableWindow : MovableOverlay
{
    private TimeSpan _lookahead = TimeSpan.FromMinutes(60);
    private const double PanelWidth = 260;

    private readonly StackPanel _body = new();
    private readonly TextBlock _title = new() { FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(2, 0, 0, 4) };
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public TimetableWindow() : base(OverlayPlaces.Timetable, new Border(), Load, Save, SystemParameters.WorkArea.Right - PanelWidth - 40, 120, LoadSize, SaveSize, OverlayEdgeMode.Width)
    {
        _title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        var stack = new StackPanel { MinWidth = PanelWidth - 12 };
        stack.Children.Add(_title);
        stack.Children.Add(_body);
        // the dark background at the overlay's opacity (the designer sets the opacity, the colour and how clear the text is, like for every overlay)
        Body = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 5, 6, 5), Child = stack, Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)) };
        ApplyLookahead(MeterSettings.Load().TimetableLookaheadMinutes);
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

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.TimetableLeft, s.TimetableTop, s.TimetableScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.TimetableLeft = left;
        s.TimetableTop = top;
        s.TimetableScale = scale;
        s.Save();
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.TimetableWidth, null);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.TimetableWidth = width;
        s.Save();
    }

    /// <summary>Takes the content out of a window that is never shown, to put it into a tool's own window (a live view); <see cref="Refresh"/> keeps filling it.</summary>
    public UIElement DetachView()
    {
        ApplyLocked(true);
        return DetachBody()!;
    }

    /// <summary>How many minutes ahead an event is listed as "starts within ..." (Settings, 0 to 60; 0 lists nothing as "about to start").</summary>
    public void ApplyLookahead(int minutes) => _lookahead = TimeSpan.FromMinutes(Math.Clamp(minutes, 0, 60));

    /// <summary>Rebuilds the lines for the current time: what runs now with the time left, what
    /// starts within the hour with the time to go, else when the next one starts.</summary>
    public void Refresh()
    {
        var loc = LocalizationManager.Instance;
        string language = loc.Language;
        DateTime now = Aion2.ServerClock.Now; // the PC's time, corrected by the server clock's offset
        _title.Text = loc["Timetable.Title"];

        _body.Children.Clear();
        var shown = MeterSettings.Load().TimetableEvents;
        var events = EventSchedule.Events.Where(e => !shown.TryGetValue(e.Id, out var s) || s.Show).ToList(); // the events switched off in the settings stay out
        var (active, soon) = EventSchedule.Evaluate(events, now, _lookahead);
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

            EventOccurrence? next = events
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

    /// <summary>Hides the content (the window stays, so the hotkey and the settings still see it as on) while the game is not in front or a game menu is open.</summary>
    public void ApplyGate(bool show)
    {
        if (Content is UIElement content)
        {
            content.Visibility = show ? Visibility.Visible : Visibility.Hidden;
        }
    }

    private static string Label(EventOccurrence o, string language)
    {
        string name = o.Event.Players is { Length: > 0 } players ? $"{o.Event.NameIn(language)} ({players})" : o.Event.NameIn(language);
        return o.Event.IsPossible ? $"{name} ({LocalizationManager.Instance.In("Timetable.Possible", language)})" : name;
    }

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
