using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// The local player's Aion 2 character on one screen, opened beside the meter: profile (name,
/// class, level, legion, key numbers), what is worth upgrading, the three Daevanion boards as small
/// maps, the equipment as a table with item-level bars, and the skills with level pips. Three
/// columns so nothing needs a click. Built in code (like the Character panel it replaces) from the
/// data the game sends at login and zone changes - see <see cref="Aion2EntityDirectory"/> - and
/// redrawn whenever a fresh record arrives.
/// </summary>
public sealed class CharacterWindow : Window
{
    private static readonly Color Gold = Color.FromRgb(0xF0, 0xB8, 0x40);
    private static readonly Color Blue = Color.FromRgb(0x4C, 0x8D, 0xFF);
    private static readonly Color Green = Color.FromRgb(0x48, 0xB3, 0x6A);
    private static readonly Color Orange = Color.FromRgb(0xFF, 0x9F, 0x43);
    private static readonly Color Red = Color.FromRgb(0xFF, 0x8F, 0x78);

    private readonly Aion2EntityDirectory _directory;
    private readonly ContentControl _host = new();

    public CharacterWindow(Aion2EntityDirectory directory)
    {
        _directory = directory;
        Title = "Character";
        Width = 1040;
        Height = 740;
        MinWidth = 820;
        MinHeight = 480;
        FontSize = 12.5;
        SetResourceReference(BackgroundProperty, "Brush.Window");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _host };

