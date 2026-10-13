using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Combat;
using AionDPS.History;
using Microsoft.Win32;

namespace AionDPS.Ui;

/// <summary>
/// Every stay the meter has recorded (History/SessionRecorder), grouped by day: look at who did what, put one into the meter, keep one
/// as a file and load such a file again for a later look. The recorder is the caller's and keeps recording while this is open.
/// </summary>
public partial class SessionManagerWindow : Window
{
    private readonly SessionRecorder _recorder;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _loading;

    /// <summary>Builds the combat analyzer for one player of a session (set by the main window): the player's details limited to the session's time.</summary>
    public Func<SessionInfo, PlayerRow, Window?>? AnalyzerFactory { get; set; }

    private Window? _analyzer;
    private bool _analyzerFollows = true;
    private bool _moving;
    private Point _lastLocation;
    private bool _syncingPlayers;
    private int? _analyzedPlayer;
    private int? _analyzedSession;

    /// <summary>Gives the rotation, effect, stagger and replay data of a session for saving; set by the main window (it holds the capture). Null result: none to save.</summary>
    public Func<SessionInfo, Combat.FightExtras?>? ExtrasFor { get; set; }

    public SessionManagerWindow(SessionRecorder recorder)
    {
        InitializeComponent();
        ThemedChrome.Apply(this);
        _recorder = recorder;

        LocalizationManager loc = LocalizationManager.Instance;
        _loading = true;
        SaveExtrasBox.IsChecked = MeterSettings.Load().SessionSaveExtras;
        string[] kinds = { "Sessions.FilterAll", "Sessions.FilterMaps", "Sessions.FilterInstances", "Sessions.FilterSaved" };
        for (int i = 0; i < kinds.Length; i++)
        {
            var chip = new RadioButton { Content = loc[kinds[i]], Tag = i, GroupName = "kind", IsChecked = i == 0, Style = (Style)FindResource("Chip") };
            chip.Checked += OnFilterChanged;
            KindChips.Children.Add(chip);
        }

        TimeSpan[] spans = { TimeSpan.FromHours(24), TimeSpan.FromHours(12), TimeSpan.FromHours(6), TimeSpan.FromHours(1) };
        foreach (TimeSpan span in spans)
        {
            var chip = new RadioButton
            {
                Content = $"{(int)span.TotalHours} h",
                Tag = span,
                GroupName = "zoom",
                IsChecked = span == Timeline.Span,
                Style = (Style)FindResource("Chip"),
            };
            chip.Checked += (_, _) => Timeline.SetSpan(span);
            ZoomChips.Children.Add(chip);
        }

        var now = new Button { Content = loc["Sessions.JumpNow"], Margin = new Thickness(0, 0, 0, 0), Padding = new Thickness(10, 1, 10, 1) };
        now.Click += (_, _) => Timeline.JumpToNow();
        ZoomChips.Children.Add(now);
        Timeline.SpanChanged += () =>
        {
            foreach (RadioButton chip in ZoomChips.Children.OfType<RadioButton>())
            {
                chip.IsChecked = chip.Tag is TimeSpan span && span == Timeline.Span;
            }
        };
        _loading = false;

        _recorder.Changed += OnRecorderChanged;
        // The numbers of the stay in progress grow; the list itself is only rebuilt on a change, so it does not jump under the mouse.
        _tick.Tick += (_, _) =>
        {
            Timeline.Follow();
            if (SessionList.SelectedItem is Item { Session.IsOpen: true } running)
            {
                ShowDetails(running.Session);
            }
        };
        _tick.Start();
        Closed += (_, _) =>
        {
            CloseAnalyzer();
            _tick.Stop();
            _recorder.Changed -= OnRecorderChanged;
        };
        Refresh();
    }

    /// <summary>Raised when the user asks to see a session in the meter; MainWindow decides how (the stay in progress is the live view).</summary>
    public event Action<SessionInfo>? ShowRequested;

