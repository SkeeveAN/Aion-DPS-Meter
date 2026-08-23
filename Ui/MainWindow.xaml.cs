using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Input;
using AionSniffer.Combat;
using AionSniffer.Protocol;

namespace AionSniffer.Ui;

/// <summary>
/// The main meter window. Currently self-contained (its own LiveAggregator, fed only by
/// "Load Demo Data") -- wiring it to Program.cs's real capture loop is a follow-up once the
/// calibration run confirms the opcodes; see OnAttackDecoded, which is already shaped for that.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ObservableCollection<PlayerRow> _rows = new();
    private readonly Dictionary<int, PlayerRow> _rowsByObjectId = new();
    private readonly LiveAggregator _aggregator = new();
    private NativeOverlay? _overlay;
    private bool _paused;
    private bool _hideUiActive;

    public MainWindow()
    {
        InitializeComponent();
        PlayersGrid.ItemsSource = _rows;
        OverlayContent.ItemsSource = _rows;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _overlay = new NativeOverlay(this);
        _overlay.HotkeyPressed += () => Dispatcher.Invoke(SetHideUi, System.Windows.Threading.DispatcherPriority.Input);
    }

    protected override void OnClosed(EventArgs e)
    {
        _overlay?.Dispose();
        base.OnClosed(e);
    }

    /// <summary>
    /// Real wiring point for the capture pipeline: Program.cs's HandleDecoded would call this
    /// (via Dispatcher.Invoke/BeginInvoke, since packets arrive on SharpPcap's capture thread,
    /// not the UI thread) once SM_ATTACK opcode 0x36 is confirmed against a live server.
    /// </summary>
    public void OnAttackDecoded(DateTime timestamp, CombatPacketParser.AttackPacket attack)
    {
        if (_paused)
        {
            return;
        }

        _aggregator.IngestAttack(timestamp, attack);
        RefreshRows();
    }

    private void RefreshRows()
    {
        foreach (var group in _aggregator.Events.GroupBy(ev => ev.SourceObjectId))
        {
            if (!_rowsByObjectId.TryGetValue(group.Key, out var row))
            {
                row = new PlayerRow(group.Key) { Name = $"0x{group.Key:X8}" };
                _rowsByObjectId[group.Key] = row;
                _rows.Add(row);
            }

            row.Damage = group.Sum(ev => ev.Amount);
            row.Dps = DpsCalculator.AllDpsWallClock(_aggregator.Events, group.Key);
        }
    }

    private void OnLoadDemoDataClicked(object sender, RoutedEventArgs e)
    {
        // Same numbers as SelfCheck's Gladiator/Zauberer scenario -- lets the UI be checked
        // visually without a capture, and the DPS column can be eyeballed against the selftest's
        // console output for the same inputs.
        var start = DateTime.UtcNow;
        const int gladiator = 1;
        const int zauberer = 2;
        const int boss = 100;

        _aggregator.IngestAttack(start, new CombatPacketParser.AttackPacket(gladiator, boss, 90, 100,
            new List<CombatPacketParser.AttackHit> { new(1000, 5, 0), new(1200, 5, 0) }));
        _aggregator.IngestAttack(start.AddSeconds(2), new CombatPacketParser.AttackPacket(gladiator, boss, 80, 100,
            new List<CombatPacketParser.AttackHit> { new(1100, 5, 0) }));
        _aggregator.IngestAttack(start.AddSeconds(1), new CombatPacketParser.AttackPacket(zauberer, boss, 85, 95,
            new List<CombatPacketParser.AttackHit> { new(50_000, 7, 0) }));

        RefreshRows();

        if (_rowsByObjectId.TryGetValue(gladiator, out var g))
        {
            g.Name = "Strohmie";
            g.ClassName = "Gladiator";
            g.Level = 80;
        }

        if (_rowsByObjectId.TryGetValue(zauberer, out var z))
        {
            z.Name = "Nxrse";
            z.ClassName = "Sorcerer";
            z.Level = 80;
        }
    }

    private void OnClearClicked(object sender, RoutedEventArgs e)
    {
        _rows.Clear();
        _rowsByObjectId.Clear();
        // Note: LiveAggregator itself has no Clear() yet -- a real "new session" would need one
        // (or a fresh instance) so DpsCalculator doesn't keep averaging in pre-clear events.
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e) => CopyRowsToClipboard();

    private void OnCopyAllClicked(object sender, RoutedEventArgs e) => CopyRowsToClipboard();

    private void CopyRowsToClipboard()
    {
        var sb = new StringBuilder();
        foreach (var row in _rows)
        {
            sb.AppendLine($"{row.Name}\t{row.ClassName}\t{row.Level}\t{row.Damage}\t{row.DpsDisplay}");
        }

        if (sb.Length > 0)
        {
            Clipboard.SetText(sb.ToString());
        }
    }

    private void OnPauseClicked(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        PauseButton.Content = _paused ? "Resume" : "Pause";
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        var settings = MeterSettings.Load();
        var window = new SettingsWindow(settings) { Owner = this };
        if (window.ShowDialog() == true)
        {
            settings.Save();
        }
    }

    private void OnNetworkMenuClicked(object sender, RoutedEventArgs e)
    {
        new NetworkSettingsWindow { Owner = this }.ShowDialog();
    }

    private void OnAlwaysOnTopClicked(object sender, RoutedEventArgs e)
    {
        Topmost = AlwaysOnTopMenuItem.IsChecked;
    }

    private void OnModeClicked(object sender, RoutedEventArgs e)
    {
        // Damage is the only mode with a working data source (see the Heal/Relic tooltips for
        // why) -- always end up back on it, and explain why if the user picked something else,
        // rather than silently ignoring the click or pretending to switch modes.
        var clicked = sender as MenuItem;
        DamageModeItem.IsChecked = true;
        HealModeItem.IsChecked = false;
        RelicModeItem.IsChecked = false;

        if (clicked is not null && clicked != DamageModeItem)
        {
            MessageBox.Show(this, $"\"{clicked.Header}\" mode isn't implemented yet -- see its tooltip in the Mode menu for why.",
                "Not implemented", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnShowDamageView(object sender, RoutedEventArgs e)
    {
        // Already the only implemented view -- kept as a no-op handler for structural parity
        // with the reference's 5-icon nav rail (see the XAML comment above it).
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnHideUiClicked(object sender, RoutedEventArgs e) => SetHideUi();

    private void SetHideUi()
    {
        _hideUiActive = !_hideUiActive;
        NormalContent.Visibility = _hideUiActive ? Visibility.Collapsed : Visibility.Visible;
        OverlayContent.Visibility = _hideUiActive ? Visibility.Visible : Visibility.Collapsed;
        _overlay?.SetClickThrough(_hideUiActive);
    }
}