        _directory.CharacterChanged += OnCharacterChanged;
        Closed += (_, _) => _directory.CharacterChanged -= OnCharacterChanged;
        Render();
    }

    /// <summary>Puts the window to the right of the meter, or to its left when the screen ends
    /// there, level with its top edge.</summary>
    public void PlaceBeside(Window meter)
    {
        Rect area = SystemParameters.WorkArea;
        double left = meter.Left + meter.ActualWidth + 6;
        if (left + Width > area.Right)
        {
            left = Math.Max(area.Left, meter.Left - Width - 6);
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = Math.Max(area.Top, Math.Min(meter.Top, area.Bottom - Height));
    }

    private void OnCharacterChanged(Aion2CharacterInfo info) => Dispatcher.BeginInvoke(new Action(Render));

    // ---------------------------------------------------------------- building blocks

    private Brush Res(string key) => (Brush)FindResource(key);

    private static SolidColorBrush Solid(Color color) => new(color);

    private TextBlock Text(string text, double size = 12.5, FontWeight? weight = null, Brush? brush = null, Thickness? margin = null, TextWrapping wrap = TextWrapping.NoWrap) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight ?? FontWeights.Normal,
        Foreground = brush ?? Res("Brush.Text"),
        Margin = margin ?? new Thickness(0),
        TextWrapping = wrap,
        TextTrimming = wrap == TextWrapping.NoWrap ? TextTrimming.CharacterEllipsis : TextTrimming.None,
    };

    private Border Card(UIElement child, Thickness? padding = null, Thickness? margin = null) => new()
    {
        Background = Res("Brush.Panel"),
        BorderBrush = Res("Brush.Border"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = padding ?? new Thickness(12),
        Margin = margin ?? new Thickness(0),
        Child = child,
    };

    private TextBlock Heading(string text) => Text(text.ToUpperInvariant(), 11, FontWeights.Bold, Solid(Gold), new Thickness(0, 0, 0, 8));

    private static Color HeatColor(double ratio) => ratio >= 0.95 ? Green : ratio >= 0.8 ? Blue : ratio >= 0.65 ? Gold : Red;

    // ---------------------------------------------------------------- the three columns

    private void Render()
    {
        Aion2CharacterInfo? character = _directory.LocalCharacter;
        if (character is null)
        {
            Title = "Character";
            _host.Content = new Border
            {
                Padding = new Thickness(24),
                Child = Text("No character data yet. The game sends it when you log in or change zone - log in once with the meter running and it is kept for the next time.",
                    13, brush: Res("Brush.TextMuted"), wrap: TextWrapping.Wrap),
            };
            return;
        }

        string className = _directory.ClassOf(character.CombatId) ?? Aion2SkillNames.ClassFromCode(character.ClassCode) ?? "?";
        string? guild = _directory.GuildOf(character.CombatId);
        var gear = _directory.LocalEquipment
            .Select(e => (Item: e, Info: Aion2ItemCatalog.Find(e.ItemId)))
            .OrderBy(x => x.Item.SlotIndex)
            .ToList();
        var known = gear.Where(x => x.Info is not null).Select(x => x.Info!).ToList();
        int maxLevel = known.Count > 0 ? known.Max(i => i.ItemLevel) : 0;
        double average = known.Count > 0 ? known.Average(i => i.ItemLevel) : 0;
        var skillNames = Aion2SkillNames.Load();
        var skills = _directory.LocalSkills
            .Where(k => k.SkillId % 10000 == 0)
            .OrderByDescending(k => k.Level)
            .ThenBy(k => skillNames.GetValueOrDefault(k.SkillId, ""))
            .ToList();
        int nodes = _directory.LocalDaevanion.Sum(b => Aion2DaevanionCatalog.Summarize(b.BoardId, b.NodeIds).ActiveNodes);

        Title = $"{character.Name} - Character";

        var layout = new Grid { Margin = new Thickness(14) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 220 });

        var left = new StackPanel();
        left.Children.Add(BuildProfile(character, className, guild, average, skills.Count, nodes));
        left.Children.Add(BuildWeakest(gear.Where(x => x.Info is not null).Select(x => (x.Item, Info: x.Info!)).ToList(), maxLevel));
        left.Children.Add(BuildBoards());
        Grid.SetColumn(left, 0);

        UIElement middle = BuildGear(gear, maxLevel);
        Grid.SetColumn(middle, 2);
        UIElement right = BuildSkills(skills, skillNames);
        Grid.SetColumn(right, 4);

        layout.Children.Add(left);
        layout.Children.Add(middle);
        layout.Children.Add(right);

        var page = new StackPanel();
        page.Children.Add(layout);
        page.Children.Add(Text(
            (character.Restored
                ? $"Saved from your last login ({character.ReceivedAt:yyyy-MM-dd HH:mm}); updated on every relog. "
                : $"As sent by the game at login / zone change ({character.ReceivedAt:HH:mm:ss}). ")
            + "Stones, rolled stats and stigmas are not decoded and not shown.",
            11, brush: Res("Brush.TextMuted"), margin: new Thickness(16, 0, 16, 12), wrap: TextWrapping.Wrap));
        _host.Content = page;
    }

    private UIElement BuildProfile(Aion2CharacterInfo character, string className, string? guild, double average, int skillCount, int nodeCount)
    {
        var stack = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        var badge = new Border
        {
            Width = 50,
            Height = 50,
            CornerRadius = new CornerRadius(9),
            BorderBrush = Solid(Gold),
            BorderThickness = new Thickness(2),
            Background = Res("Brush.Control"),
            Margin = new Thickness(0, 0, 12, 0),
            Child = new TextBlock
            {
                Text = className.Length >= 2 ? className[..2].ToUpperInvariant() : "?",
                FontWeight = FontWeights.ExtraBold,
                FontSize = 18,
                Foreground = Solid(Gold),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Text(character.Name, 21, FontWeights.Bold));
        names.Children.Add(Text($"{className} · Level {character.Level}", 12.5, brush: Res("Brush.TextMuted")));
        head.Children.Add(badge);
        head.Children.Add(names);
        stack.Children.Add(head);
        if (guild is not null)
        {
            stack.Children.Add(Text($"Legion  {guild}", 12.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 10, 0, 0)));
        }

        var numbers = new UniformGrid { Columns = 3, Margin = new Thickness(0, 12, 0, 0) };
        numbers.Children.Add(Number(average.ToString("F1"), "Avg item level", Gold));
        numbers.Children.Add(Number(skillCount.ToString(), "Skills", null));
        numbers.Children.Add(Number(nodeCount.ToString(), "Daevanion nodes", null));
        stack.Children.Add(numbers);
        return Card(stack, margin: new Thickness(0, 0, 0, 12));
    }

    private UIElement Number(string value, string label, Color? color)
    {
        var s = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        s.Children.Add(new TextBlock { Text = value, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = color is { } c ? Solid(c) : Res("Brush.Text"), HorizontalAlignment = HorizontalAlignment.Center });
        s.Children.Add(new TextBlock { Text = label, FontSize = 10, Foreground = Res("Brush.TextMuted"), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        return s;
    }

    private UIElement BuildWeakest(IReadOnlyList<(Aion2EquippedItem Item, Aion2ItemInfo Info)> gear, int maxLevel)
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading("Worth upgrading"));
        foreach (var weak in gear.OrderBy(x => x.Info.ItemLevel).Take(4))
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Text(weak.Info.Name + (weak.Item.Enchant > 0 ? $" +{weak.Item.Enchant}" : "")));
            TextBlock level = Text(weak.Info.ItemLevel.ToString(), 12.5, FontWeights.Bold, Solid(HeatColor(maxLevel == 0 ? 1 : (double)weak.Info.ItemLevel / maxLevel)));
            Grid.SetColumn(level, 1);
            row.Children.Add(level);
            stack.Children.Add(row);
        }

        if (maxLevel > 0)
        {
            stack.Children.Add(Text($"Measured against your best piece (item level {maxLevel}).", 11, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 6, 0, 0), wrap: TextWrapping.Wrap));
        }

        return Card(stack, new Thickness(14, 12, 14, 12), new Thickness(0, 0, 0, 12));
    }

    private UIElement BuildBoards()
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading("Daevanion"));
        var skillNames = Aion2SkillNames.Load();
        foreach (Aion2DaevanionBoard board in _directory.LocalDaevanion)
        {
            var summary = Aion2DaevanionCatalog.Summarize(board.BoardId, board.NodeIds);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            row.Children.Add(BoardMap(board));

            var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 140 };
            text.Children.Add(Text($"{summary.Name} · {summary.ActiveNodes} nodes", 12, FontWeights.SemiBold));
            string bonuses = string.Join(", ", summary.SkillBonuses.Select(kv => $"{skillNames.GetValueOrDefault(kv.Key, kv.Key.ToString())} +{kv.Value}"));
            if (bonuses.Length > 0)
            {
                text.Children.Add(Text(bonuses, 11, brush: Res("Brush.TextMuted"), wrap: TextWrapping.Wrap));
            }

            string stats = string.Join(", ", summary.Stats.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Key} +{kv.Value}"));
            if (stats.Length > 0)
            {
                text.Children.Add(Text(stats, 10.5, brush: Res("Brush.TextMuted"), wrap: TextWrapping.Wrap));
            }

            row.Children.Add(text);
            stack.Children.Add(row);
        }

        if (_directory.LocalDaevanion.Count == 0)
        {
            stack.Children.Add(Text("Not sent yet - it comes with the login.", 11.5, brush: Res("Brush.TextMuted")));
        }
        else
        {
            stack.Children.Add(Text("The maps show the nodes the node table knows (about half of what is active).", 10.5, brush: Res("Brush.TextMuted"), wrap: TextWrapping.Wrap));
        }

        return Card(stack, new Thickness(14, 12, 14, 12));
    }

    /// <summary>A 15 x 15 map of one board: the start node gold, stat nodes blue, skill nodes orange.</summary>
    private FrameworkElement BoardMap(Aion2DaevanionBoard board)
    {
        var cells = new Dictionary<(int Row, int Col), Color>();
        foreach (int id in board.NodeIds)
        {
            if (Aion2DaevanionCatalog.Find(id) is { } node)
            {
                cells[(node.Row, node.Col)] = node.Type switch { "Start" => Gold, "SkillLevel" => Orange, _ => Blue };
            }
        }

        var grid = new Grid { Width = 120, Height = 120, VerticalAlignment = VerticalAlignment.Top };
        for (int i = 0; i < 15; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        for (int r = 1; r <= 15; r++)
        {
            for (int c = 1; c <= 15; c++)
            {
                Brush fill = cells.TryGetValue((r, c), out Color color) ? Solid(color) : Res("Brush.Control");
                var cell = new Border { Background = fill, Margin = new Thickness(0.5), CornerRadius = new CornerRadius(1) };
                Grid.SetRow(cell, r - 1);
                Grid.SetColumn(cell, c - 1);
                grid.Children.Add(cell);
            }
        }

        return grid;
    }

    private UIElement BuildGear(IReadOnlyList<(Aion2EquippedItem Item, Aion2ItemInfo? Info)> gear, int maxLevel)
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading($"Equipment · {gear.Count} slots"));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });

        int row = 0;
        foreach (var entry in gear)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            double ratio = entry.Info is null || maxLevel == 0 ? 0 : (double)entry.Info.ItemLevel / maxLevel;
            Color heat = HeatColor(ratio);

            TextBlock slot = Text(entry.Info?.Slot is { Length: > 0 } s ? s : $"Slot {entry.Item.SlotIndex}", 11, brush: Res("Brush.TextMuted"));
            TextBlock name = Text(entry.Info?.Name ?? $"Item {entry.Item.ItemId}");
            name.VerticalAlignment = VerticalAlignment.Center;
            slot.VerticalAlignment = VerticalAlignment.Center;
            name.Margin = new Thickness(0, 0, 8, 0);

            var track = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = Res("Brush.Control"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var fill = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = Solid(heat), HorizontalAlignment = HorizontalAlignment.Left };
            var fillHost = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Height = 8 };
            fillHost.Children.Add(track);
            fillHost.Children.Add(new Grid
            {
                ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(Math.Max(0.02, ratio), GridUnitType.Star) }, new ColumnDefinition { Width = new GridLength(Math.Max(0.0, 1 - ratio), GridUnitType.Star) } },
                Children = { fill },
            });
            track.Margin = new Thickness(0);

            TextBlock level = Text(entry.Info is null ? "?" : entry.Info.ItemLevel.ToString(), 12.5, FontWeights.Bold);
            level.HorizontalAlignment = HorizontalAlignment.Right;
            level.VerticalAlignment = VerticalAlignment.Center;

            UIElement badge = entry.Item.Enchant > 0
                ? new Border
                {
                    Background = Solid(Gold),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(5, 0, 5, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = $"+{entry.Item.Enchant}", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Solid(Color.FromRgb(0x1B, 0x14, 0x00)) },
                }
                : new Border();

            foreach ((UIElement element, int column) in new (UIElement, int)[] { (slot, 0), (name, 1), (fillHost, 2), (level, 3), (badge, 4) })
            {
                Grid.SetRow(element, row);
                Grid.SetColumn(element, column);
                grid.Children.Add(element);
            }

            row++;
        }

        stack.Children.Add(grid);
        return Card(stack, new Thickness(14, 12, 14, 8));
    }

    private UIElement BuildSkills(IReadOnlyList<Aion2SkillEntry> skills, IReadOnlyDictionary<int, string> names)
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading($"Skills · {skills.Count}"));
        foreach (Aion2SkillEntry skill in skills)
        {
            var row = new Grid { Height = 25 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

            TextBlock name = Text(names.GetValueOrDefault(skill.SkillId, skill.SkillId.ToString()));
            name.VerticalAlignment = VerticalAlignment.Center;
            name.Margin = new Thickness(0, 0, 8, 0);

            var pips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < 12; i++)
            {
                Brush fill = i < skill.BaseLevel ? Solid(Gold) : i < skill.Level ? Solid(Blue) : Res("Brush.Control");
                pips.Children.Add(new Border { Width = 6, Height = 10, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 2, 0), Background = fill });
            }

            TextBlock level = Text(skill.Level.ToString(), 12.5, FontWeights.Bold);
            level.HorizontalAlignment = HorizontalAlignment.Right;
            level.VerticalAlignment = VerticalAlignment.Center;

            Grid.SetColumn(pips, 1);
            Grid.SetColumn(level, 2);
            row.Children.Add(name);
            row.Children.Add(pips);
            row.Children.Add(level);
            stack.Children.Add(row);
        }

        if (skills.Count == 0)
        {
            stack.Children.Add(Text("Not sent yet - it comes with the login.", 11.5, brush: Res("Brush.TextMuted")));
        }
        else
        {
            var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            legend.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), Background = Solid(Gold), Margin = new Thickness(0, 3, 4, 0) });
            legend.Children.Add(Text("trained", 11, brush: Res("Brush.TextMuted")));
            legend.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), Background = Solid(Blue), Margin = new Thickness(12, 3, 4, 0) });
            legend.Children.Add(Text("bonus (Daevanion, gear)", 11, brush: Res("Brush.TextMuted")));
            stack.Children.Add(legend);
        }

        return Card(stack, new Thickness(14, 12, 14, 12));
    }
}
