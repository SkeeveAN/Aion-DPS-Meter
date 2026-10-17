using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// The page of the attribute tool: the attribute ring as in the game (six main attributes inside, ten Lord values around them, the player's points on each), and on the right what the
/// chosen one does and what it gives the player - 0.1 % per point on each effect - with all effects of all nodes added up below. A second tab explains the stats of the stat page and shows
/// how much of each comes from the ring. The values are the ones the server sends (stat frame 0x4936); the stat values themselves are not sent, so only the share from the ring is shown.
/// A reading page: no overlay, no hotkey, no settings.
/// </summary>
public sealed class AttributesPage : Grid
{
    private const double RingSize = 560;
    private static readonly Dictionary<string, BitmapImage?> Pictures = new();

    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Border _body = new();
    private readonly TextBlock _note = new() { FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly Button _tabRing = new();
    private readonly Button _tabStats = new();
    private bool _statsTab;
    private string _selectedNode = "str";
    private string _selectedStat = "Block";
    private string _signature = "?";
    private IReadOnlyDictionary<int, int>? _values;

    public AttributesPage(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        Margin = new Thickness(14, 10, 14, 14);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(tabs, Dock.Right);
        _tabRing.Click += (_, _) => SwitchTab(false);
        _tabStats.Click += (_, _) => SwitchTab(true);
        tabs.Children.Add(_tabRing);
        tabs.Children.Add(_tabStats);
        head.Children.Add(tabs);
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = Loc("Main.MenuTools.Attributes"), FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Text") });
        _note.Foreground = Res("Brush.TextMuted");
        titles.Children.Add(_note);
        head.Children.Add(titles);
        SetRow(head, 0);
        Children.Add(head);
        SetRow(_body, 1);
        Children.Add(_body);

        _refresh.Tick += (_, _) => Poll();
        _refresh.Start();
        LocalizationManager.Instance.PropertyChanged += OnLanguage;
        Poll(force: true);
    }

    /// <summary>Shows the stat tab (used by the picture mode).</summary>
    public void ShowStatsTab() => SwitchTab(true);

    public void Stop()
    {
        _refresh.Stop();
        LocalizationManager.Instance.PropertyChanged -= OnLanguage;
    }

    private void OnLanguage(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Poll(force: true);

    private static string Loc(string key) => LocalizationManager.Instance[key];

    private static string Lang => LocalizationManager.Instance.Language;

    private static string Game(string key) => Aion2AttributeTable.Text(key, Lang);

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    private int Points(AttributeNode node) => _values is not null && _values.TryGetValue(node.Id, out int v) ? v : 0;

    private static string Percent(double percent, bool lowers) => (lowers ? "−" : "+") + percent.ToString("0.0", CultureInfo.CurrentCulture) + " %";

    private static string NodeName(AttributeNode node) => Loc("Character.Attr." + node.Key);

    private static string NodeFullName(AttributeNode node) => node.Lord ? $"{NodeName(node)} [{Loc("Character.Lord." + node.Key)}]" : NodeName(node);

    private void SwitchTab(bool stats)
    {
        _statsTab = stats;
        Rebuild();
    }

    private void Poll(bool force = false)
    {
        IReadOnlyDictionary<int, int>? values = _directory()?.LocalAttributes;
        string signature = values is null ? "-" : string.Join(',', Aion2AttributeTable.Nodes.Select(n => values.GetValueOrDefault(n.Id)));
        if (!force && signature == _signature)
        {
            return;
        }

        _signature = signature;
        _values = values;
        Rebuild();
    }

    private void Rebuild()
    {
        StyleTab(_tabRing, Loc("Main.MenuTools.Attributes"), !_statsTab);
        StyleTab(_tabStats, Loc("Attributes.TabStats"), _statsTab);
        _note.Text = _statsTab ? Loc("Attributes.StatsNote") : _values is null ? Loc("Attributes.NoData") : Loc("Attributes.Note");
        _note.Foreground = !_statsTab && _values is null ? Res("Brush.Warning") : Res("Brush.TextMuted");
        _body.Child = _statsTab ? BuildStats() : BuildRing();
    }

    private static void StyleTab(Button button, string text, bool active)
    {
        button.Content = text;
        button.Padding = new Thickness(16, 6, 16, 6);
        button.MinWidth = 90;
        button.Cursor = System.Windows.Input.Cursors.Hand;
        button.BorderThickness = new Thickness(1);
        button.BorderBrush = Res("Brush.Border");
        button.Background = active ? Res("Brush.Accent") : Res("Brush.Control");
        button.Foreground = active ? new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x00)) : Res("Brush.TextSubtle");
        button.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
    }

