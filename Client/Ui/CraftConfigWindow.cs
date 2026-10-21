using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// The configuration of the crafting tool, a window of its own: the recipe search that folds out above the list of the marked recipes (count and "whole account"),
/// and the options of the overlay and the calculation. "Save" keeps everything and closes; "Close" asks first when something changed (Save / Discard / Cancel), as
/// every configuration window does.
/// </summary>
public sealed class CraftConfigWindow : Window
{
    private static readonly string[] Categories = { "Blacksmithing", "Tailoring", "Jewelcrafting", "Alchemy", "Cooking" };
    private const int MaxResults = 200;

    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly string _character;
    private readonly bool _elyos;
    private readonly MeterSettings _settings;
    private readonly List<CraftEntry> _entries;
    private double _opacity;
    private readonly string _baseline;
    private bool _allowClose;

    private string _category = "Alchemy";
    private string _tab = "";
    private bool _searchOpen = true;
    private readonly TextBox _search = new() { Width = 260, Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly CheckBox _onlyMarked = new();
    private readonly StackPanel _tree = new();
    private readonly StackPanel _results = new();
    private readonly StackPanel _mine = new();
    private readonly TextBlock _mineSummary = new() { FontSize = 12 };
    private readonly Border _searchBody = new();
    private readonly TextBlock _foldMark = new() { FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _foldHint = new() { FontSize = 12 };

    public CraftConfigWindow(Func<Aion2EntityDirectory?> directory)
    {
        var loc = LocalizationManager.Instance;
        _directory = directory;
        _character = CraftText.Character(directory());
        _elyos = CraftText.IsElyos(directory());
        _settings = MeterSettings.Load();
        _opacity = _settings.OverlayOpacities.GetValueOrDefault(OverlayPlaces.Craft, 0.6);
        _entries = CraftPlan.For(_character).Select(e => new CraftEntry { Recipe = e.Recipe, Count = e.Count, AccountWide = e.AccountWide, Character = e.Character }).ToList();

        Title = loc["Craft.ConfigTitle"];
        Width = 720;
        Height = Math.Min(980, SystemParameters.WorkArea.Height - 40);
        MinWidth = 640;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(Control.ForegroundProperty, "Brush.Text");

        var content = new StackPanel { Margin = new Thickness(14, 12, 14, 8) };
        content.Children.Add(BuildSearch());
        content.Children.Add(BuildMine());
        content.Children.Add(BuildDisplay());

        var save = new Button { Content = loc["Config.Save"], Padding = new Thickness(22, 4, 22, 4), Margin = new Thickness(0, 0, 8, 0), FontWeight = FontWeights.Bold };
        save.SetResourceReference(Control.BackgroundProperty, "Brush.Accent");
        save.Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x00));
        save.Click += (_, _) =>
        {
            SaveAll();
            _allowClose = true;
            Close();
        };
        var close = new Button { Content = loc["Config.Close"], Padding = new Thickness(22, 4, 22, 4) };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(14, 8, 14, 12) };
        buttons.Children.Add(save);
        buttons.Children.Add(close);

