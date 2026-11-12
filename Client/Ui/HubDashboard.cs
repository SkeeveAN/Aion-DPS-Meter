using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Schedule;

namespace AionDPS.Ui;

/// <summary>
/// The overview page of the new interface (Dashboard tab): the game and character, what is on in the game now and soon, the world bosses and the stored fights. Cards on
/// real data, drawn again every second.
/// </summary>
public static class HubDashboard
{
    public static ToolPage Build(Func<HubStatus> status, Action<string> openTool)
    {
        var loc = LocalizationManager.Instance;
        var wrap = new WrapPanel { Margin = new Thickness(8, 8, 8, 20) };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void Refresh()
        {
            wrap.Children.Clear();
            DateTime now = DateTime.Now;
            var (active, soon) = EventSchedule.Evaluate(EventSchedule.Events, now, TimeSpan.FromHours(3));
            wrap.Children.Add(Card(loc["Hub.Dash.Connection"], Connection(status(), loc), 420));
            wrap.Children.Add(Card(loc["Hub.Dash.Soon"], Events(active, soon, now, loc), 520));
            wrap.Children.Add(Card(loc["Settings.Nav.Boss"], Bosses(now, loc), 520, () => openTool("boss")));
            wrap.Children.Add(Card(loc["Hub.Dash.Tools"], Shortcuts(openTool, loc), 420));
        }

        Refresh();
        timer.Tick += (_, _) => Refresh();
        timer.Start();
        return new ToolPage
        {
            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = wrap },
            Cleanup = timer.Stop,
        };
    }

    private static Border Card(string title, UIElement body, double width, Action? open = null)
    {
        var stack = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var caption = new TextBlock { Text = title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        if (open is not null)
        {
            var link = new TextBlock { Text = "↗", Cursor = System.Windows.Input.Cursors.Hand, FontSize = 14 };
            link.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            link.MouseLeftButtonUp += (_, _) => open();
            DockPanel.SetDock(link, Dock.Right);
            head.Children.Add(link);
        }

        head.Children.Add(caption);
        stack.Children.Add(head);
        stack.Children.Add(body);
        var card = new Border { Width = width, Margin = new Thickness(8), Padding = new Thickness(16, 12, 16, 14), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top, Child = stack };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    private static TextBlock Text(string text, bool bold = false, string brush = "Brush.Text", double size = 13)
    {
        var block = new TextBlock { Text = text, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private static Grid Line(string left, string right, bool strong = false, string rightBrush = "Brush.Accent")
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var l = Text(left, strong);
        var r = Text(right, true, rightBrush);
        r.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(r, 1);
        grid.Children.Add(l);
        grid.Children.Add(r);
        return grid;
    }

    private static UIElement Connection(HubStatus status, LocalizationManager loc)
    {
        var stack = new StackPanel();
        stack.Children.Add(Line(loc["Hub.Dash.Game"], status.GameOn ? loc["Hub.GameOn"] : loc["Hub.GameOff"], false, status.GameOn ? "Brush.Heal" : "Brush.TextMuted"));
        stack.Children.Add(Line(loc["Hub.Dash.Character"], status.CharacterName ?? loc["Hub.NoCharacter"]));
        if (status.ClassName is { Length: > 0 })
        {
            stack.Children.Add(Line(loc["Hub.Dash.Class"], status.ClassName));
        }

        if (status.Server is { Length: > 0 })
        {
            stack.Children.Add(Line(loc["Hub.Dash.Server"], status.Server));
        }

        return stack;
    }

    private static UIElement Events(List<EventOccurrence> active, List<EventOccurrence> soon, DateTime now, LocalizationManager loc)
    {
        var stack = new StackPanel();
        string language = loc.Language;
        foreach (EventOccurrence o in active.Where(a => !a.IsAlways).Take(4))
        {
            stack.Children.Add(Line(o.Event.NameIn(language), string.Format(loc["Hub.Dash.Until"], o.End.ToString("HH:mm")), true, "Brush.Heal"));
        }

        foreach (EventOccurrence o in soon.Take(6))
        {
            stack.Children.Add(Line(o.Event.NameIn(language), WorldBossText.Duration(o.Start - now) + "  ·  " + o.Start.ToString("HH:mm")));
        }

        if (stack.Children.Count == 0)
        {
            stack.Children.Add(Text(loc["Hub.Dash.NothingSoon"], false, "Brush.TextMuted"));
        }

        return stack;
    }

    private static UIElement Bosses(DateTime now, LocalizationManager loc)
    {
        var stack = new StackPanel();
        string language = loc.Language;
        var rows = new List<(string Name, string Text, bool Now, DateTime At)>();
        foreach (var map in Aion2FieldBosses.Maps)
        {
            foreach (var boss in map.Bosses)
            {
                BossStatus st = Aion2FieldBosses.StatusOf(boss, now);
                if (st.Phase == BossPhase.Unknown)
                {
                    continue;
                }

                rows.Add((Aion2FieldBosses.DisplayName(map, boss, language), WorldBossText.Of(st, now), st.Phase == BossPhase.Now, st.At ?? DateTime.MaxValue));
            }
        }

        foreach (var row in rows.OrderBy(r => r.Now ? 0 : 1).ThenBy(r => r.At).Take(7))
        {
            stack.Children.Add(Line(row.Name, row.Text, row.Now, row.Now ? "Brush.Heal" : "Brush.Accent"));
        }

        if (rows.Count == 0)
        {
            stack.Children.Add(Text(loc["Boss.OverlayEmpty"], false, "Brush.TextMuted"));
        }

        return stack;
    }

    private static UIElement Shortcuts(Action<string> openTool, LocalizationManager loc)
    {
        var panel = new WrapPanel();
        foreach (var (id, key) in new[] { ("checklist", "Main.MenuTools.Checklist"), ("craft", "Main.MenuTools.Craft"), ("petmap", "Settings.Nav.PetMap"), ("timetable", "Settings.Nav.Timetable"), ("loot", "Main.MenuTools.Loot"), ("boss", "Settings.Nav.Boss") })
        {
            var button = new Button { Content = loc[key], Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 4, 12, 4) };
            string tool = id;
            button.Click += (_, _) => openTool(tool);
            panel.Children.Add(button);
        }

        return panel;
    }
}
