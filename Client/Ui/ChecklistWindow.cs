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
        Width = 1180;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        MinWidth = 900;
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

        List<ChecklistRow> rows = ChecklistRows.Build(character, seenAt, now, _premium, language);
        // the three weekly counters of the Odyle energy (bought in the trade guild, converted) belong to the energy card; the rest of the weekly list stays below
        ChecklistRow[] odyleRows = rows.Where(r => r.Key is "weekly:shopodyle" or "weekly:convertbig" or "weekly:convertsmall").ToArray();
        ChecklistRow[] entries = rows.Where(r => r.Group == ChecklistGroup.Overview && !r.Key.StartsWith("corridor:", StringComparison.Ordinal)).ToArray();
        ChecklistRow[] corridors = rows.Where(r => r.Key.StartsWith("corridor:", StringComparison.Ordinal)).ToArray();
        ChecklistRow[] daily = rows.Where(r => r.Group == ChecklistGroup.Daily).ToArray();
        ChecklistRow[] weekly = rows.Where(r => r.Group == ChecklistGroup.Weekly && !odyleRows.Contains(r)).ToArray();

        // the numbers of the left halves (entries, daily) and of the right halves (corridors, weekly) share their column widths, so the slashes stand under each other in both cards
        RowLook left = Look(entries.Concat(daily), bar: true, warnColumn: true);
        RowLook right = Look(corridors.Concat(weekly), bar: true, warnColumn: false);
        RowLook plain = Look(odyleRows, bar: false, warnColumn: false, minValue: 46, minCap: 34);

        // the top row: Odyle energy, subscriptions and tickets, Daeva passes
        var top = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
        bool odyleLocked = Aion2Tickets.IsLocked(character, Aion2Tickets.OdyleId);
        if ((odyleLocked || character.Tickets.ContainsKey(Aion2Tickets.OdyleId)) && Aion2Tickets.Find(Aion2Tickets.OdyleId) is { } odyleDef)
        {
            UIElement card = OdyleCard(odyleDef, odyleLocked ? default : character.Tickets[Aion2Tickets.OdyleId], language, odyleLocked, _premium, character.Name, odyleRows, plain, character);
            ((FrameworkElement)card).Margin = new Thickness(0, 0, 6, 0);
            top.Children.Add(card);
        }

        UIElement? subs = BuildSubscriptions(loc["Checklist.Subs"], TimeTickets.Select(id => Aion2Tickets.Find(id)).Where(d => d is not null && (character.Tickets.ContainsKey(d.Id) || Aion2Tickets.IsLocked(character, d.Id))).Select(d => d!), character, language);
        if (subs is not null)
        {
            Grid.SetColumn(subs, 1);
            ((FrameworkElement)subs).Margin = new Thickness(0, 0, 6, 0);
            top.Children.Add(subs);
        }

        UIElement? pass = BuildPass(loc["Checklist.DaevaPass"], character, language);
        if (pass is not null)
        {
            Grid.SetColumn(pass, 2);
            ((FrameworkElement)pass).Margin = new Thickness(0);
            top.Children.Add(pass);
        }

        _body.Children.Add(top);

        if (entries.Length + corridors.Length > 0)
        {
            _body.Children.Add(SplitCard(loc["Checklist.Entries"], (null, entries, left), (null, corridors, right), character));
        }

        if (daily.Length + weekly.Length > 0)
        {
            _body.Children.Add(SplitCard(null, (loc["Checklist.Daily"], daily, left), (loc["Checklist.Weekly"], weekly, right), character));
        }

        _body.Children.Add(Legend(loc));
    }

    /// <summary>The Odyle energy as the game's tooltip shows it: the basic energy (cap 840 with the subscription) and the charged energy (cap 2000), then its three weekly counters.</summary>
    private UIElement OdyleCard(TicketDef def, TicketValue value, string language, bool locked, bool premium, string characterName, ChecklistRow[] weeklyRows, RowLook weeklyLook, TicketCharacter character)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var stack = new StackPanel();
        // a small switch in front of the title: whether the checklist widget shows the energy
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        CheckBox pick = Pick("odyle");
        pick.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(pick, Dock.Left);
        head.Children.Add(pick);
        head.Children.Add(Label(def.NameIn(language), locked ? "Brush.TextSubtle" : "Brush.Text", 15, bold: true));
        stack.Children.Add(head);
        if (!locked)
        {
            // as the guild list shows it: now (+ already charged) (+ still in items); the items are known for the character in the game only
            int? items = string.Equals(characterName, Aion2Tickets.CurrentName, StringComparison.OrdinalIgnoreCase) ? Aion2.Aion2Stock.OdyleItemEnergy() : null;
            TextBlock summary = HubDashboard.OdyleSummary(value.Count, value.Bonus, items, 19);
            summary.Margin = new Thickness(0, 0, 0, 3);
            summary.ToolTip = loc["Checklist.OdyleSummaryHint"];
            stack.Children.Add(summary);
        }

        int baseCap = Math.Max(premium ? def.SubscribeMax : def.Max, value.Count);
        int chargedCap = Math.Max(OdyleChargedMax, value.Bonus);
        RowLook bars = weeklyLook;
        stack.Children.Add(OdyleLine(loc["Checklist.OdyleBase"], value.Count, baseCap, bars, locked));
        stack.Children.Add(OdyleLine(loc["Checklist.OdyleCharged"], value.Bonus, chargedCap, bars, locked));
        foreach (ChecklistRow r in weeklyRows)
        {
            stack.Children.Add(Row(r, character, weeklyLook, withTopLine: true));
        }

        return Card(stack);
    }

    private const int OdyleChargedMax = 2000;

    private static UIElement OdyleLine(string label, int value, int cap, RowLook look, bool locked = false)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        TextBlock name = Label(label, locked ? "Brush.TextSubtle" : "Brush.Text", 13);
        UIElement numbers = NumberBlock(value.ToString("N0", CultureInfo.CurrentCulture), cap.ToString("N0", CultureInfo.CurrentCulture), value > 0 && !locked, look);
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

    /// <summary>How a group of rows is laid out: with a small bar and a column for the warning, and the widths of the two number cells (all rows of a group share them, so the slashes stand under each other).</summary>
    internal sealed record RowLook(bool Bar, bool WarnColumn, double ValueWidth, double CapWidth);

    /// <summary>The width of a number cell for a text of that many characters (13 px semibold digits).</summary>
    private static double NumberWidth(int characters) => Math.Max(14, characters * 8.1 + 4);

    private static string ValueText(ChecklistRow r) => (r.Estimated ? "~" : "") + r.Count.ToString("N0", CultureInfo.CurrentCulture) + (r.Bonus > 0 ? $" (+{r.Bonus.ToString("N0", CultureInfo.CurrentCulture)})" : "");

    private static RowLook Look(IEnumerable<ChecklistRow> rows, bool bar, bool warnColumn, double minValue = 14, double minCap = 14)
    {
        ChecklistRow[] list = rows.ToArray();
        double value = list.Length == 0 ? minValue : Math.Max(minValue, NumberWidth(list.Max(r => ValueText(r).Length)));
        double cap = list.Length == 0 ? minCap : Math.Max(minCap, NumberWidth(list.Max(r => r.Cap.ToString("N0", CultureInfo.CurrentCulture).Length)));
        return new RowLook(bar, warnColumn, value, cap);
    }

    /// <summary>The number of a row as three cells so the numbers stand one under the other: the count right-aligned, the slash, the maximum left-aligned.</summary>
    private static UIElement NumberBlock(string count, string cap, bool open, RowLook look)
    {
        string brush = open ? "Brush.Accent" : "Brush.TextSubtle";
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(look.ValueWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(look.CapWidth) });
        TextBlock now = Label(count, brush, 13, bold: true);
        now.HorizontalAlignment = HorizontalAlignment.Right;
        TextBlock slash = Label(" / ", "Brush.TextSubtle", 13);
        TextBlock max = Label(cap, brush, 13, bold: true);
        max.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(slash, 1);
        Grid.SetColumn(max, 2);
        grid.Children.Add(now);
        grid.Children.Add(slash);
        grid.Children.Add(max);
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

    /// <summary>The level (and item level) from which a counter exists: shown behind its name, so a lower character knows why it is missing or 0.</summary>
    private static (int Level, int ItemLevel)? Requirement(string key) => key switch
    {
        "ticket:101" => (30, 0),
        "ticket:4" or "ticket:80000001" or "daily:missions" or "weekly:verteron" => (45, 0),
        "weekly:abyss" => (45, 1000),
        _ when key.StartsWith("corridor:", StringComparison.Ordinal) => (45, 1000),
        _ => null,
    };

    private static UIElement RequirementChip((int Level, int ItemLevel) need)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string text = need.ItemLevel > 0
            ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.FromLevelItem"], need.Level, need.ItemLevel.ToString("N0", CultureInfo.CurrentCulture))
            : string.Format(CultureInfo.CurrentCulture, loc["Checklist.FromLevel"], need.Level);
        var chip = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = Label(text, "Brush.TextMuted", 9.5) };
        chip.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return chip;
    }

    /// <summary>One counter row: switch, name (with its level hint), a small bar, where there is one the warning of a refill that would run over the cap or the tick of a finished day, and the number.</summary>
    private UIElement Row(ChecklistRow r, TicketCharacter character, RowLook look, bool withTopLine = false)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        bool open = r.CountsUp ? r.Count < r.Cap : r.Count > 0;
        var grid = new Grid { MinHeight = 27 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int col = 2;
        int barColumn = -1, warnColumn = -1;
        if (look.Bar)
        {
            barColumn = col++;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        }

        if (look.WarnColumn)
        {
            warnColumn = col++;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
        }

        int numberColumn = col;
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        CheckBox pick = Pick(r.Key);
        grid.Children.Add(pick);
        var name = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 6, 2) };
        TextBlock title = Label(r.Label, open ? "Brush.Text" : "Brush.TextSubtle", 13);
        title.TextWrapping = TextWrapping.Wrap;
        name.Children.Add(title);
        if (Requirement(r.Key) is { } need)
        {
            name.Children.Add(RequirementChip(need));
        }

        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        if (barColumn >= 0)
        {
            var bar = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, r.Cap), Value = Math.Min(r.Count, r.Cap), Height = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            bar.SetResourceReference(Control.ForegroundProperty, open ? "Brush.Accent" : "Brush.TextSubtle");
            bar.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
            bar.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
            Grid.SetColumn(bar, barColumn);
            grid.Children.Add(bar);
        }

        if (warnColumn >= 0)
        {
            UIElement? extra = null;
            if (r.Def is { } def && r.Group == ChecklistGroup.Overview)
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
            }

            if (extra is FrameworkElement shown)
            {
                shown.HorizontalAlignment = HorizontalAlignment.Right;
                shown.Margin = new Thickness(0, 0, 8, 0);
                Grid.SetColumn(shown, warnColumn);
                grid.Children.Add(shown);
            }
        }

        UIElement numbers = NumberBlock(r.Estimated ? "~" + r.Count.ToString("N0", CultureInfo.CurrentCulture) + (r.Bonus > 0 ? $" (+{r.Bonus.ToString("N0", CultureInfo.CurrentCulture)})" : "") : ValueText(r), r.Cap.ToString("N0", CultureInfo.CurrentCulture), open, look);
        Grid.SetColumn(numbers, numberColumn);
        grid.Children.Add(numbers);
        if (r.Manual is { } counter)
        {
            var hit = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "+1: left click / -1: right click" };
            Grid.SetColumn(hit, 1);
            Grid.SetColumnSpan(hit, numberColumn);
            hit.MouseLeftButtonDown += (_, e) => { StepManual(character.Name, counter, 1); e.Handled = true; };
            hit.MouseRightButtonDown += (_, e) => { StepManual(character.Name, counter, -1); e.Handled = true; };
            grid.Children.Add(hit);
        }

        if (!withTopLine)
        {
            return grid;
        }

        var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = grid };
        line.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return line;
    }

    /// <summary>A card with two equal halves (so what is on the right stands in one line in every such card), each with its own rows and, where given, a heading.</summary>
    private UIElement SplitCard(string? title, (string? Heading, ChecklistRow[] Rows, RowLook Look) left, (string? Heading, ChecklistRow[] Rows, RowLook Look) right, TicketCharacter character)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int index = 0;
        foreach ((string? heading, ChecklistRow[] rows, RowLook look) in new[] { left, right })
        {
            var half = new StackPanel();
            if (heading is not null)
            {
                TextBlock h = Label(heading, "Brush.Text", 14, bold: true);
                h.Margin = new Thickness(0, 0, 0, 3);
                half.Children.Add(h);
            }

            bool first = true;
            foreach (ChecklistRow r in rows)
            {
                half.Children.Add(Row(r, character, look, withTopLine: !first));
                first = false;
            }

            Grid.SetColumn(half, index * 2);
            grid.Children.Add(half);
            index++;
        }

        var card = new StackPanel();
        if (title is not null)
        {
            TextBlock t = Label(title, "Brush.Text", 14, bold: true);
            t.Margin = new Thickness(0, 0, 0, 4);
            card.Children.Add(t);
        }

        card.Children.Add(grid);
        Border border = Card(card);
        return border;
    }

    /// <summary>What the switches and the signs mean, as short lines: three left, three right.</summary>
    private static UIElement Legend(LocalizationManager loc)
    {
        var grid = new Grid { Margin = new Thickness(4, 2, 4, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var leftColumn = new StackPanel();
        var rightColumn = new StackPanel { Margin = new Thickness(18, 0, 0, 0) };
        Grid.SetColumn(rightColumn, 1);

        var pick = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        var pill = new Border { Width = 22, Height = 11, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Child = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 2, 0) } };
        pill.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
        ((System.Windows.Shapes.Ellipse)pill.Child).SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Brush.Window");
        pick.Children.Add(pill);
        pick.Children.Add(Label(loc["Checklist.PickHint"], "Brush.TextSubtle", 11));
        leftColumn.Children.Add(pick);
        foreach (string key in new[] { "Checklist.LegendNumbers", "Checklist.LegendEstimate" })
        {
            leftColumn.Children.Add(LegendLine(loc[key]));
        }

        foreach (string key in new[] { "Checklist.LegendMissions", "Checklist.LegendPurchases", "Checklist.WarnLegend" })
        {
            rightColumn.Children.Add(LegendLine(loc[key]));
        }

        grid.Children.Add(leftColumn);
        grid.Children.Add(rightColumn);
        return grid;
    }

    private static TextBlock LegendLine(string text)
    {
        TextBlock line = Label(text, "Brush.TextSubtle", 11);
        line.TextWrapping = TextWrapping.Wrap;
        line.Margin = new Thickness(0, 0, 0, 2);
        return line;
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

    /// <summary>A time left as cells so the units stand under each other ("10 d 3 h" over "10 d 13 h"); a text of another shape ("6:14") stands right-aligned on the same edge.</summary>
    private static UIElement TimeCell(string text, string brush)
    {
        var grid = new Grid { Width = 96, HorizontalAlignment = HorizontalAlignment.Right };
        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(text, @"^(\d+)\s(\D+?)\s(\d+)\s(\D+)$");
        if (!m.Success)
        {
            TextBlock raw = Label(text, brush, 13, bold: true);
            raw.HorizontalAlignment = HorizontalAlignment.Right;
            grid.Children.Add(raw);
            return grid;
        }

        foreach (double width in new[] { 22, 22, 22, 30 })
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        }

        for (int i = 0; i < 4; i++)
        {
            TextBlock cell = Label(m.Groups[i + 1].Value, brush, 13, bold: true);
            if (i % 2 == 0)
            {
                cell.HorizontalAlignment = HorizontalAlignment.Right;
            }
            else
            {
                cell.Margin = new Thickness(4, 0, 0, 0);
            }

            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        return grid;
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
        label.TextWrapping = TextWrapping.Wrap;
        label.Margin = new Thickness(0, 0, 6, 0);
        Grid.SetColumn(label, 1);
        var value = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (warnBrush is not null)
        {
            UIElement badge = WarnBadge("", warnBrush);
            ((StackPanel)badge).Margin = new Thickness(0, 0, 6, 0);
            value.Children.Add(badge);
        }

        value.Children.Add(TimeCell(right, rightBrush));
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
    private UIElement? BuildSubscriptions(string title, IEnumerable<TicketDef> defs, TicketCharacter character, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        TicketDef[] times = defs.ToArray();
        DateTime now = DateTime.Now;
        bool hasMember = character.Tickets.TryGetValue(Aion2Tickets.PremiumId, out TicketValue member) && member.TimeMs > 0;
        bool hasLoot = character.Tickets.TryGetValue(Aion2Tickets.AutoLootId, out TicketValue loot);
        if (times.Length == 0 && !hasMember && !hasLoot)
        {
            return null;
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

        return Card(stack);
    }

    /// <summary>The Daeva passes of the character: level (where the cost of the levels is known), experience of the week against the weekly limit, end of the pass.</summary>
    private UIElement? BuildPass(string title, TicketCharacter character, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var found = Aion2Tickets.Passes.Where(p => character.Tickets.ContainsKey(p.TicketId)).ToArray();
        if (found.Length == 0)
        {
            return null;
        }

        var stack = new StackPanel();
        stack.Children.Add(Label(title, "Brush.Text", 14, bold: true));
        ((TextBlock)stack.Children[0]).Margin = new Thickness(0, 0, 0, 2);
        foreach (Aion2Tickets.PassDef pass in found)
        {
            TicketValue value = character.Tickets[pass.TicketId];
            int week = Math.Min(value.Bonus, pass.WeeklyLimit);
            bool full = value.Bonus >= pass.WeeklyLimit;
            var head = new Grid { Margin = new Thickness(0, found[0] == pass ? 4 : 10, 0, 0) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            head.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            CheckBox passPick = Pick("pass:" + pass.TicketId);
            Grid.SetRowSpan(passPick, 2);
            head.Children.Add(passPick);
            TextBlock passName = Label(pass.NameFor(character.ServerId, language), "Brush.Text", 13, bold: true);
            Grid.SetColumn(passName, 1);
            head.Children.Add(passName);
            bool ended = DateTime.Now >= pass.End;
            TextBlock end = Label(string.Format(CultureInfo.CurrentCulture, loc[ended ? "Checklist.PassEnded" : "Checklist.PassEnds"], pass.End.ToString("g", CultureInfo.CurrentCulture)), "Brush.TextSubtle", 11);
            Grid.SetColumn(end, 1);
            Grid.SetRow(end, 1);
            head.Children.Add(end);
            stack.Children.Add(head);

            // the week first (the number that matters), its bar, then what the pass holds in total (the level where the cost of the levels is known)
            TextBlock weekText = Label(string.Format(CultureInfo.CurrentCulture, loc["Checklist.PassWeek"], week.ToString("N0", CultureInfo.CurrentCulture), pass.WeeklyLimit.ToString("N0", CultureInfo.CurrentCulture)), full ? "Brush.Heal" : "Brush.Accent", 14, bold: true);
            weekText.Margin = new Thickness(32, 3, 0, 0);
            stack.Children.Add(weekText);

            var bar = new ProgressBar { Minimum = 0, Maximum = pass.WeeklyLimit, Value = week, Height = 5, Margin = new Thickness(32, 4, 0, 0) };
            bar.SetResourceReference(Control.ForegroundProperty, full ? "Brush.Heal" : "Brush.Accent");
            bar.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
            bar.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
            stack.Children.Add(bar);
            Aion2Tickets.PassLevel? level = Aion2Tickets.PassLevelOf(pass.TicketId, value.Count);
            string levelText = level is { } l
                ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.PassLevel"], l.Level, l.Into.ToString("N0", CultureInfo.CurrentCulture), l.Need.ToString("N0", CultureInfo.CurrentCulture))
                : string.Format(CultureInfo.CurrentCulture, loc["Checklist.PassTotal"], value.Count.ToString("N0", CultureInfo.CurrentCulture));
            TextBlock total = Label(levelText, "Brush.TextSubtle", 12);
            total.Margin = new Thickness(32, 3, 0, 0);
            stack.Children.Add(total);
        }

        return Card(stack);
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
