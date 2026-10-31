using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Combat;
using AionDPS.Upload;
using AionDPS.Update;

namespace AionDPS.Ui;

/// <summary>
/// The page of the stat calculator: the character's stats as the game sends them, a change to any of them, and what that does to the damage against the same boss. The simple mode
/// asks for what a player knows (crit rate, which side the hits come from); the expert mode adds the buffs and the boss's resistance. The calculator is an estimate (see
/// <see cref="StatCalc"/>); a recording of a fight, sent with the player's consent, is how it gets better. A reading page: no overlay, no hotkey.
/// </summary>
public sealed class StatCalcPage : Grid
{
    private static readonly StatKey[] SimpleRows = { StatKey.GearAttack, StatKey.AttackRatio, StatKey.AllAmp, StatKey.BossAmp, StatKey.CritAmp, StatKey.Perfect, StatKey.Smite };
    private static Dictionary<string, Dictionary<string, string>>? _names;

    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly Func<IReadOnlyList<DamageEvent>> _events;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Border _body = new();
    private readonly TextBlock _note = new() { FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly Button _tabSimple = new();
    private readonly Button _tabExpert = new();
    private readonly Dictionary<StatKey, TextBox> _changeBoxes = new();
    private readonly Dictionary<StatKey, double> _changes = new();
    private readonly Dictionary<string, TextBox> _expertBoxes = new();
    private readonly Dictionary<string, double> _expert = new()
    {
        ["attack"] = 0, ["amp"] = 0, ["critAmp"] = 0, ["smite"] = 0, ["resist"] = 0, ["critResist"] = 0, ["critBase"] = StatCalc.DefaultCritBase, ["smiteBonus"] = StatCalc.DefaultSmiteBonus,
    };

    private readonly StatRecorder _recorder = new();
    private readonly TextBlock _recordingState = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _recordButton = new();
    private readonly Button _sendButton = new();
    private readonly TextBlock _gain = new();
    private readonly TextBlock _afterText = new();
    private readonly StackPanel _levers = new();
    private readonly Dictionary<StatKey, (TextBlock Current, TextBlock After)> _cells = new();
    private readonly Dictionary<AttackSide, Button> _sideButtons = new();
    private readonly TextBlock _measured = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Slider _critSlider = new() { Minimum = 0, Maximum = 100, Value = 30, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 5, VerticalAlignment = VerticalAlignment.Center };
    private readonly Slider _sideSlider = new() { Minimum = 0, Maximum = 100, Value = 70, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 5, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _critValue = new() { Width = 46, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _sideValue = new() { Width = 46, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private FrameworkElement? _sideRateRow;

    private bool _expertMode;
    private bool _sideChosen;
    private readonly TextBlock _classHint = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private AttackSide _side = AttackSide.Unknown;
    private string _signature = "?";
    private IReadOnlyDictionary<int, int>? _frame;
    private Dictionary<StatKey, double> _stats = new();
    private string? _recordingJson;
    private string _recordingSummary = "";
    private bool _building;

    public StatCalcPage(Func<Aion2EntityDirectory?> directory, Func<IReadOnlyList<DamageEvent>> events)
    {
        _directory = directory;
        _events = events;
        Margin = new Thickness(14, 10, 14, 14);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        MeterSettings saved = MeterSettings.Load();
        _expertMode = saved.StatCalcExpert;
        _sideChosen = saved.StatCalcSideChosen || saved.StatCalcSide != "unknown"; // a side picked in 1.7.x counts as chosen
        _side = saved.StatCalcSide switch { "front" => AttackSide.Front, "back" => AttackSide.Back, _ => AttackSide.Unknown };
        _sideSlider.Value = Math.Clamp(saved.StatCalcSideRate, 0, 100);

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(tabs, Dock.Right);
        _tabSimple.Click += (_, _) => SetMode(false);
        _tabExpert.Click += (_, _) => SetMode(true);
        tabs.Children.Add(_tabSimple);
        tabs.Children.Add(_tabExpert);
        head.Children.Add(tabs);
        var titles = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock { Text = Loc("Main.MenuTools.StatCalc"), FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Text") });
        titleRow.Children.Add(BetaChip());
        titles.Children.Add(titleRow);
        _note.Foreground = Res("Brush.TextMuted");
        titles.Children.Add(_note);
        head.Children.Add(titles);
        SetRow(head, 0);
        Children.Add(head);
        SetRow(_body, 1);
        Children.Add(_body);

        _critSlider.ValueChanged += (_, _) => OnAssumptionChanged();
        _sideSlider.ValueChanged += (_, _) => OnAssumptionChanged();
        if (Application.Current?.TryFindResource("ThemedSlider") is Style style)
        {
            _critSlider.Style = style;
            _sideSlider.Style = style;
        }

        _refresh.Tick += (_, _) => Poll();
        _refresh.Start();
        LocalizationManager.Instance.PropertyChanged += OnLanguage;
        Poll(force: true);
    }

    public void Stop()
    {
        _refresh.Stop();
        LocalizationManager.Instance.PropertyChanged -= OnLanguage;
        if (_recorder.Running)
        {
            _recorder.Stop();
        }
    }

    /// <summary>Types a change into a stat's field (used by the picture mode); nothing happens for a stat the page does not list.</summary>
    public void ApplyChange(StatKey key, double value)
    {
        if (_changeBoxes.TryGetValue(key, out TextBox? box))
        {
            box.Text = Num(value);
        }
    }

    /// <summary>Sets the crit rate and the side the way a click on "take from my last fight" would (used by the picture mode).</summary>
    public void ApplyAssumptions(double critRate, AttackSide side, double sideRate)
    {
        _side = side;
        _critSlider.Value = critRate;
        _sideSlider.Value = sideRate;
        UpdateSideButtons();
        OnAssumptionChanged();
    }

    private void OnLanguage(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Poll(force: true);

    private static string Loc(string key) => LocalizationManager.Instance[key];

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    private static string Num(double value, string format = "0.##") => value.ToString(format, CultureInfo.CurrentCulture);

    /// <summary>The client's own name of a stat in the player's game language (English where the client has none: Chinese).</summary>
    private static string StatName(StatKey key)
    {
        _names ??= LoadNames();
        string lang = LocalizationManager.Instance.Language;
        return _names.TryGetValue(key.ToString(), out var row) ? row.GetValueOrDefault(lang) ?? row.GetValueOrDefault("en") ?? key.ToString() : key.ToString();
    }

    private static Dictionary<string, Dictionary<string, string>> LoadNames()
    {
        var table = new Dictionary<string, Dictionary<string, string>>();
        try
        {
            string path = AssetStore.Path("aion2", "stats", "calc_names.json");
            if (File.Exists(path))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                foreach (JsonProperty entry in document.RootElement.EnumerateObject())
                {
                    table[entry.Name] = entry.Value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // no names: the rows show the keys
        }

        return table;
    }

    // ---------------------------------------------------------------- state

    private void SetMode(bool expert)
    {
        if (_expertMode == expert)
        {
            return;
        }

        _expertMode = expert;
        MeterSettings s = MeterSettings.Load();
        s.StatCalcExpert = expert;
        s.Save();
        Rebuild();
    }

    private void SetSide(AttackSide side)
    {
        _side = side;
        _sideChosen = true;
        MeterSettings s = MeterSettings.Load();
        s.StatCalcSide = side switch { AttackSide.Front => "front", AttackSide.Back => "back", _ => "unknown" };
        s.StatCalcSideChosen = true;
        s.Save();
        UpdateSideButtons();
        OnAssumptionChanged();
    }

    private void OnAssumptionChanged()
    {
        if (_building)
        {
            return;
        }

        _critValue.Text = Num(_critSlider.Value, "0") + " %";
        _sideValue.Text = Num(_sideSlider.Value, "0") + " %";
        if (_sideRateRow is not null)
        {
            _sideRateRow.Visibility = _side == AttackSide.Unknown ? Visibility.Collapsed : Visibility.Visible;
        }

        MeterSettings s = MeterSettings.Load();
        s.StatCalcSideRate = (int)_sideSlider.Value;
        s.Save();
        Recalculate();
    }

    private StatCalcSetup Setup() => new(_critSlider.Value, _side, _sideSlider.Value,
        _expertMode ? _expert["attack"] : 0, _expertMode ? _expert["amp"] : 0, _expertMode ? _expert["critAmp"] : 0, _expertMode ? _expert["smite"] : 0, _expertMode ? _expert["resist"] : 0,
        _expert["critBase"], _expertMode ? _expert["smiteBonus"] : StatCalc.DefaultSmiteBonus, _expertMode ? _expert["critResist"] : 0);

    private void Poll(bool force = false)
    {
        IReadOnlyDictionary<int, int>? frame = _directory()?.LocalStats;
        string signature = frame is null ? "-" : string.Join(',', StatCalc.Keys.Select(k => frame.GetValueOrDefault(StatCalc.Spec(k).Id))) + (StatCalc.IsSupported(frame) ? "+" : "");
        if (_recorder.Running)
        {
            if (_recorder.Poll(DateTime.Now, frame))
            {
                StopRecording(auto: true);
            }
            else
            {
                ShowRecordingState();
            }
        }

        if (!force && signature == _signature)
        {
            return;
        }

        bool rebuild = force || (_frame is null) != (frame is null) || signature.EndsWith('+') != _signature.EndsWith('+');
        _signature = signature;
        _frame = frame;
        _stats = frame is null ? new Dictionary<StatKey, double>() : StatCalc.FromFrame(frame);
        if (rebuild)
        {
            AutoSide();
            Rebuild();
        }
        else
        {
            Recalculate();
        }
    }

    private void Rebuild()
    {
        _building = true;
        StyleTab(_tabSimple, Loc("StatCalc.ModeSimple"), !_expertMode);
        StyleTab(_tabExpert, Loc("StatCalc.ModeExpert"), _expertMode);
        _changeBoxes.Clear();
        _cells.Clear();
        _expertBoxes.Clear();
        _sideButtons.Clear();

        bool supported = _frame is not null && StatCalc.IsSupported(_frame);
        _note.Text = _frame is null ? Loc("StatCalc.NoData") : !supported ? Loc("StatCalc.Unsupported") : Loc("StatCalc.Note");
        _note.Foreground = !supported ? Res("Brush.Warning") : Res("Brush.TextMuted");

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 420 });

        var left = new StackPanel();
        left.Children.Add(BuildAssumptions());
        if (_expertMode)
        {
            left.Children.Add(BuildExpert());
        }

        left.Children.Add(BuildRecording());
        SetColumn(left, 0);
        columns.Children.Add(left);

        var right = new StackPanel();
        if (supported)
        {
            right.Children.Add(BuildTable());
            right.Children.Add(BuildResult());
            right.Children.Add(BuildLevers());
        }

        SetColumn(right, 2);
        columns.Children.Add(right);
        _body.Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = columns };
        _building = false;
        OnAssumptionChanged();
        ShowRecordingState();
    }

    // ---------------------------------------------------------------- cards

    private Border BuildAssumptions()
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading(Loc("StatCalc.Assumptions")));
        stack.Children.Add(SliderRow(Loc("StatCalc.CritRate"), _critSlider, _critValue));

        stack.Children.Add(Label(Loc("StatCalc.Side"), 13, brush: Res("Brush.Text"), margin: new Thickness(0, 12, 0, 4)));
        var sides = new StackPanel { Orientation = Orientation.Horizontal };
        foreach ((AttackSide side, string key) in new[] { (AttackSide.Front, "StatCalc.SideFront"), (AttackSide.Back, "StatCalc.SideBack"), (AttackSide.Unknown, "StatCalc.SideUnknown") })
        {
            var button = new Button { Margin = new Thickness(0, 0, 6, 0), Tag = Loc(key) };
            AttackSide chosen = side;
            button.Click += (_, _) => SetSide(chosen);
            _sideButtons[side] = button;
            sides.Children.Add(button);
        }

        stack.Children.Add(sides);
        UpdateSideButtons();
        _classHint.Text = ClassHint();
        _classHint.Visibility = _classHint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _classHint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        if (_classHint.Parent is Panel oldHint)
        {
            oldHint.Children.Remove(_classHint);
        }

        stack.Children.Add(_classHint);
        _sideRateRow = SliderRow(Loc("StatCalc.SideRate"), _sideSlider, _sideValue);
        _sideRateRow.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(_sideRateRow);

        var take = new Button { Content = Loc("StatCalc.TakeFromFight"), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        take.Click += (_, _) => TakeFromFight();
        stack.Children.Add(take);
        _measured.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        if (_measured.Parent is Panel oldMeasured)
        {
            oldMeasured.Children.Remove(_measured);
        }

        stack.Children.Add(_measured);
        return Card(stack, new Thickness(0, 0, 0, 12));
    }

    private Border BuildExpert()
    {
        var stack = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        titleRow.Children.Add(new TextBlock { Text = Loc("StatCalc.ModeExpert").ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.TextMuted"), VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(BetaChip());
        stack.Children.Add(titleRow);
        var beta = Label(Loc("StatCalc.ExpertBeta"), 12, brush: Res("Brush.Warning"));
        beta.Margin = new Thickness(0, 0, 0, 10);
        stack.Children.Add(beta);
        foreach ((string key, string text, double fallback) in new[]
        {
            ("attack", "StatCalc.Expert.Attack", 0.0), ("amp", "StatCalc.Expert.Amp", 0.0), ("critAmp", "StatCalc.Expert.CritAmp", 0.0), ("smite", "StatCalc.Expert.Smite", 0.0),
            ("resist", "StatCalc.Expert.Resist", 0.0), ("critResist", "StatCalc.Expert.CritResist", 0.0), ("critBase", "StatCalc.Expert.CritBase", StatCalc.DefaultCritBase), ("smiteBonus", "StatCalc.Expert.SmiteBonus", StatCalc.DefaultSmiteBonus),
        })
        {
            stack.Children.Add(ExpertRow(key, Loc(text), fallback));
        }

        return Card(stack, new Thickness(0, 0, 0, 12));
    }

    private Border BuildRecording()
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading(Loc("StatCalc.Recording")));
        stack.Children.Add(Label(Loc("StatCalc.RecordingText"), 12, brush: Res("Brush.TextMuted")));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        _recordButton.Padding = new Thickness(12, 5, 12, 5);
        _recordButton.Margin = new Thickness(0, 0, 8, 0);
        _recordButton.Click -= OnRecordClicked;
        _recordButton.Click += OnRecordClicked;
        _sendButton.Content = Loc("StatCalc.Send");
        _sendButton.Padding = new Thickness(12, 5, 12, 5);
        _sendButton.Click -= OnSendClicked;
        _sendButton.Click += OnSendClicked;
        if (_recordButton.Parent is Panel old)
        {
            old.Children.Remove(_recordButton);
        }

        if (_sendButton.Parent is Panel oldSend)
        {
            oldSend.Children.Remove(_sendButton);
        }

        buttons.Children.Add(_recordButton);
        buttons.Children.Add(_sendButton);
        stack.Children.Add(buttons);
        if (_recordingState.Parent is Panel oldState)
        {
            oldState.Children.Remove(_recordingState);
        }

        _recordingState.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        stack.Children.Add(_recordingState);
        return Card(stack);
    }

    private Border BuildTable()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        void Header(string text, int column)
        {
            var block = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.TextMuted"), TextAlignment = column == 0 ? TextAlignment.Left : TextAlignment.Right, Margin = new Thickness(4, 0, 4, 4) };
            SetRow(block, 0);
            SetColumn(block, column);
            grid.Children.Add(block);
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Header(Loc("StatCalc.Col.Stat"), 0);
        Header(Loc("StatCalc.Col.Current"), 1);
        Header(Loc("StatCalc.Col.Change"), 2);
        Header(Loc("StatCalc.Col.New"), 3);

        IEnumerable<StatKey> rows = _expertMode ? StatCalc.Keys : SimpleRows.Concat(_side switch { AttackSide.Front => new[] { StatKey.FrontAmp }, AttackSide.Back => new[] { StatKey.BackAmp }, _ => Array.Empty<StatKey>() });
        int row = 1;
        foreach (StatKey key in rows)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new TextBlock { Text = StatName(key), Foreground = Res("Brush.Text"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 3, 4, 3), TextTrimming = TextTrimming.CharacterEllipsis };
            var current = new TextBlock { Foreground = Res("Brush.TextSubtle"), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Margin = new Thickness(4, 3, 4, 3) };
            var box = new TextBox { Text = _changes.TryGetValue(key, out double kept) && kept != 0 ? Num(kept) : "", TextAlignment = TextAlignment.Right, Margin = new Thickness(4, 2, 4, 2), VerticalContentAlignment = VerticalAlignment.Center, Tag = key };
            StyleBox(box);
            box.TextChanged += (_, _) => OnChangeEdited(key, box);
            var after = new TextBlock { Foreground = Res("Brush.Text"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Margin = new Thickness(4, 3, 4, 3) };
            foreach ((UIElement element, int column) in new (UIElement, int)[] { (name, 0), (current, 1), (box, 2), (after, 3) })
            {
                SetRow(element, row);
                SetColumn(element, column);
                grid.Children.Add(element);
            }

            _changeBoxes[key] = box;
            _cells[key] = (current, after);
            row++;
        }

        var stack = new StackPanel();
        stack.Children.Add(Heading(Loc("StatCalc.Changes")));
        stack.Children.Add(grid);
        return Card(stack, new Thickness(0, 0, 0, 12));
    }

    private Border BuildResult()
    {
        var grid = new Grid();
        for (int i = 0; i < 3; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        Border Tile(string caption, TextBlock value, int column)
        {
            var inner = new StackPanel();
            inner.Children.Add(new TextBlock { Text = caption, FontSize = 11, Foreground = Res("Brush.TextMuted"), TextWrapping = TextWrapping.Wrap });
            if (value.Parent is Panel oldTile)
            {
                oldTile.Children.Remove(value);
            }

            value.FontSize = 24;
            value.FontWeight = FontWeights.Bold;
            value.Margin = new Thickness(0, 4, 0, 0);
            inner.Children.Add(value);
            var tile = new Border { Background = Res("Brush.Control"), BorderBrush = Res("Brush.Border"), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(column == 0 ? 0 : 8, 0, 0, 0), Child = inner };
            SetColumn(tile, column);
            return tile;
        }

        var nowText = new TextBlock { Text = "100 %", Foreground = Res("Brush.Text") };
        _afterText.Foreground = Res("Brush.Accent");
        _gain.Foreground = Res("Brush.Heal");
        grid.Children.Add(Tile(Loc("StatCalc.DamageNow"), nowText, 0));
        grid.Children.Add(Tile(Loc("StatCalc.DamageAfter"), _afterText, 1));
        grid.Children.Add(Tile(Loc("StatCalc.Gain"), _gain, 2));
        var stack = new StackPanel();
        stack.Children.Add(Heading(Loc("StatCalc.Result")));
        stack.Children.Add(grid);
        stack.Children.Add(Label(Loc("StatCalc.ResultNote"), 11, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 8, 0, 0)));
        return Card(stack, new Thickness(0, 0, 0, 12));
    }

    private Border BuildLevers()
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading(Loc("StatCalc.Levers")));
        stack.Children.Add(Label(Loc("StatCalc.LeversText"), 11, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 0, 0, 8)));
        _levers.Children.Clear();
        if (_levers.Parent is Panel old)
        {
            old.Children.Remove(_levers);
        }

        stack.Children.Add(_levers);
        return Card(stack);
    }

