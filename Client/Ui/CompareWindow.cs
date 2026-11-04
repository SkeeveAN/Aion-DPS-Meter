using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AionDPS.Combat;
using AionDPS.History;
using Microsoft.Win32;

namespace AionDPS.Ui;

/// <summary>
/// Puts two or more players side by side: from sessions of the meter (live, or loaded from files) or from a public fight of aiondps.com (its address), the first one being the
/// baseline the others are held against. The table lists the skills by how far the players are from the baseline, so the skills that make up a gap in damage come first;
/// below it the hits and cast notices of one skill on a common time axis. A local file never leaves the PC: the online side only downloads.
/// </summary>
public sealed class CompareWindow : Window
{
    private static readonly Brush Good = new SolidColorBrush(Color.FromRgb(0x5c, 0xc8, 0x6b));
    private static readonly Brush Bad = new SolidColorBrush(Color.FromRgb(0xe0, 0x6c, 0x6c));

    private readonly SessionRecorder _recorder;
    private readonly Func<SessionInfo, FightExtras?> _extrasFor;
    private readonly Func<SessionFile.LoadedSession, SessionInfo> _import;
    private readonly List<Source> _sources = new();
    private readonly WrapPanel _cards = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly StackPanel _results = new();
    private int _baseline;
    private string? _timelineSkill;

    /// <summary>One column of the comparison: a player of a local session, or of an online fight.</summary>
    private sealed class Source
    {
        public SessionInfo? Session;
        public List<BossAttempt> Attempts = new();
        public ComboBox? BossBox;
        public ComboBox PlayerBox = new() { MinWidth = 240, Margin = new Thickness(0, 2, 0, 2) };
        public List<int> PlayerIds = new();
        public CompareModel.OnlineFight? Fight;
        public CompareEntry? Entry;
        public string Title = "";
    }

    public CompareWindow(SessionRecorder recorder, Func<SessionInfo, FightExtras?> extrasFor, Func<SessionFile.LoadedSession, SessionInfo> import, IEnumerable<SessionInfo>? start = null)
    {
        _recorder = recorder;
        _extrasFor = extrasFor;
        _import = import;
        LocalizationManager loc = LocalizationManager.Instance;
        Title = loc["Compare.Title"];
        Width = 1240;
        Height = 820;
        MinWidth = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Brush.Window");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        SetResourceReference(FontSizeProperty, "FontSize.Base");

        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(Button(loc["Compare.AddSessions"], OnAddSessions));
        bar.Children.Add(Button(loc["Compare.LoadFiles"], OnLoadFiles));
        bar.Children.Add(Button(loc["Compare.AddOnline"], OnAddOnline));

        var root = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
        root.Children.Add(bar);
        root.Children.Add(_cards);
        root.Children.Add(_results);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        ThemedChrome.Apply(this);

        foreach (SessionInfo session in start ?? Array.Empty<SessionInfo>())
        {
            AddLocal(session, null);
        }

        Render();
    }

