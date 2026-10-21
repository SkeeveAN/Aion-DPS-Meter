using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// The page of the crafting tool: every recipe the player marked as a card with its percent and the materials that are still missing (the raw materials, an ingot
/// or a dust already broken down into its ore), set against the stock the data stream reported. Meant to stay open on a second screen; the overlay shows the same
/// as a list. Which recipes are marked, and how many of each, is set in the configuration window (the button of the header).
/// </summary>
public sealed class CraftPage : StackPanel
{
    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly Button _tabMissing = new();
    private readonly Button _tabAll = new();
    private readonly TextBlock _stock = new() { Margin = new Thickness(0, 0, 0, 8), FontSize = 12 };
    private readonly StackPanel _cards = new();
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private bool _showAll;
    private string _shown = "";

    /// <summary>The two switch buttons "only what is missing / everything", for the header's left side.</summary>
    public UIElement ModeBar { get; }

    public CraftPage(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        _showAll = MeterSettings.Load().CraftShowAll;
        _tabMissing.Click += (_, _) => SetMode(false);
        _tabAll.Click += (_, _) => SetMode(true);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(_tabMissing);
        bar.Children.Add(_tabAll);
        ModeBar = bar;
        StyleTabs();

        _stock.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        Children.Add(_stock);
        Children.Add(_cards);
        MinHeight = 200;
        SetResourceReference(TextElement.ForegroundProperty, "Brush.Text");

        _refresh.Tick += (_, _) => Refresh();
        Loaded += (_, _) => _refresh.Start();
        Unloaded += (_, _) => _refresh.Stop();
        Refresh();
    }

    public void Stop() => _refresh.Stop();

    private void SetMode(bool all)
    {
        _showAll = all;
        MeterSettings s = MeterSettings.Load();
        s.CraftShowAll = all;
        s.Save();
        StyleTabs();
        _shown = "";
        Refresh();
    }

    private void StyleTabs()
    {
        var loc = LocalizationManager.Instance;
        StyleTab(_tabMissing, loc["Craft.OnlyMissing"], !_showAll);
        StyleTab(_tabAll, loc["Craft.Everything"], _showAll);
    }

