using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;
using AionDPS.History;

namespace AionDPS.Ui;

/// <summary>
/// The dummy test window (Tools menu): "Capture" starts timed runs at a training dummy and keeps each as its own run, "Analyse" sets chosen runs side by side
/// (damage numbers and a timeline of skills and effects). Built in code, in the look of the other windows (<see cref="ThemedChrome"/>).
/// </summary>
public sealed class DummyTestWindow : Window
{
    private static readonly Color[] RunColors =
    {
        Color.FromRgb(0x5A, 0xA9, 0xFF), Color.FromRgb(0xFF, 0xBA, 0x42), Color.FromRgb(0x6F, 0xD6, 0x9B),
        Color.FromRgb(0xB5, 0x8C, 0xFF), Color.FromRgb(0xFF, 0x7A, 0xA8), Color.FromRgb(0x4F, 0xD1, 0xD1),
    };

    private readonly DummyTestController _controller;
    private readonly Action _toggleAllOverlays;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly Border _captureTab = new(), _analyseTab = new();
    private readonly Grid _capturePage = new(), _analysePage = new();

    // capture page
    private readonly TextBox _durationBox = new(), _noteBox = new();
    private readonly TextBlock _durationHint = new();
    private readonly Button _startButton = new(), _stopButton = new();
    private readonly TextBlock _runTitle = new(), _clockText = new(), _stateText = new(), _dpsText = new(), _damageText = new(), _hitsText = new();
    private readonly CheckBox _overlayOn = new(), _overlayLocked = new();
    private readonly StackPanel _runList = new();
    private bool _syncing;
    private int _lastSettingsSecond = -1;

    // analyse page
    private readonly WrapPanel _picker = new();
    private readonly StackPanel _result = new();
    private readonly HashSet<int> _selected = new();

