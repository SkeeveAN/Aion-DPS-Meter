using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AionDPS.Ui;

/// <summary>
/// The design picture of the live meter with two lists side by side (render-tool with AIONDPS_LIVEMOCK=<json of aion2-meter-data>): left everything of all sessions,
/// right the last instance with its bosses one below the other (the end boss on top), under each list the encounter link and two copy buttons (the right one with the
/// Discord mark behind it). Not part of the real page: a proposal to look at, fed with the Draupnir run of a recording.
/// </summary>
internal static class LiveMeterMock
{
    // the Discord mark (a 24 x 24 box, public-domain path), for the background of the second copy button
    private const string DiscordMark = "M 20.317 4.3698 a 19.7913 19.7913 0 0 0 -4.8851 -1.5152 a .0741 .0741 0 0 0 -.0785 .0371 c -.211 .3753 -.4447 .8648 -.6083 1.2495 c -1.8447 -.2762 -3.68 -.2762 -5.4868 0 c -.1636 -.3933 -.4058 -.8742 -.6177 -1.2495 a .077 .077 0 0 0 -.0785 -.037 a 19.7363 19.7363 0 0 0 -4.8852 1.515 a .0699 .0699 0 0 0 -.0321 .0277 C .5334 9.0458 -.319 13.5799 .0992 18.0578 a .0824 .0824 0 0 0 .0312 .0561 c 2.0528 1.5076 4.0413 2.4228 5.9929 3.0294 a .0777 .0777 0 0 0 .0842 -.0276 c .4616 -.6304 .8731 -1.2952 1.226 -1.9942 a .076 .076 0 0 0 -.0416 -.1057 c -.6528 -.2476 -1.2743 -.5495 -1.8722 -.8923 a .077 .077 0 0 1 -.0076 -.1277 c .1258 -.0943 .2517 -.1923 .3718 -.2914 a .0743 .0743 0 0 1 .0776 -.0105 c 3.9278 1.7933 8.18 1.7933 12.0614 0 a .0739 .0739 0 0 1 .0785 .0095 c .1202 .099 .246 .1981 .3728 .2924 a .077 .077 0 0 1 -.0066 .1276 a 12.2986 12.2986 0 0 1 -1.873 .8914 a .0766 .0766 0 0 0 -.0407 .1067 c .3604 .698 .7719 1.3628 1.225 1.9932 a .076 .076 0 0 0 .0842 .0286 c 1.961 -.6067 3.9495 -1.5219 6.0023 -3.0294 a .077 .077 0 0 0 .0313 -.0552 c .5004 -5.177 -.8382 -9.6739 -3.5485 -13.6604 a .061 .061 0 0 0 -.0312 -.0286 z M 8.02 15.3312 c -1.1825 0 -2.1569 -1.0857 -2.1569 -2.419 c 0 -1.3332 .9555 -2.4189 2.157 -2.4189 c 1.2108 0 2.1757 1.0952 2.1568 2.419 c 0 1.3332 -.9555 2.4189 -2.1569 2.4189 z m 7.9748 0 c -1.1825 0 -2.1569 -1.0857 -2.1569 -2.419 c 0 -1.3332 .9554 -2.4189 2.1569 -2.4189 c 1.2108 0 2.1757 1.0952 2.1568 2.419 c 0 1.3332 -.946 2.4189 -2.1568 2.4189 Z";

    // AIONDPS_MOCKVARIANT=base: the first picture's basic look (no legend line, no end boss tag) with one link under the whole right list; otherwise a link under every boss
    private static readonly bool Base = Environment.GetEnvironmentVariable("AIONDPS_MOCKVARIANT") == "base";

