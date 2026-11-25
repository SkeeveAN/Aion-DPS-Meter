using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// The page of the look "Own" in the settings of the DPS meter: a drop-down with the modes the player made (empty at first, only "create a mode"), the name and the sorting
/// of the mode, its template with the settings of the field the cursor stands in, the preview and the buttons Save and Discard. The modes are kept as a working copy:
/// nothing is saved before the player says so, and "Discard" goes back to the saved state.
/// </summary>
public sealed class OwnLookEditor : StackPanel
{
    private const string NewEntry = "+new";
    private readonly LocalizationManager _loc = LocalizationManager.Instance;
    private List<OwnMode> _saved;
    private List<OwnMode> _work;
    private string _selectedId = "";
    private bool _busy;
    private bool _hadFocus;

    private readonly ComboBox _modeBox = new() { Width = 210, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _modePlaceholder;
    private readonly TextBox _nameBox = new() { Width = 150, MaxLength = 24 };
    private readonly ComboBox _sortBox = new() { Width = 130, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _deleteButton = new() { Height = 30, Padding = new Thickness(16, 0, 16, 0), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _templateBox = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), FontSize = 13, MinHeight = 76, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(6, 5, 6, 5),
    };
    private readonly TextBlock _errorText = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xe5, 0x73, 0x73)), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _selectedText = new() { Margin = new Thickness(0, 10, 0, 4), FontSize = 11.5 };
    private readonly Grid _inspector = new();
    private readonly TextBox _widthBox = new() { Width = 52, MaxLength = 3, HorizontalContentAlignment = HorizontalAlignment.Right };
    private readonly TextBox _sizeBox = new() { Width = 52, MaxLength = 3, HorizontalContentAlignment = HorizontalAlignment.Right };
    private readonly CheckBox _boldBox = new();
    private readonly RadioButton _longBox = new() { GroupName = "OwnNumber", Margin = new Thickness(0, 0, 10, 0) };
    private readonly RadioButton _shortBox = new() { GroupName = "OwnNumber" };
    private readonly RadioButton _leftBox = new() { GroupName = "OwnSide", Margin = new Thickness(0, 0, 10, 0) };
    private readonly RadioButton _rightBox = new() { GroupName = "OwnSide" };
    private readonly CheckBox _symbolBox = new();
    private readonly StackPanel _preview = new();
    private readonly Button _saveButton = new() { Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 0, 8, 0), IsDefault = false };
    private readonly Button _discardButton = new() { Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 0, 10, 0) };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
    private readonly TextBlock _emptyHint = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) };
    private readonly StackPanel _body = new();
    private ParsedTemplate _parsed = new();

    public OwnLookEditor()
    {
        MeterSettings now = MeterSettings.Load();
        _saved = now.OwnModes.Select(m => m.Copy()).ToList();
        _work = _saved.Select(m => m.Copy()).ToList();
        _selectedId = (_work.FirstOrDefault(m => m.Id == now.OwnModeId) ?? _work.FirstOrDefault())?.Id ?? "";

        Brush muted = Res("Brush.TextMuted"), text = Res("Brush.Text");
        foreach (TextBox box in new[] { _nameBox, _templateBox, _widthBox, _sizeBox })
        {
            box.Background = Res("Brush.Window");
            box.Foreground = text;
            box.CaretBrush = text;
            box.BorderBrush = Res("Brush.Border");
            box.BorderThickness = new Thickness(1);
            box.VerticalContentAlignment = VerticalAlignment.Center;
        }

        _nameBox.Height = 30;
        _widthBox.Height = 28;
        _sizeBox.Height = 28;
        _modeBox.Height = 30;
        _sortBox.Height = 30;
        _selectedText.Foreground = text;
        _status.Foreground = muted;
        _emptyHint.Foreground = muted;
        _emptyHint.Text = _loc["Own.Empty"];

        // the mode, its name, its sorting, delete: one line
        _modePlaceholder = new TextBlock { Text = _loc["Own.PickMode"], Foreground = muted, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var modeHost = new Grid { Width = 210, HorizontalAlignment = HorizontalAlignment.Left };
        modeHost.Children.Add(_modeBox);
        modeHost.Children.Add(_modePlaceholder);
        foreach ((string key, _) in OverlayTemplate.SortKeys)
        {
            _sortBox.Items.Add(new ComboBoxItem { Content = _loc["Own.Sort." + key], Tag = key });
        }

        _deleteButton.Content = _loc["Own.Delete"];
        var line = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        for (int i = 0; i < 4; i++)
        {
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        }

        UIElement[] cells = { Labeled(_loc["Own.Mode"], modeHost), Labeled(_loc["Own.ModeName"], _nameBox), Labeled(_loc["Own.SortBy"], _sortBox), Labeled(" ", _deleteButton) };
        for (int i = 0; i < cells.Length; i++)
        {
            ((FrameworkElement)cells[i]).Margin = new Thickness(i == 0 ? 0 : 18, 0, 0, 0);
            Grid.SetColumn(cells[i], i);
            line.Children.Add(cells[i]);
        }

        Children.Add(line);
        Children.Add(_emptyHint);

        // the template and the field the cursor stands in (left), the preview (right)
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel();
        left.Children.Add(Muted(_loc["Own.Template"]));
        left.Children.Add(_templateBox);
        left.Children.Add(_errorText);
        left.Children.Add(_selectedText);
        left.Children.Add(BuildInspector());
        left.Children.Add(Muted(_loc["Own.Insert"], new Thickness(0, 10, 0, 3)));
        left.Children.Add(BuildInsertButtons());
        var right = new StackPanel();
        right.Children.Add(Muted(_loc["Own.Preview"]));
        var previewBox = new Border { Background = Res("Brush.Control"), BorderBrush = Res("Brush.Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(8), Child = _preview };
        _preview.Resources["Brush.OverlayBg"] = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
        _preview.Resources["Brush.OverlayText"] = Brushes.White;
        _preview.Resources["Brush.OverlayEnemy"] = new SolidColorBrush(Color.FromArgb(150, 90, 20, 20));
        right.Children.Add(previewBox);
        right.Children.Add(Muted(_loc["Own.PreviewNote"], new Thickness(0, 6, 0, 0)));
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 2);
        columns.Children.Add(left);
        columns.Children.Add(right);
        _body.Children.Add(columns);

        // Save / Discard
        var bar = new Border { Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(0, 12, 0, 0), BorderBrush = Res("Brush.Border"), BorderThickness = new Thickness(0, 1, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        _saveButton.Content = _loc["Own.Save"];
        _discardButton.Content = _loc["Own.Discard"];
        buttons.Children.Add(_saveButton);
        buttons.Children.Add(_discardButton);
        buttons.Children.Add(_status);
        bar.Child = buttons;
        _body.Children.Add(bar);
        Children.Add(_body);

        _modeBox.SelectionChanged += OnModeChosen;
        _nameBox.TextChanged += (_, _) => Edit(m => m.Name = _nameBox.Text);
        _sortBox.SelectionChanged += (_, _) =>
        {
            if (_sortBox.SelectedItem is ComboBoxItem { Tag: string key })
            {
                Edit(m => m.SortBy = key);
            }
        };
        _templateBox.TextChanged += (_, _) => Edit(m => m.Template = _templateBox.Text);
        _templateBox.SelectionChanged += (_, _) => ShowSelectedField();
        _templateBox.GotKeyboardFocus += (_, _) => _hadFocus = true;
        _nameBox.LostFocus += (_, _) => RefreshNames();
        _deleteButton.Click += OnDelete;
        _saveButton.Click += (_, _) => Save();
        _discardButton.Click += (_, _) =>
        {
            _work = _saved.Select(m => m.Copy()).ToList();
            if (_work.All(m => m.Id != _selectedId))
            {
                _selectedId = _work.FirstOrDefault()?.Id ?? "";
            }

            Rebuild();
        };
        Rebuild();
    }

    /// <summary>Drives the editor of a page like a player would (choosing, typing, clicking) and tells what came out; for "render-tool" with AIONDPS_OWN_TEST, nothing is shown.</summary>
    internal static void RunInteractionTest(DependencyObject root)
    {
        static IEnumerable<T> Descend<T>(DependencyObject parent) where T : DependencyObject
        {
            foreach (object child in LogicalTreeHelper.GetChildren(parent))
            {
                if (child is not DependencyObject d)
                {
                    continue;
                }

                if (d is T hit)
                {
                    yield return hit;
                }

                foreach (T deeper in Descend<T>(d))
                {
                    yield return deeper;
                }
            }
        }

        OwnLookEditor? editor = Descend<OwnLookEditor>(root).FirstOrDefault();
        if (editor is null)
        {
            Console.WriteLine("own-test: no editor on the page");
            return;
        }

        bool ok = true;
        void Check(string what, bool good)
        {
            ok &= good;
            Console.WriteLine($"own-test: {what}: {good}");
        }

        ComboBoxItem create = editor._modeBox.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == NewEntry);
        editor._modeBox.SelectedItem = create;
        Check("a mode is made by choosing \"create\"", editor._work.Count == 1 && editor._selectedId == editor._work[0].Id && editor._saveButton.IsEnabled);
        editor._templateBox.Text = "%NAME%(16) %DPS%";
        editor._templateBox.CaretIndex = 3;
        Check("the cursor in a field selects it", editor._selectedText.Text.Contains("%NAME%") && editor._inspector.IsEnabled);
        editor._widthBox.Text = "20";
        Check("the width box writes into the template", editor._templateBox.Text == "%NAME%(20) %DPS%");
        editor._boldBox.IsChecked = true;
        editor._boldBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check("bold writes into the template", editor._templateBox.Text == "%NAME%(20,b) %DPS%");
        editor._templateBox.CaretIndex = editor._templateBox.Text.Length;
        Button share = Descend<Button>(editor).First(b => Equals(b.Content, "%ANTEIL%") || Equals(b.Content, "%SHARE%"));
        share.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check("a variable button puts the variable at the cursor, behind a space", editor._templateBox.Text.StartsWith("%NAME%(20,b) %DPS% %") && editor._selectedText.Text.Contains("%"));
        editor._templateBox.Text = "%NAME% %DSP%";
        Check("an unknown variable is told", editor._errorText.Visibility == Visibility.Visible && editor._errorText.Text.Contains("DSP"));
        editor._templateBox.Text = "%NAME%(16,b) %DPS%(short,r)";
        editor._nameBox.Text = "Schaden";
        editor._sortBox.SelectedIndex = 1;
        editor._saveButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        MeterSettings saved = MeterSettings.Load();
        Check("save writes the mode into the settings", saved.OwnModes.Count == 1 && saved.OwnModes[0] is { Name: "Schaden", SortBy: "Dps", Template: "%NAME%(16,b) %DPS%(short,r)" } && saved.OwnModeId == saved.OwnModes[0].Id && !editor._saveButton.IsEnabled);
        editor._templateBox.Text = "changed";
        Check("a change makes Save active", editor._saveButton.IsEnabled && editor._discardButton.IsEnabled);
        editor._discardButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check("discard goes back to the saved state", editor._templateBox.Text == "%NAME%(16,b) %DPS%(short,r)" && !editor._saveButton.IsEnabled && MeterSettings.Load().OwnModes[0].Template == "%NAME%(16,b) %DPS%(short,r)");
        Console.WriteLine("own-test: " + (ok ? "ALL GOOD" : "FAILED"));
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    private TextBlock Muted(string text, Thickness? margin = null) => new() { Text = text, FontSize = 11, Foreground = Res("Brush.TextMuted"), Margin = margin ?? new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap };

    private UIElement Labeled(string label, UIElement control)
    {
        var stack = new StackPanel();
        stack.Children.Add(Muted(label));
        stack.Children.Add(control);
        return stack;
    }

    private OwnMode? Current => _work.FirstOrDefault(m => m.Id == _selectedId);

    // ------------------------------------------------------------------ inspector and buttons

    private UIElement BuildInspector()
    {
        for (int i = 0; i < 6; i++)
        {
            _inspector.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 5 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        }

        _longBox.Content = _loc["Own.NumLong"];
        _shortBox.Content = _loc["Own.NumShort"];
        _leftBox.Content = _loc["Own.AlignLeft"];
        _rightBox.Content = _loc["Own.AlignRight"];
        _symbolBox.Content = new TextBlock { Text = _loc["Own.NoSymbol"], TextWrapping = TextWrapping.NoWrap };
        var number = new StackPanel { Orientation = Orientation.Horizontal };
        number.Children.Add(_longBox);
        number.Children.Add(_shortBox);
        var side = new StackPanel { Orientation = Orientation.Horizontal };
        side.Children.Add(_leftBox);
        side.Children.Add(_rightBox);
        _symbolBox.VerticalAlignment = VerticalAlignment.Center;
        UIElement[] parts = { Labeled(_loc["Own.Width"], _widthBox), Labeled(_loc["Own.Size"], _sizeBox), Labeled(_loc["Own.Bold"], _boldBox), Labeled(_loc["Own.Number"], number), Labeled(_loc["Own.Align"], side), Labeled(" ", _symbolBox) };
        for (int i = 0; i < parts.Length; i++)
        {
            ((FrameworkElement)parts[i]).Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(parts[i], i);
            _inspector.Children.Add(parts[i]);
        }

        _widthBox.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        _sizeBox.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(c => char.IsDigit(c) || c is '+' or '-');
        _widthBox.TextChanged += (_, _) => ChangeField(f => f.Width = int.TryParse(_widthBox.Text, out int w) ? Math.Clamp(w, 0, 100) : 0);
        _sizeBox.TextChanged += (_, _) =>
        {
            if (_sizeBox.Text is "+" or "-")
            {
                return;
            }

            ChangeField(f => f.Size = int.TryParse(_sizeBox.Text, out int s) ? Math.Clamp(s, -6, 12) : 0);
        };
        _boldBox.Click += (_, _) => ChangeField(f => f.Bold = _boldBox.IsChecked == true);
        _longBox.Click += (_, _) => ChangeField(f => f.Short = false);
        _shortBox.Click += (_, _) => ChangeField(f => f.Short = true);
        _leftBox.Click += (_, _) => ChangeField(f => f.Right = false);
        _rightBox.Click += (_, _) => ChangeField(f => f.Right = true);
        _symbolBox.Click += (_, _) => ChangeField(f => f.NoSymbol = _symbolBox.IsChecked == true);
        var box = new Border { Background = Res("Brush.Control"), BorderBrush = Res("Brush.Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(10), Child = _inspector };
        return box;
    }

    private UIElement BuildInsertButtons()
    {
        var wrap = new WrapPanel();
        foreach (string german in OverlayTemplate.Offered)
        {
            // the German spelling for German, the English one for every other language (both mean the same in a template)
            string name = _loc.Language == "de" ? german : OverlayTemplate.EnglishOf(german);
            wrap.Children.Add(InsertButton("%" + name + "%", () => InsertVariable(name)));
        }

        wrap.Children.Add(InsertButton("↵ " + _loc["Own.NewLine"], () => InsertText("\\n")));
        wrap.Children.Add(InsertButton("⇥ " + _loc["Own.Tab"], () => InsertText("\\t")));
        return wrap;
    }

    private static Button InsertButton(string text, Action click)
    {
        var button = new Button { Content = text, FontFamily = new FontFamily("Consolas"), FontSize = 11.5, Padding = new Thickness(9, 2, 9, 3), Margin = new Thickness(0, 0, 6, 6), Focusable = false };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Puts text at the cursor of the template (at the end while the cursor was never in the box); the cursor then stands behind it.</summary>
    private void InsertText(string insert)
    {
        if (Current is null)
        {
            return;
        }

        int at = _hadFocus ? _templateBox.SelectionStart : _templateBox.Text.Length;
        int length = _hadFocus ? _templateBox.SelectionLength : 0;
        string text = _templateBox.Text;
        _templateBox.Text = text[..at] + insert + text[(at + length)..];
        _templateBox.CaretIndex = at + insert.Length;
        _hadFocus = true;
    }

    private void InsertVariable(string name)
    {
        string text = _templateBox.Text;
        int at = _hadFocus ? _templateBox.SelectionStart : text.Length;
        string lead = at > 0 && !char.IsWhiteSpace(text[at - 1]) && !(at >= 2 && text[at - 2] == '\\' && text[at - 1] is 'n' or 't') ? " " : "";
        InsertText(lead + "%" + name + "%");
    }

    private void ChangeField(Action<TemplateField> change)
    {
        if (_busy || Current is null)
        {
            return;
        }

        TemplateField? field = _parsed.FieldAt(_templateBox.SelectionStart);
        if (field is null)
        {
            return;
        }

        change(field);
        string text = _templateBox.Text;
        string written = field.ToText();
        _templateBox.Text = text[..field.Start] + written + text[(field.Start + field.Length)..];
        _templateBox.CaretIndex = field.Start + written.Length;
    }

    // ------------------------------------------------------------------ state

    private void OnModeChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_busy || _modeBox.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        if (tag == NewEntry)
        {
            int number = _work.Count + 1;
            while (_work.Any(m => m.Name == string.Format(_loc["Own.NewName"], number)))
            {
                number++;
            }

            var made = new OwnMode { Name = string.Format(_loc["Own.NewName"], number), Template = OverlayTemplate.DefaultText };
            _work.Add(made);
            _selectedId = made.Id;
        }
        else
        {
            _selectedId = tag;
        }

        Rebuild();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        OwnMode? mode = Current;
        if (mode is null)
        {
            return;
        }

        string ask = string.Format(_loc["Own.DeleteAsk"], mode.Name);
        if (ThemedMessageBox.Show(Window.GetWindow(this), ask, _loc["Own.Delete"], MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _work.Remove(mode);
        _selectedId = _work.FirstOrDefault()?.Id ?? "";
        Rebuild();
    }

    private void Save()
    {
        MeterSettings settings = MeterSettings.Load();
        settings.OwnModes = _work.Select(m => m.Copy()).ToList();
        settings.OwnModeId = _selectedId;
        settings.Save();
        _saved = _work.Select(m => m.Copy()).ToList();
        OwnRowView.RaiseModesSaved();
        UpdateState();
    }

    /// <summary>Changes the mode in work and draws what depends on it.</summary>
    private void Edit(Action<OwnMode> change)
    {
        if (_busy || Current is not { } mode)
        {
            return;
        }

        change(mode);
        if (mode.Id == _selectedId && !_nameBox.IsKeyboardFocusWithin)
        {
            // a renamed mode shows its new name in the list when the box is left
        }

        _parsed = OverlayTemplate.Parse(mode.Template);
        ShowErrors();
        ShowSelectedField();
        DrawPreview();
        UpdateState();
    }

    /// <summary>Everything again from the working copy (a mode was chosen, made, deleted or the changes thrown away).</summary>
    private void Rebuild()
    {
        _busy = true;
        try
        {
            _modeBox.Items.Clear();
            foreach (OwnMode m in _work)
            {
                _modeBox.Items.Add(new ComboBoxItem { Content = m.Name, Tag = m.Id });
            }

            _modeBox.Items.Add(new ComboBoxItem { Content = _loc["Own.NewMode"], Tag = NewEntry, FontWeight = FontWeights.SemiBold });
            ComboBoxItem? chosen = _modeBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _selectedId);
            _modeBox.SelectedItem = chosen;
            _modePlaceholder.Visibility = chosen is null ? Visibility.Visible : Visibility.Collapsed;
            OwnMode? mode = Current;
            bool any = mode is not null;
            _emptyHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            _body.IsEnabled = any;
            _body.Opacity = any ? 1 : 0.35;
            _nameBox.IsEnabled = any;
            _sortBox.IsEnabled = any;
            _deleteButton.IsEnabled = any;
            _nameBox.Text = mode?.Name ?? "";
            _sortBox.SelectedItem = _sortBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == (mode?.SortBy ?? "Damage"));
            _templateBox.Text = mode?.Template ?? "";
            _parsed = OverlayTemplate.Parse(mode?.Template);
        }
        finally
        {
            _busy = false;
        }

        ShowErrors();
        ShowSelectedField();
        DrawPreview();
        UpdateState();
    }

    /// <summary>The names in the drop-down follow an edited name once the player leaves the name box.</summary>
    private void RefreshNames()
    {
        _busy = true;
        foreach (ComboBoxItem item in _modeBox.Items.OfType<ComboBoxItem>().Where(i => (string)i.Tag != NewEntry))
        {
            item.Content = _work.FirstOrDefault(m => m.Id == (string)item.Tag)?.Name ?? "";
        }

        _busy = false;
    }

    private void UpdateState()
    {
        bool dirty = !SameModes(_saved, _work);
        _saveButton.IsEnabled = dirty;
        _discardButton.IsEnabled = dirty;
        _status.Text = dirty ? "●  " + _loc["Own.Dirty"] : "✓  " + _loc["Own.Saved"];
        _status.Foreground = dirty ? Res("Brush.Accent") : Res("Brush.TextMuted");
        _saveButton.Background = dirty ? Res("Brush.Accent") : Res("Brush.Control");
        _saveButton.Foreground = dirty ? Brushes.Black : Res("Brush.Text");
        if (!_nameBox.IsKeyboardFocusWithin)
        {
            RefreshNames();
        }
    }

    private static bool SameModes(List<OwnMode> a, List<OwnMode> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.Id == p.Second.Id && p.First.Name == p.Second.Name && p.First.Template == p.Second.Template && p.First.SortBy == p.Second.SortBy);

    private void ShowErrors()
    {
        _errorText.Text = string.Join("\n", _parsed.Errors.Select(e => e.Kind switch
        {
            "Variable" => string.Format(_loc["Own.Err.Variable"], e.Arg),
            "Option" => string.Format(_loc["Own.Err.Option"], e.Arg),
            _ => _loc["Own.Err.Bracket"],
        }));
        _errorText.Visibility = _parsed.Errors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The inspector shows the field the cursor stands in (or says that the cursor is between the fields).</summary>
    private void ShowSelectedField()
    {
        TemplateField? field = _parsed.FieldAt(_templateBox.SelectionStart);
        _busy = true;
        try
        {
            _selectedText.Text = field is null ? _loc["Own.SelectedNone"] : _loc["Own.Selected"] + "  %" + field.Written + "%";
            _inspector.IsEnabled = field is not null;
            _inspector.Opacity = field is null ? 0.4 : 1;
            if (field is not null)
            {
                // a box the player is typing in keeps what he typed (a lone "-" or "+" is on its way to a number)
                if (!_widthBox.IsKeyboardFocusWithin)
                {
                    _widthBox.Text = field.Width > 0 ? field.Width.ToString() : "";
                }

                if (!_sizeBox.IsKeyboardFocusWithin)
                {
                    _sizeBox.Text = field.Size != 0 ? (field.Size > 0 ? "+" : "") + field.Size : "";
                }
                _boldBox.IsChecked = field.Bold;
                _longBox.IsChecked = !field.Short;
                _shortBox.IsChecked = field.Short;
                _leftBox.IsChecked = !field.Right;
                _rightBox.IsChecked = field.Right;
                bool hasSymbol = field.Var is TemplateVar.Taken or TemplateVar.Heal or TemplateVar.Power or TemplateVar.Deaths;
                _symbolBox.IsEnabled = hasSymbol;
                _symbolBox.IsChecked = field.NoSymbol;
            }
        }
        finally
        {
            _busy = false;
        }

        DrawPreview();
    }

    private static readonly (string Name, string Class, long Damage, double Dps, long Taken, long Heal, int Power, int Deaths)[] Sample =
    {
        ("Jinwoo", "Gladiator", 2_514_236, 8300, 412_000, 0, 212_145, 0),
        ("Goku", "Ranger", 2_168_400, 7100, 198_500, 0, 203_900, 0),
        ("Ichigo", "Sorcerer", 1_904_100, 6800, 305_200, 0, 196_400, 1),
        ("Naruto", "Cleric", 934_200, 3900, 150_800, 1_384_500, 188_700, 0),
        ("Tsubasa", "Spiritmaster", 540_800, 2100, 88_000, 0, 172_500, 0),
    };

    private void DrawPreview()
    {
        _preview.Children.Clear();
        long total = Sample.Sum(s => s.Damage);
        TemplateField? marked = _parsed.FieldAt(_templateBox.SelectionStart);
        int rank = 0;
        foreach (var s in Sample)
        {
            var row = new PlayerRow(++rank)
            {
                Name = s.Name, ClassName = s.Class, Level = 55, Damage = s.Damage, Dps = s.Dps, SharePercent = 100.0 * s.Damage / total, FillPercent = 100.0 * s.Damage / Sample[0].Damage, Rank = rank,
                DamageTaken = s.Taken, Healing = s.Heal, ShowCombatPower = true, CombatPower = s.Power, Deaths = s.Deaths,
            };
            _preview.Children.Add(new OwnRowView { DataContext = row, Template = _parsed.Items.Count == 0 ? OverlayTemplate.Parse(OverlayTemplate.DefaultText) : _parsed, Marked = marked, ShareBars = true });
        }
    }
}