    public DummyTestWindow(DummyTestController controller, Action toggleAllOverlays)
    {
        _controller = controller;
        _toggleAllOverlays = toggleAllOverlays;
        LocalizationManager loc = LocalizationManager.Instance;
        Title = loc["Dummy.Title"];
        Width = 1180;
        Height = 860;
        MinWidth = 900;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12, 10, 12, 12) };
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(tabs, Dock.Top);
        BuildTab(_captureTab, loc["Dummy.TabCapture"], () => ShowTab(0));
        BuildTab(_analyseTab, loc["Dummy.TabAnalyse"], () => ShowTab(1));
        tabs.Children.Add(_captureTab);
        tabs.Children.Add(_analyseTab);
        root.Children.Add(tabs);

        BuildCapturePage();
        BuildAnalysePage();
        var pages = new Grid();
        pages.Children.Add(_capturePage);
        pages.Children.Add(_analysePage);
        root.Children.Add(pages);
        Content = root;
        ThemedChrome.Apply(this);

        ShowTab(0);
        _controller.Model.Changed += OnModelChanged;
        _tick.Tick += (_, _) => Update();
        _tick.Start();
        Closed += (_, _) =>
        {
            _tick.Stop();
            _controller.Model.Changed -= OnModelChanged;
        };
        MeterSettings settings = MeterSettings.Load();
        _durationBox.Text = FormatDuration(TimeSpan.FromSeconds(settings.DummyTestSeconds));
        SyncOverlayBoxes(settings);
        RebuildRuns();
        Update();
    }

    // ---- small building blocks -------------------------------------------------------------------------------------

    private static TextBlock Label(string text, string brush = "Brush.Text", double size = 13, bool bold = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private static Border Card(UIElement child, Thickness? margin = null)
    {
        var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10), Margin = margin ?? new Thickness(0, 0, 0, 10), Child = child };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return border;
    }

    private static void StyleBox(TextBox box)
    {
        box.Padding = new Thickness(6, 4, 6, 4);
        box.VerticalContentAlignment = VerticalAlignment.Center;
        box.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
        box.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        box.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        box.SetResourceReference(TextBox.CaretBrushProperty, "Brush.Text");
    }

    private static Button MakeButton(string text, RoutedEventHandler click, double minWidth = 0)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0), MinWidth = minWidth };
        button.Click += click;
        return button;
    }

    private void BuildTab(Border tab, string text, Action select)
    {
        tab.Padding = new Thickness(18, 6, 18, 6);
        tab.Margin = new Thickness(0, 0, 8, 0);
        tab.BorderThickness = new Thickness(1);
        tab.CornerRadius = new CornerRadius(6);
        tab.Cursor = Cursors.Hand;
        tab.Child = Label(text, size: 14, bold: true);
        tab.MouseLeftButtonDown += (_, _) => select();
    }

    private void ShowTab(int index)
    {
        _capturePage.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        _analysePage.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        foreach ((Border tab, bool active) in new[] { (_captureTab, index == 0), (_analyseTab, index == 1) })
        {
            tab.SetResourceReference(Border.BackgroundProperty, active ? "Brush.ControlRaised" : "Brush.Control");
            tab.SetResourceReference(Border.BorderBrushProperty, active ? "Brush.Accent" : "Brush.Border");
        }

        if (index == 1)
        {
            RebuildPicker();
        }
    }

    // ---- capture ---------------------------------------------------------------------------------------------------

    private void BuildCapturePage()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var column = new StackPanel();

        // time, start and stop, note
        var controls = new WrapPanel { VerticalAlignment = VerticalAlignment.Bottom };
        var timeBlock = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        timeBlock.Children.Add(Label(loc["Dummy.Duration"], "Brush.TextSubtle", 12));
        var timeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        _durationBox.Width = 84;
        _durationBox.FontSize = 20;
        _durationBox.FontWeight = FontWeights.Bold;
        StyleBox(_durationBox);
        _durationBox.ToolTip = loc["Dummy.DurationHint"];
        timeRow.Children.Add(_durationBox);
        foreach (int seconds in new[] { 60, 120, 180, 300 })
        {
            int s = seconds;
            Button preset = MakeButton(FormatDuration(TimeSpan.FromSeconds(s)), (_, _) => _durationBox.Text = FormatDuration(TimeSpan.FromSeconds(s)));
            preset.Margin = new Thickness(6, 0, 0, 0);
            preset.Padding = new Thickness(8, 4, 8, 4);
            timeRow.Children.Add(preset);
        }

        timeBlock.Children.Add(timeRow);
        controls.Children.Add(timeBlock);

        _startButton.Content = loc["Dummy.Start"];
        _startButton.Padding = new Thickness(26, 9, 26, 9);
        _startButton.Margin = new Thickness(8, 0, 8, 0);
        _startButton.FontWeight = FontWeights.Bold;
        _startButton.Click += OnStart;
        _stopButton.Content = loc["Dummy.Stop"];
        _stopButton.Padding = new Thickness(20, 9, 20, 9);
        _stopButton.Margin = new Thickness(0, 0, 12, 0);
        _stopButton.Click += (_, _) =>
        {
            _controller.Model.Stop(DateTime.Now);
            Update();
        };
        controls.Children.Add(_startButton);
        controls.Children.Add(_stopButton);

        var noteBlock = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        noteBlock.Children.Add(Label(loc["Dummy.NoteNext"], "Brush.TextSubtle", 12));
        _noteBox.Width = 320;
        _noteBox.Margin = new Thickness(0, 4, 0, 0);
        StyleBox(_noteBox);
        _noteBox.ToolTip = loc["Dummy.NoteHint"];
        noteBlock.Children.Add(_noteBox);
        controls.Children.Add(noteBlock);
        _durationHint.Text = loc["Dummy.ClockHint"];
        _durationHint.FontSize = 12;
        _durationHint.Margin = new Thickness(0, 0, 0, 6);
        _durationHint.VerticalAlignment = VerticalAlignment.Bottom;
        _durationHint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        controls.Children.Add(_durationHint);
        column.Children.Add(Card(controls));

        // the state of the run now
        var status = new Grid();
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        for (int i = 0; i < 3; i++)
        {
            status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var left = new StackPanel();
        _runTitle.FontSize = 13;
        _runTitle.FontWeight = FontWeights.SemiBold;
        _runTitle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        _clockText.FontSize = 38;
        _clockText.FontWeight = FontWeights.Bold;
        _clockText.FontFamily = new FontFamily("Consolas");
        _clockText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        _stateText.FontSize = 12;
        _stateText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        left.Children.Add(_runTitle);
        left.Children.Add(_clockText);
        left.Children.Add(_stateText);
        status.Children.Add(left);
        AddStat(status, 1, loc["Dummy.DpsNow"], _dpsText);
        AddStat(status, 2, loc["Dummy.DamageTotal"], _damageText);
        AddStat(status, 3, loc["Dummy.Hits"], _hitsText);
        column.Children.Add(Card(status));

        // the overlay
        var overlay = new WrapPanel();
        overlay.Children.Add(Label(loc["Dummy.Overlay"], "Brush.TextSubtle", 13, true));
        ((TextBlock)overlay.Children[0]).Margin = new Thickness(0, 0, 18, 0);
        _overlayOn.Content = loc["Dummy.OverlayOn"];
        _overlayLocked.Content = loc["Settings.PetFarm.Locked"];
        foreach (CheckBox box in new[] { _overlayOn, _overlayLocked })
        {
            box.Margin = new Thickness(0, 0, 22, 0);
            box.VerticalAlignment = VerticalAlignment.Center;
            box.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
            box.Click += OnOverlayBoxChanged;
            overlay.Children.Add(box);
        }

        Button all = MakeButton(loc["Dummy.OverlayAll"], (_, _) =>
        {
            _toggleAllOverlays();
            SyncOverlayBoxes(MeterSettings.Load());
        });
        all.Margin = new Thickness(0, 0, 16, 0);
        overlay.Children.Add(all);
        var hint = Label(loc["Dummy.OverlayHint"], "Brush.TextMuted", 12);
        overlay.Children.Add(hint);
        var overlayStack = new StackPanel();
        overlayStack.Children.Add(overlay);
        overlayStack.Children.Add(OverlayControls.OpacityRow(OverlayPlaces.Dummy, 0.6));
        overlayStack.Children.Add(OverlayControls.ResetButton(OverlayPlaces.Dummy));
        column.Children.Add(Card(overlayStack));

        // the runs
        var header = Label(loc["Dummy.Runs"], "Brush.Text", 15, true);
        header.Margin = new Thickness(2, 4, 0, 6);
        column.Children.Add(header);
        column.Children.Add(_runList);

        _capturePage.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = column });
    }

    private static void AddStat(Grid grid, int column, string label, TextBlock value)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Label(label, "Brush.TextMuted", 12));
        value.FontSize = 24;
        value.FontWeight = FontWeights.Bold;
        value.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        stack.Children.Add(value);
        Grid.SetColumn(stack, column);
        grid.Children.Add(stack);
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        if (!TryParseDuration(_durationBox.Text, out TimeSpan planned))
        {
            _durationBox.SetResourceReference(Control.BorderBrushProperty, "Brush.Danger");
            return;
        }

        StyleBox(_durationBox);
        _durationBox.Text = FormatDuration(planned);
        _controller.Start(planned, _noteBox.Text.Trim());
        _noteBox.Text = "";
        Update();
    }

    private void OnOverlayBoxChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        MeterSettings settings = MeterSettings.Load();
        settings.ShowDummyOverlay = _overlayOn.IsChecked == true;
        settings.DummyOverlayLocked = _overlayLocked.IsChecked != true;
        settings.Save();
    }

    private void SyncOverlayBoxes(MeterSettings settings)
    {
        _syncing = true;
        _overlayOn.IsChecked = settings.ShowDummyOverlay;
        _overlayLocked.IsChecked = !settings.DummyOverlayLocked;
        _syncing = false;
    }

    /// <summary>"2:00", "90" (seconds) or "1:05:00" as a time between five seconds and an hour.</summary>
    public static bool TryParseDuration(string text, out TimeSpan span)
    {
        span = TimeSpan.Zero;
        string[] parts = text.Trim().Split(':');
        var numbers = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        double seconds = parts.Length switch
        {
            1 => numbers[0],
            2 => numbers[0] * 60 + numbers[1],
            3 => numbers[0] * 3600 + numbers[1] * 60 + numbers[2],
            _ => -1,
        };
        if (seconds is < 5 or > 3600)
        {
            return false;
        }

        span = TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static string FormatDuration(TimeSpan span) => $"{(int)span.TotalMinutes}:{span.Seconds:00}";

    private void OnModelChanged() => Dispatcher.BeginInvoke(new Action(() =>
    {
        RebuildRuns();
        if (_analysePage.Visibility == Visibility.Visible)
        {
            RebuildPicker();
        }

        Update();
    }));

    /// <summary>Redraws the clock and numbers of the run in progress (five times a second) and keeps the overlay boxes in step with the settings.</summary>
    private void Update()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        DummyTest model = _controller.Model;
        DateTime now = DateTime.Now;
        _startButton.IsEnabled = model.CanStart;
        _stopButton.IsEnabled = model.State is DummyState.Armed or DummyState.Running;
        DummyRun? run = model.Current;
        _runTitle.Text = model.State == DummyState.Idle || model.State == DummyState.Armed && run is null
            ? loc["Dummy.NoRun"]
            : string.Format(loc["Dummy.CurrentRun"], run?.Number ?? 0);
        switch (model.State)
        {
            case DummyState.Armed:
                _clockText.Text = DummyTestController.Clock(model.Planned);
                _stateText.Text = loc["Dummy.Waiting"];
                break;
            case DummyState.Running:
                _clockText.Text = DummyTestController.Clock(model.Remaining(now));
                _stateText.Text = string.Format(loc["Dummy.StateRunning"], run?.Start.ToString("HH:mm:ss", CultureInfo.CurrentCulture));
                break;
            case DummyState.Finished:
                _clockText.Text = loc["Dummy.Finished"];
                _stateText.Text = loc["Dummy.FinishedHint"];
                break;
            default:
                _clockText.Text = "–";
                _stateText.Text = loc["Dummy.Idle"];
                break;
        }

        if (run is not null && model.State != DummyState.Armed)
        {
            double seconds = Math.Max(1, Math.Min((now - run.Start).TotalSeconds, run.Length.TotalSeconds));
            long total = run.Events.Sum(e => e.Amount);
            _dpsText.Text = DummyTestController.Compact(total / seconds);
            _damageText.Text = DummyTestController.Compact(total);
            _hitsText.Text = run.Events.Where(e => !e.IsTick).Sum(e => Math.Max(1, e.HitCount)).ToString("N0");
        }
        else
        {
            _dpsText.Text = _damageText.Text = _hitsText.Text = "–";
        }

        if (now.Second != _lastSettingsSecond)
        {
            _lastSettingsSecond = now.Second;
            SyncOverlayBoxes(MeterSettings.Load());
        }
    }

    private void RebuildRuns()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        _runList.Children.Clear();
        string[] heads = { loc["Dummy.ColRun"], loc["Dummy.ColStart"], loc["Dummy.ColLength"], loc["Dummy.ColAvgDps"], loc["Dummy.ColMaxDps"], loc["Dummy.ColDamage"], loc["Dummy.ColNote"], "" };
        Grid Row(bool header) => RunGrid();
        Grid head = Row(true);
        for (int i = 0; i < heads.Length; i++)
        {
            TextBlock text = Label(heads[i], "Brush.TextSubtle", 12, true);
            text.Margin = new Thickness(8, 6, 8, 6);
            Grid.SetColumn(text, i);
            head.Children.Add(text);
        }

        head.SetResourceReference(Panel.BackgroundProperty, "Brush.Control");
        _runList.Children.Add(head);
        if (_controller.Model.Runs.Count == 0)
        {
            TextBlock none = Label(loc["Dummy.RunsNone"], "Brush.TextMuted", 13);
            none.Margin = new Thickness(8, 12, 8, 4);
            _runList.Children.Add(none);
            return;
        }

        int index = 0;
        foreach (DummyRun run in _controller.Model.Runs.Reverse())
        {
            DummyRun shown = run;
            Grid row = Row(false);
            if (index++ % 2 == 1)
            {
                row.SetResourceReference(Panel.BackgroundProperty, "Brush.RowAlt");
            }

            DummyStats? stats = run.Events.Count > 0 || run.Closed ? DummyAnalysis.Summarize(run) : null;
            string[] cells =
            {
                string.Format(loc["Dummy.RunNumber"], run.Number),
                run.Start.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
                run.Closed ? FormatDuration(run.Length) : $"{FormatDuration(run.Length)} …",
                stats is null ? "–" : stats.AvgDps.ToString("N0"),
                stats is null ? "–" : stats.MaxDps.ToString("N0"),
                run.Closed && stats is not null ? DummyTestController.Compact(stats.Total) : loc["Dummy.RunLive"],
            };
            for (int i = 0; i < cells.Length; i++)
            {
                TextBlock text = Label(cells[i], i == 5 && !run.Closed ? "Brush.Heal" : "Brush.Text", 13);
                text.Margin = new Thickness(8, 7, 8, 7);
                Grid.SetColumn(text, i);
                row.Children.Add(text);
            }

            var note = new TextBox { Text = run.Note, Margin = new Thickness(6, 3, 6, 3), ToolTip = loc["Dummy.NoteEdit"], Background = Brushes.Transparent, BorderThickness = new Thickness(0, 0, 0, 1) };
            StyleBox(note);
            note.Background = Brushes.Transparent;
            note.BorderThickness = new Thickness(0, 0, 0, 1);
            note.LostFocus += (_, _) =>
            {
                if (shown.Note != note.Text)
                {
                    shown.Note = note.Text.Trim();
                    _controller.Model.NoteEdited();
                }
            };
            note.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Keyboard.ClearFocus();
                    note.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                }
            };
            Grid.SetColumn(note, 6);
            row.Children.Add(note);

            var remove = new Button { Content = "✕", Width = 26, Height = 24, Margin = new Thickness(4, 0, 6, 0), Padding = new Thickness(0), ToolTip = loc["Dummy.Remove"] };
            remove.Click += (_, _) =>
            {
                if (ThemedMessageBox.Show(this, string.Format(loc["Dummy.RemoveAsk"], string.Format(loc["Dummy.RunNumber"], shown.Number)), loc["Dummy.Title"],
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _controller.Model.Remove(shown);
                }
            };
            remove.IsEnabled = run.Closed;
            Grid.SetColumn(remove, 7);
            row.Children.Add(remove);
            _runList.Children.Add(row);
        }
    }

    private static Grid RunGrid()
    {
        var grid = new Grid();
        double[] widths = { 90, 90, 100, 100, 100, 130, 0, 44 };
        for (int i = 0; i < widths.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = widths[i] == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(widths[i]) });
        }

        return grid;
    }

    // ---- analyse ---------------------------------------------------------------------------------------------------

    private void BuildAnalysePage()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var column = new StackPanel();
        var pick = new DockPanel();
        var compare = MakeButton(loc["Dummy.Compare"], (_, _) => RunCompare());
        compare.FontWeight = FontWeights.Bold;
        compare.Padding = new Thickness(22, 7, 22, 7);
        compare.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(compare, Dock.Right);
        pick.Children.Add(compare);
        var all = MakeButton(loc["Dummy.SelectAll"], (_, _) =>
        {
            foreach (DummyRun run in _controller.Model.Runs.Where(r => r.Closed))
            {
                _selected.Add(run.Number);
            }

            RebuildPicker();
        });
        all.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(all, Dock.Right);
        pick.Children.Add(all);
        pick.Children.Add(_picker);
        column.Children.Add(Card(pick));
        column.Children.Add(_result);
        _analysePage.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = column });
    }

    private void RebuildPicker()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        _picker.Children.Clear();
        var closed = _controller.Model.Runs.Where(r => r.Closed).ToList();
        _selected.IntersectWith(closed.Select(r => r.Number));
        if (closed.Count == 0)
        {
            _picker.Children.Add(Label(loc["Dummy.PickNone"], "Brush.TextMuted"));
            return;
        }

        foreach (DummyRun run in closed)
        {
            DummyRun shown = run;
            string text = string.Format(loc["Dummy.RunNumber"], run.Number) + (run.Note.Length > 0 ? " · " + run.Note : "") + " · " + FormatDuration(run.Length);
            var box = new CheckBox { Content = text, IsChecked = _selected.Contains(run.Number), Margin = new Thickness(0, 4, 22, 4), VerticalAlignment = VerticalAlignment.Center };
            box.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
            box.Click += (_, _) =>
            {
                if (box.IsChecked == true)
                {
                    _selected.Add(shown.Number);
                }
                else
                {
                    _selected.Remove(shown.Number);
                }
            };
            _picker.Children.Add(box);
        }
    }

    private void RunCompare()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        _result.Children.Clear();
        var runs = _controller.Model.Runs.Where(r => r.Closed && _selected.Contains(r.Number)).OrderBy(r => r.Number).ToList();
        if (runs.Count == 0)
        {
            _result.Children.Add(Label(loc["Dummy.CompareNone"], "Brush.TextMuted"));
            return;
        }

        var stats = runs.Select(DummyAnalysis.Summarize).ToList();
        TextBlock title = Label(loc["Dummy.Comparison"], "Brush.Text", 15, true);
        title.Margin = new Thickness(2, 4, 0, 6);
        _result.Children.Add(title);

        string[] heads =
        {
            loc["Dummy.ColRun"], loc["Dummy.ColLength"], loc["Dummy.ColAvgDps"], loc["Dummy.ColMinDps"], loc["Dummy.ColMaxDps"], loc["Dummy.ColMinHit"], loc["Dummy.ColMaxHit"],
            loc["Dummy.ColAvgHit"], loc["Dummy.ColDamage"], loc["Dummy.Hits"], loc["Dummy.ColCrit"], loc["Dummy.ColSkills"], loc["Dummy.ColDelta"], loc["Dummy.ColNote"],
        };
        double[] widths = { 150, 70, 90, 90, 90, 100, 100, 100, 110, 70, 70, 70, 90, 0 };
        Grid Row()
        {
            var grid = new Grid();
            foreach (double w in widths)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            }

            return grid;
        }

        Grid head = Row();
        head.SetResourceReference(Panel.BackgroundProperty, "Brush.Control");
        for (int i = 0; i < heads.Length; i++)
        {
            TextBlock text = Label(heads[i], "Brush.TextSubtle", 12, true);
            text.Margin = new Thickness(8, 6, 8, 6);
            Grid.SetColumn(text, i);
            head.Children.Add(text);
        }

        _result.Children.Add(head);
        for (int r = 0; r < runs.Count; r++)
        {
            DummyRun run = runs[r];
            DummyStats s = stats[r];
            Grid row = Row();
            if (r % 2 == 1)
            {
                row.SetResourceReference(Panel.BackgroundProperty, "Brush.RowAlt");
            }

            double delta = stats[0].AvgDps > 0 ? 100.0 * (s.AvgDps - stats[0].AvgDps) / stats[0].AvgDps : 0;
            string[] cells =
            {
                string.Format(loc["Dummy.RunNumber"], run.Number), FormatDuration(run.Length), s.AvgDps.ToString("N0"), s.MinDps.ToString("N0"), s.MaxDps.ToString("N0"),
                s.MinHit.ToString("N0"), s.MaxHit.ToString("N0"), s.AvgHit.ToString("N0"), DummyTestController.Compact(s.Total), s.Hits.ToString("N0"),
                s.CritPercent.ToString("0.0", CultureInfo.CurrentCulture) + " %", s.SkillUses.ToString("N0"),
                r == 0 ? "—" : (delta >= 0 ? "+" : "") + delta.ToString("0.0", CultureInfo.CurrentCulture) + " %", run.Note,
            };
            for (int i = 0; i < cells.Length; i++)
            {
                string brush = i == 12 && r > 0 ? (delta >= 0 ? "Brush.Heal" : "Brush.DangerSoft") : i == 13 ? "Brush.TextSubtle" : "Brush.Text";
                TextBlock text = Label(cells[i], brush, 13);
                text.Margin = new Thickness(8, 7, 8, 7);
                text.TextTrimming = TextTrimming.CharacterEllipsis;
                if (i == 0)
                {
                    var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(RunColors[r % RunColors.Length]), Margin = new Thickness(8, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                    text.Margin = new Thickness(26, 7, 8, 7);
                    Grid.SetColumn(dot, 0);
                    row.Children.Add(dot);
                }

                Grid.SetColumn(text, i);
                row.Children.Add(text);
            }

            _result.Children.Add(row);
        }

        TextBlock timelineTitle = Label(loc["Dummy.Timeline"], "Brush.Text", 15, true);
        timelineTitle.Margin = new Thickness(2, 18, 0, 6);
        _result.Children.Add(timelineTitle);
        var lanes = DummyAnalysis.Timeline(runs);
        var view = new DummyTimelineView(lanes, runs.Select((_, i) => RunColors[i % RunColors.Length]).ToList(), runs.Select(r => r.Length.TotalSeconds).ToList(),
            loc["Dummy.LaneSkills"], loc["Dummy.LaneEffects"], loc["Dummy.TimelineNone"]);
        _result.Children.Add(Card(view, new Thickness(0, 0, 0, 16)));
    }
}