    /// <summary>One line of the list.</summary>
    public sealed record Item(SessionInfo Session, string Title, string Facts, string Status, Brush Accent, DateTime Start, string Badge, Brush BadgeBrush);

    public sealed record PlayerRow(int Id, string Name, string ClassName, long Damage, string DpsDisplay);

    /// <summary>One line of the plausibility check.</summary>
    public sealed record FindingRow(string Icon, Brush Brush, string Text);

    /// <summary>One boss attempt of the stay.</summary>
    public sealed record BossRow(string Name, string Start, string Length, string Result, string Damage, string Enrage);

    private void OnRecorderChanged() => Dispatcher.BeginInvoke(new Action(Refresh));

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            Refresh();
        }
    }

    public void Refresh()
    {
        if (_loading)
        {
            return;
        }

        LocalizationManager loc = LocalizationManager.Instance;
        int? keep = (SessionList.SelectedItem as Item)?.Session.Number;
        string search = SearchBox.Text.Trim();
        int kind = KindChips.Children.OfType<RadioButton>().FirstOrDefault(chip => chip.IsChecked == true)?.Tag as int? ?? 0;

        var items = new List<Item>();
        foreach (SessionInfo session in _recorder.Sessions.Reverse())
        {
            if (kind == 1 && session.Kind != SessionKind.Map || kind == 2 && session.Kind != SessionKind.Instance || kind == 3 && !session.IsSaved)
            {
                continue;
            }

            string name = SessionLabels.NameOf(session);
            if (search.Length > 0 && !name.Contains(search, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            TimeSpan length = session.Duration(DateTime.Now);
            string facts = $"{SessionLabels.TimeSpanOf(session)} · {FormatLength(length)} · {session.Data.Events.Count:N0} {loc["Sessions.Hits"]}";
            string status = session.IsOpen ? loc["Sessions.Running"] : session.IsSaved ? "★" : "";
            var accent = new SolidColorBrush(SessionLabels.ColorOf(session.Kind));
            accent.Freeze();
            // The check badge: only for a finished stay that had something to check (the open one is judged when it is picked).
            SessionAnalysis? analysis = session.IsOpen ? null : SessionAnalyzer.Analyze(session);
            string badge = analysis is { HasChecks: true } ? SessionLabels.IconOf(analysis.Overall) : "";
            items.Add(new Item(session, name, facts, status, accent, session.Start, badge, SessionLabels.LevelBrush(analysis?.Overall ?? FindingLevel.Info)));
        }

        var view = new ListCollectionView(items);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Item.Start), new DayConverter()));
        SessionList.ItemsSource = view;
        Timeline.SetSessions(_recorder.Sessions.ToList(), keep is int selectedNumber ? _recorder.Sessions.FirstOrDefault(x => x.Number == selectedNumber) : null);
        CountText.Text = string.Format(loc["Sessions.Count"], items.Count);
        if (keep is int number && items.FirstOrDefault(item => item.Session.Number == number) is { } again)
        {
            SessionList.SelectedItem = again;
        }
        else
        {
            ShowDetails(null);
        }
    }

    /// <summary>Groups the list by calendar day, with "Today" and "Yesterday" in words.</summary>
    private sealed class DayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        {
            DateTime day = ((DateTime)value).Date;
            LocalizationManager loc = LocalizationManager.Instance;
            return day == DateTime.Today ? loc["Sessions.Today"]
                : day == DateTime.Today.AddDays(-1) ? loc["Sessions.Yesterday"]
                : day.ToString("ddd d. MMM", culture);
        }

        public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    private static string FormatLength(TimeSpan length) => length.TotalHours >= 1
        ? $"{(int)length.TotalHours} h {length.Minutes:00} min"
        : $"{Math.Max(1, (int)length.TotalMinutes)} min";

    private void OnSessionSelected(object sender, SelectionChangedEventArgs e)
    {
        SessionInfo? session = (SessionList.SelectedItem as Item)?.Session;
        ShowDetails(session);
        if (session is not null)
        {
            Timeline.SetSessions(_recorder.Sessions.ToList(), session);
            Timeline.Reveal(session);
        }
    }

    /// <summary>A click in the strip picks the same stay in the list.</summary>
    private void OnTimelinePicked(SessionInfo session)
    {
        if (SessionList.ItemsSource is ListCollectionView view && view.Cast<Item>().FirstOrDefault(item => item.Session.Number == session.Number) is { } item)
        {
            SessionList.SelectedItem = item;
            SessionList.ScrollIntoView(item);
        }
    }

    private void OnEarlierClicked(object sender, RoutedEventArgs e) => Timeline.Pan(-0.5);

    private void OnLaterClicked(object sender, RoutedEventArgs e) => Timeline.Pan(0.5);

    private void ShowDetails(SessionInfo? session)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        bool any = session is not null;
        EmptyText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        PlayersGrid.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        CheckPanel.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        BossGrid.Visibility = Visibility.Collapsed;
        ShowButton.IsEnabled = any;
        SaveButton.IsEnabled = any;
        DeleteButton.IsEnabled = session is { IsOpen: false };
        if (session is null)
        {
            DetailTitle.Text = "";
            DetailFacts.Text = "";
            PlayersGrid.ItemsSource = null;
            CloseAnalyzer();
            FindingsList.ItemsSource = null;
            BossGrid.ItemsSource = null;
            return;
        }

        DetailTitle.Text = SessionLabels.NameOf(session);
        DetailFacts.Text = $"{(session.Kind == SessionKind.Instance ? loc["Sessions.KindInstance"] : loc["Sessions.KindMap"])} \u00B7 "
            + $"{SessionLabels.TimeSpanOf(session)} \u00B7 {FormatLength(session.Duration(DateTime.Now))}";
        List<PlayerRow> players = PlayersOf(session);
        _syncingPlayers = true;
        PlayersGrid.ItemsSource = players;
        PlayerRow? analyzed = _analyzedSession == session.Number ? players.FirstOrDefault(row => row.Id == _analyzedPlayer) : null;
        if (analyzed is not null)
        {
            PlayersGrid.SelectedItem = analyzed;
        }

        _syncingPlayers = false;
        if (_analyzedSession != session.Number)
        {
            // a session picked: the analyzer opens beside the manager with its first player
            if (players.Count > 0)
            {
                _syncingPlayers = true;
                PlayersGrid.SelectedItem = players[0];
                _syncingPlayers = false;
                OpenAnalyzer(session, players[0]);
            }
            else
            {
                CloseAnalyzer();
            }
        }

        // The open stay is read again from the capture's hit points each time it is looked at.
        if (session.IsOpen)
        {
            _recorder.CaptureHp(session);
        }

        SessionAnalysis analysis = SessionAnalyzer.Analyze(session);
        var findings = analysis.Findings
            .Select(f => new FindingRow(SessionLabels.IconOf(f.Level), SessionLabels.LevelBrush(f.Level), string.Format(loc[f.Key], f.Args)))
            .ToList();
        if (analysis.Attempts.Count == 0)
        {
            findings.Insert(0, new FindingRow(SessionLabels.IconOf(FindingLevel.Info), SessionLabels.LevelBrush(FindingLevel.Info), loc["Sessions.Check.Nothing"]));
        }

        FindingsList.ItemsSource = findings;
        BossGrid.ItemsSource = analysis.Attempts.Select(a => BossRowOf(a, loc)).ToList();
        BossGrid.Visibility = analysis.Attempts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static BossRow BossRowOf(BossAttempt attempt, LocalizationManager loc)
    {
        string result = attempt.Check is null && !attempt.Killed ? "-" : attempt.Killed ? loc["Sessions.Boss.Killed"] : loc["Sessions.Boss.NotKilled"];
        string damage = attempt.Counted.ToString("N0") + (attempt.Check is { } check && check.Lost > 0 ? $" ({check.Ratio:P0})" : "");
        string enrage = attempt.Enrage is { } rage
            ? string.Format(
                loc[rage.Exact ? (rage.Factor > 0 ? "Sessions.Boss.EnrageSeen" : "Sessions.Boss.EnrageSeenAt") : "Sessions.Boss.Enrage"],
                $"{(int)rage.At.TotalMinutes:00}:{rage.At.Seconds:00}",
                rage.Factor.ToString("0.#", CultureInfo.CurrentCulture))
            : loc["Sessions.Boss.NoEnrage"];
        return new BossRow(attempt.Name, attempt.Start.ToString("HH:mm:ss"), $"{(int)attempt.Duration.TotalMinutes:00}:{attempt.Duration.Seconds:00}", result, damage, enrage);
    }

    /// <summary>Who dealt how much in the stay: the players (the ids a class is known for; a file from the plain meter session has no classes, then everyone).</summary>
    private static List<PlayerRow> PlayersOf(SessionInfo session)
    {
        bool onlyPlayers = session.Classes.Count > 0;
        return session.Data.Events
            .Where(ev => !ev.IsHeal && (!onlyPlayers || session.Classes.ContainsKey(ev.SourceObjectId)))
            .GroupBy(ev => ev.SourceObjectId)
            .Select(group =>
            {
                List<DamageEvent> hits = group.ToList();
                double? dps = DpsCalculator.AllDpsWallClock(hits, group.Key);
                return new PlayerRow(
                    group.Key,
                    session.Names.GetValueOrDefault(group.Key) ?? $"0x{group.Key:X8}",
                    session.Classes.GetValueOrDefault(group.Key) ?? "",
                    hits.Sum(ev => ev.Amount),
                    dps is double value ? value.ToString("N0") : "-");
            })
            .OrderByDescending(row => row.Damage)
            .Take(30)
            .ToList();
    }

    /// <summary>A double click brings the analyzer to the front; the session is already analysed since it was picked.</summary>
    private void OnSessionDoubleClick(object sender, MouseButtonEventArgs e) => _analyzer?.Activate();

    private void OnPlayerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingPlayers && PlayersGrid.SelectedItem is PlayerRow row && SessionList.SelectedItem is Item item)
        {
            OpenAnalyzer(item.Session, row);
        }
    }

    // ---- the combat analyzer: the player details of the picked session, in a window of its own that sits beside the manager ------------------

    private void OpenAnalyzer(SessionInfo session, PlayerRow row)
    {
        if (AnalyzerFactory?.Invoke(session, row) is not { } next)
        {
            return;
        }

        _analyzedSession = session.Number;
        _analyzedPlayer = row.Id;
        next.Title = $"{LocalizationManager.Instance["Sessions.Analyzer.Title"]} - {row.Name} - {SessionLabels.NameOf(session)}";
        next.Owner = this;
        next.WindowStartupLocation = WindowStartupLocation.Manual;

        Window? old = _analyzer;
        if (old is not null && !_analyzerFollows)
        {
            // the user moved it away: the next player appears where it stands
            next.Left = old.Left;
            next.Top = old.Top;
            next.Width = old.ActualWidth;
            next.Height = old.ActualHeight;
        }
        else
        {
            _analyzerFollows = true;
            _moving = true;
            double width = next.Width;
            Rect area = SystemParameters.WorkArea;
            next.Left = Math.Max(area.Left, Math.Min(Left + ActualWidth, area.Right - width));
            next.Top = Top;
            next.Height = ActualHeight;
            _moving = false;
        }

        _analyzer = next;
        _lastLocation = new Point(Left, Top);
        next.LocationChanged += (_, _) =>
        {
            if (!_moving && ReferenceEquals(_analyzer, next))
            {
                _analyzerFollows = false;
            }
        };
        next.Closed += (_, _) =>
        {
            if (ReferenceEquals(_analyzer, next))
            {
                _analyzer = null;
            }
        };
        next.Show();
        old?.Close();
    }

    private void CloseAnalyzer()
    {
        _analyzedSession = null;
        _analyzedPlayer = null;
        Window? window = _analyzer;
        _analyzer = null;
        window?.Close();
    }

    /// <summary>The analyzer follows the manager when that is moved, as long as the user has not put it somewhere else.</summary>
    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (_analyzer is { } window && _analyzerFollows)
        {
            _moving = true;
            window.Left += Left - _lastLocation.X;
            window.Top += Top - _lastLocation.Y;
            _moving = false;
        }

        _lastLocation = new Point(Left, Top);
    }

    private void OnShowClicked(object sender, RoutedEventArgs e)
    {
        if (SessionList.SelectedItem is Item item)
        {
            ShowRequested?.Invoke(item.Session);
        }
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (SessionList.SelectedItem is not Item item)
        {
            return;
        }

        Directory.CreateDirectory(SessionFile.DefaultDirectory);
        SessionInfo session = item.Session;
        var dialog = new SaveFileDialog
        {
            InitialDirectory = SessionFile.DefaultDirectory,
            FileName = SafeFileName($"{SessionLabels.NameOf(session)} {session.Start:yyyy-MM-dd HH-mm}") + SessionFile.Extension,
            Filter = $"Aion DPS session (*{SessionFile.Extension})|*{SessionFile.Extension}",
            DefaultExt = SessionFile.Extension,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            SessionFile.SaveStay(dialog.FileName, session, SaveExtrasBox.IsChecked == true ? (session.Extras ?? ExtrasFor?.Invoke(session)) : null);
            session.IsSaved = true;
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ThemedMessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>The choice is kept: the next save, and the next start, begin with it.</summary>
    private void OnSaveExtrasChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded)
        {
            return;
        }

        var settings = MeterSettings.Load();
        settings.SessionSaveExtras = SaveExtrasBox.IsChecked == true;
        settings.Save();
    }

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private void OnLoadFileClicked(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SessionFile.DefaultDirectory);
        var dialog = new OpenFileDialog
        {
            InitialDirectory = SessionFile.DefaultDirectory,
            Filter = $"Aion DPS session (*{SessionFile.Extension})|*{SessionFile.Extension}",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            SessionInfo loaded = Import(SessionFile.Load(dialog.FileName));
            Refresh();
            SessionList.SelectedItem = (SessionList.ItemsSource as ListCollectionView)?.Cast<Item>().FirstOrDefault(item => item.Session.Number == loaded.Number);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            ThemedMessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>A file of one stay comes back as that stay; a plain meter-session file becomes a stay of an unknown map, from its first to its last hit.</summary>
    private SessionInfo Import(SessionFile.LoadedSession file)
    {
        SessionInfo session;
        if (file.Stay is { } stay)
        {
            session = _recorder.Import(stay.ZoneId, stay.Kind, stay.Start, stay.End);
            session.Data.IngestEvents(file.Events.Concat(stay.Shields));
            session.Extras = stay.Extras;
            foreach ((int id, string cls) in stay.Classes)
            {
                session.Classes[id] = cls;
            }
        }
        else
        {
            DateTime first = file.Events.Count > 0 ? file.Events.Min(ev => ev.Timestamp) : file.SavedAt;
            DateTime last = file.Events.Count > 0 ? file.Events.Max(ev => ev.Timestamp) : file.SavedAt;
            session = _recorder.Import(0, SessionKind.Map, first, last);
            session.Data.IngestEvents(file.Events);
        }

        foreach ((int id, string name) in file.Names)
        {
            session.Names[id] = name;
        }

        return session;
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (SessionList.SelectedItem is not Item { Session.IsOpen: false } item)
        {
            return;
        }

        LocalizationManager loc = LocalizationManager.Instance;
        if (ThemedMessageBox.Show(this, string.Format(loc["Sessions.DeleteAsk"], SessionLabels.NameOf(item.Session)), Title,
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _recorder.Remove(item.Session);
        }
    }
}