        var layout = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        layout.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content });
        layout.SetResourceReference(TextElement.ForegroundProperty, "Brush.Text"); // also when the content is lifted out of the window (the picture mode)
        Content = layout;

        ShowTree();
        ShowResults();
        ShowMine();
        ApplyFold();
        ThemedChrome.Apply(this);
        _baseline = Snapshot();
    }

    // ---------------------------------------------------------------- the search (folds out above the list)
    private Border BuildSearch()
    {
        var loc = LocalizationManager.Instance;
        var card = Card();
        var stack = new StackPanel();
        card.Child = stack;
        card.Margin = new Thickness(0, 0, 0, 12);

        var head = new DockPanel { Cursor = Cursors.Hand, Background = Brushes.Transparent };
        _foldMark.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        var title = new TextBlock { Text = loc["Craft.Search"], FontWeight = FontWeights.SemiBold };
        _foldHint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        DockPanel.SetDock(_foldHint, Dock.Right);
        head.Children.Add(_foldHint);
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(_foldMark);
        left.Children.Add(title);
        head.Children.Add(left);
        head.MouseLeftButtonUp += (_, _) =>
        {
            _searchOpen = !_searchOpen;
            ApplyFold();
        };
        stack.Children.Add(head);

        var body = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _searchBody.Child = body;
        stack.Children.Add(_searchBody);

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        StyleBox(_search);
        var searchHost = new Grid { Width = 260 };
        _search.Width = double.NaN;
        searchHost.Children.Add(_search);
        var hint = new TextBlock { Text = loc["Craft.SearchHint"], IsHitTestVisible = false, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        searchHost.Children.Add(hint);
        _search.TextChanged += (_, _) =>
        {
            hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowResults();
        };
        bar.Children.Add(searchHost);
        var onlyMarked = Switch(_onlyMarked, loc["Craft.OnlyMarked"]);
        onlyMarked.HorizontalAlignment = HorizontalAlignment.Right;
        _onlyMarked.Click += (_, _) => ShowResults();
        DockPanel.SetDock(onlyMarked, Dock.Right);
        bar.Children.Insert(0, onlyMarked);
        body.Children.Add(bar);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var treeScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 340, Content = _tree, Margin = new Thickness(0, 0, 8, 0) };
        grid.Children.Add(treeScroll);
        var resultScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 340, Content = _results };
        Grid.SetColumn(resultScroll, 1);
        grid.Children.Add(resultScroll);
        body.Children.Add(grid);
        return card;
    }

    private void ApplyFold()
    {
        var loc = LocalizationManager.Instance;
        _searchBody.Visibility = _searchOpen ? Visibility.Visible : Visibility.Collapsed;
        _foldMark.Text = _searchOpen ? "▾" : "▸";
        _foldHint.Text = _searchOpen ? loc["Craft.FoldIn"] : loc["Craft.FoldOut"];
    }

    private static string TabName(string tab)
    {
        string key = "Craft.Tab." + tab;
        string text = LocalizationManager.Instance[key];
        return text == key ? tab : text;
    }

    private IEnumerable<CraftRecipe> InCategory() => Aion2CraftData.Visible(_elyos).Where(r => r.Category == _category);

    private void ShowTree()
    {
        var loc = LocalizationManager.Instance;
        _tree.Children.Clear();
        foreach (string category in Categories)
        {
            int count = Aion2CraftData.Visible(_elyos).Count(r => r.Category == category);
            bool open = category == _category;
            _tree.Children.Add(TreeRow((open ? "▾ " : "▸ ") + loc["Craft.Cat." + category], count, open && _tab.Length == 0 && false, open, 0, () =>
            {
                _category = category;
                _tab = "";
                ShowTree();
                ShowResults();
            }));
            if (!open)
            {
                continue;
            }

            _tree.Children.Add(TreeRow(loc["Craft.All"], count, _tab.Length == 0, false, 1, () =>
            {
                _tab = "";
                ShowTree();
                ShowResults();
            }));
            foreach (IGrouping<string, CraftRecipe> tab in InCategory().GroupBy(r => r.Tab.Length > 0 ? r.Tab : r.SubCategory).OrderBy(g => TabName(g.Key), StringComparer.CurrentCultureIgnoreCase))
            {
                string key = tab.Key;
                _tree.Children.Add(TreeRow(TabName(key), tab.Count(), _tab == key, false, 1, () =>
                {
                    _tab = key;
                    ShowTree();
                    ShowResults();
                }));
            }
        }
    }

    private static Border TreeRow(string text, int count, bool active, bool header, int indent, Action click)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock { Text = text, FontWeight = header || active ? FontWeights.SemiBold : FontWeights.Normal, FontSize = indent > 0 ? 12.5 : 13, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 6, 0) };
        if (active)
        {
            name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        }
        else
        {
            name.SetResourceReference(TextBlock.ForegroundProperty, indent > 0 ? "Brush.TextSubtle" : "Brush.Text");
        }

        grid.Children.Add(name);
        var number = new TextBlock { Text = count.ToString(), FontSize = 11.5 };
        number.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        Grid.SetColumn(number, 1);
        grid.Children.Add(number);
        var row = new Border { Child = grid, Padding = new Thickness(8 + indent * 16, 3, 8, 3), CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        if (header && !active)
        {
            row.Background = new SolidColorBrush(Color.FromArgb(0x30, 0x4D, 0x7B, 0xA8));
        }

        row.MouseLeftButtonUp += (_, _) => click();
        return row;
    }

    private bool IsMarked(CraftRecipe recipe) => _entries.Any(e => Aion2CraftData.ForFaction(e.Recipe, _elyos)?.Id == recipe.Id);

    private void ShowResults()
    {
        var loc = LocalizationManager.Instance;
        _results.Children.Clear();
        string text = _search.Text.Trim();
        IEnumerable<CraftRecipe> found = text.Length > 0 ? Aion2CraftData.Visible(_elyos) : InCategory();
        if (text.Length == 0 && _tab.Length > 0)
        {
            found = found.Where(r => (r.Tab.Length > 0 ? r.Tab : r.SubCategory) == _tab);
        }

        if (text.Length > 0)
        {
            found = found.Where(r => CraftText.RecipeName(r).Contains(text, StringComparison.CurrentCultureIgnoreCase));
        }

        if (_onlyMarked.IsChecked == true)
        {
            found = found.Where(IsMarked);
        }

        List<CraftRecipe> list = found.OrderBy(r => r.Type == "Succession").ThenBy(r => r.Level).ThenBy(r => r.Order).ThenBy(r => r.Id).ToList();
        foreach (CraftRecipe recipe in list.Take(MaxResults))
        {
            _results.Children.Add(ResultRow(recipe));
        }

        if (list.Count == 0)
        {
            var none = new TextBlock { Text = loc["Craft.NoResults"], Margin = new Thickness(6, 4, 0, 0) };
            none.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            _results.Children.Add(none);
        }
        else if (list.Count > MaxResults)
        {
            var more = new TextBlock { Text = string.Format(loc["Craft.MoreResults"], list.Count - MaxResults), Margin = new Thickness(6, 6, 0, 0) };
            more.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            _results.Children.Add(more);
        }
    }

    private UIElement ResultRow(CraftRecipe recipe)
    {
        var loc = LocalizationManager.Instance;
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new CheckBox { IsChecked = IsMarked(recipe), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), IsHitTestVisible = false };
        grid.Children.Add(box);
        var icon = CraftText.IconTile(recipe.Product, 26);
        icon.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(icon, 1);
        grid.Children.Add(icon);
        var name = new TextBlock { Text = CraftText.RecipeName(recipe), Foreground = new SolidColorBrush(CraftText.GradeColor(recipe.Product)), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(name, 2);
        grid.Children.Add(name);
        var level = new TextBlock { Text = recipe.Level > 0 ? string.Format(loc["Craft.Level"], recipe.Level) : "", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) };
        level.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        Grid.SetColumn(level, 3);
        grid.Children.Add(level);
        grid.MouseLeftButtonUp += (_, _) =>
        {
            if (IsMarked(recipe))
            {
                _entries.RemoveAll(e => Aion2CraftData.ForFaction(e.Recipe, _elyos)?.Id == recipe.Id);
            }
            else
            {
                _entries.Add(new CraftEntry { Recipe = recipe.Id, Count = 1, AccountWide = true, Character = _character });
            }

            box.IsChecked = IsMarked(recipe);
            ShowMine();
            if (_onlyMarked.IsChecked == true)
            {
                ShowResults();
            }
        };
        return grid;
    }

    // ---------------------------------------------------------------- the marked recipes
    private Border BuildMine()
    {
        var loc = LocalizationManager.Instance;
        var card = Card();
        card.Margin = new Thickness(0, 0, 0, 12);
        var stack = new StackPanel();
        card.Child = stack;
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        _mineSummary.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        DockPanel.SetDock(_mineSummary, Dock.Right);
        head.Children.Add(_mineSummary);
        head.Children.Add(Heading(loc["Craft.Mine"]));
        stack.Children.Add(head);
        stack.Children.Add(_mine);
        return card;
    }

    private void ShowMine()
    {
        var loc = LocalizationManager.Instance;
        _mine.Children.Clear();
        _mineSummary.Text = string.Format(loc["Craft.MineSummary"], _entries.Count, _entries.Sum(e => e.Count)) + " · " + loc["Craft.OrderHint"];
        if (_entries.Count == 0)
        {
            var empty = new TextBlock { Text = loc["Craft.MineEmpty"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            _mine.Children.Add(empty);
            return;
        }

        var header = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        SetColumns(header);
        TextBlock Label(string text, int column, TextAlignment align)
        {
            var block = new TextBlock { Text = text, FontSize = 11, TextAlignment = align };
            block.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            Grid.SetColumn(block, column);
            return block;
        }

        header.Children.Add(Label(loc["Craft.Recipe"], 2, TextAlignment.Left));
        header.Children.Add(Label(loc["Craft.Count"], 3, TextAlignment.Center));
        header.Children.Add(Label(loc["Craft.WholeAccount"], 4, TextAlignment.Right));
        _mine.Children.Add(header);
        foreach (CraftEntry entry in _entries.ToList())
        {
            _mine.Children.Add(MineRow(entry));
        }
    }

    private static void SetColumns(Grid grid)
    {
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
    }

    private UIElement MineRow(CraftEntry entry)
    {
        CraftRecipe? recipe = Aion2CraftData.ForFaction(entry.Recipe, _elyos);
        int product = recipe?.Product ?? 0;
        var row = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 4, 0, 4), AllowDrop = true, Background = Brushes.Transparent };
        row.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var grid = new Grid();
        SetColumns(grid);
        row.Child = grid;

        var handle = new TextBlock { Text = "⠿", FontSize = 17, Cursor = Cursors.SizeAll, ToolTip = LocalizationManager.Instance["Craft.DragTip"], VerticalAlignment = VerticalAlignment.Center };
        handle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        handle.MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragDrop.DoDragDrop(handle, new DataObject(typeof(CraftEntry), entry), DragDropEffects.Move);
            }
        };
        grid.Children.Add(handle);
        var icon = CraftText.IconTile(product, 28);
        Grid.SetColumn(icon, 1);
        grid.Children.Add(icon);

        var names = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = recipe is null ? "#" + entry.Recipe : CraftText.RecipeName(recipe), FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(CraftText.GradeColor(product)), TextTrimming = TextTrimming.CharacterEllipsis });
        var level = new TextBlock { Text = recipe is null || recipe.Level == 0 ? "" : string.Format(LocalizationManager.Instance["Craft.Level"], recipe.Level), FontSize = 11.5, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        level.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        names.Children.Add(level);
        Grid.SetColumn(names, 2);
        grid.Children.Add(names);

        var stepper = Stepper(entry);
        Grid.SetColumn(stepper, 3);
        grid.Children.Add(stepper);

        var whole = new CheckBox { IsChecked = entry.AccountWide, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Width = 46, Margin = new Thickness(0) };
        if (Application.Current?.TryFindResource("SettingsSwitch") is Style style)
        {
            whole.Style = style;
        }

        whole.Click += (_, _) =>
        {
            entry.AccountWide = whole.IsChecked == true;
            entry.Character = entry.AccountWide ? entry.Character : _character;
        };
        Grid.SetColumn(whole, 4);
        grid.Children.Add(whole);

        var remove = new TextBlock { Text = "✕", Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        remove.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        remove.MouseLeftButtonUp += (_, _) =>
        {
            _entries.Remove(entry);
            ShowMine();
            ShowResults();
        };
        Grid.SetColumn(remove, 5);
        grid.Children.Add(remove);

        row.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(typeof(CraftEntry)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
        row.Drop += (_, e) =>
        {
            if (e.Data.GetData(typeof(CraftEntry)) is CraftEntry dragged && dragged != entry)
            {
                int to = _entries.IndexOf(entry);
                _entries.Remove(dragged);
                _entries.Insert(Math.Clamp(to, 0, _entries.Count), dragged);
                ShowMine();
            }

            e.Handled = true;
        };
        return row;
    }

    /// <summary>"−  [20]  +": how often the recipe is crafted; the field takes digits, 1 to 9999.</summary>
    private static UIElement Stepper(CraftEntry entry)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var box = new TextBox { Text = entry.Count.ToString(), Width = 46, Height = 26, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 4 };
        StyleBox(box);
        void Set(int value)
        {
            entry.Count = Math.Clamp(value, 1, 9999);
            box.Text = entry.Count.ToString();
        }

        var minus = new Button { Content = "−", Width = 26, Height = 26, Padding = new Thickness(0) };
        var plus = new Button { Content = "+", Width = 26, Height = 26, Padding = new Thickness(0) };
        minus.Click += (_, _) => Set(entry.Count - 1);
        plus.Click += (_, _) => Set(entry.Count + 1);
        box.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        box.LostFocus += (_, _) => Set(int.TryParse(box.Text, out int typed) ? typed : entry.Count);
        box.TextChanged += (_, _) =>
        {
            if (int.TryParse(box.Text, out int typed) && typed is >= 1 and <= 9999)
            {
                entry.Count = typed;
            }
        };
        panel.Children.Add(minus);
        panel.Children.Add(box);
        panel.Children.Add(plus);
        return panel;
    }

    // ---------------------------------------------------------------- the display options
    private bool _resolve, _countInventory, _countStorage, _countAccount, _movable;
    private int _lines;

    private Border BuildDisplay()
    {
        var loc = LocalizationManager.Instance;
        _resolve = _settings.CraftResolve;
        _countInventory = _settings.CraftCountInventory;
        _countStorage = _settings.CraftCountStorage;
        _countAccount = _settings.CraftCountAccountStorage;
        _movable = !_settings.CraftOverlayLocked;
        _lines = _settings.CraftOverlayLines;

        var card = Card();
        var stack = new StackPanel();
        card.Child = stack;
        stack.Children.Add(Heading(loc["Craft.Display"]));
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var overlay = new StackPanel();
        overlay.Children.Add(SubHeading(loc["Craft.Overlay"]));
        overlay.Children.Add(OptionRow(loc["Craft.Movable"], SwitchBox(_movable, v => _movable = v)));
        overlay.Children.Add(OptionRow(loc["Craft.Lines"], LinesBox()));
        overlay.Children.Add(OptionRow(loc["Craft.Opacity"], OpacityBox()));
        var reset = new Button { Content = loc["Craft.ResetPlace"], Padding = new Thickness(12, 2, 12, 2), MinHeight = 24 };
        reset.Click += (_, _) => OverlayPlaces.Reset(OverlayPlaces.Craft);
        overlay.Children.Add(OptionRow(loc["Craft.PlaceLabel"], reset));
        grid.Children.Add(overlay);

        var calc = new StackPanel();
        calc.Children.Add(SubHeading(loc["Craft.Calculation"]));
        calc.Children.Add(OptionRow(loc["Craft.Resolve"], SwitchBox(_resolve, v => _resolve = v)));
        calc.Children.Add(OptionRow(loc["Craft.CountInventory"], SwitchBox(_countInventory, v => _countInventory = v)));
        calc.Children.Add(OptionRow(loc["Craft.CountStorage"], SwitchBox(_countStorage, v => _countStorage = v)));
        calc.Children.Add(OptionRow(loc["Craft.CountAccount"], SwitchBox(_countAccount, v => _countAccount = v)));
        Grid.SetColumn(calc, 2);
        grid.Children.Add(calc);
        stack.Children.Add(grid);
        return card;
    }

    private UIElement LinesBox()
    {
        var box = new ComboBox { Width = 74, Height = 24 };
        foreach (int lines in new[] { 3, 5, 8, 10, 15, 20, 30 })
        {
            box.Items.Add(new ComboBoxItem { Content = lines.ToString(), Tag = lines, IsSelected = lines == _lines });
        }

        if (box.SelectedItem is null)
        {
            box.SelectedIndex = 3;
        }

        box.SelectionChanged += (_, _) => _lines = box.SelectedItem is ComboBoxItem { Tag: int lines } ? lines : _lines;
        return box;
    }

    private UIElement OpacityBox()
    {
        var box = new ComboBox { Width = 74, Height = 24 };
        double nearest = Math.Round(_opacity * 10) / 10;
        foreach (int percent in new[] { 20, 30, 40, 50, 60, 70, 80, 90, 100 })
        {
            box.Items.Add(new ComboBoxItem { Content = percent + " %", Tag = percent / 100.0, IsSelected = Math.Abs(percent / 100.0 - nearest) < 0.01 });
        }

        if (box.SelectedItem is null)
        {
            box.SelectedIndex = 4;
        }

        box.SelectionChanged += (_, _) => _opacity = box.SelectedItem is ComboBoxItem { Tag: double fraction } ? fraction : _opacity;
        return box;
    }

    private static CheckBox SwitchBox(bool on, Action<bool> set)
    {
        var box = new CheckBox { IsChecked = on, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Width = 46 };
        if (Application.Current?.TryFindResource("SettingsSwitch") is Style style)
        {
            box.Style = style;
        }

        box.Click += (_, _) => set(box.IsChecked == true);
        return box;
    }

    /// <summary>A label and its control on one line of fixed height, a thin line above (so the rows of both columns line up).</summary>
    private static Border OptionRow(string label, UIElement control)
    {
        var grid = new Grid { Height = 34 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        if (control is FrameworkElement element)
        {
            element.HorizontalAlignment = HorizontalAlignment.Right;
            element.VerticalAlignment = VerticalAlignment.Center;
        }

        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        var row = new Border { Child = grid, BorderThickness = new Thickness(0, 1, 0, 0) };
        row.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return row;
    }

    // ---------------------------------------------------------------- saving, and closing with changes
    private string Snapshot() => JsonSerializer.Serialize(new
    {
        Entries = _entries.Select(e => new { e.Recipe, e.Count, e.AccountWide }),
        _resolve, _countInventory, _countStorage, _countAccount, _movable, _lines, _opacity,
    });

    private void SaveAll()
    {
        CraftPlan.ReplaceFor(_character, _entries);
        // the overlay saves its place straight to disk while this window is open: only the fields of this window are written, to a fresh copy
        MeterSettings s = MeterSettings.Load();
        s.CraftResolve = _resolve;
        s.CraftCountInventory = _countInventory;
        s.CraftCountStorage = _countStorage;
        s.CraftCountAccountStorage = _countAccount;
        s.CraftOverlayLocked = !_movable;
        s.CraftOverlayLines = _lines;
        s.OverlayOpacities[OverlayPlaces.Craft] = _opacity;
        s.Save();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose && Snapshot() != _baseline)
        {
            MessageBoxResult answer = ThemedMessageBox.AskUnsaved(this, Title);
            if (answer == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (answer == MessageBoxResult.Yes)
            {
                SaveAll();
            }
        }

        base.OnClosing(e);
    }

    // ---------------------------------------------------------------- small helpers
    private static Border Card()
    {
        var card = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 14, 10) };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    private static TextBlock Heading(string text)
    {
        var block = new TextBlock { Text = text.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        return block;
    }

    private static TextBlock SubHeading(string text)
    {
        var block = new TextBlock { Text = text.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        return block;
    }

    private static StackPanel Switch(CheckBox box, string label)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        box.VerticalAlignment = VerticalAlignment.Center;
        box.Width = 46;
        if (Application.Current?.TryFindResource("SettingsSwitch") is Style style)
        {
            box.Style = style;
        }

        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        panel.Children.Add(box);
        return panel;
    }

    private static void StyleBox(TextBox box)
    {
        box.Padding = new Thickness(6, 2, 6, 2);
        box.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
        box.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        box.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        box.SetResourceReference(TextBox.CaretBrushProperty, "Brush.Text");
    }
}