    public static ToolPage Build(string jsonPath)
    {
        var loc = LocalizationManager.Instance;
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        JsonElement root = doc.RootElement;

        // the title and the mode chips as the live meter page has them
        var title = new TextBlock { Text = loc["Hub.LiveMeter"], FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        var head = new DockPanel { Margin = new Thickness(14, 12, 14, 0) };
        head.Children.Add(title);
        var chips = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 10, 14, 0) };
        string[] names = { loc["Main.ModeDamage"], loc["Main.ModeHeal"], loc["Main.ModeTaken"], loc["Main.ModeAll"], loc["Main.ModeAllCompact"] };
        for (int i = 0; i < names.Length; i++)
        {
            var chipText = new TextBlock { FontWeight = i == 0 ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
            var chipRow = new StackPanel { Orientation = Orientation.Horizontal };
            var chipSymbol = new TextBlock { Text = LiveMeterPage.ModeSymbols[i], FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 13, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            chipText.Text = names[i];
            chipRow.Children.Add(chipSymbol);
            chipRow.Children.Add(chipText);
            string[] chipTips =
            {
                "Schaden der Gruppe: wer wie viel Schaden gemacht hat (DPS, Gesamtschaden, Anteil)",
                "Heilung: wer wie viel geheilt hat (HPS, Gesamtheilung, Anteil)",
                "Erlittener Schaden: was jeder Spieler eingesteckt hat, mit Anzahl der Tode",
                "All in One: Schaden, Heilung und erlittener Schaden in einer Zeile",
                "All in One +: wie All in One, mit Aufschlüsselung nach Fertigkeiten",
            };
            var chip = new Border { Child = chipRow, ToolTip = chipTips[i], CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(1), Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
            chipText.SetResourceReference(TextBlock.ForegroundProperty, i == 0 ? "Brush.Accent" : "Brush.TextMuted");
            chipSymbol.SetResourceReference(TextBlock.ForegroundProperty, i == 0 ? "Brush.Accent" : "Brush.TextMuted");
            chip.SetResourceReference(Border.BorderBrushProperty, i == 0 ? "Brush.Accent" : "Brush.Border");
            chips.Children.Add(chip);
        }

        // left: all sessions
        var allRows = RowsOf(root.GetProperty("all"), 10);
        UIElement left = Card("Alle Sitzungen", "Draupnir · 10:42 - 11:00", Base ? "" : "DPS  ·  Schaden  ·  Anteil", new ItemsControl { ItemTemplate = RowTemplate(), ItemsSource = allRows });

        // right: the last instance, the end boss on top
        var bosses = new StackPanel();
        int boss = 0;
        foreach (JsonElement b in root.GetProperty("bosses").EnumerateArray())
        {
            bosses.Children.Add(BossSection(b, boss++));
        }

        if (Base)
        {
            bosses.Children.Add(LinkRow("https://aiondps.com/encounters/48219"));
        }

        UIElement right = Card("Letzte Instanz", "Draupnir · " + root.GetProperty("bosses").GetArrayLength() + " " + "Bosse", Base ? "" : "iDPS  ·  Schaden  ·  Anteil", bosses);

        var columns = new Grid { Margin = new Thickness(14, 10, 14, 14) };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 2);
        columns.Children.Add(left);
        columns.Children.Add(right);

        var page = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        page.Children.Add(head);
        DockPanel.SetDock(chips, Dock.Top);
        page.Children.Add(chips);
        page.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = columns });
        return new ToolPage { Content = page };
    }

    private static DataTemplate RowTemplate() => (DataTemplate)XamlReader.Parse(LiveMeterPage.RowXaml);

    private static ObservableCollection<PlayerRow> RowsOf(JsonElement rows, int max)
    {
        var list = new ObservableCollection<PlayerRow>();
        double top = rows.GetArrayLength() > 0 ? rows[0].GetProperty("damage").GetDouble() : 1;
        int rank = 1;
        foreach (JsonElement r in rows.EnumerateArray().Take(max))
        {
            long damage = r.GetProperty("damage").GetInt64();
            list.Add(new PlayerRow(-rank)
            {
                Name = r.GetProperty("name").GetString() ?? "?", ClassName = r.GetProperty("cls").GetString() ?? "", Faction = r.TryGetProperty("faction", out var faction) ? faction.GetString() ?? "" : "", Damage = damage, Dps = r.GetProperty("dps").GetDouble(),
                FillPercent = 100.0 * damage / top, SharePercent = r.GetProperty("share").GetDouble(), Rank = rank++,
            });
        }

        return list;
    }

    private static UIElement BossSection(JsonElement b, int index)
    {
        var loc = LocalizationManager.Instance;
        bool killed = b.GetProperty("killed").GetBoolean();
        int seconds = (int)Math.Round(b.GetProperty("seconds").GetDouble());
        int attempts = b.GetProperty("attempts").GetInt32();
        string status = (killed ? "Besiegt" : "Wipe") + " · " + (attempts > 1 ? "Versuch" + " " + attempts + " · " : "")
            + $"{seconds / 60}:{seconds % 60:00} · " + (b.GetProperty("total").GetInt64() / 1_000_000.0).ToString("0.0") + " " + "Mio.";
        var name = new TextBlock { Text = (index == 0 ? "\u2605 " : "") + (b.GetProperty("name").GetString() ?? "?") + (index == 0 && !Base ? "  \u00B7  Endboss" : ""), FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, index == 0 ? "Brush.Accent" : "Brush.Text");
        var state = new TextBlock { Text = status, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        state.SetResourceReference(TextBlock.ForegroundProperty, killed ? "Brush.Heal" : "Brush.Danger");
        var header = new DockPanel { Margin = new Thickness(0, index == 0 ? 0 : 14, 0, 4) };
        DockPanel.SetDock(state, Dock.Right);
        header.Children.Add(state);
        header.Children.Add(name);
        var rule = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 2) };
        rule.SetResourceReference(Border.BackgroundProperty, "Brush.Border");
        var section = new StackPanel();
        section.Children.Add(header);
        section.Children.Add(rule);
        section.Children.Add(new ItemsControl { ItemTemplate = RowTemplate(), ItemsSource = RowsOf(b.GetProperty("rows"), 6) });
        if (!Base)
        {
            section.Children.Add(LinkRow("https://aiondps.com/encounters/" + (48219 - index)));
        }

        return section;
    }

    /// <summary>A list in a card: its title, a muted line under it, the list, and below it the encounter link with the two copy buttons.</summary>
    private static UIElement Card(string heading, string sub, string legend, UIElement body)
    {
        var title = new TextBlock { Text = heading, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        var subtitle = new TextBlock { Text = sub, FontSize = 12, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        var legendText = new TextBlock { Text = legend, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        legendText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
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
        var card = new Border { Padding = new Thickness(16, 12, 16, 14), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top, Child = dock };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    /// <summary>
    /// Under a boss meter: the encounter's link (when the automatic upload is on) in a field with its own copy button at the right end of the field (copies the link),
    /// then a copy button for the chat text (DPS, HPS and so on as plain text), then the copy button with the Discord mark (the table for Discord).
    /// </summary>
    private static UIElement LinkRow(string link)
    {
        var box = new TextBox { Text = link, IsReadOnly = true, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 4, 40, 4), Height = 32, FontSize = 12.5 };
        box.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
        box.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        box.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        // the link's own copy button sits inside the field, at its right end
        var inner = CopyButton(false, 28, 24);
        inner.ToolTip = "Encounter-Link kopieren";
        inner.Margin = new Thickness(0, 0, 4, 0);
        inner.HorizontalAlignment = HorizontalAlignment.Right;
        inner.VerticalAlignment = VerticalAlignment.Center;
        inner.BorderThickness = new Thickness(0);
        inner.Background = Brushes.Transparent;
        var field = new Grid();
        field.Children.Add(box);
        field.Children.Add(inner);
        var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = CopyButton(false);
        text.ToolTip = "DPS / HPS und mehr als Text für den Chat kopieren";
        var discord = CopyButton(true);
        discord.ToolTip = "Tabelle für Discord kopieren (mit dem Link)";
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
