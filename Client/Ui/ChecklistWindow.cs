using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// The checklist (Tools menu, no overlay): the Odyle energy and the daily and weekly counters of a character - entry counts, reward counts, boss
/// kills, access times - as the game reports them (see <see cref="Aion2Tickets"/>), with the next refill of each. The numbers are those of the last
/// login or change the meter saw; "~" marks a number that was worked out from the refills since then. Built in code, in the look of the other windows.
/// </summary>
public sealed class ChecklistWindow : Window
{
    private readonly WrapPanel _characters = new();
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

    public ChecklistWindow()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        Title = loc["Checklist.Title"];
        Width = 480;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        MinWidth = 440;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12, 10, 12, 12) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(header, Dock.Top);
        _seen.VerticalAlignment = VerticalAlignment.Center;
        _seen.Margin = new Thickness(14, 0, 0, 0);
        _seen.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        DockPanel.SetDock(_seen, Dock.Right);
        header.Children.Add(_seen);
        header.Children.Add(_characters);
        root.Children.Add(header);

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
            bool active = name == wanted;
            var chip = new Border
            {
                Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 0, 8, 4), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Cursor = System.Windows.Input.Cursors.Hand, Child = Label(name, "Brush.Text", 14, bold: true),
            };
            chip.SetResourceReference(Border.BackgroundProperty, active ? "Brush.ControlRaised" : "Brush.Control");
            chip.SetResourceReference(Border.BorderBrushProperty, active ? "Brush.Accent" : "Brush.Border");
            chip.MouseLeftButtonDown += (_, _) =>
            {
                _selected = name;
                Refresh();
            };
            _characters.Children.Add(chip);
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
        if (character is null)
        {
            _seen.Text = "";
            _body.Children.Add(Card(Label(loc["Checklist.Empty"], "Brush.TextMuted", 13)));
            return;
        }

        DateTime now = DateTime.Now;
        DateTime seenAt = character.SavedAt.ToLocalTime();
        _seen.Text = string.Format(CultureInfo.CurrentCulture, loc["Checklist.Seen"], seenAt.ToString("g", CultureInfo.CurrentCulture));

        if (character.Tickets.TryGetValue(Aion2Tickets.OdyleId, out TicketValue odyle) && Aion2Tickets.Find(Aion2Tickets.OdyleId) is { } odyleDef)
        {
            _body.Children.Add(OdyleCard(odyleDef, odyle, language));
        }

        AddOverview(loc["Checklist.Overall"], Overview.Select(id => Aion2Tickets.Find(id)).Where(d => d is not null && character.Tickets.ContainsKey(d.Id)).Select(d => d!), character, seenAt, now, language);
        AddDaily(loc["Checklist.Daily"], character);
        AddWeekly(loc["Checklist.Weekly"], character, seenAt, now, language);

        AddTimes(loc["Checklist.Time"], TimeTickets.Select(id => Aion2Tickets.Find(id)).Where(d => d is not null && character.Tickets.ContainsKey(d.Id)).Select(d => d!), character, language);

        var note = Label(loc["Checklist.Note"], "Brush.TextSubtle", 11);
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(4, 4, 4, 0);
        _body.Children.Add(note);
    }

    /// <summary>The Odyle energy as the game's tooltip shows it: the basic energy (cap 840 with the subscription) and the charged energy (cap 2000).</summary>
    private static UIElement OdyleCard(TicketDef def, TicketValue value, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var stack = new StackPanel();
        stack.Children.Add(Label(def.NameIn(language), "Brush.Text", 16, bold: true));
        ((TextBlock)stack.Children[0]).Margin = new Thickness(0, 0, 0, 4);
        stack.Children.Add(OdyleLine(loc["Checklist.OdyleBase"], value.Count, Math.Max(def.SubscribeMax, value.Count)));
        stack.Children.Add(OdyleLine(loc["Checklist.OdyleCharged"], value.Bonus, Math.Max(OdyleChargedMax, value.Bonus)));
        return Card(stack);
    }

    private const int OdyleChargedMax = 2000;

    private static UIElement OdyleLine(string label, int value, int cap)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        TextBlock name = Label(label, "Brush.Text", 13);
        TextBlock numbers = Label($"{value:N0} / {cap:N0}", value > 0 ? "Brush.Accent" : "Brush.TextSubtle", 13, bold: true);
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
        TicketDef[] list = defs.ToArray();
        if (list.Length == 0)
        {
            return;
        }

        var stack = new StackPanel();
        stack.Children.Add(Label(title, "Brush.Text", 14, bold: true));
        ((TextBlock)stack.Children[0]).Margin = new Thickness(0, 0, 0, 4);
        Grid grid = NumberGrid(0);
        for (int i = 0; i < list.Length; i++)
        {
            TicketDef def = list[i];
            int reported = character.Tickets[def.Id].Count;
            int bonus = character.Tickets[def.Id].Bonus;
            int count = Aion2Tickets.Expected(def, reported, seenAt, now);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock name = Label(def.ShortIn(language), count > 0 ? "Brush.Text" : "Brush.TextSubtle", 13);
            name.Margin = new Thickness(0, 1, 0, 1);
            Grid.SetRow(name, i);
            grid.Children.Add(name);
            AddNumberCells(grid, i, 1, count, Math.Max(CapOf(def), count + bonus), count != reported, count > 0, bonus);
        }

        stack.Children.Add(grid);
        _body.Children.Add(Card(stack));
    }

    /// <summary>The mandatory daily tasks ("Missions") of the home region (Verteron for Elyos, Altgard for Asmodians) as one counter, handed in of five: from the
    /// game's quest list. Five of five gets a tick.</summary>
    private void AddDaily(string title, TicketCharacter character)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        int done = character.Tickets.TryGetValue(Aion2Tickets.MissionsId, out TicketValue missions) ? missions.Count : 0;
        string region = loc[character.ServerId / 1000 == 2 ? "Checklist.RegionAsmodian" : "Checklist.RegionElyos"];
        var stack = new StackPanel();
        stack.Children.Add(Label(title, "Brush.Text", 14, bold: true));
        ((TextBlock)stack.Children[0]).Margin = new Thickness(0, 0, 0, 4);
        Grid grid = NumberGrid(1);
        AddTableRow(grid, 0, string.Format(CultureInfo.CurrentCulture, loc["Checklist.DailyTasks"], region), done, Aion2Tickets.MissionsPerDay, false, countsUp: true);
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

    /// <summary>The weekly part: the daily dungeon's entries (from the game) and the scroll purchases at the NPC (counted by hand; the shop limit is per server, so
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
        if (Aion2Tickets.Find(DailyDungeonTicket) is { } dungeon && character.Tickets.TryGetValue(dungeon.Id, out TicketValue value))
        {
            int count = Aion2Tickets.Expected(dungeon, value.Count, seenAt, now);
            AddTableRow(grid, row++, dungeon.ShortIn(language), count, Math.Max(CapOf(dungeon), count + value.Bonus), count != value.Count, countsUp: false, bonus: value.Bonus);
        }

        AddTableRow(grid, row++, ScrollName("verteron", language, loc["Checklist.ScrollVerteron"]), shared.Verteron, ChecklistManual.VerteronMax, false, countsUp: true);
        AddTableRow(grid, row++, ScrollName("abyss", language, loc["Checklist.ScrollAbyss"]), shared.Abyss, ChecklistManual.AbyssMax, false, countsUp: true);
        stack.Children.Add(grid);
        _body.Children.Add(Card(stack));
    }

    /// <summary>One table row: name, number (one under the other) and, for the hand-counted ones, the minus and plus.</summary>
    private static void AddTableRow(Grid grid, int row, string name, int count, int cap, bool estimated, bool countsUp, int bonus = 0)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bool open = countsUp ? count < cap : count > 0;
        TextBlock label = Label(name, open ? "Brush.Text" : "Brush.TextSubtle", 13);
        label.Margin = new Thickness(0, 1, 0, 1);
        Grid.SetRow(label, row);
        grid.Children.Add(label);
        AddNumberCells(grid, row, 1, count, cap, estimated, open, bonus);
    }

    /// <summary>The cap the game shows: the subscriber's one where there is one (Shugo Festival keys 12, with the subscription 21), else the plain one.</summary>
    private static int CapOf(TicketDef def) => def.SubscribeMax > def.Max ? def.SubscribeMax : def.Max;

    /// <summary>The time left of the access-time tickets (h:mm), in the same name column as the other tables.</summary>
    private void AddTimes(string title, IEnumerable<TicketDef> defs, TicketCharacter character, string language)
    {
        TicketDef[] list = defs.ToArray();
        if (list.Length == 0)
        {
            return;
        }

        var stack = new StackPanel();
        stack.Children.Add(Label(title, "Brush.Text", 14, bold: true));
        ((TextBlock)stack.Children[0]).Margin = new Thickness(0, 0, 0, 4);
        Grid grid = NumberGrid(0);
        for (int i = 0; i < list.Length; i++)
        {
            TicketDef def = list[i];
            TimeSpan left = TimeSpan.FromMilliseconds(character.Tickets[def.Id].TimeMs);
            bool open = left > TimeSpan.Zero;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock name = Label(def.NameIn(language), open ? "Brush.Text" : "Brush.TextSubtle", 13);
            name.Margin = new Thickness(0, 1, 0, 1);
            Grid.SetRow(name, i);
            TextBlock time = Label($"{(int)left.TotalHours}:{left.Minutes:00}", open ? "Brush.Accent" : "Brush.TextSubtle", 13, bold: true);
            time.HorizontalAlignment = HorizontalAlignment.Right;
            time.Margin = new Thickness(0, 1, 0, 1);
            Grid.SetRow(time, i);
            Grid.SetColumn(time, 1);
            Grid.SetColumnSpan(time, 3);
            grid.Children.Add(name);
            grid.Children.Add(time);
        }

        stack.Children.Add(grid);
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
                string path = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "tickets", "scrolls.json");
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