/// <summary>The timeline of the compared runs: one line per skill (a tick per use) and per effect (a bar per stretch it was on), a thin row for each run in its colour.</summary>
public sealed class DummyTimelineView : FrameworkElement
{
    private const double LabelWidth = 230, Icon = 22, Sub = 9, Pad = 6, Section = 24, AxisHeight = 26;
    private readonly IReadOnlyList<DummyAnalysis.Lane> _lanes;
    private readonly IReadOnlyList<Color> _colors;
    private readonly double _length;
    private readonly string _skillsHeader, _effectsHeader, _empty;

    public DummyTimelineView(IReadOnlyList<DummyAnalysis.Lane> lanes, IReadOnlyList<Color> colors, IReadOnlyList<double> lengths, string skillsHeader, string effectsHeader, string empty)
    {
        _lanes = lanes;
        _colors = colors;
        _length = Math.Max(1, lengths.Count == 0 ? 1 : lengths.Max());
        _skillsHeader = skillsHeader;
        _effectsHeader = effectsHeader;
        _empty = empty;
        ClipToBounds = true;
    }

    private double LaneHeight => Math.Max(Icon + 4, _colors.Count * Sub + Pad);

    protected override Size MeasureOverride(Size available)
    {
        double sections = (_lanes.Any(l => !l.IsEffect) ? 1 : 0) + (_lanes.Any(l => l.IsEffect) ? 1 : 0);
        double height = _lanes.Count == 0 ? 30 : sections * Section + _lanes.Count * (LaneHeight + 2) + AxisHeight;
        return new Size(double.IsInfinity(available.Width) ? 900 : available.Width, height);
    }