    // ---------------------------------------------------------------- small helpers

    private static TextBlock Label(string text, double size = 13, FontWeight? weight = null, Brush? brush = null) =>
        new() { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, Foreground = brush ?? Res("Brush.Text"), TextWrapping = TextWrapping.Wrap };

    private static TextBlock Heading(string text) =>
        new() { Text = text.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.TextMuted"), Margin = new Thickness(0, 0, 0, 8) };

    private static Border Card(UIElement child, Thickness? margin = null) =>
        new() { Background = Res("Brush.Panel"), BorderBrush = Res("Brush.Border"), BorderThickness = new Thickness(1), Padding = new Thickness(16, 14, 16, 14), Margin = margin ?? new Thickness(0), Child = child };

    private static BitmapImage? Picture(AttributeNode node)
    {
        string file = (node.Lord ? "lords_" : "") + (node.Key == "con" ? "constitution" : node.Key) + ".png";
        if (Pictures.TryGetValue(file, out BitmapImage? known))
        {
            return known;
        }

        BitmapImage? image = null;
        try
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "stats", file);
            if (File.Exists(path))
            {
                image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path);
                image.EndInit();
                image.Freeze();
            }
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
        {
            image = null;
        }

        Pictures[file] = image;
        return image;
    }

    private static Image? Icon(AttributeNode node, double size) =>
        Picture(node) is { } source ? new Image { Source = source, Width = size, Height = size, Stretch = Stretch.Uniform } : null;

    // ---------------------------------------------------------------- tab 1: the ring

    private UIElement BuildRing()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RingSize + 2) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 320 });

        var ringBox = new Border
        {
            Background = Res("Brush.RowAlt"), BorderBrush = Res("Brush.Border"), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top,
            Child = Ring(),
        };
        SetColumn(ringBox, 0);
        grid.Children.Add(ringBox);

        AttributeNode selected = Aion2AttributeTable.Node(_selectedNode);
        var right = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        right.Children.Add(Detail(selected));
        right.Children.Add(Sums(selected));
        SetColumn(right, 1);
        grid.Children.Add(right);
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = grid };
    }

    private UIElement Ring()
    {
        double centre = RingSize / 2;
        var canvas = new Canvas { Width = RingSize, Height = RingSize };

        void Circle(double radius, double thickness, DoubleCollection? dash = null)
        {
            var ellipse = new Ellipse { Width = 2 * radius, Height = 2 * radius, Stroke = Res("Brush.Border"), StrokeThickness = thickness, StrokeDashArray = dash };
            Canvas.SetLeft(ellipse, centre - radius);
            Canvas.SetTop(ellipse, centre - radius);
            canvas.Children.Add(ellipse);
        }

        const double Outer = 212, Inner = 100;
        Circle(Outer + 46, 1);
        Circle(Outer - 52, 1, new DoubleCollection { 3, 6 });
        Circle(Inner + 34, 1);
        for (int i = 0; i < 3; i++)
        {
            double angle = i * Math.PI / 3, reach = Outer - 52;
            canvas.Children.Add(new Line
            {
                X1 = centre + Math.Sin(angle) * reach, Y1 = centre - Math.Cos(angle) * reach, X2 = centre - Math.Sin(angle) * reach, Y2 = centre + Math.Cos(angle) * reach,
                Stroke = Res("Brush.Border"), StrokeThickness = 1, Opacity = 0.6,
            });
        }

        var star = new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,-14 L4,-4 14,0 4,4 0,14 -4,4 -14,0 -4,-4 Z"), Fill = Res("Brush.Accent"), Opacity = 0.9 };
        Canvas.SetLeft(star, centre);
        Canvas.SetTop(star, centre);
        canvas.Children.Add(star);

        void Place(UIElement node, double width, double height, double angleDeg, double radius)
        {
            double angle = angleDeg * Math.PI / 180;
            Canvas.SetLeft(node, centre + Math.Sin(angle) * radius - width / 2);
            Canvas.SetTop(node, centre - Math.Cos(angle) * radius - height / 2);
            canvas.Children.Add(node);
        }

        var outer = Aion2AttributeTable.Nodes.Where(n => n.Lord).ToList();
        for (int i = 0; i < outer.Count; i++)
        {
            // clockwise from the top left: ten nodes 36 degrees apart, the first at -18
            Place(NodeView(outer[i], 36, false), 110, 100, -18 + 36 * i, Outer);
        }

        var inner = Aion2AttributeTable.Nodes.Where(n => !n.Lord).ToList();
        for (int i = 0; i < inner.Count; i++)
        {
            Place(NodeView(inner[i], 39, true), 78, 78, 60 * i, Inner);
        }

        return canvas;
    }

    /// <summary>One circle of the ring: symbol, points and (inside) the name; the name of a Lord sits below the circle.</summary>
    private UIElement NodeView(AttributeNode node, double radius, bool inside)
    {
        int points = Points(node);
        bool chosen = node.Key == _selectedNode;
        var disc = new Grid { Width = radius * 2, Height = radius * 2, Cursor = System.Windows.Input.Cursors.Hand, Opacity = points == 0 ? 0.6 : 1, ToolTip = NodeFullName(node) };
        disc.Children.Add(new Ellipse
        {
            Fill = chosen ? Res("Brush.ControlHoverStrong") : Res("Brush.Panel"),
            Stroke = chosen ? Res("Brush.Accent") : Res("Brush.Border"),
            StrokeThickness = chosen ? 2.5 : 1.5,
        });
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        if (Icon(node, inside ? 20 : 24) is { } icon)
        {
            stack.Children.Add(icon);
        }

        var number = Label(points.ToString(CultureInfo.CurrentCulture), inside ? 15 : 16, FontWeights.Bold, Res("Brush.Accent"));
        number.TextAlignment = TextAlignment.Center;
        stack.Children.Add(number);
        if (inside)
        {
            var name = Label(NodeName(node), 10.5, FontWeights.SemiBold, Res("Brush.TextSubtle"));
            name.TextAlignment = TextAlignment.Center;
            name.TextWrapping = TextWrapping.NoWrap;
            stack.Children.Add(name);
        }

        disc.Children.Add(stack);
        disc.MouseLeftButtonUp += (_, _) =>
        {
            _selectedNode = node.Key;
            Rebuild();
        };
        if (inside)
        {
            return disc;
        }

        var cell = new StackPanel { Width = 110, HorizontalAlignment = HorizontalAlignment.Center };
        disc.HorizontalAlignment = HorizontalAlignment.Center;
        cell.Children.Add(disc);
        var caption = Label(NodeName(node), 11.5, brush: chosen ? Res("Brush.Text") : Res("Brush.TextMuted"));
        caption.TextAlignment = TextAlignment.Center;
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        cell.Children.Add(caption);
        cell.MouseLeftButtonUp += (_, _) =>
        {
            _selectedNode = node.Key;
            Rebuild();
        };
        return cell;
    }

    private UIElement Detail(AttributeNode node)
    {
        int points = Points(node);
        var box = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var iconBox = new Border { Width = 58, Height = 58, Background = Res("Brush.Control"), BorderBrush = Res("Brush.Border"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 14, 0), Child = Icon(node, 38) };
        head.Children.Add(iconBox);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Label(NodeName(node), 22, FontWeights.SemiBold));
        if (node.Lord)
        {
            names.Children.Add(Label("[" + Loc("Character.Lord." + node.Key) + "]", 13, brush: Res("Brush.Accent")));
        }

        names.Children.Add(Label(node.Lord ? Loc("Character.AttrLords") : Loc("Character.AttrMain"), 11.5, brush: Res("Brush.TextMuted")));
        SetColumn(names, 1);
        head.Children.Add(names);
        var pts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var big = Label(points.ToString(CultureInfo.CurrentCulture), 40, FontWeights.Bold, Res("Brush.Accent"));
        big.HorizontalAlignment = HorizontalAlignment.Right;
        pts.Children.Add(big);
        var caption = Label(Loc("Attributes.Points").ToUpperInvariant(), 10.5, brush: Res("Brush.TextMuted"));
        caption.HorizontalAlignment = HorizontalAlignment.Right;
        pts.Children.Add(caption);
        SetColumn(pts, 2);
        head.Children.Add(pts);
        box.Children.Add(head);

        var desc = Label(Game("desc." + node.Key), 13.5, brush: Res("Brush.TextSubtle"));
        desc.Margin = new Thickness(0, 12, 0, 0);
        box.Children.Add(desc);
        var table = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        int row = 0;
        void Row(string a, string b, string c, bool header)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            UIElement[] cells =
            {
                header ? Heading(a) : Label(a, 14.5),
                header ? Heading(b) : Label(b, 13, brush: Res("Brush.TextMuted")),
                header ? Heading(c) : Label(c, 20, FontWeights.Bold, points > 0 ? Res("Brush.Heal") : Res("Brush.TextMuted")),
            };
            for (int i = 0; i < 3; i++)
            {
                if (cells[i] is TextBlock t && i > 0)
                {
                    t.HorizontalAlignment = HorizontalAlignment.Right;
                    t.TextAlignment = TextAlignment.Right;
                }

                var cell = new Border { BorderBrush = Res("Brush.Border"), BorderThickness = new Thickness(0, header ? 0 : 1, 0, 0), Padding = new Thickness(0, header ? 0 : 8, 0, header ? 0 : 8), Child = cells[i] };
                SetRow(cell, row);
                SetColumn(cell, i);
                table.Children.Add(cell);
            }

            row++;
        }

        Row(Loc("Attributes.Effect"), Loc("Attributes.PerPoint"), Loc("Attributes.Bonus"), true);
        foreach (AttributeEffect effect in node.Effects)
        {
            Row(Game("stat." + effect.Name), Percent(Aion2AttributeTable.PercentPerPoint, effect.Lowers), Percent(Aion2AttributeTable.Percent(points), effect.Lowers), false);
        }

        box.Children.Add(table);
        return Card(box);
    }

    private UIElement Sums(AttributeNode selected)
    {
        var box = new StackPanel();
        box.Children.Add(Heading(Loc("Attributes.Sources")));
        var rows = new UniformGrid { Columns = 2 };
        foreach (AttributeBonus bonus in Aion2AttributeTable.Totals(Points))
        {
            bool mine = selected.Effects.Any(e => e.Name == bonus.Effect.Name);
            var line = new DockPanel { LastChildFill = true };
            var value = Label(Percent(bonus.Percent, bonus.Effect.Lowers), 15, FontWeights.SemiBold, Res("Brush.Heal"));
            DockPanel.SetDock(value, Dock.Right);
            line.Children.Add(value);
            line.Children.Add(Label(Game("stat." + bonus.Effect.Name), 13, brush: Res("Brush.TextSubtle")));
            rows.Children.Add(new Border
            {
                Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 14, 0),
                BorderBrush = mine ? Res("Brush.Accent") : Res("Brush.Border"), BorderThickness = new Thickness(mine ? 3 : 0, 0, 0, 1),
                Background = mine ? Res("Brush.ControlHoverStrong") : Brushes.Transparent, Child = line,
            });
        }

        box.Children.Add(rows);
        return Card(box, new Thickness(0, 12, 0, 0));
    }

    // ---------------------------------------------------------------- tab 2: the stats

    private UIElement BuildStats()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(372) });

        var list = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        foreach ((string group, string[] stats) in Aion2AttributeTable.StatGroups)
        {
            var heading = Label(Game("grp." + group).ToUpperInvariant(), 13, FontWeights.SemiBold, Res("Brush.TextSubtle"));
            heading.Margin = new Thickness(0, 4, 0, 6);
            list.Children.Add(heading);
            var rows = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 12) };
            foreach (string stat in stats)
            {
                rows.Children.Add(StatRow(stat));
            }

            list.Children.Add(rows);
        }

        var listScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list };
        SetColumn(listScroll, 0);
        grid.Children.Add(listScroll);
        var detail = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = StatDetail(_selectedStat) };
        SetColumn(detail, 1);
        grid.Children.Add(detail);
        return grid;
    }

    private UIElement StatRow(string stat)
    {
        double share = Aion2AttributeTable.Sources(stat, Points).Sum(s => s.Lowers ? -s.Percent : s.Percent);
        bool chosen = stat == _selectedStat;
        var line = new DockPanel { LastChildFill = true };
        if (share != 0)
        {
            var value = Label(Percent(Math.Abs(share), share < 0), 14, FontWeights.SemiBold, Res("Brush.Heal"));
            DockPanel.SetDock(value, Dock.Right);
            line.Children.Add(value);
        }

        line.Children.Add(Label(Game("stat." + stat), 13.5));
        var row = new Border
        {
            Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 8, 4), Cursor = System.Windows.Input.Cursors.Hand,
            Background = chosen ? Res("Brush.ControlHoverStrong") : Res("Brush.Panel"),
            BorderBrush = chosen ? Res("Brush.Accent") : Res("Brush.Border"), BorderThickness = new Thickness(chosen ? 2 : 1), Child = line,
        };
        row.MouseLeftButtonUp += (_, _) =>
        {
            _selectedStat = stat;
            Rebuild();
        };
        return row;
    }

    private UIElement StatDetail(string stat)
    {
        var box = new StackPanel();
        box.Children.Add(Label(Game("stat." + stat), 22, FontWeights.SemiBold));
        foreach (string line in Game("tip." + stat).Split('\n'))
        {
            var text = Label(line, 13.5, brush: Res("Brush.TextSubtle"));
            text.Margin = new Thickness(0, 8, 0, 0);
            box.Children.Add(text);
        }

        var heading = Heading(Loc("Attributes.FromRing"));
        heading.Margin = new Thickness(0, 18, 0, 8);
        box.Children.Add(heading);
        var sources = Aion2AttributeTable.Sources(stat, Points);
        if (sources.Count == 0)
        {
            box.Children.Add(Label(Loc("Attributes.NoSource"), 12.5, brush: Res("Brush.TextMuted")));
        }

        foreach (AttributeSource source in sources)
        {
            var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6) };
            var value = Label(Percent(source.Percent, source.Lowers), 15, FontWeights.SemiBold, Res("Brush.Heal"));
            value.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(value, Dock.Right);
            line.Children.Add(value);
            if (Icon(source.Node, 22) is { } icon)
            {
                icon.Margin = new Thickness(0, 0, 10, 0);
                DockPanel.SetDock(icon, Dock.Left);
                line.Children.Add(icon);
            }

            var name = Label($"{NodeFullName(source.Node)}  ·  {source.Points}", 13);
            name.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(name);
            box.Children.Add(new Border { Background = Res("Brush.Control"), Padding = new Thickness(10, 7, 10, 7), Child = line });
        }

        return Card(box);
    }
}
