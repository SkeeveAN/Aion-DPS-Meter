using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Data;
using AionDPS.Update;

namespace AionDPS.Ui;

/// <summary>
/// The checklist (Tools menu, no overlay): the Odyle energy and the daily and weekly counters of a character - entry counts, reward counts, boss
/// kills, access times - as the game reports them (see <see cref="Aion2Tickets"/>), with the next refill of each. The numbers are those of the last
/// login or change the meter saw; "~" marks a number that was worked out from the refills since then. Built in code, in the look of the other windows.
/// </summary>
public sealed class ChecklistWindow : Window
{
    private readonly StackPanel _characters = new();
    private readonly TextBlock _seen = new();
    private readonly StackPanel _body = new();
    /// <summary>Not in the daily list (the user's choice): the reward counts of the conquest dungeons (alone and in pairs) and the Shugofesta keys.</summary>
    private static readonly HashSet<int> NotDaily = new()
    {
        60000003, 60000004, 60000005, 60000101, 60001201, 60002201, 60003201, 60007201, 60009201, 1, 7,
    };

    /// <summary>The overview (the user's list, in this order): the instances and mini events with the number left, by their short names.</summary>
    private static readonly int[] Overview = { 1, 8, 4, 80000001 };

    /// <summary>The access times shown (the user's choice): the lower level of the Abyss.</summary>
    private static readonly int[] TimeTickets = { 3 };

