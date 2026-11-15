using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// The right list of the live meter page: the last instance with its boss fights one below the other (the newest on top), each with its players and, under it, the link of
/// the fight (once the upload answered) in a field with its own copy button, a copy button for the chat text and one with the Discord mark for the table.
/// </summary>
internal static class LiveInstanceView
{
    // the Discord mark (a 24 x 24 box, public-domain path), for the background of the third copy button
    private const string DiscordMark = "M 20.317 4.3698 a 19.7913 19.7913 0 0 0 -4.8851 -1.5152 a .0741 .0741 0 0 0 -.0785 .0371 c -.211 .3753 -.4447 .8648 -.6083 1.2495 c -1.8447 -.2762 -3.68 -.2762 -5.4868 0 c -.1636 -.3933 -.4058 -.8742 -.6177 -1.2495 a .077 .077 0 0 0 -.0785 -.037 a 19.7363 19.7363 0 0 0 -4.8852 1.515 a .0699 .0699 0 0 0 -.0321 .0277 C .5334 9.0458 -.319 13.5799 .0992 18.0578 a .0824 .0824 0 0 0 .0312 .0561 c 2.0528 1.5076 4.0413 2.4228 5.9929 3.0294 a .0777 .0777 0 0 0 .0842 -.0276 c .4616 -.6304 .8731 -1.2952 1.226 -1.9942 a .076 .076 0 0 0 -.0416 -.1057 c -.6528 -.2476 -1.2743 -.5495 -1.8722 -.8923 a .077 .077 0 0 1 -.0076 -.1277 c .1258 -.0943 .2517 -.1923 .3718 -.2914 a .0743 .0743 0 0 1 .0776 -.0105 c 3.9278 1.7933 8.18 1.7933 12.0614 0 a .0739 .0739 0 0 1 .0785 .0095 c .1202 .099 .246 .1981 .3728 .2924 a .077 .077 0 0 1 -.0066 .1276 a 12.2986 12.2986 0 0 1 -1.873 .8914 a .0766 .0766 0 0 0 -.0407 .1067 c .3604 .698 .7719 1.3628 1.225 1.9932 a .076 .076 0 0 0 .0842 .0286 c 1.961 -.6067 3.9495 -1.5219 6.0023 -3.0294 a .077 .077 0 0 0 .0313 -.0552 c .5004 -5.177 -.8382 -9.6739 -3.5485 -13.6604 a .061 .061 0 0 0 -.0312 -.0286 z M 8.02 15.3312 c -1.1825 0 -2.1569 -1.0857 -2.1569 -2.419 c 0 -1.3332 .9555 -2.4189 2.157 -2.4189 c 1.2108 0 2.1757 1.0952 2.1568 2.419 c 0 1.3332 -.9555 2.4189 -2.1569 2.4189 z m 7.9748 0 c -1.1825 0 -2.1569 -1.0857 -2.1569 -2.419 c 0 -1.3332 .9554 -2.4189 2.1569 -2.4189 c 1.2108 0 2.1757 1.0952 2.1568 2.419 c 0 1.3332 -.946 2.4189 -2.1568 2.4189 Z";

    private static DataTemplate RowTemplate() => (DataTemplate)XamlReader.Parse(LiveMeterPage.RowXaml);

    private static TextBlock Text(string text, double size, string brush, FontWeight? weight = null)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    /// <summary>The card "Last instance": it draws itself again whenever the store changes.</summary>
    public static Border Card(LastInstanceStore last, Action<BossSnapshot, int>? copy)
    {
        var loc = LocalizationManager.Instance;
        var host = new Border();
        void Fill()
        {
            var body = new StackPanel();
            if (last.Bosses.Count == 0)
            {
                var none = Text(loc["Hub.Live.NoBoss"], 12.5, "Brush.TextMuted");
                none.TextWrapping = TextWrapping.Wrap;
                none.Margin = new Thickness(0, 6, 0, 6);
                body.Children.Add(none);
            }

            for (int i = 0; i < last.Bosses.Count; i++)
            {
                body.Children.Add(BossSection(last.Bosses[i], i, last.Bosses.Count, copy));
            }

            string sub = last.Bosses.Count == 0 ? "" : (last.Title.Length > 0 ? last.Title + " · " : "") + string.Format(loc["Hub.Live.Bosses"], last.Bosses.Count);
            host.Child = Layout(loc["Hub.Live.LastInstance"], sub, loc["Hub.Live.Legend"], body);
        }

        Fill();
        host.Loaded += (_, _) =>
        {
            last.Changed += Fill;
            Fill();
        };
        host.Unloaded += (_, _) => last.Changed -= Fill;
        return host;
    }

    /// <summary>A list in a card: its title, a muted line beside it, the legend of the columns, then the list.</summary>
    public static UIElement Layout(string heading, string sub, string legend, UIElement body)
    {
        var title = Text(heading, 15, "Brush.Text", FontWeights.SemiBold);
        var subtitle = Text(sub, 12, "Brush.TextMuted");
        subtitle.Margin = new Thickness(10, 0, 0, 0);
        var legendText = Text(legend, 11, "Brush.TextMuted");
        legendText.HorizontalAlignment = HorizontalAlignment.Right;
        var titles = new StackPanel { Orientation = Orientation.Horizontal };
        titles.Children.Add(title);
        titles.Children.Add(subtitle);
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(legendText, Dock.Right);
        top.Children.Add(legendText);
        top.Children.Add(titles);
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(body);
        return dock;
    }

