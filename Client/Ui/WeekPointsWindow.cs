using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Data;

namespace AionDPS.Ui;

/// <summary>
/// The week points page (Planning, below the checklist): the points of the weekly reward of the season missions (steps 1,500 to 8,000), the time to the weekly reset and a plan for the time
/// the player has: which weekly missions bring the most points per minute, which are finished but not collected (the points come only with the collecting), and which cannot be done before
/// the reset because their boss or the artifact siege does not come in time (see <see cref="WeekPlanner"/>). Built in code, in the look of the checklist.
/// </summary>
public sealed class WeekPointsWindow : Window
{
    private static readonly int[] Budgets = { 60, 120, 150, 180, 0 };
    private static int _budget = 150;

    private readonly StackPanel _characters = new();
    private readonly StackPanel _body = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(30) };
    private string? _selected;

    public WeekPointsWindow()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        Title = loc["WeekPoints.Title"];
        Width = 1180;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        MinWidth = 900;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12, 10, 12, 12) };
        var sidebar = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Width = 184, Margin = new Thickness(0, 0, 2, 0), Content = _characters };
        DockPanel.SetDock(sidebar, Dock.Left);
        root.Children.Add(sidebar);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body });
        Content = root;
        ThemedChrome.Apply(this);

        Aion2Tickets.Changed += OnChanged;
        Aion2FieldBosses.Changed += OnChanged;
        _tick.Tick += (_, _) => Render();
        _tick.Start();
        Closed += (_, _) =>
        {
            _tick.Stop();
            Aion2Tickets.Changed -= OnChanged;
            Aion2FieldBosses.Changed -= OnChanged;
        };
        Refresh();
    }

    private void OnChanged() => Dispatcher.BeginInvoke(new Action(Refresh));

    private void Refresh()
    {
        IReadOnlyList<TicketCharacter> all = Aion2Tickets.Snapshot();
        string? current = Aion2Tickets.CurrentName;
        string? wanted = _selected is not null && all.Any(c => c.Name == _selected) ? _selected : current is not null && all.Any(c => c.Name == current) ? current : all.FirstOrDefault()?.Name;
        _characters.Children.Clear();
        foreach (TicketCharacter c in all)
        {
            string name = c.Name;
            _characters.Children.Add(ChecklistWindow.CharacterCard(c, name == wanted, name == current, () =>
            {
                _selected = name;
                Refresh();
            }));
        }

        _selected = wanted;
        Render();
    }

    private void Render()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string language = loc.Language;
        _body.Children.Clear();
        TicketCharacter? character = Aion2Tickets.Snapshot().FirstOrDefault(c => c.Name == _selected);
        DateTime now = DateTime.Now;
        WeekPlan? plan = character is null ? null : WeekPlanner.Build(character, now, _budget, npc => Aion2FieldBosses.ServerStatusOf(npc, now));
        if (plan is null || !plan.HasData)
        {
            _body.Children.Add(Card(Label(loc[character is null ? "Checklist.Empty" : "WeekPoints.NoData"], "Brush.TextMuted", 13)));
            return;
        }

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        UIElement points = PointsCard(plan, loc);
        Grid.SetColumn(points, 0);
        UIElement reset = ResetCard(plan, now, loc);
        Grid.SetColumn(reset, 1);
        ((FrameworkElement)points).Margin = new Thickness(0, 0, 8, 6);
        ((FrameworkElement)reset).Margin = new Thickness(0, 0, 0, 6);
        top.Children.Add(points);
        top.Children.Add(reset);
        _body.Children.Add(top);
        _body.Children.Add(EveningCard(plan, now, language, loc));
        TextBlock note = Label(loc["WeekPoints.Note"], "Brush.TextSubtle", 10.5);
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(2, 0, 0, 0);
        _body.Children.Add(note);
    }

    private static UIElement PointsCard(WeekPlan plan, LocalizationManager loc)
    {
        var stack = new StackPanel();
        stack.Children.Add(Label(loc["WeekPoints.Header"], "Brush.Text", 13, bold: true));
        var big = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) };
        big.Children.Add(Label(plan.Claimed.ToString("N0", CultureInfo.CurrentCulture), "Brush.Text", 22, bold: true));
        TextBlock of = Label(" / " + Aion2Tickets.WeeklyPointsMax.ToString("N0", CultureInfo.CurrentCulture), "Brush.TextMuted", 15);
        of.Margin = new Thickness(2, 6, 0, 0);
        big.Children.Add(of);
        stack.Children.Add(big);

        // the bar over the five steps (1,500 / 1,500 / 1,500 / 1,500 / 2,000 wide), the step numbers under it
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        int before = 0;
        foreach (int threshold in WeekPlan.Thresholds)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(threshold - before, GridUnitType.Star) });
            before = threshold;
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var bar = new ProgressBar { Minimum = 0, Maximum = Aion2Tickets.WeeklyPointsMax, Value = Math.Min(plan.Claimed, Aion2Tickets.WeeklyPointsMax), Height = 8 };
        bar.SetResourceReference(Control.ForegroundProperty, "Brush.Accent");
        bar.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
        bar.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        Grid.SetColumnSpan(bar, WeekPlan.Thresholds.Count);
        grid.Children.Add(bar);
        for (int i = 0; i < WeekPlan.Thresholds.Count; i++)
        {
            int threshold = WeekPlan.Thresholds[i];
            bool reached = plan.Claimed >= threshold;
            TextBlock label = Label(threshold.ToString("N0", CultureInfo.CurrentCulture), reached ? "Brush.Accent" : "Brush.TextSubtle", 10.5);
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.Margin = new Thickness(0, 3, 0, 0);
            Grid.SetColumn(label, i);
            Grid.SetRow(label, 1);
            grid.Children.Add(label);
            if (i < WeekPlan.Thresholds.Count - 1)
            {
                var tick = new Border { Width = 2, Height = 12, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, -2, -1, 0), VerticalAlignment = VerticalAlignment.Top };
                tick.SetResourceReference(Border.BackgroundProperty, reached ? "Brush.Accent" : "Brush.Border");
                Grid.SetColumn(tick, i);
                grid.Children.Add(tick);
            }
        }

        stack.Children.Add(grid);

        var tiles = new UniformGrid { Columns = 3 };
        tiles.Children.Add(Tile(plan.Claimed.ToString("N0", CultureInfo.CurrentCulture), loc["WeekPoints.Collected"], false));
        tiles.Children.Add(Tile("+" + plan.Waiting.ToString("N0", CultureInfo.CurrentCulture), loc["WeekPoints.Waiting"], plan.Waiting > 0));
        tiles.Children.Add(Tile(Math.Max(0, Aion2Tickets.WeeklyPointsMax - plan.Claimed - plan.Waiting).ToString("N0", CultureInfo.CurrentCulture),
            string.Format(CultureInfo.CurrentCulture, loc["WeekPoints.Missing"], Aion2Tickets.WeeklyPointsMax.ToString("N0", CultureInfo.CurrentCulture)), false));
        stack.Children.Add(tiles);
        return Card(stack);
    }

    private static UIElement Tile(string value, string text, bool accent)
    {
        var inner = new StackPanel();
        inner.Children.Add(Label(value, accent ? "Brush.Accent" : "Brush.Text", 15, bold: true));
        TextBlock caption = Label(text, "Brush.TextSubtle", 10.5);
        caption.TextWrapping = TextWrapping.Wrap;
        inner.Children.Add(caption);
        var border = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0), Child = inner };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return border;
    }

    private static UIElement ResetCard(WeekPlan plan, DateTime now, LocalizationManager loc)
    {
        TimeSpan left = plan.Reset - now;
        var stack = new StackPanel();
        stack.Children.Add(Label(loc["WeekPoints.Reset"], "Brush.Text", 13, bold: true));
        TextBlock countdown = Label(string.Format(CultureInfo.CurrentCulture, loc["WeekPoints.Countdown"], (int)left.TotalHours, left.Minutes), "Brush.Text", 20, bold: true);
        countdown.Margin = new Thickness(0, 4, 0, 2);
        stack.Children.Add(countdown);
        stack.Children.Add(Label(string.Format(CultureInfo.CurrentCulture, loc["WeekPoints.ResetAt"], plan.Reset.ToString("dddd HH:mm", CultureInfo.CurrentCulture), now.ToString("dddd HH:mm", CultureInfo.CurrentCulture)), "Brush.TextSubtle", 11));
        int stand = plan.Claimed + plan.Waiting;
        int? next = WeekPlan.NextThreshold(plan.Claimed);
        TextBlock thresholds = Label(next is { } n ? string.Format(CultureInfo.CurrentCulture, loc["WeekPoints.NextStep"], n.ToString("N0", CultureInfo.CurrentCulture), (n - plan.Claimed).ToString("N0", CultureInfo.CurrentCulture)) : loc["WeekPoints.AllSteps"], "Brush.Text", 12);
        thresholds.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(thresholds);
        if (plan.Waiting > 0 && WeekPlan.NextThreshold(plan.Claimed) is { } waitingNext && stand >= waitingNext)
        {
            TextBlock hint = Label(string.Format(CultureInfo.CurrentCulture, loc["WeekPoints.WaitingReaches"], waitingNext.ToString("N0", CultureInfo.CurrentCulture)), "Brush.Accent", 11);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(hint);
        }

        return Card(stack);
    }

    private UIElement EveningCard(WeekPlan plan, DateTime now, string language, LocalizationManager loc)
    {
        var stack = new StackPanel();
        stack.Children.Add(Label(loc["WeekPoints.Evening"], "Brush.Text", 13, bold: true));

        var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 6) };
        TextBlock time = Label(loc["WeekPoints.TimeLabel"], "Brush.TextSubtle", 11.5);
        time.Margin = new Thickness(0, 0, 8, 0);
        chips.Children.Add(time);
        foreach (int minutes in Budgets)
        {
            int budget = minutes;
            string text = minutes == 0 ? loc["WeekPoints.UntilReset"] : Duration(minutes, loc);
            bool on = budget == _budget;
            TextBlock label = Label(text, on ? "Brush.Accent" : "Brush.TextSubtle", 11.5, bold: on);
            var chip = new Border { CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand, Child = label };
            chip.SetResourceReference(Border.BackgroundProperty, on ? "Brush.ControlRaised" : "Brush.Panel");
            chip.SetResourceReference(Border.BorderBrushProperty, on ? "Brush.Accent" : "Brush.Border");
            chip.MouseLeftButtonDown += (_, _) =>
            {
                _budget = budget;
                Render();
            };
            chips.Children.Add(chip);
        }

        stack.Children.Add(chips);

        // the result in one sentence
        int gain = plan.Waiting + plan.PlannedPoints;
        var sentence = new List<string>
        {
            plan.PlannedPoints == 0 && plan.Waiting == 0
                ? loc["WeekPoints.NothingFits"]
                : string.Format(CultureInfo.CurrentCulture, loc["WeekPoints.Result"], Duration((int)Math.Round(plan.PlannedMinutes), loc), gain.ToString("N0", CultureInfo.CurrentCulture), plan.Final.ToString("N0", CultureInfo.CurrentCulture)),
        };
        int reached = WeekPlan.Thresholds.Where(t => t > plan.Claimed && t <= plan.Final).DefaultIfEmpty(0).Max();
        int? missing = WeekPlan.NextThreshold(plan.Final);
        if (reached > 0)
        {
            sentence.Add(string.Format(CultureInfo.CurrentCulture, loc["WeekPoints.StepReached"], reached.ToString("N0", CultureInfo.CurrentCulture)));
        }

        if (missing is { } m)
        {
            sentence.Add(string.Format(CultureInfo.CurrentCulture, loc["WeekPoints.StepMissing"], m.ToString("N0", CultureInfo.CurrentCulture), (m - plan.Final).ToString("N0", CultureInfo.CurrentCulture)));
        }

        TextBlock result = Label(string.Join(" ", sentence), "Brush.Text", 12);
        result.TextWrapping = TextWrapping.Wrap;
        var resultBox = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 0, 6), Child = result };
        resultBox.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        resultBox.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        stack.Children.Add(resultBox);

        var table = new StackPanel();
        table.Children.Add(HeaderRow(loc));
        int number = 0;
        foreach (PlanState state in new[] { PlanState.Waiting, PlanState.Plan, PlanState.Spare, PlanState.Blocked })
        {
            List<PlanRow> rows = plan.Rows.Where(r => r.State == state).ToList();
            if (rows.Count == 0)
            {
                continue;
            }

            TextBlock section = Label(loc["WeekPoints.Section." + state], "Brush.TextSubtle", 10.5);
            section.Margin = new Thickness(0, 8, 0, 2);
            table.Children.Add(section);
            // what no longer fits: the best few by points per minute (the others are a long list of steps nobody needs to see)
            int shown = rows.Count;
            if (state == PlanState.Spare && rows.Count > SpareShown)
            {
                rows = rows.OrderByDescending(r => r.Points / Math.Max(r.Minutes, 0.5)).ToList();
                shown = SpareShown;
            }

            foreach (PlanRow row in rows.Take(shown))
            {
                table.Children.Add(PlanLine(row, state == PlanState.Plan ? ++number : 0, now, language, loc));
            }

            if (shown < rows.Count)
            {
                TextBlock more = Label(string.Format(CultureInfo.CurrentCulture, loc["WeekPoints.More"], rows.Count - shown), "Brush.TextSubtle", 10.5);
                more.Margin = new Thickness(26, 2, 0, 0);
                table.Children.Add(more);
            }
        }

        stack.Children.Add(table);
        return Card(stack);
    }

    private const int SpareShown = 5;

    private static readonly double[] Columns = { 26, 0, 104, 128, 56, 72, 78, 86 };

    private static Grid NewRow()
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        foreach (double width in Columns)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
        }

        return grid;
    }

    private static UIElement HeaderRow(LocalizationManager loc)
    {
        Grid grid = NewRow();
        string[] keys = { "WeekPoints.Col.No", "WeekPoints.Col.Mission", "WeekPoints.Col.Tab", "WeekPoints.Col.Progress", "WeekPoints.Col.Points", "WeekPoints.Col.Effort", "WeekPoints.Col.When", "WeekPoints.Col.After" };
        for (int i = 0; i < keys.Length; i++)
        {
            TextBlock text = Label(loc[keys[i]], "Brush.TextSubtle", 10.5);
            text.HorizontalAlignment = i >= 4 ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            Grid.SetColumn(text, i);
            grid.Children.Add(text);
        }

        return grid;
    }

    private static UIElement PlanLine(PlanRow row, int number, DateTime now, string language, LocalizationManager loc)
    {
        Grid grid = NewRow();
        bool dim = row.State is PlanState.Spare or PlanState.Blocked;
        string main = dim ? "Brush.TextMuted" : "Brush.Text";
        grid.Opacity = row.State == PlanState.Blocked ? 0.7 : 1;

        void Put(UIElement element, int column)
        {
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }

        Put(Label(number > 0 ? number.ToString(CultureInfo.CurrentCulture) : row.State == PlanState.Waiting ? "✓" : "–", "Brush.TextSubtle", 11), 0);

        var name = new Grid { ClipToBounds = true };
        name.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        name.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        TextBlock title = Label(row.Step.TitleIn(language), main, 12.5);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Children.Add(title);
        TextBlock what = Label(row.Steps > 1 ? string.Format(CultureInfo.CurrentCulture, loc["WeekPoints.StepsOf"], row.Steps) + " \u00b7 " + row.Step.TextIn(language) : row.Step.TextIn(language), "Brush.TextSubtle", 10.5);
        what.Margin = new Thickness(8, 1, 6, 0);
        what.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(what, 1);
        name.Children.Add(what);
        Put(name, 1);

        Border tag = new() { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(6, 0, 6, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Child = Label(Aion2Missions.TabName(row.Mission.Tab, language), "Brush.TextSubtle", 9.5) };
        tag.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        Put(tag, 2);

        var progress = new DockPanel { Margin = new Thickness(0, 0, 8, 0) };
        TextBlock counter = Label(row.Count.ToString("N0", CultureInfo.CurrentCulture) + " / " + row.Target.ToString("N0", CultureInfo.CurrentCulture), "Brush.Text", 11.5);
        counter.MinWidth = 68;
        DockPanel.SetDock(counter, Dock.Left);
        progress.Children.Add(counter);
        var bar = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, row.Target), Value = Math.Min(row.Count, row.Target), Height = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        bar.SetResourceReference(Control.ForegroundProperty, row.State == PlanState.Waiting ? "Brush.Heal" : "Brush.Accent");
        bar.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
        bar.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        progress.Children.Add(bar);
        Put(progress, 3);

        TextBlock points = Label("+" + row.Points.ToString("N0", CultureInfo.CurrentCulture), dim ? "Brush.TextMuted" : "Brush.Accent", 12, bold: true);
        points.HorizontalAlignment = HorizontalAlignment.Right;
        Put(points, 4);

        string effort = row.State == PlanState.Waiting ? loc["WeekPoints.Collect"] : row.Passive ? loc["WeekPoints.Along"] : row.State == PlanState.Blocked ? "–" : string.Format(CultureInfo.CurrentCulture, loc["WeekPoints.Minutes"], Math.Max(1, (int)Math.Round(row.Minutes)));
        TextBlock effortText = Label(effort, row.Passive || row.State == PlanState.Waiting ? "Brush.Accent" : main, 11.5);
        effortText.HorizontalAlignment = HorizontalAlignment.Right;
        Put(effortText, 5);

        string when;
        string whenBrush = main;
        if (row.State == PlanState.Blocked)
        {
            whenBrush = "Brush.Warning";
            when = row.When is { } at ? WhenText(at, row.WhenIsDay, now) : loc["WeekPoints.Why." + row.Why];
        }
        else
        {
            when = row.State == PlanState.Waiting || row.When is null ? loc["WeekPoints.Now"] : WhenText(row.When.Value, row.WhenIsDay, now);
        }

        TextBlock whenText = Label(when, whenBrush, 11.5);
        whenText.HorizontalAlignment = HorizontalAlignment.Right;
        Put(whenText, 6);

        TextBlock after = Label(row.StandAfter > 0 ? row.StandAfter.ToString("N0", CultureInfo.CurrentCulture) : "–", "Brush.Text", 11.5);
        after.HorizontalAlignment = HorizontalAlignment.Right;
        Put(after, 7);
        return grid;
    }

    /// <summary>"22:00" for today, "Thu 21:40" later; only the day when the time of day is not known to the minute.</summary>
    private static string WhenText(DateTime at, bool dayOnly, DateTime now)
    {
        if (at.Date == now.Date && !dayOnly)
        {
            return at.ToString("HH:mm", CultureInfo.CurrentCulture);
        }

        return at.ToString(dayOnly ? "ddd" : "ddd HH:mm", CultureInfo.CurrentCulture);
    }

    private static string Duration(int minutes, LocalizationManager loc) =>
        minutes % 60 == 0 ? string.Format(CultureInfo.CurrentCulture, loc["WeekPoints.Hours"], minutes / 60) : minutes < 60 ? string.Format(CultureInfo.CurrentCulture, loc["WeekPoints.Mins"], minutes) : string.Format(CultureInfo.CurrentCulture, loc["WeekPoints.HoursMinutes"], minutes / 60, minutes % 60);

    private static TextBlock Label(string text, string brush, double size, bool bold = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private static Border Card(UIElement child)
    {
        var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 0, 6), Child = child };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return border;
    }
}