    // ---------------------------------------------------------------- calculating

    private void OnChangeEdited(StatKey key, TextBox box)
    {
        if (_building)
        {
            return;
        }

        _changes[key] = TryParse(box.Text, out double v) ? v : 0;
        Recalculate();
    }

    private static bool TryParse(string text, out double value)
    {
        string t = text.Trim().Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private void Recalculate()
    {
        if (_building || _cells.Count == 0 || _frame is null)
        {
            return;
        }

        StatCalcSetup setup = Setup();
        var changes = _changes.Where(c => _cells.ContainsKey(c.Key)).ToDictionary(c => c.Key, c => c.Value);
        StatCalcResult result = StatCalc.Calculate(_stats, changes, setup);
        foreach ((StatKey key, (TextBlock current, TextBlock after)) in _cells)
        {
            bool percent = StatCalc.Spec(key).Percent;
            double now = _stats.GetValueOrDefault(key);
            current.Text = percent ? Num(now) + " %" : Num(now, "0");
            double changed = now + changes.GetValueOrDefault(key);
            after.Text = percent ? Num(changed) + " %" : Num(changed, "0");
        }

        double ratio = result.Before > 0 ? result.After / result.Before * 100 : 100;
        _afterText.Text = Num(ratio, "0.0") + " %";
        _gain.Text = (result.GainPercent >= 0 ? "+" : "") + Num(result.GainPercent, "0.0") + " %";
        _gain.Foreground = result.GainPercent < -0.0499 ? Res("Brush.Danger") : result.GainPercent > 0.0499 ? Res("Brush.Heal") : Res("Brush.Text");

        _levers.Children.Clear();
        foreach (StatCalcLever lever in result.Levers.Where(l => _expertMode || _cells.ContainsKey(l.Key)).Take(8))
        {
            bool percent = StatCalc.Spec(lever.Key).Percent;
            string amount = (percent ? "+" + Num(lever.ForOnePercent, "0.0#") + " %" : "+" + Num(Math.Ceiling(lever.ForOnePercent), "0"));
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var value = new TextBlock { Text = amount, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Accent") };
            DockPanel.SetDock(value, Dock.Right);
            row.Children.Add(value);
            row.Children.Add(new TextBlock { Text = StatName(lever.Key), Foreground = Res("Brush.Text"), TextTrimming = TextTrimming.CharacterEllipsis });
            _levers.Children.Add(row);
        }
    }

    // ---------------------------------------------------------------- measured rates

    /// <summary>The player's own hits of the last fight (back from the newest, until a pause of a minute): how many direct hits, and the shares of crits, back, front and doubles.</summary>
    private (int Hits, double Crit, double Back, double Front, double Double, double Perfect, double? CritRatio)? Measure()
    {
        int local = _directory()?.LocalPlayerId ?? -1;
        if (local < 0)
        {
            return null;
        }

        var mine = _events().Where(e => e.SourceObjectId == local && !e.IsHeal && !e.IsTick && e.Amount > 0 && !e.Estimated).OrderBy(e => e.Timestamp).ToList();
        if (mine.Count == 0)
        {
            return null;
        }

        int first = mine.Count - 1;
        while (first > 0 && mine[first].Timestamp - mine[first - 1].Timestamp <= TimeSpan.FromSeconds(60))
        {
            first--;
        }

        var fight = mine.Skip(first).ToList();
        if (fight.Count < 10)
        {
            return null;
        }

        double Share(Func<DamageEvent, bool> test) => 100.0 * fight.Count(test) / fight.Count;
        bool Has(DamageEvent e, HitKinds kind) => e.Kinds is { } k && (k & kind) != 0;
        return (fight.Count, Share(e => e.IsCritical), Share(e => Has(e, HitKinds.Back)), Share(e => Has(e, HitKinds.Front)), Share(e => Has(e, HitKinds.Double)), Share(e => Has(e, HitKinds.Perfect)),
            StatCalc.MeasureCritRatio(fight));
    }

    private void TakeFromFight()
    {
        var measured = Measure();
        if (measured is not { } m)
        {
            _measured.Text = Loc("StatCalc.NoFight");
            return;
        }

        _sideChosen = true;
        MeterSettings s = MeterSettings.Load();
        s.StatCalcSideChosen = true;
        s.Save();
        ApplyMeasured(m);
    }

    /// <summary>Sets the crit rate, the side and its share and - where the fight had enough crits and plain hits - the crit multiplier from the measured fight. The multiplier is kept as the
    /// base that adds up with the crit amplification of the stats to exactly what was measured, so a change of that stat still counts.</summary>
    private void ApplyMeasured((int Hits, double Crit, double Back, double Front, double Double, double Perfect, double? CritRatio) m)
    {
        _building = true;
        _critSlider.Value = Math.Round(m.Crit);
        AttackSide side = m.Back >= m.Front && m.Back >= 10 ? AttackSide.Back : m.Front > m.Back && m.Front >= 10 ? AttackSide.Front : AttackSide.Unknown;
        _side = side;
        _sideSlider.Value = Math.Round(side == AttackSide.Back ? m.Back : side == AttackSide.Front ? m.Front : _sideSlider.Value);
        string text = string.Format(Loc("StatCalc.Measured"), m.Hits, Num(m.Crit, "0"), Num(m.Back, "0"), Num(m.Front, "0"), Num(m.Double, "0"), Num(m.Perfect, "0"));
        if (m.CritRatio is { } ratio)
        {
            double amp = _stats.GetValueOrDefault(StatKey.CritAmp);
            double critBase = Math.Clamp(Math.Round(ratio * 100 - amp), 100, 400);
            _expert["critBase"] = critBase;
            if (_expertBoxes.TryGetValue("critBase", out TextBox? box))
            {
                box.Text = Num(critBase);
            }

            text += " " + string.Format(Loc("StatCalc.MeasuredCrit"), Num(ratio, "0.00"), Num(critBase, "0"));
        }

        _measured.Text = text;
        _building = false;
        UpdateSideButtons();
        OnAssumptionChanged();
    }

    /// <summary>Until the player has chosen a side, takes what the last fight shows, or else the side the character's own direction bonuses favour.</summary>
    private void AutoSide()
    {
        if (_sideChosen || _frame is null)
        {
            return;
        }

        if (Measure() is { } m)
        {
            ApplyMeasured(m);
            return;
        }

        AttackSide fromStats = StatCalc.SideFromStats(_stats);
        _side = fromStats;
        UpdateSideButtons();
        if (fromStats != AttackSide.Unknown)
        {
            _measured.Text = string.Format(Loc("StatCalc.SideAuto"), Loc(fromStats == AttackSide.Front ? "StatCalc.SideFront" : "StatCalc.SideBack"));
        }
    }

    /// <summary>What the skills of the player's class say about a side (the client's skill tables: effects that only work on a front or a back attack). A hint, not a default: how the player really attacks is another matter.</summary>
    private string ClassHint()
    {
        int code = _directory()?.LocalCharacter?.ClassCode ?? 0;
        return code > 0 ? ((code - 1) / 4) switch { 1 or 2 => Loc("StatCalc.SideHintFront"), 4 or 8 => Loc("StatCalc.SideHintBack"), _ => "" } : "";
    }

    // ---------------------------------------------------------------- recording

    private void OnRecordClicked(object sender, RoutedEventArgs e)
    {
        if (_recorder.Running)
        {
            StopRecording(auto: false);
            return;
        }

        IReadOnlyDictionary<int, int>? frame = _directory()?.LocalStats;
        if (frame is null)
        {
            _recordingState.Text = Loc("StatCalc.NoData");
            return;
        }

        _recordingJson = null;
        _recorder.Start(DateTime.Now, frame);
        ShowRecordingState();
    }

    private void StopRecording(bool auto)
    {
        DateTime end = DateTime.Now;
        DateTime start = _recorder.Started;
        IReadOnlyDictionary<int, int> statsEnd = _recorder.Stop();
        Aion2EntityDirectory? directory = _directory();
        int local = directory?.LocalPlayerId ?? -1;
        var mine = _events().Where(x => x.SourceObjectId == local && x.Timestamp >= start && x.Timestamp <= end).ToList();
        FightExtras? extras = directory?.Combat.Slice(start, end, _ => null); // no names: the recording holds ids only
        Aion2CharacterInfo? character = directory?.LocalCharacter;
        int? serverId = character is { ServerId: > 0 } ? character.ServerId : null;
        int? classId = character is { ClassCode: > 0 } ? (character.ClassCode - 1) / 4 : null;
        StatCalcSetup setup = Setup();
        var recordingSetup = new StatRecordingSetup(_expertMode ? "expert" : "simple", _side.ToString().ToLowerInvariant(), setup.SideRate, setup.CritRate, setup.AttackRatioBuff, setup.AmpBuff,
            setup.CritAmpBuff, setup.SmiteBuff, setup.BossResistance, setup.CritBase, setup.SmiteBonus, setup.CritResist);
        _recordingJson = StatRecording.Build(MeterSettings.EnsureAnonymousId(), start, end, recordingSetup, new StatRecordingCharacter(classId, character?.Level, serverId, StatRecording.FactionOf(serverId)),
            _recorder.StartStats, statsEnd, _recorder.Changes, mine, extras, local,
            id => directory?.NpcIdOf(id), id => directory?.MonsterNameFor(id));
        int hits = mine.Count(x => !x.IsHeal && !x.IsTick && x.Amount > 0);
        _recordingSummary = string.Format(Loc("StatCalc.Recorded"), Clock(end - start), hits);
        ShowRecordingState(auto ? Loc("StatCalc.AutoStopped") : null);
    }

    private static string Clock(TimeSpan span) => $"{(int)span.TotalMinutes}:{span.Seconds:00}";

    private void ShowRecordingState(string? extra = null)
    {
        _recordButton.Content = Loc(_recorder.Running ? "StatCalc.RecordStop" : "StatCalc.RecordStart");
        _sendButton.IsEnabled = !_recorder.Running && _recordingJson is not null;
        if (_recorder.Running)
        {
            _recordingState.Text = string.Format(Loc("StatCalc.Recording.Running"), Clock(DateTime.Now - _recorder.Started), Clock(StatRecording.MaxLength));
        }
        else if (_recordingJson is not null)
        {
            _recordingState.Text = _recordingSummary + (extra is null ? "" : " " + extra) + " " + Loc("StatCalc.ReadyToSend");
        }
        else
        {
            _recordingState.Text = "";
        }
    }

    private async void OnSendClicked(object sender, RoutedEventArgs e)
    {
        if (_recordingJson is null)
        {
            return;
        }

        string text = Loc("StatCalc.Send.Intro") + "\n\n" + Loc("StatCalc.Send.Contains") + "\n" + string.Join("\n", StatRecording.Contents.Select(k => "• " + Loc(k)))
            + "\n\n" + Loc("StatCalc.Send.NotContains") + "\n" + string.Join("\n", StatRecording.NotContained.Select(k => "• " + Loc(k))) + "\n\n" + Loc("StatCalc.Send.Question");
        if (ThemedMessageBox.Show(Window.GetWindow(this), text, Loc("StatCalc.Send.Title"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _sendButton.IsEnabled = false;
        UploadResult result = await UploadClient.SendStatRecordingAsync(_recordingJson);
        if (result.Success)
        {
            _recordingJson = null;
            _recordingState.Text = Loc("StatCalc.Sent");
            _sendButton.IsEnabled = false;
        }
        else
        {
            _recordingState.Text = string.Format(Loc("StatCalc.SendFailed"), result.Error ?? "");
            _sendButton.IsEnabled = true;
        }
    }

    // ---------------------------------------------------------------- small helpers

    private void UpdateSideButtons()
    {
        foreach ((AttackSide side, Button button) in _sideButtons)
        {
            StyleTab(button, (string)button.Tag, side == _side);
            button.MinWidth = 0;
            button.Padding = new Thickness(12, 5, 12, 5);
        }
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

    private static void StyleBox(TextBox box)
    {
        box.Padding = new Thickness(6, 3, 6, 3);
        box.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
        box.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        box.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        box.SetResourceReference(TextBox.CaretBrushProperty, "Brush.Text");
    }

    private static Border BetaChip() => new()
    {
        Background = Res("Brush.WarningSoft"), Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = "BETA", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x00)) },
    };

    private static TextBlock Label(string text, double size = 13, FontWeight? weight = null, Brush? brush = null, Thickness? margin = null) =>
        new() { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, Foreground = brush ?? Res("Brush.Text"), TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0) };

    private static TextBlock Heading(string text) =>
        new() { Text = text.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.TextMuted"), Margin = new Thickness(0, 0, 0, 8) };

    private static Border Card(UIElement child, Thickness? margin = null) =>
        new() { Background = Res("Brush.Panel"), BorderBrush = Res("Brush.Border"), BorderThickness = new Thickness(1), Padding = new Thickness(16, 14, 16, 14), Margin = margin ?? new Thickness(0), Child = child };

    private static Grid SliderRow(string label, Slider slider, TextBlock value)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock { Text = label, Foreground = Res("Brush.Text"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        value.Foreground = Res("Brush.Text");
        value.FontWeight = FontWeights.SemiBold;
        if (slider.Parent is Panel old)
        {
            old.Children.Remove(slider);
        }

        if (value.Parent is Panel oldValue)
        {
            oldValue.Children.Remove(value);
        }

        slider.Margin = new Thickness(6, 0, 6, 0);
        SetColumn(slider, 1);
        SetColumn(value, 2);
        row.Children.Add(name);
        row.Children.Add(slider);
        row.Children.Add(value);
        return row;
    }

    private UIElement ExpertRow(string key, string label, double fallback)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        var name = new TextBlock { Text = label, Foreground = Res("Brush.Text"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) };
        var box = new TextBox { Text = Num(_expert.GetValueOrDefault(key, fallback)), TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center };
        StyleBox(box);
        box.TextChanged += (_, _) =>
        {
            if (_building)
            {
                return;
            }

            _expert[key] = TryParse(box.Text, out double v) ? v : fallback;
            Recalculate();
        };
        SetColumn(box, 1);
        row.Children.Add(name);
        row.Children.Add(box);
        _expertBoxes[key] = box;
        return row;
    }
}
