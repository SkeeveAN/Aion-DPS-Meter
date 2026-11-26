using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// The assist window (Tools menu, also the dashboard widget's page): the switches of the overlay, three lists - the order of the circles, "my buffs" and "group buffs"
/// (a tick puts one into the order) - and on the right the arrangement (rows or columns) above a preview that is the very panel the overlay uses.
/// Built in code, in the look of the other windows.
/// </summary>
public sealed class AssistWindow : Window
{
    private static readonly Color GroupColor = Color.FromRgb(0x5C, 0xC8, 0xFF);

    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly AssistPanel _preview = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly StackPanel _orderList = new();
    private readonly StackPanel _mineList = new();
    private readonly StackPanel _groupList = new();
    private readonly Border _horizontalTile;
    private readonly Border _verticalTile;
    private MeterSettings _settings = MeterSettings.Load();
    private IReadOnlyList<AssistItem> _items = Array.Empty<AssistItem>();
    private List<int> _catalog = new();
    private string _listsKey = "";
    private Point _dragStart;

    public AssistWindow(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        LocalizationManager loc = LocalizationManager.Instance;
        Title = loc["Assist.Title"];
        Width = 1180;
        Height = 640;
        MinWidth = 900;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(12, 10, 12, 12) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // left: the switches and the three lists
        var left = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        var switches = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(switches, Dock.Top);
        switches.Children.Add(Switch(loc["Assist.ShowOverlay"], _settings.ShowAssistOverlay, on => Change(s => s.ShowAssistOverlay = on)));
        switches.Children.Add(Switch(loc["Settings.PetFarm.Locked"], !_settings.AssistOverlayLocked, on => Change(s => s.AssistOverlayLocked = !on)));
        switches.Children.Add(Switch(loc["Assist.SortByTime"], _settings.AssistSortByTime, on => Change(s => s.AssistSortByTime = on)));
        switches.Children.Add(OverlayControls.OpacityRow(OverlayPlaces.Assist, 0.6));
        switches.Children.Add(OverlayControls.ResetButton(OverlayPlaces.Assist));
        left.Children.Add(switches);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var showAll = new Button { Content = loc["Assist.ShowAll"], Padding = new Thickness(10, 2, 10, 2) };
        showAll.Click += (_, _) =>
        {
            Change(s => s.AssistOverlayHidden.Clear());
            Rebuild();
        };
        var resetOrder = new Button { Content = loc["Assist.ResetOrder"], Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0) };
        resetOrder.Click += (_, _) =>
        {
            Change(s => s.AssistOrder.Clear());
            Rebuild();
        };
        buttons.Children.Add(showAll);
        buttons.Children.Add(resetOrder);
        left.Children.Add(buttons);

        var lists = new Grid();
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddColumn(lists, 0, "1  " + loc["Assist.OrderList"], "Brush.Text", loc["Assist.TipOrder"], _orderList);
        AddColumn(lists, 1, "2  " + loc["Assist.MyBuffs"], "Brush.Accent", loc["Assist.TipTick"], _mineList);
        AddColumn(lists, 2, "3  " + loc["Assist.GroupBuffs"], GroupColor, loc["Assist.TipTick"], _groupList);
        left.Children.Add(lists);
        root.Children.Add(left);

        // right: the arrangement above the preview
        var right = new StackPanel { Width = 520 };
        Grid.SetColumn(right, 1);
        _horizontalTile = Tile(loc["Assist.Horizontal"], horizontal: true);
        _verticalTile = Tile(loc["Assist.Vertical"], horizontal: false);
        _horizontalTile.MouseLeftButtonUp += (_, _) => SetVertical(false);
        _verticalTile.MouseLeftButtonUp += (_, _) => SetVertical(true);
        var tiles = new StackPanel { Orientation = Orientation.Horizontal };
        tiles.Children.Add(_horizontalTile);
        tiles.Children.Add(_verticalTile);
        right.Children.Add(Card(loc["Assist.Arrangement"], tiles));

