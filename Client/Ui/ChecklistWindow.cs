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
            _body.Children.Add(OdyleCard(odyleDef, odyleLocked ? default : character.Tickets[Aion2Tickets.OdyleId], language, odyleLocked, _premium));
        }

        AddOverview(loc["Checklist.Overall"], Overview.Select(id => Aion2Tickets.Find(id)).Where(d => d is not null && (character.Tickets.ContainsKey(d.Id) || Aion2Tickets.IsLocked(character, d.Id))).Select(d => d!), character, seenAt, now, language);
        AddDaily(loc["Checklist.Daily"], character);
        AddWeekly(loc["Checklist.Weekly"], character, seenAt, now, language);

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
    private static UIElement OdyleCard(TicketDef def, TicketValue value, string language, bool locked, bool premium)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var stack = new StackPanel();
        stack.Children.Add(Label(def.NameIn(language), locked ? "Brush.TextSubtle" : "Brush.Text", 16, bold: true));
        ((TextBlock)stack.Children[0]).Margin = new Thickness(0, 0, 0, 4);
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

    /// <summary>The overview as a table: the names left, the numbers one under the other in a column right behind them.</summary>
    private void AddOverview(string title, IEnumerable<TicketDef> defs, TicketCharacter character, DateTime seenAt, DateTime now, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        TicketDef[] list = defs.ToArray();
        if (list.Length == 0)
        {
            return;
        }

        var stack = new StackPanel();
        stack.Children.Add(Label(title, "Brush.Text", 14, bold: true));
        ((TextBlock)stack.Children[0]).Margin = new Thickness(0, 0, 0, 4);
        Grid grid = NumberGrid(1);
        for (int i = 0; i < list.Length; i++)
        {
            TicketDef def = list[i];
            bool locked = Aion2Tickets.IsLocked(character, def.Id); // the level is too low: grey, 0 / cap
            TicketValue own = locked || !character.Tickets.TryGetValue(def.Id, out TicketValue found) ? default : found;
            int reported = own.Count;
            int bonus = own.Bonus;
            int count = locked ? 0 : Aion2Tickets.Expected(def, reported, seenAt, now, _premium);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock name = Label(def.ShortIn(language), count > 0 ? "Brush.Text" : "Brush.TextSubtle", 13);
            name.Margin = new Thickness(0, 1, 0, 1);
            Grid.SetRow(name, i);
            grid.Children.Add(name);
            AddNumberCells(grid, i, 1, count, Math.Max(CapOf(def), count + bonus), count != reported, count > 0, bonus);
            // the next daily refill would run over the cap: part of it is lost tomorrow
            int lost = locked ? 0 : Aion2Tickets.LostAtNextRefill(def, count, _premium);
            if (lost > 0)
            {
                UIElement badge = WarnBadge(string.Format(CultureInfo.CurrentCulture, loc[lost == 1 ? "Checklist.WarnLostOne" : "Checklist.WarnLostMany"], lost), "Brush.Warning");
                Grid.SetRow(badge, i);
                Grid.SetColumn(badge, 4);
                grid.Children.Add(badge);
            }
        }

        stack.Children.Add(grid);
        _body.Children.Add(Card(stack));
    }

    /// <summary>The daily part: the mandatory tasks ("Missions") of the home region (Verteron for Elyos, Altgard for Asmodians) as one counter, handed in of five
    /// (five of five gets a tick).</summary>
    private void AddDaily(string title, TicketCharacter character)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        // (Stacy's fix from her fork, MIT) the count read last, or 0 (shown as an estimate) when a daily reset came since that reading: the game sends it at a login, a hand-in or when the journal is opened, never at the reset
        bool stale = character.Tickets.TryGetValue(Aion2Tickets.MissionsId, out TicketValue missions) && Aion2Tickets.MissionsStale(missions, DateTime.Now);
        int done = stale ? 0 : missions.Count;
        string region = loc[character.ServerId / 1000 == 2 ? "Checklist.RegionAsmodian" : "Checklist.RegionElyos"];
        var stack = new StackPanel();
        stack.Children.Add(Label(title, "Brush.Text", 14, bold: true));
        ((TextBlock)stack.Children[0]).Margin = new Thickness(0, 0, 0, 4);
        Grid grid = NumberGrid(1);
        int row = 0;
        AddTableRow(grid, row++, string.Format(CultureInfo.CurrentCulture, loc["Checklist.DailyTasks"], region), done, Aion2Tickets.MissionsPerDay, stale, countsUp: true);
        if (done >= Aion2Tickets.MissionsPerDay)
        {
            TextBlock tick = Label("\u2713", "Brush.Accent", 15, bold: true);
            tick.Margin = new Thickness(14, 0, 0, 0);
            Grid.SetColumn(tick, 4);
            grid.Children.Add(tick);
        }

        stack.Children.Add(grid);
        _body.Children.Add(Card(stack));
    }

    /// <summary>The weekly part: the entries of the "Daily Dungeon" (the name is misleading: its counter is weekly; cap 14, extra entries come on top as the bonus behind the count)
    /// and the scroll purchases at the NPC (counted by hand; the shop limit is per server, so
    /// the counters are shared by all characters).</summary>
    private void AddWeekly(string title, TicketCharacter character, DateTime seenAt, DateTime now, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        ChecklistManual.State shared = ChecklistManual.Get(ChecklistManual.SharedKey, now);
        var stack = new StackPanel();
        stack.Children.Add(Label(title, "Brush.Text", 14, bold: true));
        ((TextBlock)stack.Children[0]).Margin = new Thickness(0, 0, 0, 4);
        Grid grid = NumberGrid(0);
        int row = 0;
        bool dungeonLocked = Aion2Tickets.IsLocked(character, DailyDungeonTicket);
        if (Aion2Tickets.Find(DailyDungeonTicket) is { } dungeon && (dungeonLocked || character.Tickets.ContainsKey(dungeon.Id)))
        {
            TicketValue value = dungeonLocked ? default : character.Tickets[dungeon.Id];
            int count = dungeonLocked ? 0 : Aion2Tickets.Expected(dungeon, value.Count, seenAt, now, _premium);
            AddTableRow(grid, row++, dungeon.ShortIn(language), count, Math.Max(dungeon.Max, count), count != value.Count, countsUp: false, bonus: value.Bonus);
        }

        AddTableRow(grid, row++, ScrollName("verteron", language, loc["Checklist.ScrollVerteron"]), shared.Verteron, ChecklistManual.VerteronMax, false, countsUp: true);
        AddTableRow(grid, row++, ScrollName("abyss", language, loc["Checklist.ScrollAbyss"]), shared.Abyss, ChecklistManual.AbyssMax, false, countsUp: true);
        // the purchase counter of the trade guild comes from the game; an older reset time means the week is over
        character.Tickets.TryGetValue(Aion2Tickets.ShopOdyleId, out TicketValue shop);
        long nowMs = new DateTimeOffset(DateTime.UtcNow, TimeSpan.Zero).ToUnixTimeMilliseconds();
        int bought = shop.TimeMs > nowMs ? Math.Min(shop.Count, Aion2Tickets.ShopOdyleMax) : 0;
        AddTableRow(grid, row++, loc["Checklist.ShopOdyle"], bought, Aion2Tickets.ShopOdyleMax, false, countsUp: true);

        // the week's points of the season missions (frame 0x8356 at login): points of 8,000; one that the game last changed more than a week ago is of a week long past
        if (character.Tickets.TryGetValue(Aion2Tickets.WeeklyPointsId, out TicketValue weekly))
        {
            int points = nowMs - weekly.TimeMs < 7L * 24 * 3600 * 1000 ? Math.Min(weekly.Count, Aion2Tickets.WeeklyPointsMax) : 0;
            AddTableRow(grid, row++, loc["Checklist.WeeklyPoints"], points, Aion2Tickets.WeeklyPointsMax, false, countsUp: true);
        }

        // the two conversions are a note ticked by hand (click = +1, right click = -1): the meter has not found their frame
        ChecklistManual.State own = ChecklistManual.Get(character.Name, now);
        AddTableRow(grid, row++, loc["Checklist.ConvertBig"], own.ConvertBig, ChecklistManual.ConvertBigMax, false, countsUp: true, step: d => StepManual(character.Name, ChecklistManual.Counter.ConvertBig, d));
        AddTableRow(grid, row++, loc["Checklist.ConvertSmall"], own.ConvertSmall, ChecklistManual.ConvertSmallMax, false, countsUp: true, step: d => StepManual(character.Name, ChecklistManual.Counter.ConvertSmall, d));
        stack.Children.Add(grid);
        _body.Children.Add(Card(stack));
    }

    private void StepManual(string character, ChecklistManual.Counter counter, int delta)
    {
        ChecklistManual.Step(character, counter, delta, DateTime.Now);
        Render();
    }

    /// <summary>One table row: name, number (one under the other) and, for the hand-counted ones, a click area (left click +1, right click -1).</summary>
    private static void AddTableRow(Grid grid, int row, string name, int count, int cap, bool estimated, bool countsUp, int bonus = 0, Action<int>? step = null)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bool open = countsUp ? count < cap : count > 0;
        TextBlock label = Label(name, open ? "Brush.Text" : "Brush.TextSubtle", 13);
        label.Margin = new Thickness(0, 1, 0, 1);
        Grid.SetRow(label, row);
        grid.Children.Add(label);
        AddNumberCells(grid, row, 1, count, cap, estimated, open, bonus);
        if (step is not null)
        {
            var hit = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "+1: left click / -1: right click" };
            Grid.SetRow(hit, row);
            Grid.SetColumnSpan(hit, 4);
            hit.MouseLeftButtonDown += (_, e) => { step(1); e.Handled = true; };
            hit.MouseRightButtonDown += (_, e) => { step(-1); e.Handled = true; };
            grid.Children.Add(hit);
        }
    }

    /// <summary>The cap the game shows: the subscriber's one where there is one (Shugo Festival keys 12, with the subscription 21), else the plain one.</summary>
    private int CapOf(TicketDef def) => _premium && def.SubscribeMax > def.Max ? def.SubscribeMax : def.Max;

    /// <summary>How soon a membership or ticket counts as "runs out soon" (red or amber with a warning triangle).</summary>
    private static readonly TimeSpan SoonLeft = TimeSpan.FromDays(3);

    /// <summary>The time left as days and hours ("11 d 23 h"), as hours under a day.</summary>
    private static string LeftText(TimeSpan left)
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
    private static void AddSubscriptionRow(StackPanel stack, string name, string right, string rightBrush, string? warnBrush, bool muted, params string[] notes)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock label = Label(name, muted ? "Brush.TextSubtle" : "Brush.Text", 13);
        var value = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (warnBrush is not null)
        {
            UIElement badge = WarnBadge("", warnBrush);
            ((StackPanel)badge).Margin = new Thickness(0, 0, 6, 0);
            value.Children.Add(badge);
        }

        value.Children.Add(Label(right, rightBrush, 13, bold: true));
        Grid.SetColumn(value, 1);
        grid.Children.Add(label);
        grid.Children.Add(value);
        stack.Children.Add(grid);
        foreach (string note in notes)
        {
            TextBlock line = Label(note, warnBrush == "Brush.Danger" ? "Brush.Danger" : "Brush.TextSubtle", 11);
            line.TextWrapping = TextWrapping.Wrap;
            line.Margin = new Thickness(0, 1, 0, 0);
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
                AddSubscriptionRow(stack, loc["Checklist.Premium"], LeftText(left), soon ? "Brush.Danger" : "Brush.Accent", soon ? "Brush.Danger" : null, false,
                    string.Format(CultureInfo.CurrentCulture, loc["Checklist.Until"], end.ToString("g", CultureInfo.CurrentCulture)) + "  ·  " + gone);
            }
            else
            {
                AddSubscriptionRow(stack, loc["Checklist.Premium"], string.Format(CultureInfo.CurrentCulture, loc["Checklist.PremiumEnded"], end.ToString("d", CultureInfo.CurrentCulture)), "Brush.Danger", "Brush.Danger", false,
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
                AddSubscriptionRow(stack, lootName, LeftText(left), "Brush.Accent", soon ? "Brush.Warning" : null, false,
                    string.Format(CultureInfo.CurrentCulture, loc["Checklist.Until"], end.ToString("g", CultureInfo.CurrentCulture)) + "  ·  " + loc["Checklist.AutoLootNote"]);
            }
            else
            {
                AddSubscriptionRow(stack, lootName, loc["Checklist.AutoLootNone"], "Brush.TextSubtle", null, true);
            }
        }

        foreach (TicketDef def in times)
        {
            TimeSpan left = Aion2Tickets.IsLocked(character, def.Id) || !character.Tickets.TryGetValue(def.Id, out TicketValue timeValue) ? TimeSpan.Zero : TimeSpan.FromMilliseconds(timeValue.TimeMs);
            bool open = left > TimeSpan.Zero;
            AddSubscriptionRow(stack, def.NameIn(language), $"{(int)left.TotalHours}:{left.Minutes:00}", open ? "Brush.Accent" : "Brush.TextSubtle", null, !open);
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
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(Label(loc[pass.NameKeyFor(character.ServerId)], "Brush.Text", 13, bold: true));
            bool ended = DateTime.Now >= pass.End;
            TextBlock end = Label(string.Format(CultureInfo.CurrentCulture, loc[ended ? "Checklist.PassEnded" : "Checklist.PassEnds"], pass.End.ToString("g", CultureInfo.CurrentCulture)), "Brush.TextSubtle", 11);
            Grid.SetColumn(end, 1);
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
    private static string ScrollName(string kind, string language, string fallback)
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
