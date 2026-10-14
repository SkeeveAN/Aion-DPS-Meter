using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// The theme editor (Tools menu): the settings on the left, a live preview of the main window and the overlay on the right. The preview hangs
/// the draft's own resource dictionary on its panel, so nothing else changes until "Save and apply" - then the main window and every overlay
/// repaint. The own colours override the chosen theme (see <see cref="ThemeManager.Build"/>); each field's ✕ goes back to the theme's colour.
/// </summary>
public sealed class ThemeEditorWindow : Window
{
    private static readonly (string Name, string Class, string Dps, string Total, double Fill)[] Sample =
    {
        ("Aahz", "Gladiator", "48 210", "2.31M", 100),
        ("Skeeve", "Sorcerer", "39 874", "1.91M", 83),
        ("Chumley", "Assassin", "30 502", "1.46M", 63),
        ("Nunzio", "Cleric", "17 940", "860K", 37),
    };

    private readonly MeterSettings _settings;
    private readonly ColorField _background = new();
    private readonly ColorField _titleBar = new();
    private readonly ColorField _panel = new();
    private readonly ColorField _control = new();
    private readonly ColorField _switch = new();
    private readonly ColorField _toggle = new();
    private readonly ColorField _bar = new();
    private readonly ComboBox _barMode = new();
    private readonly StackPanel _singlePanel = new();
    private readonly StackPanel _classPanel = new();
    private readonly ColorField _text = new();
    private readonly Slider _fontSize = new() { Minimum = 8, Maximum = 20, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 130, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _fontValue = new() { Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<string, ColorField> _classFields = new();
    private readonly ComboBox _themes = new() { Width = 200 };
    private bool _populating;
    private readonly TextBox _name = new() { Width = 170, MaxLength = 40, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Border _preview = new() { Padding = new Thickness(16) };
    private bool _loading = true;

    /// <summary>Raised on "Save and apply" with the settings holding the new custom values.</summary>
    public event Action<MeterSettings>? Saved;

    public ThemeEditorWindow(MeterSettings settings)
    {
        _settings = settings;
        LocalizationManager loc = LocalizationManager.Instance;
        Title = loc["ThemeEditor.Title"];
        Width = 1200;
        Height = Math.Min(940, SystemParameters.WorkArea.Height - 40); // tall enough for the whole form without scrolling
        MinWidth = 900;
        MinHeight = 560;

        // the labels of the form took the plain Windows text colour (near black on the dark window): they wear the theme's text colour
        SetResourceReference(Control.ForegroundProperty, "Brush.Text");

        // the text boxes of the form (theme name, the hex codes) wear the theme: dark field, light text; the plain Windows look was white on white
        var boxStyle = new Style(typeof(TextBox));
        boxStyle.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Brush.Control")));
        boxStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Brush.Text")));
        boxStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("Brush.Border")));
        boxStyle.Setters.Add(new Setter(TextBox.CaretBrushProperty, new DynamicResourceExtension("Brush.Text")));
        Resources[typeof(TextBox)] = boxStyle;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var form = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
        form.Children.Add(Row(loc["ThemeEditor.Select"], _themes));
        form.Children.Add(Row(loc["ThemeEditor.Name"], _name));
        form.Children.Add(Hint(loc["ThemeEditor.Hint"]));
        form.Children.Add(Row(loc["ThemeEditor.TitleBar"], _titleBar));
        form.Children.Add(Row(loc["Settings.Custom.Background"], _background));
        form.Children.Add(Row(loc["Settings.Custom.Text"], _text));
        form.Children.Add(Row(loc["ThemeEditor.Panel"], _panel));
        form.Children.Add(Row(loc["ThemeEditor.Control"], _control));
        form.Children.Add(Row(loc["ThemeEditor.Switch"], _switch));
        form.Children.Add(Row(loc["ThemeEditor.Toggle"], _toggle));
        var fontRow = new DockPanel();
        DockPanel.SetDock(_fontValue, Dock.Right);
        fontRow.Children.Add(_fontValue);
        fontRow.Children.Add(_fontSize);
        form.Children.Add(Row(loc["Settings.Custom.FontSize"], fontRow));

        // The bars take either one colour per class or one colour for all: the user picks which, then sets the colours of that choice.
        form.Children.Add(new TextBlock { Text = loc["ThemeEditor.BarColours"], FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 6) });
        _barMode.Items.Add(new ComboBoxItem { Content = loc["ThemeEditor.BarModeClass"] });
        _barMode.Items.Add(new ComboBoxItem { Content = loc["ThemeEditor.BarModeSingle"] });
        _barMode.SelectedIndex = 0;
        _barMode.Margin = new Thickness(0, 0, 0, 6);
        _barMode.SelectionChanged += (_, _) =>
        {
            ShowBarMode();
            Rebuild();
        };
        form.Children.Add(_barMode);
        _singlePanel.Children.Add(Row(loc["Settings.Custom.Bar"], _bar));
        _classPanel.Children.Add(Hint(loc["ThemeEditor.ClassColorsHint"]));
        foreach (string cls in ClassBrushes.Names)
        {
            var field = new ColorField();
            _classFields[cls] = field;
            field.Changed += (_, _) => Rebuild();
            _classPanel.Children.Add(Row(AionDPS.Data.ClassCatalog.DisplayName(cls, loc.Language), field));
        }

        form.Children.Add(_singlePanel);
        form.Children.Add(_classPanel);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 0) };
        var save = new Button { Content = loc["ThemeEditor.Save"], Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
        var reset = new Button { Content = loc["ThemeEditor.Reset"], Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
        var export = new Button { Content = loc["ThemeEditor.Export"], Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 8, 8, 0) };
        var import = new Button { Content = loc["ThemeEditor.Import"], Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 8, 8, 0) };
        export.Click += (_, _) => ExportTheme();
        import.Click += (_, _) => ImportTheme();
        var close = new Button { Content = loc["ThemeEditor.Close"], Padding = new Thickness(14, 4, 14, 4) };
        save.Click += (_, _) => Save();
        reset.Click += (_, _) =>
        {
            _background.Hex = "";
            _titleBar.Hex = "";
            _panel.Hex = "";
            _control.Hex = "";
            _switch.Hex = "";
            _toggle.Hex = "";
            _bar.Hex = "";
            _barMode.SelectedIndex = 0;
            _text.Hex = "";
            _fontSize.Value = _fontSize.Minimum;
            _loading = true;
            foreach (ColorField field in _classFields.Values)
            {
                field.Hex = "";
            }

            _loading = false;
            Rebuild();
        };
        close.Click += (_, _) => Close();
        buttons.Children.Add(save);
        buttons.Children.Add(reset);
        buttons.Children.Add(close);
        form.Children.Add(buttons);
        form.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { export, import } });

        var left = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = form, Width = 400, Margin = new Thickness(0, 0, 0, 0) };
        var right = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _preview }, ClipToBounds = true };
        right.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel { Margin = new Thickness(16, 12, 16, 16) };
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(left);
        root.Children.Add(right);
        Content = root;
        ThemedChrome.Apply(this);

        string? active = settings.Theme.StartsWith(ThemeFile.FilePrefix, StringComparison.Ordinal) ? settings.Theme[ThemeFile.FilePrefix.Length..] : null;
        ThemeFile? start = ThemeFile.Resolve(settings);
        _currentName = start is null ? null : active;
        _name.Text = _currentName ?? "";
        LoadFile(start ?? new ThemeFile());
        FillThemeList(_currentName);
        _themes.SelectionChanged += (_, _) => OnThemePicked();
        foreach (ColorField field in new[] { _background, _titleBar, _panel, _control, _switch, _toggle })
        {
            field.Changed += (_, _) => Rebuild();
        }
        ShowDefaults();

        _bar.Changed += (_, _) => Rebuild();
        _text.Changed += (_, _) => Rebuild();
        _fontSize.ValueChanged += (_, _) => Rebuild();
        _loading = false;
        Rebuild();
    }

    /// <summary>What every field holds while it has no colour of its own: the base theme's colours and the standard class colours, shown dimmed.</summary>
    private void ShowDefaults()
    {
        ResourceDictionary baseTheme = ThemeManager.Build(_settings.Theme, null);
        string Hex(string key) => baseTheme.Contains(key) && baseTheme[key] is Color c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : "";
        _background.DefaultHex = Hex("Color.Window");
        _titleBar.DefaultHex = Hex("Color.TitleBar");
        _panel.DefaultHex = Hex("Color.Panel");
        _control.DefaultHex = Hex("Color.Control");
        _switch.DefaultHex = Hex("Color.Switch");
        _toggle.DefaultHex = Hex("Color.Toggle");
        _text.DefaultHex = Hex("Color.Text");
        foreach (var (cls, field) in _classFields)
        {
            Color standard = ClassBrushes.ClassColor(cls);
            field.DefaultHex = $"#{standard.R:X2}{standard.G:X2}{standard.B:X2}";
        }
    }

    /// <summary>The list: "Create new theme" first, then every saved theme; <paramref name="select"/> is chosen (null = the new one).</summary>
    private void FillThemeList(string? select)
    {
        _populating = true;
        _themes.Items.Clear();
        _themes.Items.Add(new ComboBoxItem { Content = LocalizationManager.Instance["ThemeEditor.NewTheme"] });
        foreach (string name in ThemeFile.List())
        {
            _themes.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        }

        _themes.SelectedItem = _themes.Items.OfType<ComboBoxItem>().FirstOrDefault(i => select is not null && (i.Tag as string) == select) ?? _themes.Items[0];
        _populating = false;
    }

    /// <summary>Another theme in the list: its colours go into the fields; "Create new theme" starts empty.</summary>
    private void OnThemePicked()
    {
        if (_populating || _themes.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        if (item.Tag is string name && ThemeFile.Read(ThemeFile.PathFor(name)) is { } file)
        {
            _currentName = name;
            _name.Text = name;
            LoadFile(file);
        }
        else
        {
            _currentName = null;
            _name.Text = "";
            LoadFile(new ThemeFile());
        }
    }

    private bool SingleBar => _barMode.SelectedIndex == 1;

    private void ShowBarMode()
    {
        _singlePanel.Visibility = SingleBar ? Visibility.Visible : Visibility.Collapsed;
        _classPanel.Visibility = SingleBar ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Only the colours of the chosen bar mode count; the other mode's fields are ignored (and not saved).</summary>
    private Dictionary<string, string> ClassColorsNow() => SingleBar
        ? new()
        : _classFields.Where(f => f.Value.Hex.Length > 0).ToDictionary(f => f.Key, f => f.Value.Hex);

    private string? BarNow() => SingleBar ? OrNull(_bar) : null;

    private static string? OrNull(ColorField field) => field.Hex.Length > 0 ? field.Hex : null;

    private ThemeOverrides Overrides => new(OrNull(_background), OrNull(_titleBar), OrNull(_panel), OrNull(_control), OrNull(_switch), OrNull(_toggle), OrNull(_text));

    private double? FontPoints => _fontSize.Value >= ThemeManager.MinFontPoints ? _fontSize.Value : null;

    private static TextBlock Hint(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        return block;
    }

    private static Grid Row(string label, UIElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        return grid;
    }

    /// <summary>The theme file the editor works on: the one whose name is loaded (null for a first save or a built-in start).</summary>
    private string? _currentName;

    /// <summary>A name is required. Saving over a theme this editor loaded asks first: overwrite it or make a new one. The fields are also kept as the
    /// "Personal" look; the saved theme becomes the active one.</summary>
    private void Save()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string name = ThemeFile.CleanName(_name.Text);
        if (name.Length == 0)
        {
            ThemedMessageBox.Show(this, loc["ThemeEditor.NameRequired"], Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            _name.Focus();
            return;
        }

        if (ThemeFile.IsReserved(name))
        {
            ThemedMessageBox.Show(this, loc["ThemeEditor.NameReserved"], Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool sameAsCurrent = _currentName is not null && string.Equals(_currentName, name, StringComparison.OrdinalIgnoreCase);
        if (_currentName is not null)
        {
            MessageBoxResult answer = ThemedMessageBox.Show(this, string.Format(loc["ThemeEditor.OverwriteAsk"], _currentName), Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel)
            {
                return;
            }

            if (answer == MessageBoxResult.Yes && !sameAsCurrent)
            {
                name = _currentName; // overwrite means this theme's own file, whatever else is in the name box
                _name.Text = name;
            }

            if (answer == MessageBoxResult.No && sameAsCurrent)
            {
                ThemedMessageBox.Show(this, string.Format(loc["ThemeEditor.NameTaken"], _currentName), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                _name.Focus();
                return;
            }
        }
        else if (File.Exists(ThemeFile.PathFor(name)) &&
                 ThemedMessageBox.Show(this, string.Format(loc["ThemeEditor.ExistsAsk"], name), Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        ThemeFile file = CurrentFile();
        file.Name = name;
        if (!file.Write(ThemeFile.PathFor(name)))
        {
            ThemedMessageBox.Show(this, loc["ThemeEditor.ImportFailed"], Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _settings.Theme = ThemeFile.SelectionFor(name);
        _currentName = name;
        FillThemeList(name);
        Saved?.Invoke(_settings);
    }

    /// <summary>Writes the fields (not the saved settings) to a .aiondps-theme file to share or keep.</summary>
    private void ExportTheme()
    {
        string name = ThemeFile.CleanName(_name.Text);
        if (name.Length == 0)
        {
            ThemedMessageBox.Show(this, LocalizationManager.Instance["ThemeEditor.NameRequired"], Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            _name.Focus();
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Aion DPS design (*.aiondps-theme)|*.aiondps-theme|JSON (*.json)|*.json",
            FileName = name + ThemeFile.Extension,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ThemeFile exported = CurrentFile();
        exported.Name = name;
        if (!exported.Write(dialog.FileName))
        {
            ThemedMessageBox.Show(this, LocalizationManager.Instance["ThemeEditor.ImportFailed"], Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private ThemeFile CurrentFile() => new()
    {
        Background = OrNull(_background), TitleBar = OrNull(_titleBar), Panel = OrNull(_panel), Control = OrNull(_control), Switch = OrNull(_switch),
        Toggle = OrNull(_toggle), Bar = BarNow(), Text = OrNull(_text), FontSize = FontPoints, Classes = ClassColorsNow(),
    };

    /// <summary>Loads a theme file into the fields and the preview; "Save and apply" makes it the real look.</summary>
    private void ImportTheme()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Aion DPS design (*.aiondps-theme;*.json)|*.aiondps-theme;*.json" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ThemeFile? file = ThemeFile.Read(dialog.FileName);
        if (file is null)
        {
            ThemedMessageBox.Show(this, LocalizationManager.Instance["ThemeEditor.ImportFailed"], Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        LoadFile(file);
        _currentName = null; // an imported theme is a new one until it is saved
        FillThemeList(null);
        _name.Text = ThemeFile.CleanName(file.Name) is { Length: > 0 } named ? named : ThemeFile.CleanName(Path.GetFileNameWithoutExtension(dialog.FileName));
    }

    private void LoadFile(ThemeFile file)
    {
        _loading = true; // one repaint at the end, not one per field
        _background.Hex = file.Background ?? "";
        _titleBar.Hex = file.TitleBar ?? "";
        _panel.Hex = file.Panel ?? "";
        _control.Hex = file.Control ?? "";
        _switch.Hex = file.Switch ?? "";
        _toggle.Hex = file.Toggle ?? "";
        _bar.Hex = file.Bar ?? "";
        _barMode.SelectedIndex = string.IsNullOrEmpty(file.Bar) ? 0 : 1; // a file with one bar colour opens in that mode
        ShowBarMode();
        _text.Hex = file.Text ?? "";
        _fontSize.Value = file.FontSize is double pt ? Math.Clamp(pt, ThemeManager.MinFontPoints, _fontSize.Maximum) : _fontSize.Minimum;
        foreach (var (cls, field) in _classFields)
        {
            field.Hex = file.Classes is not null && file.Classes.TryGetValue(cls, out string? hex) ? hex : "";
        }

        _loading = false;
        Rebuild();
    }

    /// <summary>Builds the preview again from the fields; the panel's own dictionary carries the draft colours.</summary>
    private void Rebuild()
    {
        if (_loading)
        {
            return;
        }

        _fontValue.Text = FontPoints is double pt ? $"{pt:0} pt" : "–";
        double size = ThemeManager.FontPointsFor(_settings.FontSize, FontPoints);
        ResourceDictionary theme = ThemeManager.Build(_settings.Theme, Overrides);
        Color? barColor = ThemeManager.TryParse(BarNow());
        _previewClasses = ClassColorsNow();

        var panel = new StackPanel();
        TextElement.SetFontSize(panel, size);
        panel.Resources.MergedDictionaries.Add(theme);
        ThemeManager.AddOverlayFontSizes(panel.Resources, size);
        panel.Children.Add(Caption(LocalizationManager.Instance["ThemeEditor.PreviewMain"]));
        panel.Children.Add(MainWindowMock(barColor));
        panel.Children.Add(Caption(LocalizationManager.Instance["ThemeEditor.PreviewSettings"]));
        panel.Children.Add(SettingsMock());
        panel.Children.Add(Caption(LocalizationManager.Instance["ThemeEditor.PreviewOverlay"]));
        panel.Children.Add(OverlayMock(barColor));
        panel.Children.Add(Caption(LocalizationManager.Instance["ThemeEditor.PreviewBossHp"]));
        panel.Children.Add(BossHpMock());
        panel.Children.Add(Caption(LocalizationManager.Instance["ThemeEditor.PreviewTarget"]));
        panel.Children.Add(TargetMock());

        _preview.Background = new SolidColorBrush((Color)theme["Color.Window"]);
        _preview.Child = panel;
        _preview.Resources.MergedDictionaries.Clear();
        _preview.Resources.MergedDictionaries.Add(theme);
        _preview.SetResourceReference(BackgroundProperty, "Brush.Window");
    }

    private static TextBlock Caption(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        return block;
    }

    private static Dictionary<string, string> _previewClasses = new();

    private static Brush Bar(string className, Color? own, byte alpha)
    {
        Color c = own ?? (_previewClasses.TryGetValue(className, out string? hex) ? ThemeManager.TryParse(hex) : null) ?? ClassBrushes.ClassColor(className);
        return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
    }

    private static TextBlock Text(string text, string brush, bool right = false, bool bold = false)
    {
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        if (bold)
        {
            block.FontWeight = FontWeights.SemiBold;
        }

        return block;
    }

    private static UIElement MainWindowMock(Color? barColor)
    {
        var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true };
        frame.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        frame.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        var stack = new StackPanel();

        var title = new Border { Padding = new Thickness(10, 6, 10, 6), Child = Text("Aion DPS", "Brush.Text", bold: true) };
        title.SetResourceReference(Border.BackgroundProperty, "Brush.TitleBar");
        stack.Children.Add(title);

        var header = new Grid { Margin = new Thickness(10, 8, 10, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        header.Children.Add(Text("Name", "Brush.TextSubtle"));
        var dps = Text("DPS", "Brush.TextSubtle", right: true);
        Grid.SetColumn(dps, 1);
        header.Children.Add(dps);
        var total = Text("Damage", "Brush.TextSubtle", right: true);
        Grid.SetColumn(total, 2);
        header.Children.Add(total);
        stack.Children.Add(header);

        int index = 0;
        foreach (var (name, cls, dpsText, totalText, fill) in Sample)
        {
            var row = new Grid { MinHeight = 32 };
            row.SetResourceReference(Panel.BackgroundProperty, index++ % 2 == 0 ? "Brush.Panel" : "Brush.RowAlt");
            var bar = new Border { HorizontalAlignment = HorizontalAlignment.Left, Background = Bar(cls, barColor, 0x6B) };
            row.SizeChanged += (_, e) => bar.Width = e.NewSize.Width * fill / 100;
            row.Children.Add(bar);

            var cells = new Grid { Margin = new Thickness(10, 4, 10, 4) };
            cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            cells.Children.Add(Text(name, "Brush.Text", bold: true));
            var d = Text(dpsText, "Brush.Text", right: true);
            Grid.SetColumn(d, 1);
            cells.Children.Add(d);
            var t = Text(totalText, "Brush.TextSubtle", right: true);
            Grid.SetColumn(t, 2);
            cells.Children.Add(t);
            row.Children.Add(cells);
            stack.Children.Add(row);
        }

        var footer = new Grid { Margin = new Thickness(10, 8, 10, 8) };
        footer.Children.Add(Text("Elapsed 02:41 · Muted / disabled text", "Brush.TextMuted"));
        var button = new Border { Padding = new Thickness(12, 3, 12, 3), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Right, Child = Text("Reset", "Brush.Text") };
        button.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        button.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        footer.Children.Add(button);
        stack.Children.Add(footer);
        frame.Child = stack;
        return frame;
    }

    private static Border Switch(bool on)
    {
        var knob = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Margin = new Thickness(2, 0, 2, 0), HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left };
        knob.SetResourceReference(Border.BackgroundProperty, on ? "Brush.Window" : "Brush.TextSubtle");
        var track = new Border { Width = 38, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1), Child = knob, VerticalAlignment = VerticalAlignment.Center };
        track.SetResourceReference(Border.BackgroundProperty, on ? "Brush.Switch" : "Brush.Control");
        track.SetResourceReference(Border.BorderBrushProperty, on ? "Brush.Switch" : "Brush.CheckBorder");
        return track;
    }

    private static Border Segment(string text, bool on, CornerRadius corner)
    {
        var label = Text(text, on ? "Brush.Window" : "Brush.Text", bold: on);
        var seg = new Border { Padding = new Thickness(12, 3, 12, 3), BorderThickness = new Thickness(1), CornerRadius = corner, Child = label };
        seg.SetResourceReference(Border.BackgroundProperty, on ? "Brush.Toggle" : "Brush.ControlHover");
        seg.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return seg;
    }

    /// <summary>A block of the settings: two switches, a button and the PVE/PVP pair, as the real windows draw them.</summary>
    private static UIElement SettingsMock()
    {
        var card = new Border { Padding = new Thickness(14, 10, 14, 10), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Width = 420, HorizontalAlignment = HorizontalAlignment.Left };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var stack = new StackPanel();
        var title = Text("Anzeige", "Brush.TextMuted", bold: true);
        title.Margin = new Thickness(0, 0, 0, 6);
        stack.Children.Add(title);
        foreach (bool on in new[] { true, false })
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var sw = Switch(on);
            DockPanel.SetDock(sw, Dock.Right);
            row.Children.Add(sw);
            row.Children.Add(Text(on ? "Always on top" : "Show damage taken", "Brush.Text"));
            stack.Children.Add(row);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var button = new Border { Padding = new Thickness(12, 3, 12, 3), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 14, 0), Child = Text("Button", "Brush.Text") };
        button.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        button.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        buttons.Children.Add(button);
        buttons.Children.Add(Segment("PVE", true, new CornerRadius(4, 0, 0, 4)));
        buttons.Children.Add(Segment("PVP", false, new CornerRadius(0, 4, 4, 0)));
        stack.Children.Add(buttons);
        card.Child = stack;
        return card;
    }

    /// <summary>The boss hit point bar as the combat overlay draws it (the real view; it keeps its own fixed colours).</summary>
    private static UIElement BossHpMock()
    {
        var bar = new HpBarView { Width = 420, HorizontalAlignment = HorizontalAlignment.Left };
        bar.Show(6_480_000, 9_000_000, debuffed: false, elapsed: TimeSpan.FromSeconds(161));
        return bar;
    }

    /// <summary>The target of the target: picture, name and hit points, as in the combat overlay.</summary>
    private static UIElement TargetMock()
    {
        var data = new TargetOfTargetData("Tahabata Pyrelord", "Aahz", null, IsBoss: true, Hp: 6_480_000, MaxHp: 9_000_000);
        var icon = new TargetIconView { Width = 56, Height = 56, Margin = new Thickness(0, 0, 6, 0) };
        icon.Show(data);
        var name = new TargetNameView { Margin = new Thickness(0, 0, 0, 4) };
        name.Show(data);
        var hp = new HpBarView();
        hp.Show(data.Hp, data.MaxHp, debuffed: true);
        var right = new StackPanel { Width = 354, Children = { name, hp } };
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, right } };
    }

    private static UIElement OverlayMock(Color? barColor)
    {
        var frame = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 5, 6, 5), Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        frame.SetResourceReference(Border.BackgroundProperty, "Brush.OverlayBg");
        var stack = new StackPanel();
        var head = Text("Boss · 02:41", "Brush.OverlayText", bold: true);
        head.Margin = new Thickness(2, 0, 0, 4);
        stack.Children.Add(head);
        foreach (var (name, cls, dpsText, _, fill) in Sample.Take(3))
        {
            var row = new Grid { MinHeight = 20, Margin = new Thickness(0, 1, 0, 1) };
            var track = new Border { CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)) };
            var bar = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Background = Bar(cls, barColor, 0x73) };
            row.SizeChanged += (_, e) => bar.Width = e.NewSize.Width * fill / 100;
            row.Children.Add(track);
            row.Children.Add(bar);
            var cells = new DockPanel { Margin = new Thickness(4, 0, 4, 0) };
            var d = Text(dpsText, "Brush.OverlayText", right: true);
            DockPanel.SetDock(d, Dock.Right);
            cells.Children.Add(d);
            cells.Children.Add(Text(name, "Brush.OverlayText"));
            row.Children.Add(cells);
            stack.Children.Add(row);
        }

        frame.Child = stack;
        return frame;
    }
}