        _preview.Reorder = (moved, target, after) => Move(moved, target, after);
        var frame = new Border
        {
            Width = 470, Height = 230, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), ClipToBounds = true, Padding = new Thickness(8),
            Child = _preview, HorizontalAlignment = HorizontalAlignment.Left,
            Background = new LinearGradientBrush(Color.FromRgb(0x2E, 0x4A, 0x5E), Color.FromRgb(0x23, 0x35, 0x25), 90),
        };
        frame.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var grip = new Thumb { Width = 14, Height = 14, Cursor = Cursors.SizeNWSE, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0.7 };
        grip.Template = new ControlTemplate(typeof(Thumb))
        {
            VisualTree = Triangle(),
        };
        var framed = new Grid { Width = 470, Height = 230, HorizontalAlignment = HorizontalAlignment.Left };
        framed.Children.Add(frame);
        framed.Children.Add(grip);
        grip.DragDelta += (_, e) =>
        {
            double w = Math.Clamp(frame.Width + e.HorizontalChange, 90, 490);
            double h = Math.Clamp(frame.Height + e.VerticalChange, 90, 400);
            frame.Width = framed.Width = w;
            frame.Height = framed.Height = h;
        };
        right.Children.Add(Card(loc["Assist.Preview"], framed));
        root.Children.Add(right);

        Content = root;
        ThemedChrome.Apply(this);

        // the lists are rebuilt only when something changed; the preview counts down four times a second, but not while a circle is being dragged
        _tick.Tick += (_, _) => Update();
        _tick.Start();
        Closed += (_, _) => _tick.Stop();
        Rebuild();
    }

    private void Update()
    {
        if (Mouse.LeftButton == MouseButtonState.Pressed && (_preview.IsMouseOver || _orderList.IsMouseOver))
        {
            return;
        }

        _settings = MeterSettings.Load();
        Rebuild();
    }

    /// <summary>The preview and - when the keys or the settings changed - the three lists.</summary>
    private void Rebuild()
    {
        _settings = MeterSettings.Load();
        _preview.Hidden = _settings.AssistOverlayHidden.ToHashSet();
        _preview.Order = _settings.AssistOrder;
        _preview.Vertical = _settings.AssistVertical;
        _preview.FontColor = _settings.OverlayFontColors.GetValueOrDefault(OverlayPlaces.Assist);
        _preview.SortByTime = _settings.AssistSortByTime;
        _preview.Seen = AssistKeys.Remember;
        _items = _preview.Collect(_directory());
        var shown = _items.Count > 0 ? _items : AssistPanel.Sample();
        _preview.Show(shown);
        Mark(_horizontalTile, !_settings.AssistVertical);
        Mark(_verticalTile, _settings.AssistVertical);

        // what the lists offer: everything seen so far (or a made-up Gladiator while the game never ran)
        var keys = _settings.AssistKnown.Concat(_items.Select(i => i.Key)).Concat(_settings.AssistKnown.Count == 0 && _items.Count == 0 ? AssistPanel.SampleCatalog() : Array.Empty<int>()).Distinct().ToList();
        _catalog = keys;
        string key = string.Join(',', keys) + "|" + string.Join(',', _settings.AssistOrder) + "|" + string.Join(',', _settings.AssistOverlayHidden);
        if (key == _listsKey)
        {
            return;
        }

        _listsKey = key;
        BuildLists();
    }

    private void BuildLists()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var hidden = _settings.AssistOverlayHidden.ToHashSet();
        var order = AssistKeys.Ordered(_catalog.Where(k => !hidden.Contains(k)), _settings.AssistOrder);

        _orderList.Children.Clear();
        int number = 1;
        foreach (int key in order)
        {
            _orderList.Children.Add(OrderRow(key, number++, order));
        }

        if (order.Count == 0)
        {
            _orderList.Children.Add(Muted(loc["Assist.NoBuffs"]));
        }

        _mineList.Children.Clear();
        _groupList.Children.Clear();
        foreach (int key in AssistKeys.Ordered(_catalog, Array.Empty<int>()))
        {
            (AssistKeys.IsGroup(key) ? _groupList : _mineList).Children.Add(TickRow(key, !hidden.Contains(key)));
        }
    }

    // ---- rows ---------------------------------------------------------------------------------------------------------

    private static string NameOf(int key) => Aion2SkillNames.DisplayOf(AssistKeys.SkillOf(key));

    private static Image Icon(int key, double size)
    {
        int id = AssistKeys.SkillOf(key);
        string? path = Aion2SkillIcons.PathFor(id) ?? Aion2Artwork.EffectIconPath(id);
        var image = new Image { Width = size, Height = size, Margin = new Thickness(0, 0, 8, 0) };
        if (path is not null && System.IO.File.Exists(path))
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.DecodePixelWidth = 64;
            bmp.EndInit();
            bmp.Freeze();
            image.Source = bmp;
        }

        return image;
    }

    private UIElement OrderRow(int key, int number, IReadOnlyList<int> shown)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent, AllowDrop = true, Cursor = Cursors.SizeAll };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Add(grid, 0, Text("⋮⋮", "Brush.TextMuted", 15));
        Add(grid, 1, Text(number.ToString(), "Brush.TextMuted", 12.5));
        Add(grid, 2, Icon(key, 26));
        var name = Text(NameOf(key), "Brush.Text", 13);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        Add(grid, 3, name);
        Add(grid, 4, Pill(AssistKeys.IsGroup(key) ? loc["Assist.Group"] : loc["Assist.Mine"], AssistKeys.IsGroup(key)));

        var insert = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, Margin = new Thickness(0, -1, 0, 0) };
        insert.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
        Grid.SetColumnSpan(insert, 5);
        grid.Children.Add(insert);

        grid.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(this);
        grid.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Vector moved = e.GetPosition(this) - _dragStart;
            if (Math.Abs(moved.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(moved.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                DragDrop.DoDragDrop(grid, new DataObject("aiondps-assist-key", key), DragDropEffects.Move);
            }
        };
        grid.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent("aiondps-assist-key") ? DragDropEffects.Move : DragDropEffects.None;
            insert.Visibility = e.Effects == DragDropEffects.Move ? Visibility.Visible : Visibility.Collapsed;
            e.Handled = true;
        };
        grid.DragLeave += (_, _) => insert.Visibility = Visibility.Collapsed;
        grid.Drop += (_, e) =>
        {
            insert.Visibility = Visibility.Collapsed;
            if (e.Data.GetData("aiondps-assist-key") is int moved && moved != key)
            {
                Move(moved, key, after: false);
            }
        };
        return grid;
    }

    private UIElement TickRow(int key, bool on)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent };
        var box = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 0) };
        box.Click += (_, _) =>
        {
            bool tick = box.IsChecked == true;
            Change(s =>
            {
                s.AssistOverlayHidden.Remove(key);
                s.AssistOrder.Remove(key);
                if (tick)
                {
                    s.AssistOrder.Add(key); // new in the order: at the end
                }
                else
                {
                    s.AssistOverlayHidden.Add(key);
                }
            });
            Rebuild();
        };
        row.Children.Add(box);
        row.Children.Add(Icon(key, 26));
        var name = Text(NameOf(key), on ? "Brush.Text" : "Brush.TextMuted", 13);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        row.Children.Add(name);
        return row;
    }

    private void Move(int moved, int target, bool after)
    {
        var hidden = _settings.AssistOverlayHidden.ToHashSet();
        var shown = AssistKeys.Ordered(_catalog.Where(k => !hidden.Contains(k)), _settings.AssistOrder);
        var next = AssistKeys.Move(shown, _settings.AssistOrder, moved, target, after);
        Change(s => s.AssistOrder = next);
        _listsKey = "";
        Rebuild();
    }

    private void SetVertical(bool vertical)
    {
        Change(s => s.AssistVertical = vertical);
        Rebuild();
    }

    // ---- pieces -------------------------------------------------------------------------------------------------------

    private static void AddColumn(Grid grid, int column, string title, object color, string tip, StackPanel list)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        var heading = new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        if (color is Color c)
        {
            heading.Foreground = new SolidColorBrush(c);
        }
        else
        {
            heading.SetResourceReference(TextBlock.ForegroundProperty, (string)color);
        }

        head.Children.Add(heading);
        head.Children.Add(InfoMark(tip));
        var column0 = new DockPanel { Margin = new Thickness(column == 0 ? 0 : 8, 0, 0, 0) };
        DockPanel.SetDock(head, Dock.Top);
        column0.Children.Add(head);
        var box = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 6, 8, 6), Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list } };
        box.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        box.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        column0.Children.Add(box);
        Grid.SetColumn(column0, column);
        grid.Children.Add(column0);
    }

    /// <summary>A small round "i": the hint is its tooltip.</summary>
    private static UIElement InfoMark(string tip)
    {
        var circle = new Border
        {
            Width = 16, Height = 16, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(6, 0, 0, 0), Background = Brushes.Transparent,
            ToolTip = tip, Cursor = Cursors.Help, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "i", FontSize = 10.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        circle.SetResourceReference(Border.BorderBrushProperty, "Brush.TextMuted");
        ((TextBlock)circle.Child).SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        ToolTipService.SetInitialShowDelay(circle, 150);
        return circle;
    }

    private static Border Card(string title, UIElement content)
    {
        var stack = new StackPanel();
        var heading = new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        stack.Children.Add(heading);
        stack.Children.Add(content);
        var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 0, 0, 10), Child = stack };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return border;
    }

    /// <summary>A button of the arrangement: little dots in a row or in a column, the name under them.</summary>
    private static Border Tile(string label, bool horizontal)
    {
        var dots = new StackPanel { Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
        dots.VerticalAlignment = VerticalAlignment.Center;
        for (int i = 0; i < (horizontal ? 4 : 3); i++)
        {
            var dot = new Ellipse { Width = 11, Height = 11, Margin = new Thickness(2) };
            dot.SetResourceReference(Shape.FillProperty, "Brush.Accent");
            dots.Children.Add(dot);
        }

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new Border { Height = 52, Child = dots });
        var text = Text(label, "Brush.Text", 12.5);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(text);
        var tile = new Border { Width = 112, Height = 96, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1.5), Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(6), Child = stack, Cursor = Cursors.Hand };
        tile.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        return tile;
    }

    private static void Mark(Border tile, bool on)
    {
        tile.SetResourceReference(Border.BorderBrushProperty, on ? "Brush.Accent" : "Brush.Border");
    }

    private static Border Pill(string text, bool group)
    {
        Color color = group ? GroupColor : (Application.Current?.TryFindResource("Brush.Accent") is SolidColorBrush a ? a.Color : Color.FromRgb(0xFF, 0xD2, 0x7A));
        return new Border
        {
            CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Padding = new Thickness(8, 0, 8, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = new SolidColorBrush(color), Background = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B)),
            Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(color) },
        };
    }

    private static FrameworkElementFactory Triangle()
    {
        var path = new FrameworkElementFactory(typeof(Polygon));
        path.SetValue(Polygon.PointsProperty, new PointCollection { new Point(14, 0), new Point(14, 14), new Point(0, 14) });
        path.SetValue(Shape.FillProperty, Brushes.White);
        return path;
    }

    private static TextBlock Text(string text, string brush, double size)
    {
        var block = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private static TextBlock Muted(string text)
    {
        TextBlock block = Text(text, "Brush.TextMuted", 12);
        block.TextWrapping = TextWrapping.Wrap;
        return block;
    }

    private static void Add(Grid grid, int column, UIElement element)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static CheckBox Switch(string text, bool on, Action<bool> changed)
    {
        var box = new CheckBox { Content = text, IsChecked = on };
        if (Application.Current?.TryFindResource("SettingsSwitch") is Style style)
        {
            box.Style = style;
        }

        box.Click += (_, _) => changed(box.IsChecked == true);
        return box;
    }

    /// <summary>Changes one setting in the file; the overlay's controller reads it within a second.</summary>
    private static void Change(Action<MeterSettings> apply)
    {
        MeterSettings settings = MeterSettings.Load();
        apply(settings);
        settings.Save();
    }
}