    private Brush Themed(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private FormattedText Text(string text, double size, Brush brush, double maxWidth = 0, bool bold = false)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (maxWidth > 0)
        {
            formatted.MaxTextWidth = maxWidth;
            formatted.MaxLineCount = 1;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }

        return formatted;
    }

    protected override void OnRender(DrawingContext dc)
    {
        Brush text = Themed("Brush.Text", Brushes.White), muted = Themed("Brush.TextMuted", Brushes.Gray), accent = Themed("Brush.Accent", Brushes.Orange);
        Brush border = Themed("Brush.Border", Brushes.DimGray), track = Themed("Brush.Control", Brushes.Black);
        if (_lanes.Count == 0)
        {
            dc.DrawText(Text(_empty, 13, muted), new Point(2, 4));
            return;
        }

        double x0 = LabelWidth, x1 = Math.Max(x0 + 50, ActualWidth - 8), width = x1 - x0;
        double X(double seconds) => x0 + width * Math.Clamp(seconds / _length, 0, 1);
        double y = 0;
        foreach (bool effects in new[] { false, true })
        {
            var lanes = _lanes.Where(l => l.IsEffect == effects).ToList();
            if (lanes.Count == 0)
            {
                continue;
            }

            dc.DrawText(Text(effects ? _effectsHeader : _skillsHeader, 12.5, accent, bold: true), new Point(2, y + 3));
            dc.DrawLine(new Pen(border, 1), new Point(x0, y + Section - 4), new Point(x1, y + Section - 4));
            y += Section;
            foreach (DummyAnalysis.Lane lane in lanes)
            {
                double top = y + (LaneHeight - Icon) / 2;
                ImageSource? icon = OverlayPictures.Load(Aion2SkillIcons.PathFor(lane.SkillId));
                var box = new Rect(6, top, Icon, Icon);
                if (icon is not null)
                {
                    dc.DrawImage(icon, box);
                    dc.DrawRoundedRectangle(null, new Pen(border, 1), box, 3, 3);
                }
                else
                {
                    dc.DrawRoundedRectangle(track, new Pen(border, 1), box, 3, 3);
                    FormattedText letter = Text(lane.Name.Length > 0 ? lane.Name[..1].ToUpperInvariant() : "?", 12, text, bold: true);
                    dc.DrawText(letter, new Point(box.X + (Icon - letter.Width) / 2, box.Y + (Icon - letter.Height) / 2));
                }

                dc.DrawText(Text(lane.Name, 12.5, text, LabelWidth - Icon - 18), new Point(6 + Icon + 8, top + 2));
                for (int r = 0; r < lane.PerRun.Count; r++)
                {
                    double ry = y + Pad / 2 + r * Sub;
                    dc.DrawRectangle(track, null, new Rect(x0, ry, width, Sub - 2));
                    var brush = new SolidColorBrush(_colors[r]);
                    foreach (var (start, end) in lane.PerRun[r])
                    {
                        double a = X(start), b = X(end);
                        dc.DrawRectangle(brush, null, new Rect(a, ry, Math.Max(lane.IsEffect ? 2 : 3, b - a), Sub - 2));
                    }
                }

                y += LaneHeight + 2;
            }
        }

        // the time axis
        dc.DrawLine(new Pen(border, 1), new Point(x0, y + 2), new Point(x1, y + 2));
        double step = _length <= 70 ? 10 : _length <= 200 ? 15 : _length <= 400 ? 30 : 60;
        for (double s = 0; s <= _length + 0.01; s += step)
        {
            double x = X(s);
            dc.DrawLine(new Pen(border, 1), new Point(x, y + 2), new Point(x, y + 7));
            FormattedText label = Text($"{(int)s / 60}:{(int)s % 60:00}", 11, muted);
            dc.DrawText(label, new Point(Math.Min(x - label.Width / 2, ActualWidth - label.Width), y + 9));
        }
    }
}