    /// <summary>One of the two switch buttons; as high as the hotkey chip next to them.</summary>
    private static void StyleTab(Button button, string text, bool active)
    {
        button.Content = text;
        button.Padding = new Thickness(14, 2, 14, 2);
        button.MinHeight = 26;
        button.Cursor = System.Windows.Input.Cursors.Hand;
        button.BorderThickness = new Thickness(1);
        button.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        if (active)
        {
            button.SetResourceReference(Control.BackgroundProperty, "Brush.Accent");
            button.Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x00));
        }
        else
        {
            button.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
            button.SetResourceReference(Control.ForegroundProperty, "Brush.TextSubtle");
        }

        button.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
    }

    private void Refresh()
    {
        var loc = LocalizationManager.Instance;
        MeterSettings settings = MeterSettings.Load();
        Aion2EntityDirectory? directory = _directory();
        List<CraftResult> results = CraftText.Results(directory, settings);
        string note = CraftText.StockNote();
        string key = note + "|" + _showAll + "|" + loc.Language + "|" + string.Join(";", results.Select(r =>
            r.Recipe.Id + "x" + r.Count + ":" + string.Join(",", r.Materials.Select(m => m.Item + "=" + m.Have + "/" + m.Needed)) + "+" + string.Join(",", r.Intermediates.Select(i => i.Item + "*" + i.Crafts))));
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        _stock.Text = note;
        _cards.Children.Clear();
        if (results.Count == 0)
        {
            var empty = new TextBlock { Text = loc["Craft.Empty"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            _cards.Children.Add(empty);
            return;
        }

        foreach (CraftResult result in results)
        {
            _cards.Children.Add(Card(result));
        }
    }

    private Border Card(CraftResult result)
    {
        var loc = LocalizationManager.Instance;
        var card = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 8) };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var stack = new StackPanel();
        card.Child = stack;

        int product = result.Recipe.Product;
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var icon = CraftText.IconTile(product, 26);
        icon.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(icon, Dock.Left);
        head.Children.Add(icon);
        var bar = Bar(result.Percent);
        DockPanel.SetDock(bar, Dock.Right);
        head.Children.Add(bar);
        var percent = new TextBlock { Text = result.Percent + " %", FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        percent.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        if (result.Percent >= 100)
        {
            percent.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Heal");
        }

        DockPanel.SetDock(percent, Dock.Right);
        head.Children.Add(percent);
        head.Children.Add(new TextBlock
        {
            Text = $"{result.Count}× {CraftText.RecipeName(result.Recipe)}", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(CraftText.GradeColor(product)), TextTrimming = TextTrimming.CharacterEllipsis,
        });
        stack.Children.Add(head);

        if (result.Intermediates.Count > 0)
        {
            var made = new TextBlock
            {
                Text = loc["Craft.AlsoMake"] + " " + string.Join(" · ", result.Intermediates.Select(i => $"{i.Crafts * (Aion2CraftData.ProducerOf(i.Item, result.Recipe.Elyos)?.Count ?? 1):N0}× {CraftText.ItemName(i.Item)}")),
                FontSize = 12, Margin = new Thickness(36, 0, 0, 3), TextWrapping = TextWrapping.Wrap,
            };
            made.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            stack.Children.Add(made);
        }

        var lines = result.Materials.Where(m => _showAll || m.Missing > 0).OrderByDescending(m => m.Missing).ThenBy(m => CraftText.ItemName(m.Item), StringComparer.CurrentCultureIgnoreCase).ToList();
        if (lines.Count == 0)
        {
            var done = new TextBlock { Text = loc["Craft.AllThere"], Margin = new Thickness(36, 2, 0, 2) };
            done.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Heal");
            stack.Children.Add(done);
        }

        foreach (CraftLine line in lines)
        {
            stack.Children.Add(Line(line));
        }

        return card;
    }

    private static Grid Line(CraftLine line)
    {
        var grid = new Grid { Margin = new Thickness(36, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = CraftText.IconTile(line.Item, 24);
        icon.Margin = new Thickness(0, 0, 8, 0);
        grid.Children.Add(icon);
        var name = new TextBlock { Text = CraftText.ItemName(line.Item), Foreground = new SolidColorBrush(CraftText.GradeColor(line.Item)), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        var where = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (string place in CraftText.Whereabouts(line))
        {
            var chip = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Padding = new Thickness(7, 0, 7, 1), Margin = new Thickness(6, 0, 0, 0) };
            chip.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
            var text = new TextBlock { Text = place, FontSize = 11 };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            chip.Child = text;
            where.Children.Add(chip);
        }

        Grid.SetColumn(where, 2);
        grid.Children.Add(where);
        var count = new TextBlock { Text = CraftText.HaveOfNeed(line), MinWidth = 74, TextAlignment = TextAlignment.Right, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        count.SetResourceReference(TextBlock.ForegroundProperty, line.Missing == 0 ? "Brush.Heal" : "Brush.TextMuted");
        Grid.SetColumn(count, 3);
        grid.Children.Add(count);
        return grid;
    }

    /// <summary>A flat progress bar (the amber part is what is there).</summary>
    internal static Border Bar(int percent, double width = 110)
    {
        var track = new Border { Width = width, Height = 6, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        track.SetResourceReference(Border.BackgroundProperty, "Brush.Window");
        track.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var fill = new Border { HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(0, (width - 2) * Math.Clamp(percent, 0, 100) / 100.0) };
        fill.SetResourceReference(Border.BackgroundProperty, percent >= 100 ? "Brush.Heal" : "Brush.Accent");
        track.Child = fill;
        return track;
    }
}