    /// <summary>The weekly counter of the daily dungeon (the unknown fissure's entry count).</summary>
    private const int DailyDungeonTicket = 101;

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(30) };
    private string? _selected;

    /// <summary>Whether the shown character has the Quai premium membership (true when it is not known: the caps then are the member's, as before).</summary>
    private bool _premium = true;

    public ChecklistWindow()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        Title = loc["Checklist.Title"];
        Width = 700;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        MinWidth = 640;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12, 10, 12, 12) };
        // the characters as a column of cards on the left (Stacy's fork has the idea: class icon, name in the class colour, class and level below)
        var sidebar = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Width = 184, Margin = new Thickness(0, 0, 2, 0), Content = _characters };
        DockPanel.SetDock(sidebar, Dock.Left);
        root.Children.Add(sidebar);
        // the "Stand: date" line is no longer shown (_seen is still kept up to date)

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body };
        root.Children.Add(scroll);
        Content = root;
        ThemedChrome.Apply(this);

        Aion2Tickets.Changed += OnTicketsChanged;
        _tick.Tick += (_, _) => Render();
        _tick.Start();
        Closed += (_, _) =>
        {
            _tick.Stop();
            Aion2Tickets.Changed -= OnTicketsChanged;
        };
        Refresh();
    }

    private void OnTicketsChanged() => Dispatcher.BeginInvoke(new Action(Refresh));

    /// <summary>Rebuilds the character list (the one logged in now is chosen first) and the page.</summary>
    private void Refresh()
    {
        IReadOnlyList<TicketCharacter> all = Aion2Tickets.Snapshot();
        string? current = Aion2Tickets.CurrentName;
        string? wanted = _selected is not null && all.Any(c => c.Name == _selected) ? _selected : current is not null && all.Any(c => c.Name == current) ? current : all.FirstOrDefault()?.Name;
        _characters.Children.Clear();
        foreach (TicketCharacter c in all)
        {
            string name = c.Name;
            _characters.Children.Add(CharacterCard(c, name == wanted, name == current, () =>
            {
                _selected = name;
                Refresh();
            }));
        }

        _selected = wanted;
        Render();
    }

    /// <summary>
    /// One character of the column on the left: the class icon, the name in the class colour (a dot when it is the one logged in), the class, level and server below and, in
    /// small type, when it was last online. The form is Stacy's (her fork, 17 October 2026: alts with close names were hard to tell apart); smaller type than hers.
    /// </summary>
    private static UIElement CharacterCard(TicketCharacter c, bool active, bool online, Action choose)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string language = loc.Language;
        string? className = c.ClassCode > 0 ? Aion2.Protocol.Aion2SkillNames.ClassFromCode(c.ClassCode) : null;
        var lines = new StackPanel();
        var top = new DockPanel();
        if (online)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = loc["Checklist.OnlineNow"] };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Brush.Accent");
            DockPanel.SetDock(dot, Dock.Right);
            top.Children.Add(dot);
        }

        if (className is not null && new ClassIconConverter().Convert(className, typeof(ImageSource), null, CultureInfo.CurrentCulture) is ImageSource icon)
        {
            var image = new Image { Source = icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(image, Dock.Left);
            top.Children.Add(image);
        }

        TextBlock title = Label(c.Name, c.Tickets.Count > 0 ? "Brush.Text" : "Brush.TextSubtle", 12.5, bold: true);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        if (className is not null)
        {
            title.Foreground = ClassBrushes.Solid(className);
        }

        // the legion right behind the name (not at the far edge), so the column stays narrow
        var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
        nameLine.Children.Add(title);
        if (!string.IsNullOrEmpty(c.Legion))
        {
            TextBlock legion = Label("<" + c.Legion + ">", "Brush.TextMuted", 10);
            legion.Margin = new Thickness(6, 1, 0, 0);
            legion.MaxWidth = 90;
            legion.TextTrimming = TextTrimming.CharacterEllipsis;
            nameLine.Children.Add(legion);
        }

        top.Children.Add(nameLine);
        lines.Children.Add(top);

        var parts = new List<string>();
        if (className is not null)
        {
            parts.Add(ClassCatalog.DisplayName(className, language));
        }

        if (c.Level > 0)
        {
            parts.Add(string.Format(CultureInfo.CurrentCulture, loc["Checklist.Level"], c.Level));
        }

        if (c.ServerId > 0)
        {
            parts.Add(Aion2.Protocol.Aion2Servers.NameOf(c.ServerId).Replace("Europe - ", "", StringComparison.Ordinal));
        }

        if (parts.Count > 0)
        {
            TextBlock info = Label(string.Join(" \u00b7 ", parts), "Brush.TextSubtle", 10.5);
            info.Margin = new Thickness(0, 2, 0, 0);
            info.TextTrimming = TextTrimming.CharacterEllipsis;
            lines.Children.Add(info);
        }

        // the character that is online now shows the time of its list as the last time: only the others tell something
        if (!online && c.LastOnlineMs > 0)
        {
            TextBlock seen = Label(string.Format(CultureInfo.CurrentCulture, loc["Checklist.LastOnline"], DateTimeOffset.FromUnixTimeMilliseconds(c.LastOnlineMs).LocalDateTime.ToString("g", CultureInfo.CurrentCulture)), "Brush.TextMuted", 10);
            seen.Margin = new Thickness(0, 1, 0, 0);
            seen.TextTrimming = TextTrimming.CharacterEllipsis;
            lines.Children.Add(seen);
        }

        var card = new Border
        {
            CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 6, 9, 6), Margin = new Thickness(0, 0, 0, 3), BorderThickness = new Thickness(1),
            Child = lines, Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
        };
        if (active)
        {
            card.SetResourceReference(Border.BackgroundProperty, "Brush.ControlRaised");
            card.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
        }
        else
        {
            card.MouseEnter += (_, _) => card.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            card.MouseLeave += (_, _) => card.Background = Brushes.Transparent;
        }

        card.MouseLeftButtonDown += (_, _) => choose();
        return card;
    }

    private void Render()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string language = loc.Language;
        _body.Children.Clear();
        TicketCharacter? character = Aion2Tickets.Snapshot().FirstOrDefault(c => c.Name == _selected);
        if (character is null)
        {
            _seen.Text = "";
            _body.Children.Add(Card(Label(loc["Checklist.Empty"], "Brush.TextMuted", 13)));
            return;
        }

        if (character.Tickets.Count == 0)
        {
            // a character the login's list showed but that never logged in with the meter: its basics, and what it takes to see its counters
            _seen.Text = "";
            var listed = new StackPanel();
            listed.Children.Add(Label(character.Name, "Brush.Text", 16, bold: true));
            TextBlock relogNew = Label(loc["Checklist.RelogNew"], "Brush.Accent", 12);
            relogNew.TextWrapping = TextWrapping.Wrap;
            relogNew.Margin = new Thickness(0, 8, 0, 0);
            listed.Children.Add(relogNew);
            _body.Children.Add(Card(listed));
            return;
        }

        if (Aion2Tickets.CurrentName is { } online && online != character.Name)
        {
            TextBlock relogOld = Label(loc["Checklist.RelogOld"], "Brush.TextMuted", 11);
            relogOld.TextWrapping = TextWrapping.Wrap;
            relogOld.Margin = new Thickness(4, 0, 4, 6);
            _body.Children.Add(relogOld);
        }

        if (Aion2Tickets.CurrentName is null)
        {
            // the counters come with the login: a meter started after the login has not read the character's yet
            var hint = Label(loc["Checklist.NeedLogin"], "Brush.Accent", 12);
            hint.TextWrapping = TextWrapping.Wrap;
            _body.Children.Add(Card(hint));
        }

        _premium = Aion2Tickets.HasPremium(character, DateTime.UtcNow) != false;
        DateTime now = DateTime.Now;
        DateTime seenAt = character.SavedAt.ToLocalTime();
        _seen.Text = string.Format(CultureInfo.CurrentCulture, loc["Checklist.Seen"], seenAt.ToString("g", CultureInfo.CurrentCulture));

        bool odyleLocked = Aion2Tickets.IsLocked(character, Aion2Tickets.OdyleId);
        if ((odyleLocked || character.Tickets.ContainsKey(Aion2Tickets.OdyleId)) && Aion2Tickets.Find(Aion2Tickets.OdyleId) is { } odyleDef)
        {
            _body.Children.Add(OdyleCard(odyleDef, odyleLocked ? default : character.Tickets[Aion2Tickets.OdyleId], language, odyleLocked, _premium, character.Name));
        }

        List<ChecklistRow> rows = ChecklistRows.Build(character, seenAt, now, _premium, language);
        foreach ((ChecklistGroup group, string title) in new[] { (ChecklistGroup.Overview, loc["Checklist.Overall"]), (ChecklistGroup.Daily, loc["Checklist.Daily"]), (ChecklistGroup.Weekly, loc["Checklist.Weekly"]) })
        {
            ChecklistRow[] part = rows.Where(r => r.Group == group).ToArray();
            if (part.Length > 0)
            {
                _body.Children.Add(SectionCard(title, part, character, group == ChecklistGroup.Overview));
            }
        }

        AddSubscriptions(loc["Checklist.Subs"], TimeTickets.Select(id => Aion2Tickets.Find(id)).Where(d => d is not null && (character.Tickets.ContainsKey(d.Id) || Aion2Tickets.IsLocked(character, d.Id))).Select(d => d!), character, language);
        AddPass(loc["Checklist.DaevaPass"], character, language);

        var note = Label(loc["Checklist.Note"], "Brush.TextSubtle", 11);
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(4, 4, 4, 0);
        _body.Children.Add(note);
        var legend = Label(loc["Checklist.WarnLegend"], "Brush.TextSubtle", 11);
        legend.TextWrapping = TextWrapping.Wrap;
        legend.Margin = new Thickness(4, 2, 4, 0);
        _body.Children.Add(legend);
    }

    /// <summary>The Odyle energy as the game's tooltip shows it: the basic energy (cap 840 with the subscription) and the charged energy (cap 2000).</summary>
    private static UIElement OdyleCard(TicketDef def, TicketValue value, string language, bool locked, bool premium, string characterName)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var stack = new StackPanel();
        // a small switch in front of the title: whether the checklist widget shows the energy
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        CheckBox pick = Pick("odyle");
        pick.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(pick, Dock.Left);
        head.Children.Add(pick);
        head.Children.Add(Label(def.NameIn(language), locked ? "Brush.TextSubtle" : "Brush.Text", 16, bold: true));
        stack.Children.Add(head);
        if (!locked)
        {
            // as the guild list shows it: now (+ already charged) (+ still in items); the items are known for the character in the game only
            int? items = string.Equals(characterName, Aion2Tickets.CurrentName, StringComparison.OrdinalIgnoreCase) ? Aion2.Aion2Stock.OdyleItemEnergy() : null;
            TextBlock summary = HubDashboard.OdyleSummary(value.Count, value.Bonus, items, 14);
            summary.Margin = new Thickness(0, 0, 0, 4);
            summary.ToolTip = loc["Checklist.OdyleSummaryHint"];
            stack.Children.Add(summary);
        }

        stack.Children.Add(OdyleLine(loc["Checklist.OdyleBase"], value.Count, Math.Max(premium ? def.SubscribeMax : def.Max, value.Count), locked));
        stack.Children.Add(OdyleLine(loc["Checklist.OdyleCharged"], value.Bonus, Math.Max(OdyleChargedMax, value.Bonus), locked));
        return Card(stack);
    }

    private const int OdyleChargedMax = 2000;

    private static UIElement OdyleLine(string label, int value, int cap, bool locked = false)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        TextBlock name = Label(label, locked ? "Brush.TextSubtle" : "Brush.Text", 13);
        TextBlock numbers = Label($"{value:N0} / {cap:N0}", value > 0 && !locked ? "Brush.Accent" : "Brush.TextSubtle", 13, bold: true);
        Grid.SetColumn(numbers, 1);
        var bar = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, cap), Value = Math.Min(value, cap), Height = 6, Margin = new Thickness(0, 3, 0, 0) };
        bar.SetResourceReference(Control.ForegroundProperty, "Brush.Accent");
        bar.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
        bar.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        Grid.SetRow(bar, 1);
        Grid.SetColumnSpan(bar, 2);
        grid.Children.Add(name);
        grid.Children.Add(numbers);
        grid.Children.Add(bar);
        return grid;
    }

    /// <summary>The number of a row as three cells so the numbers stand one under the other: the count right-aligned, the slash, the maximum left-aligned.</summary>
    private static void AddNumberCells(Grid grid, int row, int firstColumn, int count, int cap, bool estimated, bool open, int bonus = 0)
    {
        string brush = open ? "Brush.Accent" : "Brush.TextSubtle";
        TextBlock now = Label((estimated ? "~" : "") + count.ToString(CultureInfo.CurrentCulture) + (bonus > 0 ? $" (+{bonus.ToString(CultureInfo.CurrentCulture)})" : ""), brush, 13, bold: true);
        now.HorizontalAlignment = HorizontalAlignment.Right;
        now.Margin = new Thickness(0, 1, 0, 1);
        TextBlock slash = Label(" / ", "Brush.TextSubtle", 13);
        slash.Margin = new Thickness(0, 1, 0, 1);
        TextBlock max = Label(cap.ToString(CultureInfo.CurrentCulture), brush, 13, bold: true);
        max.HorizontalAlignment = HorizontalAlignment.Left;
        max.Margin = new Thickness(0, 1, 0, 1);
        foreach ((TextBlock block, int offset) in new[] { (now, 0), (slash, 1), (max, 2) })
        {
            Grid.SetRow(block, row);
            Grid.SetColumn(block, firstColumn + offset);
            grid.Children.Add(block);
        }
    }

    private static Grid NumberGrid(int extraColumns)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });  // the switch for the widget
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(215) }); // name
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });  // count with its bonus (right-aligned)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // slash
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });  // maximum (left-aligned)
        for (int i = 0; i < extraColumns; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        return grid;
    }

    /// <summary>A small switch in front of an element of the checklist: whether the checklist widget (and its overlay) shows that element.</summary>
    private static CheckBox Pick(string key)
    {
        var box = new CheckBox { IsChecked = ChecklistRows.IsPicked(key), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1, 0, 1), ToolTip = LocalizationManager.Instance["Checklist.PickHint"] };
        // the on/off switch of the settings, not a check box
        if (Application.Current?.TryFindResource("CompactSwitch") is Style switchStyle)
        {
            box.Style = switchStyle;
        }

        box.Click += (_, _) => ChecklistRows.SetPicked(key, box.IsChecked == true);
        return box;
    }

    /// <summary>One counter row: switch, name, the number (one under the other), behind it the warning of a refill that would run over the cap or the tick of a finished day.</summary>
    private void AddRow(Grid grid, int row, ChecklistRow r, TicketCharacter character)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bool open = r.CountsUp ? r.Count < r.Cap : r.Count > 0;
        CheckBox pick = Pick(r.Key);
        Grid.SetRow(pick, row);
        grid.Children.Add(pick);
        TextBlock label = Label(r.Label, open ? "Brush.Text" : "Brush.TextSubtle", 13);
        label.Margin = new Thickness(0, 1, 0, 1);
        Grid.SetRow(label, row);
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        AddNumberCells(grid, row, 2, r.Count, r.Cap, r.Estimated, open, r.Bonus);
        if (r.Manual is { } counter)
        {
            var hit = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "+1: left click / -1: right click" };
            Grid.SetRow(hit, row);
            Grid.SetColumn(hit, 1);
            Grid.SetColumnSpan(hit, 4);
            hit.MouseLeftButtonDown += (_, e) => { StepManual(character.Name, counter, 1); e.Handled = true; };
            hit.MouseRightButtonDown += (_, e) => { StepManual(character.Name, counter, -1); e.Handled = true; };
            grid.Children.Add(hit);
        }
    }

    private UIElement SectionCard(string title, IEnumerable<ChecklistRow> rows, TicketCharacter character, bool withExtras)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var stack = new StackPanel();
        stack.Children.Add(Label(title, "Brush.Text", 14, bold: true));
        ((TextBlock)stack.Children[0]).Margin = new Thickness(0, 0, 0, 4);
        Grid grid = NumberGrid(1);
        int i = 0;
        foreach (ChecklistRow r in rows)
        {
            AddRow(grid, i, r, character);
            UIElement? extra = null;
            if (withExtras && r.Def is { } def)
            {
                // the next daily refill would run over the cap: part of it is lost tomorrow
                int lost = Aion2Tickets.IsLocked(character, def.Id) ? 0 : Aion2Tickets.LostAtNextRefill(def, r.Count, _premium);
                if (lost > 0)
                {
                    extra = WarnBadge(string.Format(CultureInfo.CurrentCulture, loc[lost == 1 ? "Checklist.WarnLostOne" : "Checklist.WarnLostMany"], lost), "Brush.Warning");
                }
            }
            else if (r.Key == "daily:missions" && r.Count >= Aion2Tickets.MissionsPerDay)
            {
                extra = Label("\u2713", "Brush.Accent", 15, bold: true);
                ((TextBlock)extra).Margin = new Thickness(14, 0, 0, 0);
            }

            if (extra is not null)
            {
                Grid.SetRow(extra, i);
                Grid.SetColumn(extra, 5);
                grid.Children.Add(extra);
            }

            i++;
        }

        stack.Children.Add(grid);
        return Card(stack);
    }

    private void StepManual(string character, ChecklistManual.Counter counter, int delta)
    {
        ChecklistManual.Step(character, counter, delta, DateTime.Now);
        Render();
    }

    /// <summary>The cap the game shows: the subscriber's one where there is one (Shugo Festival keys 12, with the subscription 21), else the plain one.</summary>
    private int CapOf(TicketDef def) => _premium && def.SubscribeMax > def.Max ? def.SubscribeMax : def.Max;

    /// <summary>How soon a membership or ticket counts as "runs out soon" (red or amber with a warning triangle).</summary>
    private static readonly TimeSpan SoonLeft = TimeSpan.FromDays(3);

    /// <summary>The time left as days and hours ("11 d 23 h"), as hours under a day.</summary>
    internal static string LeftText(TimeSpan left)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        return left.TotalDays >= 1
            ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.DaysHours"], (int)left.TotalDays, left.Hours)
            : string.Format(CultureInfo.CurrentCulture, loc["Checklist.HoursOnly"], Math.Max(1, (int)left.TotalHours));
    }

    /// <summary>A warning: a small triangle with an exclamation mark and a short text behind it.</summary>
    private static UIElement WarnBadge(string text, string brush)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var triangle = new System.Windows.Shapes.Polygon { Points = new PointCollection { new Point(8, 0), new Point(16, 14), new Point(0, 14) } };
        triangle.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
        var mark = new TextBlock { Text = "!", FontSize = 9, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        mark.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Window");
        var shape = new Grid { Width = 16, Height = 14, VerticalAlignment = VerticalAlignment.Center };
        shape.Children.Add(triangle);
        shape.Children.Add(mark);
        panel.Children.Add(shape);
        TextBlock label = Label(text, brush, 11);
        label.Margin = new Thickness(5, 0, 0, 0);
        panel.Children.Add(label);
        return panel;
    }

    /// <summary>One line of the membership card: the name left, the time right (with a triangle when it runs out soon), and under it small lines.</summary>
    private static void AddSubscriptionRow(StackPanel stack, string key, string name, string right, string rightBrush, string? warnBrush, bool muted, params string[] notes)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        CheckBox pick = Pick(key);
        grid.Children.Add(pick);
        TextBlock label = Label(name, muted ? "Brush.TextSubtle" : "Brush.Text", 13);
        Grid.SetColumn(label, 1);
        var value = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (warnBrush is not null)
        {
            UIElement badge = WarnBadge("", warnBrush);
            ((StackPanel)badge).Margin = new Thickness(0, 0, 6, 0);
            value.Children.Add(badge);
        }

        value.Children.Add(Label(right, rightBrush, 13, bold: true));
        Grid.SetColumn(value, 2);
        grid.Children.Add(label);
        grid.Children.Add(value);
        stack.Children.Add(grid);
        foreach (string note in notes)
        {
            TextBlock line = Label(note, warnBrush == "Brush.Danger" ? "Brush.Danger" : "Brush.TextSubtle", 11);
            line.TextWrapping = TextWrapping.Wrap;
            line.Margin = new Thickness(36, 1, 0, 0);
            stack.Children.Add(line);
        }
    }

    /// <summary>Membership, pet auto-loot and the access times: when they run out, and what is gone then.</summary>
    private void AddSubscriptions(string title, IEnumerable<TicketDef> defs, TicketCharacter character, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        TicketDef[] times = defs.ToArray();
        DateTime now = DateTime.Now;
        bool hasMember = character.Tickets.TryGetValue(Aion2Tickets.PremiumId, out TicketValue member) && member.TimeMs > 0;
        bool hasLoot = character.Tickets.TryGetValue(Aion2Tickets.AutoLootId, out TicketValue loot);
        if (times.Length == 0 && !hasMember && !hasLoot)
        {
            return;
        }

        var stack = new StackPanel();
        stack.Children.Add(Label(title, "Brush.Text", 14, bold: true));
        ((TextBlock)stack.Children[0]).Margin = new Thickness(0, 0, 0, 2);
        if (hasMember)
        {
            DateTime end = DateTimeOffset.FromUnixTimeMilliseconds(member.TimeMs).LocalDateTime;
            TimeSpan left = end - now;
            TicketDef? odyle = Aion2Tickets.Find(Aion2Tickets.OdyleId);
            TicketDef? keys = Aion2Tickets.Find(1);
            string gone = string.Format(CultureInfo.CurrentCulture, loc["Checklist.PremiumLoses"], odyle?.Max ?? 560, odyle?.SubscribeMax ?? 840, keys?.Max ?? 12, keys?.SubscribeMax ?? 21);
            if (left > TimeSpan.Zero)
            {
                bool soon = left < SoonLeft;
                AddSubscriptionRow(stack, "sub:premium", loc["Checklist.Premium"], LeftText(left), soon ? "Brush.Danger" : "Brush.Accent", soon ? "Brush.Danger" : null, false,
                    string.Format(CultureInfo.CurrentCulture, loc["Checklist.Until"], end.ToString("g", CultureInfo.CurrentCulture)) + "  ·  " + gone);
            }
            else
            {
                AddSubscriptionRow(stack, "sub:premium", loc["Checklist.Premium"], string.Format(CultureInfo.CurrentCulture, loc["Checklist.PremiumEnded"], end.ToString("d", CultureInfo.CurrentCulture)), "Brush.Danger", "Brush.Danger", false,
                    loc["Checklist.PremiumNow"]);
            }
        }

        if (hasLoot)
        {
            string lootName = loc["Checklist.AutoLoot"];
            DateTime end = loot.TimeMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(loot.TimeMs).LocalDateTime : DateTime.MinValue;
            TimeSpan left = end - now;
            if (loot.TimeMs > 0 && left > TimeSpan.Zero)
            {
                bool soon = left < SoonLeft;
                AddSubscriptionRow(stack, "sub:autoloot", lootName, LeftText(left), "Brush.Accent", soon ? "Brush.Warning" : null, false,
                    string.Format(CultureInfo.CurrentCulture, loc["Checklist.Until"], end.ToString("g", CultureInfo.CurrentCulture)) + "  ·  " + loc["Checklist.AutoLootNote"]);
            }
            else
            {
                AddSubscriptionRow(stack, "sub:autoloot", lootName, loc["Checklist.AutoLootNone"], "Brush.TextSubtle", null, true);
            }
        }

        foreach (TicketDef def in times)
        {
            TimeSpan left = Aion2Tickets.IsLocked(character, def.Id) || !character.Tickets.TryGetValue(def.Id, out TicketValue timeValue) ? TimeSpan.Zero : TimeSpan.FromMilliseconds(timeValue.TimeMs);
            bool open = left > TimeSpan.Zero;
            AddSubscriptionRow(stack, "time:" + def.Id, def.NameIn(language), $"{(int)left.TotalHours}:{left.Minutes:00}", open ? "Brush.Accent" : "Brush.TextSubtle", null, !open);
        }

        _body.Children.Add(Card(stack));
    }

    /// <summary>The Daeva passes of the character: level (where the cost of the levels is known), experience of the week against the weekly limit, end of the pass.</summary>
    private void AddPass(string title, TicketCharacter character, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var found = Aion2Tickets.Passes.Where(p => character.Tickets.ContainsKey(p.TicketId)).ToArray();
        if (found.Length == 0)
        {
            return;
        }

        var stack = new StackPanel();
        stack.Children.Add(Label(title, "Brush.Text", 14, bold: true));
        ((TextBlock)stack.Children[0]).Margin = new Thickness(0, 0, 0, 2);
        foreach (Aion2Tickets.PassDef pass in found)
        {
            TicketValue value = character.Tickets[pass.TicketId];
            int week = Math.Min(value.Bonus, pass.WeeklyLimit);
            bool full = value.Bonus >= pass.WeeklyLimit;
            var head = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(Pick("pass:" + pass.TicketId));
            TextBlock passName = Label(loc[pass.NameKeyFor(character.ServerId)], "Brush.Text", 13, bold: true);
            Grid.SetColumn(passName, 1);
            head.Children.Add(passName);
            bool ended = DateTime.Now >= pass.End;
            TextBlock end = Label(string.Format(CultureInfo.CurrentCulture, loc[ended ? "Checklist.PassEnded" : "Checklist.PassEnds"], pass.End.ToString("g", CultureInfo.CurrentCulture)), "Brush.TextSubtle", 11);
            Grid.SetColumn(end, 2);
            head.Children.Add(end);
            stack.Children.Add(head);

            var line = new Grid { Margin = new Thickness(0, 1, 0, 0) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Aion2Tickets.PassLevel? level = Aion2Tickets.PassLevelOf(pass.TicketId, value.Count);
            string levelText = level is { } l
                ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.PassLevel"], l.Level, l.Into.ToString("N0", CultureInfo.CurrentCulture), l.Need.ToString("N0", CultureInfo.CurrentCulture))
                : string.Format(CultureInfo.CurrentCulture, loc["Checklist.PassTotal"], value.Count.ToString("N0", CultureInfo.CurrentCulture));
            line.Children.Add(Label(levelText, "Brush.TextSubtle", 12));
            TextBlock weekText = Label(string.Format(CultureInfo.CurrentCulture, loc["Checklist.PassWeek"], week.ToString("N0", CultureInfo.CurrentCulture), pass.WeeklyLimit.ToString("N0", CultureInfo.CurrentCulture)), full ? "Brush.Heal" : "Brush.Accent", 12, bold: true);
            Grid.SetColumn(weekText, 1);
            line.Children.Add(weekText);
            stack.Children.Add(line);

            var bar = new ProgressBar { Minimum = 0, Maximum = pass.WeeklyLimit, Value = week, Height = 5, Margin = new Thickness(0, 3, 0, 0) };
            bar.SetResourceReference(Control.ForegroundProperty, full ? "Brush.Heal" : "Brush.Accent");
            bar.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
            bar.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
            stack.Children.Add(bar);
        }

        _body.Children.Add(Card(stack));
    }

    private static Dictionary<string, Dictionary<string, string>>? _scrollNames;

    /// <summary>The item name of the weekly mandatory scroll as the game's own text has it in the language (assets/aion2/tickets/scrolls.json, built by
    /// Tools/aion2-dat/build_tickets.py); the table's text where the client has none for the language (the Chinese ones).</summary>
    internal static string ScrollName(string kind, string language, string fallback)
    {
        if (_scrollNames is null)
        {
            _scrollNames = new Dictionary<string, Dictionary<string, string>>();
            try
            {
                string path = AssetStore.Path("aion2", "tickets", "scrolls.json");
                if (System.IO.File.Exists(path))
                {
                    _scrollNames = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(System.IO.File.ReadAllText(path)) ?? _scrollNames;
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.Text.Json.JsonException)
            {
                // no names file: the table's text below
            }
        }

        return _scrollNames.TryGetValue(kind, out Dictionary<string, string>? names) && names.TryGetValue(language, out string? name) ? name : fallback;
    }


    /// <summary>One line of a section: name, number, next refill - the same column widths in every section, so the numbers stand under each other.</summary>
    private static Grid RowGrid()
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        return grid;
    }

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