    private static UIElement BossSection(BossSnapshot boss, int index, int count, Action<BossSnapshot, int>? copy)
    {
        var loc = LocalizationManager.Instance;
        int seconds = (int)Math.Round(boss.Seconds);
        string state = boss.Killed ? loc["Hub.Live.Killed"] : boss.Running ? loc["Hub.Live.Running"] : boss.Wiped ? loc["Hub.Live.Wipe"] : loc["Hub.Live.NotKilled"];
        string status = $"{state} · {seconds / 60}:{seconds % 60:00} · {boss.Total / 1_000_000.0:0.0} M";
        bool endBoss = index == 0 && boss.Killed && count > 1;
        var name = Text((endBoss ? "\u2605 " : "") + boss.Name + (endBoss ? "  \u00B7  " + loc["Hub.Live.EndBoss"] : ""), 14, endBoss ? "Brush.Accent" : "Brush.Text", FontWeights.SemiBold);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var stateText = Text(status, 12, boss.Killed ? "Brush.Heal" : boss.Running ? "Brush.Accent" : "Brush.Danger");
        stateText.HorizontalAlignment = HorizontalAlignment.Right;
        var header = new DockPanel { Margin = new Thickness(0, index == 0 ? 0 : 14, 0, 4) };
        DockPanel.SetDock(stateText, Dock.Right);
        header.Children.Add(stateText);
        header.Children.Add(name);
        var rule = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 2) };
        rule.SetResourceReference(Border.BackgroundProperty, "Brush.Border");
        var section = new StackPanel();
        section.Children.Add(header);
        section.Children.Add(rule);
        section.Children.Add(new ItemsControl { ItemTemplate = RowTemplate(), ItemsSource = boss.Rows.Take(10).ToList() });
        section.Children.Add(LinkRow(boss, copy));
        return section;
    }

    /// <summary>
    /// Under a boss: the link of the fight in a field with its own copy button at the right end of the field (copies the link), then a copy button for the chat text
    /// (DPS and so on as plain text), then the copy button with the Discord mark (the table for Discord, with the link).
    /// </summary>
    private static UIElement LinkRow(BossSnapshot boss, Action<BossSnapshot, int>? copy)
    {
        var loc = LocalizationManager.Instance;
        bool has = boss.Link.Length > 0;
        var box = new TextBox { Text = has ? boss.Link : loc["Hub.Live.NoLink"], IsReadOnly = true, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 4, 40, 4), Height = 32, FontSize = 12.5 };
        box.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
        box.SetResourceReference(Control.ForegroundProperty, has ? "Brush.Text" : "Brush.TextMuted");
        box.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        // the link's own copy button sits inside the field, at its right end
        var inner = CopyButton(false, 28, 24);
        inner.ToolTip = loc["Hub.Live.LinkTip"];
        inner.Margin = new Thickness(0, 0, 4, 0);
        inner.HorizontalAlignment = HorizontalAlignment.Right;
        inner.VerticalAlignment = VerticalAlignment.Center;
        inner.BorderThickness = new Thickness(0);
        inner.Background = Brushes.Transparent;
        inner.Opacity = has ? 1 : 0.4;
        var field = new Grid();
        field.Children.Add(box);
        field.Children.Add(inner);
        var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = CopyButton(false);
        text.ToolTip = loc["Hub.Live.TextTip"];
        var discord = CopyButton(true);
        discord.ToolTip = loc["Hub.Live.DiscordTip"];
        inner.MouseLeftButtonUp += (_, _) => copy?.Invoke(boss, 0);
        text.MouseLeftButtonUp += (_, _) => copy?.Invoke(boss, 1);
        discord.MouseLeftButtonUp += (_, _) => copy?.Invoke(boss, 2);
        Grid.SetColumn(field, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(discord, 2);
        row.Children.Add(field);
        row.Children.Add(text);
        row.Children.Add(discord);
        return row;
    }

    /// <summary>The copy button: the usual two overlapping sheets; the second one carries the Discord mark in its background.</summary>
    private static Border CopyButton(bool withDiscord, double width = 34, double height = 32)
    {
        var grid = new Grid { Width = width, Height = height };
        if (withDiscord)
        {
            var mark = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(DiscordMark), Fill = new SolidColorBrush(Color.FromRgb(0x58, 0x65, 0xF2)), Opacity = 0.7, Stretch = Stretch.Uniform, Width = 24, Height = 24,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            grid.Children.Add(mark);
        }

        // one copy symbol for all three buttons (two sheets); on the Discord mark it is drawn thicker and with a dark halo (no fill: the mark shows through), so that it stands out
        var sheets = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 6,3 H 13 A 1.5,1.5 0 0 1 14.5,4.5 V 11 M 3,6 H 10 A 1.5,1.5 0 0 1 11.5,7.5 V 14 A 1.5,1.5 0 0 1 10,15.5 H 4.5 A 1.5,1.5 0 0 1 3,14 Z"),
            StrokeThickness = withDiscord ? 2.0 : 1.4, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = 18, Height = 18, Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 2, 0, 0),
        };
        if (withDiscord)
        {
            sheets.Stroke = Brushes.White;
            sheets.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.95 };
        }
        else
        {
            sheets.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Brush.Text");
        }

        grid.Children.Add(sheets);

        var button = new Border { Child = grid, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Margin = new Thickness(8, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand };
        button.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        button.SetResourceReference(Border.BorderBrushProperty, withDiscord ? "Brush.Accent" : "Brush.Border");
        return button;
    }
}