    private static Button Button(string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 8, 0) };
        button.Click += click;
        return button;
    }

    private TextBlock Text(string text, bool muted = false, bool bold = false, TextAlignment align = TextAlignment.Left, Brush? brush = null)
    {
        var block = new TextBlock { Text = text, TextAlignment = align, Margin = new Thickness(0, 1, 12, 1), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (brush is not null)
        {
            block.Foreground = brush;
        }
        else
        {
            block.SetResourceReference(TextBlock.ForegroundProperty, muted ? "Brush.TextMuted" : "Brush.Text");
        }

        if (bold)
        {
            block.FontWeight = FontWeights.SemiBold;
        }

        return block;
    }

    // ---- adding sources -----------------------------------------------------------------------------------------------

    private void OnAddSessions(object sender, RoutedEventArgs e)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var sessions = _recorder.Sessions.Where(s => s.Data.Events.Count > 0).Reverse().ToList();
        var list = new ListBox { SelectionMode = SelectionMode.Extended, MinHeight = 260, MinWidth = 520 };
        foreach (SessionInfo s in sessions)
        {
            list.Items.Add($"{SessionLabels.NameOf(s)} · {SessionLabels.TimeSpanOf(s)}{(s.FromFile ? " ·  file" : "")}");
        }

        var ok = new Button { Content = "OK", IsDefault = true, Padding = new Thickness(18, 3, 18, 3), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(list);
        panel.Children.Add(ok);
        var dialog = new Window { Title = loc["Compare.PickSessions"], Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, SizeToContent = SizeToContent.WidthAndHeight, Content = panel };
        dialog.SetResourceReference(BackgroundProperty, "Brush.Window");
        dialog.SetResourceReference(ForegroundProperty, "Brush.Text");
        ThemedChrome.Apply(dialog);
        ok.Click += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (int index in list.SelectedIndices())
        {
            AddLocal(sessions[index], null);
        }

        Render();
    }

    private void OnLoadFiles(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SessionFile.DefaultDirectory);
        var dialog = new OpenFileDialog
        {
            InitialDirectory = SessionFile.DefaultDirectory,
            Filter = $"Aion DPS session (*{SessionFile.Extension})|*{SessionFile.Extension}",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (string path in dialog.FileNames)
        {
            try
            {
                AddLocal(_import(SessionFile.Load(path)), System.IO.Path.GetFileNameWithoutExtension(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
            {
                ThemedMessageBox.Show(this, $"{System.IO.Path.GetFileName(path)}\n\n{ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        Render();
    }

    private async void OnAddOnline(object sender, RoutedEventArgs e)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var box = new TextBox { MinWidth = 520, Margin = new Thickness(0, 6, 0, 8) };
        var ok = new Button { Content = "OK", IsDefault = true, Padding = new Thickness(18, 3, 18, 3), HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = loc["Compare.OnlinePrompt"], TextWrapping = TextWrapping.Wrap, MaxWidth = 520 });
        panel.Children.Add(box);
        panel.Children.Add(ok);
        var dialog = new Window { Title = loc["Compare.AddOnline"], Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, SizeToContent = SizeToContent.WidthAndHeight, Content = panel };
        dialog.SetResourceReference(BackgroundProperty, "Brush.Window");
        dialog.SetResourceReference(ForegroundProperty, "Brush.Text");
        ThemedChrome.Apply(dialog);
        ok.Click += (_, _) => dialog.DialogResult = true;
        box.Loaded += (_, _) => box.Focus();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        long? id = CompareModel.EncounterIdOf(box.Text);
        CompareModel.OnlineFight? fight = null;
        if (id is long number)
        {
            try
            {
                fight = await CompareModel.FetchFightAsync(number);
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
            {
                fight = null;
            }
        }

        if (fight is null || fight.Players.Count == 0)
        {
            ThemedMessageBox.Show(this, loc["Compare.OnlineFailed"], Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var source = new Source { Fight = fight, Title = $"aiondps.com #{fight.EncounterId} · {fight.BossName}" };
        foreach (CompareModel.OnlinePlayer p in fight.Players.OrderByDescending(p => p.TotalDamage))
        {
            source.PlayerBox.Items.Add($"{p.Name} ({p.ClassName})");
        }

        source.PlayerBox.Tag = fight.Players.OrderByDescending(p => p.TotalDamage).ToList();
        source.PlayerBox.SelectionChanged += async (_, _) => await LoadOnlineAsync(source);
        _sources.Add(source);
        source.PlayerBox.SelectedIndex = 0;
        RenderCards();
    }

    private async Task LoadOnlineAsync(Source source)
    {
        if (source.Fight is null || source.PlayerBox.SelectedIndex < 0 || source.PlayerBox.Tag is not List<CompareModel.OnlinePlayer> players)
        {
            return;
        }

        try
        {
            source.Entry = await CompareModel.FetchEntryAsync(source.Fight, players[source.PlayerBox.SelectedIndex]);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            source.Entry = null;
        }

        Render();
    }

    private void AddLocal(SessionInfo session, string? fileName)
    {
        var source = new Source { Session = session, Title = fileName ?? SessionLabels.NameOf(session) };
        try
        {
            source.Attempts = SessionAnalyzer.Analyze(session).Attempts.ToList();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            source.Attempts = new List<BossAttempt>();
        }

        LocalizationManager loc = LocalizationManager.Instance;
        source.BossBox = new ComboBox { MinWidth = 240, Margin = new Thickness(0, 2, 0, 2) };
        source.BossBox.Items.Add(loc["Compare.WholeStay"]);
        foreach (BossAttempt a in source.Attempts)
        {
            source.BossBox.Items.Add($"{a.Name} · {a.Start:HH:mm:ss} · {(int)a.Duration.TotalSeconds} s{(a.Killed ? "" : " ✗")}");
        }

        // the longest boss attempt is the fight to look at, when there is one
        int best = source.Attempts.Count == 0 ? -1 : source.Attempts.IndexOf(source.Attempts.MaxBy(a => a.Counted)!);
        source.BossBox.SelectedIndex = best + 1;
        FillPlayers(source);
        source.BossBox.SelectionChanged += (_, _) =>
        {
            FillPlayers(source);
            Render();
        };
        source.PlayerBox.SelectionChanged += (_, _) => Render();
        _sources.Add(source);
        RenderCards();
    }

    private static BossAttempt? AttemptOf(Source source) =>
        source.BossBox is { SelectedIndex: > 0 } box && box.SelectedIndex - 1 < source.Attempts.Count ? source.Attempts[box.SelectedIndex - 1] : null;

    /// <summary>The players of a session's picked fight, the biggest damage dealer first.</summary>
    private static void FillPlayers(Source source)
    {
        SessionInfo session = source.Session!;
        IReadOnlyList<DamageEvent> scoped = SessionManagerWindow.ScopedEvents(session, AttemptOf(source), SessionManagerWindow.PlayerMode.Damage);
        var damage = scoped.Where(e => !e.IsHeal).GroupBy(e => e.SourceObjectId)
            .Where(g => session.Classes.Count == 0 || session.Classes.ContainsKey(g.Key))
            .Select(g => (Id: g.Key, Total: g.Sum(e => e.Amount)))
            .OrderByDescending(x => x.Total)
            .ToList();
        int previous = source.PlayerBox.SelectedIndex >= 0 && source.PlayerBox.SelectedIndex < source.PlayerIds.Count ? source.PlayerIds[source.PlayerBox.SelectedIndex] : -1;
        source.PlayerIds = damage.Select(x => x.Id).ToList();
        source.PlayerBox.Items.Clear();
        foreach (var (id, total) in damage)
        {
            source.PlayerBox.Items.Add($"{session.Names.GetValueOrDefault(id) ?? "#" + id} ({session.Classes.GetValueOrDefault(id) ?? "?"}) · {total:N0}");
        }

        int keep = source.PlayerIds.IndexOf(previous);
        source.PlayerBox.SelectedIndex = keep >= 0 ? keep : (source.PlayerIds.Count > 0 ? 0 : -1);
    }

    private void RenderCards()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        // the choice boxes live on from card to card: let go of the old card first (an element has one parent only)
        foreach (Source old in _sources)
        {
            foreach (FrameworkElement box in new FrameworkElement?[] { old.BossBox, old.PlayerBox }.OfType<FrameworkElement>())
            {
                if (box.Parent is Panel parent)
                {
                    parent.Children.Remove(box);
                }
            }
        }

        _cards.Children.Clear();
        for (int i = 0; i < _sources.Count; i++)
        {
            int index = i;
            Source source = _sources[i];
            var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 8), MinWidth = 200 };
            card.SetResourceReference(Border.BorderBrushProperty, i == _baseline ? "Brush.Accent" : "Brush.Border");
            card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
            var stack = new StackPanel();
            var head = new DockPanel();
            var remove = new Button { Content = "✕", Padding = new Thickness(6, 0, 6, 0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, ToolTip = loc["Compare.Remove"] };
            remove.SetResourceReference(Control.ForegroundProperty, "Brush.TextMuted");
            remove.Click += (_, _) =>
            {
                _sources.Remove(source);
                _baseline = Math.Min(_baseline, Math.Max(0, _sources.Count - 1));
                RenderCards();
                Render();
            };
            DockPanel.SetDock(remove, Dock.Right);
            head.Children.Add(remove);
            var baseline = new RadioButton { Content = loc["Compare.Baseline"], GroupName = "baseline", IsChecked = i == _baseline, Margin = new Thickness(0, 0, 8, 0) };
            baseline.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
            baseline.Checked += (_, _) =>
            {
                _baseline = index;
                RenderCards();
                Render();
            };
            head.Children.Add(baseline);
            stack.Children.Add(head);
            stack.Children.Add(Text(source.Title, muted: true));
            if (source.BossBox is not null)
            {
                stack.Children.Add(source.BossBox);
            }

            stack.Children.Add(source.PlayerBox);
            card.Child = stack;
            _cards.Children.Add(card);
        }
    }

    // ---- the result ---------------------------------------------------------------------------------------------------

    private void Render()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        foreach (Source source in _sources.Where(s => s.Session is not null))
        {
            int pick = source.PlayerBox.SelectedIndex;
            source.Entry = pick >= 0 && pick < source.PlayerIds.Count
                ? CompareModel.FromSession(source.Session!, AttemptOf(source), source.PlayerIds[pick], source.Session!.Extras ?? _extrasFor(source.Session!), source.Title)
                : null;
        }

        _results.Children.Clear();
        List<CompareEntry> entries = _sources.Where(s => s.Entry is not null).Select(s => s.Entry!).ToList();
        if (entries.Count < 2)
        {
            _results.Children.Add(Text(loc["Compare.Empty"], muted: true));
            return;
        }

        CompareEntry? baseEntry = _baseline < _sources.Count ? _sources[_baseline].Entry : null;
        int baseline = baseEntry is null ? 0 : Math.Max(0, entries.IndexOf(baseEntry));
        _results.Children.Add(Overview(entries, baseline));
        List<CompareRow> table = CompareModel.Table(entries, baseline);
        _timelineSkill = table.Any(r => r.Skill == _timelineSkill) ? _timelineSkill : table.FirstOrDefault()?.Skill;
        _results.Children.Add(SkillTable(entries, table, baseline));
        List<CompareBuffRow> buffs = CompareModel.BuffTable(entries, baseline);
        if (buffs.Count > 0)
        {
            _results.Children.Add(Text(loc["Compare.Buffs"], bold: true));
            _results.Children.Add(BuffGrid(entries, buffs, baseline));
        }

        _results.Children.Add(Text(loc["Compare.UsesNote"], muted: true));
        _results.Children.Add(Timelines(entries));
    }

    private static string Delta(double value, double baseline) =>
        baseline > 0 && Math.Abs(value - baseline) / baseline * 100 >= 0.05 ? (value >= baseline ? "+" : "") + ((value - baseline) / baseline * 100).ToString("0.0", CultureInfo.CurrentCulture) + " %" : "";

    private Grid Overview(List<CompareEntry> entries, int baseline)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        foreach (CompareEntry _ in entries)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        }

        void Row(string label, Func<CompareEntry, string> value, Func<CompareEntry, string>? delta = null, bool bold = false)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = Text(label, muted: true);
            Grid.SetRow(name, row);
            grid.Children.Add(name);
            for (int i = 0; i < entries.Count; i++)
            {
                string text = value(entries[i]);
                string d = i != baseline && delta is not null ? delta(entries[i]) : "";
                var cell = new StackPanel { Orientation = Orientation.Horizontal };
                cell.Children.Add(Text(text, bold: bold));
                if (d.Length > 0)
                {
                    cell.Children.Add(Text(d, brush: d.StartsWith('+') ? Good : Bad));
                }

                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, i + 1);
                grid.Children.Add(cell);
            }
        }

        Row("", e => $"{e.Player} ({e.ClassName})", bold: true);
        Row("", e => e.Label);
        Row(loc["Compare.Power"], e => e.CombatPower is int p ? p.ToString("N0") : "–", e => e.CombatPower is int p && entries[baseline].CombatPower is int b ? Delta(p, b) : "");
        Row(loc["Compare.Duration"], e => $"{e.Seconds:0.0} s");
        Row(loc["Compare.Damage"], e => e.TotalDamage.ToString("N0"), e => Delta(e.TotalDamage, entries[baseline].TotalDamage));
        Row(loc["Compare.Dps"], e => e.Dps.ToString("N0"), e => Delta(e.Dps, entries[baseline].Dps), bold: true);
        Row(loc["Compare.BossDamage"], e => e.BossDamage is long b ? b.ToString("N0") : "–", e => e.BossDamage is long b && entries[baseline].BossDamage is long r ? Delta(b, r) : "");
        Row(loc["Compare.AddDamage"], e => e.AddDamage is long a ? a.ToString("N0") : "–");
        Row(loc["Compare.Hits"], e => e.Hits.ToString("N0"), e => Delta(e.Hits, entries[baseline].Hits));
        Row(loc["Compare.Crit"], e => e.CritRate.ToString("0.0", CultureInfo.CurrentCulture) + " %");
        Row(loc["Compare.Deaths"], e => e.Online ? "–" : e.Deaths.ToString());
        return grid;
    }

    private Grid SkillTable(List<CompareEntry> entries, List<CompareRow> table, int baseline)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        string[] heads = { loc["Compare.Dps"], loc["Compare.Share"], loc["Compare.Hits"], loc["Compare.Avg"], loc["Compare.Uses"], loc["Compare.Gap"] };
        double[] widths = { 70, 56, 56, 70, 56, 76 };
        for (int e = 0; e < entries.Count; e++)
        {
            for (int c = 0; c < heads.Length; c++)
            {
                if (c == heads.Length - 1 && e == baseline)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                    continue;
                }

                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[c]) });
            }
        }

        // header: the player over his six columns
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int e = 0; e < entries.Count; e++)
        {
            int first = 1 + e * heads.Length;
            var who = Text(entries[e].Player + (e == baseline ? " ◆" : ""), bold: true);
            Grid.SetRow(who, 0);
            Grid.SetColumn(who, first);
            Grid.SetColumnSpan(who, heads.Length);
            grid.Children.Add(who);
            for (int c = 0; c < heads.Length; c++)
            {
                if (c == heads.Length - 1 && e == baseline)
                {
                    continue;
                }

                var head = Text(heads[c], muted: true, align: TextAlignment.Right);
                Grid.SetRow(head, 1);
                Grid.SetColumn(head, first + c);
                grid.Children.Add(head);
            }
        }

        foreach (CompareRow row in table)
        {
            int r = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = Text(Aion2.Protocol.Aion2SkillNames.Display(row.Skill), bold: row.Skill == _timelineSkill);
            name.Cursor = System.Windows.Input.Cursors.Hand;
            string skill = row.Skill;
            name.MouseLeftButtonUp += (_, _) =>
            {
                _timelineSkill = skill;
                Render();
            };
            Grid.SetRow(name, r);
            grid.Children.Add(name);
            for (int e = 0; e < entries.Count; e++)
            {
                int first = 1 + e * heads.Length;
                CompareSkill? s = row.PerEntry[e];
                string[] cells =
                {
                    s is null ? "" : row.Dps[e].ToString("N0"),
                    s is null ? "" : row.Share[e].ToString("0.0", CultureInfo.CurrentCulture) + "%",
                    s is null ? "" : s.Hits.ToString("N0"),
                    s is null ? "" : s.Average.ToString("N0"),
                    s is null || s.Uses < 0 ? "" : s.Uses.ToString("N0"),
                    e == baseline || s is null && row.PerEntry[baseline] is null ? "" : (row.Gaps[e] >= 0 ? "+" : "") + row.Gaps[e].ToString("N0"),
                };
                for (int c = 0; c < heads.Length; c++)
                {
                    if (c == heads.Length - 1 && e == baseline)
                    {
                        continue;
                    }

                    Brush? brush = c == heads.Length - 1 && cells[c].Length > 0 ? (row.Gaps[e] >= 0 ? Good : Bad) : null;
                    var cell = Text(cells[c], align: TextAlignment.Right, brush: brush);
                    Grid.SetRow(cell, r);
                    Grid.SetColumn(cell, first + c);
                    grid.Children.Add(cell);
                }
            }
        }

        return grid;
    }

    /// <summary>The effects that were on the players, the ones with the biggest difference to the baseline first: the share of the fight each was on, how often it was put on, and the difference.</summary>
    private Grid BuffGrid(List<CompareEntry> entries, List<CompareBuffRow> rows, int baseline)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        string[] heads = { loc["Compare.Uptime"], loc["Compare.Applied"], loc["Compare.Gap"] };
        double[] widths = { 70, 70, 76 };
        for (int e = 0; e < entries.Count; e++)
        {
            for (int c = 0; c < heads.Length; c++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(c == heads.Length - 1 && e == baseline ? 8 : widths[c]) });
            }
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int e = 0; e < entries.Count; e++)
        {
            var who = Text(entries[e].Player + (e == baseline ? " ◆" : ""), bold: true);
            Grid.SetRow(who, 0);
            Grid.SetColumn(who, 1 + e * heads.Length);
            Grid.SetColumnSpan(who, heads.Length);
            grid.Children.Add(who);
            for (int c = 0; c < heads.Length; c++)
            {
                if (c == heads.Length - 1 && e == baseline)
                {
                    continue;
                }

                var head = Text(heads[c], muted: true, align: TextAlignment.Right);
                Grid.SetRow(head, 1);
                Grid.SetColumn(head, 1 + e * heads.Length + c);
                grid.Children.Add(head);
            }
        }

        foreach (CompareBuffRow row in rows)
        {
            int r = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = Text(Aion2.Protocol.Aion2SkillNames.Display(row.Name));
            Grid.SetRow(name, r);
            grid.Children.Add(name);
            for (int e = 0; e < entries.Count; e++)
            {
                CompareBuff? b = row.PerEntry[e];
                string[] cells =
                {
                    b is null ? "0 %" : b.UptimePercent.ToString("0", CultureInfo.CurrentCulture) + " %",
                    b is null || b.Applications < 0 ? "" : b.Applications.ToString("N0"),
                    e == baseline ? "" : (row.Gaps[e] >= 0 ? "+" : "") + row.Gaps[e].ToString("0", CultureInfo.CurrentCulture) + " %",
                };
                for (int c = 0; c < heads.Length; c++)
                {
                    if (c == heads.Length - 1 && e == baseline)
                    {
                        continue;
                    }

                    Brush? brush = c == heads.Length - 1 && cells[c].Length > 0 ? (row.Gaps[e] >= 0 ? Good : Bad) : null;
                    var cell = Text(cells[c], align: TextAlignment.Right, brush: brush);
                    Grid.SetRow(cell, r);
                    Grid.SetColumn(cell, 1 + e * heads.Length + c);
                    grid.Children.Add(cell);
                }
            }
        }

        return grid;
    }

    /// <summary>The hits (thin) and cast notices (tall) of the picked skill, every player on the same axis from the start of his fight.</summary>
    private UIElement Timelines(List<CompareEntry> entries)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var panel = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        if (_timelineSkill is null)
        {
            return panel;
        }

        panel.Children.Add(Text(string.Format(loc["Compare.Timeline"], Aion2.Protocol.Aion2SkillNames.Display(_timelineSkill)), bold: true));
        double span = Math.Max(1, entries.Max(e => e.Seconds));
        foreach (CompareEntry entry in entries)
        {
            panel.Children.Add(Text(entry.Player, muted: true));
            if (entry.Marks is null)
            {
                panel.Children.Add(Text(loc["Compare.NoTimeline"], muted: true));
                continue;
            }

            var canvas = new Canvas { Height = 30, Margin = new Thickness(0, 0, 0, 6), ClipToBounds = true };
            canvas.SetResourceReference(Panel.BackgroundProperty, "Brush.Panel");
            canvas.Loaded += (_, _) => DrawMarks(canvas, entry, span);
            canvas.SizeChanged += (_, _) => DrawMarks(canvas, entry, span);
            panel.Children.Add(canvas);
        }

        return panel;
    }

    private void DrawMarks(Canvas canvas, CompareEntry entry, double span)
    {
        canvas.Children.Clear();
        double width = canvas.ActualWidth;
        if (width <= 0 || _timelineSkill is null)
        {
            return;
        }

        foreach (CompareMark mark in entry.Marks!.Where(m => m.Skill == _timelineSkill))
        {
            double x = mark.Seconds / span * width;
            var line = new Line { X1 = x, X2 = x, Y1 = mark.IsUse ? 2 : 14, Y2 = 28, StrokeThickness = mark.IsUse ? 1.5 : 1 };
            line.SetResourceReference(Shape.StrokeProperty, mark.IsUse ? "Brush.Warning" : "Brush.Accent");
            canvas.Children.Add(line);
        }

        // a tick every ten seconds
        for (double s = 0; s <= span; s += 10)
        {
            double x = s / span * width;
            var tick = new Line { X1 = x, X2 = x, Y1 = 0, Y2 = 4, StrokeThickness = 1 };
            tick.SetResourceReference(Shape.StrokeProperty, "Brush.TextMuted");
            canvas.Children.Add(tick);
        }
    }
}

internal static class ListBoxExtensions
{
    public static IEnumerable<int> SelectedIndices(this ListBox list) => list.SelectedItems.Cast<object>().Select(list.Items.IndexOf).OrderBy(i => i);
}
