using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;
using AionDPS.Combat.Sources;
using AionDPS.Data;
using AionDPS.History;
using AionDPS.Update;
using AionDPS.Upload;
using VelopackUpdateInfo = Velopack.UpdateInfo;

namespace AionDPS.Ui;

/// <summary>
/// The main meter window. Holds its own LiveAggregator, fed from the Aion 2 packet capture.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ObservableCollection<PlayerRow> _rows = new();
    private ListCollectionView? _playersView;

    /// <summary>Running totals over the events (see DamageIndex); the refresh takes in what is new instead of rescanning.</summary>
    private readonly DamageIndex _index = new();

    /// <summary>The stress test sets this to compare the totals of the index with those of a full rescan.</summary>
    internal bool ForceFullRefresh { get; set; }

    private readonly Dictionary<int, PlayerRow> _rowsByObjectId = new();
    private readonly Dictionary<int, string> _targetNames = new();

    /// <summary>
    /// Identity (name/class/level) per source object id, independent of PlayerRow. RefreshRows
    /// removes and later re-creates a row for any source that drops out of the current Mob/Boss
    /// filter, but a fresh PlayerRow only ever gets "0x########" -- the real identity, set once
    /// directly on the row object, would otherwise be lost for good the moment that row got
    /// filtered out once. Mirrors _targetNames, which already solved the identical problem for
    /// target display names. Reserved for the network (Aion 2 packet-capture) path, which does not
    /// populate it yet -- see ResolveDisplayName's own remarks.
    /// </summary>
    private readonly Dictionary<int, (string Name, string ClassName, int Level)> _playerIdentities = new();

    private readonly LiveAggregator _aggregator = new();
    private NativeOverlay? _overlay;
    private GlobalHotkeys? _hotkeys;
    private bool _paused;
    private bool _hideUiActive;
    private bool _topmostBeforeHideUi;

    /// <summary>Null = "All" (the Mob/Boss filter's first, always-present entry).</summary>
    private int? _selectedTargetId;

    /// <summary>
    /// Both null = the whole history of whichever target _selectedTargetId names (every kill of it
    /// combined), matching this app's original behavior. Both set = one specific numbered run
    /// picked from the dropdown (see ApplyMobBossSearchFilter's own remarks on why a boss farmed
    /// repeatedly - per the user, "e.g. Raksang Boilheart 5x in a row" - gets split into "#1".."#N"
    /// entries there instead of one entry silently averaging all of them together). Consulted by
    /// RefreshRows (to scope the live table) and the 3-arg BuildEncounterUpload overload (to scope
    /// an upload) - every OTHER place that sets _selectedTargetId directly (not via the dropdown)
    /// must also set these back to null, since they all want that target's full history, not
    /// whatever run happened to be selected in the UI before that code ran.
    /// </summary>
    private DateTime? _selectedRunWindowStart;
    private DateTime? _selectedRunWindowEnd;

    /// <summary>Null = "All" (ClassFilter's first entry, no Tag). Per the user: was purely
    /// decorative until now (see the XAML comment on ClassFilter's own history) -- expected to
    /// actually filter once other players' classes started being detected at all, and an empty
    /// result when nobody of that class is present is the correct, intended behavior, not a bug.</summary>
    private string? _selectedClassFilter;

    /// <summary>Per the user: an option to switch the whole grid to "PVP DMG" - damage against
    /// other PLAYERS only, mobs/bosses excluded. Overrides the Mob/Boss filter (a specific NPC
    /// target means nothing here) rather than combining with it - see RefreshRows/OnPvpOnlyClicked.</summary>
    private bool _pvpOnly;

    /// <summary>Master list backing the Mob/Boss dropdown -- kept separate from MobBossFilter's own
    /// Items, which now show a SEARCH-FILTERED subset (see ApplyMobBossSearchFilter/the
    /// SearchableComboBox template in the XAML). RefreshMobBossFilterItems's dedup and "upload
    /// every boss since Clear" (OnUploadLastRunClicked) both need the full, unfiltered set
    /// regardless of whatever the user currently has typed into the search box.</summary>
    private readonly List<(int TargetId, string Name)> _mobBossEntries = new();

    /// <summary>The XAML-declared "All" entry, captured once in the constructor so
    /// ApplyMobBossSearchFilter can keep re-inserting this SAME instance (preserving its
    /// {local:Loc Main.FilterAllTargets} binding) instead of fabricating a plain-text replacement every
    /// time the list rebuilds.</summary>
    // Replaced with a fresh item on every rebuild of the dropdown (see ApplyMobBossSearchFilter):
    // re-adding the same ComboBoxItem right after Items.Clear() can still have the old logical
    // parent and throws "Das Element besitzt bereits ein logisches übergeordnetes Element".
    private bool _mobBossFilterNeedsRebuild;
    private ComboBoxItem _mobBossAllItem;
    private readonly object? _mobBossAllContent;
    private readonly BindingBase? _mobBossAllContentBinding;
    private readonly object? _mobBossAllTag;

    /// <summary>Found once, in OnWindowLoaded, via the SearchableComboBox template's
    /// PART_SearchBox -- null until then, and also whenever the template hasn't produced one for
    /// some reason (defensive; every code path already tolerates a no-op filter in that case).</summary>
    private TextBox? _mobBossSearchBox;

    /// <summary>Mirror MeterSettings.ShowShareBars/ShowDamageTaken - cached here because
    /// RefreshRows runs every second and must not re-read the settings file each time.</summary>
    private bool _showShareBars = true;
    private bool _compactOverlay;
    private bool _compactClickThrough;
    private bool _showBossHp;
    private TimetableWindow? _timetable;
    private HotkeyOverlayWindow? _hotkeyOverlay;
    private WorldBossController? _worldBoss;
    private AssistController? _assist;
    private LootOverlayController? _loot;
    private CraftOverlayController? _craft;
    private CombatOverlayController? _combat;
    private MenuWatcher? _menuWatcher;
    private Streaming.StreamOverlayServer? _streamServer;
    private readonly DispatcherTimer _streamTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private IReadOnlyList<DamageEvent> _lastShownHits = Array.Empty<DamageEvent>();
    private string _streamLast = "";
    private DateTime _streamSettingsAt = DateTime.MinValue;
    private MeterSettings _streamSettings = new();
    private NetworkOverlayController? _network;
    private DummyTestController? _dummy;
    private readonly System.Windows.Threading.DispatcherTimer _gateTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _gameGateOpen = true;
    private TimetableReminder? _timetableReminder;
    private PetFarmWindow? _petFarm;
    private PetFarmController? _petFarmController;
    private PetMapController? _petMapController;
    private bool _autoReset;
    /// <summary>Whose rows are shown: everybody, the own group, or the corps (two groups together).</summary>
    private enum MeterScope { All, Group, Corps }

    /// <summary>The All / Group / Corps filter belongs to the compact transparent overlay only (its header carries the
    /// switch); the normal window and the chip overlay always list everybody whose damage arrives.</summary>
    private bool ScopeActive => _hideUiActive && _compactOverlay;

    private MeterScope _scope = MeterScope.Group;

    // The players who fought the same monsters as the group, for the corps scope (see ComputeCorps).
    private HashSet<int> _corpsIds = new();

    // Everyone the party roster named since the data was last cleared. Leaving the group (after the
    // boss is dead, out of the instance) empties the roster, but the fight on screen is still that
    // group's: the group scope has to keep showing them.
    private readonly HashSet<string> _groupNames = new(StringComparer.Ordinal);
    private HpCheckResult? _lastHpCheck;

    /// <summary>Silence after which the next damage starts a new fight (MeterSettings.AutoResetEnabled and
    /// AutoResetSeconds).</summary>
    private TimeSpan _autoResetIdle = TimeSpan.FromSeconds(10);

    /// <summary>What the rows show: damage dealt, healing done, or damage taken (with deaths) -
    /// the Mode menu, the compact overlay's badge or the mode hotkey go round the three.</summary>
    private enum MeterMode { Damage, Heal, Taken, All, AllCompact }

    private MeterMode _mode;

    private bool _healMode => _mode == MeterMode.Heal;

    private bool _takenMode => _mode == MeterMode.Taken;

    /// <summary>All in One: damage, healing and damage taken side by side on the damage rows.</summary>
    private bool _allMode => _mode == MeterMode.All && !_pvpOnly;

    /// <summary>All in One Compact: the damage rows with healing and damage taken as a second line.</summary>
    private bool _allCompactMode => _mode == MeterMode.AllCompact && !_pvpOnly;

    /// <summary>Either All in One mode: the rows then carry healing and the damage taken from monsters.</summary>
    private bool _allData => _allMode || _allCompactMode;

    // The deaths of the rows shown in taken mode.
    private readonly Dictionary<int, List<Death>> _deathsById = new();

    // The span the taken rows cover, for a player's details in that mode.
    private (DateTime Start, DateTime End)? _takenSpan;
    private bool _showDamageTaken;
    /// <summary>Avoided attacks and kill announcements from the source, kept beside the
    /// aggregator's damage events (they are not DamageEvents - see Combat/Sources). Cleared with
    /// the damage data; scoped to the shown window at refresh time like everything else.</summary>
    private readonly List<AvoidEvent> _avoids = new();
    private readonly List<KillEvent> _kills = new();


    // True while a past session is loaded into the grid instead of the live capture; the banner offers the way back.
    private bool _historyMode;

    /// <summary>The skills used, effects and stagger of the stored fight on screen (history mode), under this session's ids.</summary>
    private FightExtras? _historyExtras;
    /// <summary>Viewed session: replay id -> id in the live capture, so the details window can look up rotation and buffs.</summary>
    private Dictionary<int, int> _historyLiveIds = new();
    /// <summary>Viewed session: id in the live capture -> replay id (the other way round).</summary>
    private Dictionary<int, int> _historyReplayIds = new();

    /// <summary>"Minimize to system tray" (OnMinimizeToTrayClicked) - null until the first time
    /// it's actually used, so a user who never touches this never costs a single Shell_NotifyIcon
    /// call.</summary>
    private TrayIcon? _trayIcon;
    private int _historyTickCounter;
    /// <summary>Set by the console test modes before they construct the window: nothing may open a dialog.</summary>
    internal static bool Headless { get; set; }

    // Where combat data comes from (see Combat/Sources/ICombatSource) - the Aion 2 packet source; its Entities directory is what RefreshRows/RefreshMobBossFilterItems resolve names
    // through for ids this window didn't assign itself. Null until Settings name an install folder.
    private ICombatSource? _source;

    /// <summary>The real capture. It lives as long as the window: while a stored fight or session is on show, <see cref="_source"/> is a replay
    /// and this keeps polling, so the session recording never has a gap.</summary>
    private ICombatSource? _liveSource;

    private bool ViewingLive => ReferenceEquals(_source, _liveSource);

    /// <summary>Every stay in a map or instance since the meter started, recorded whatever the grid shows (see SessionRecorder).</summary>
    private readonly SessionRecorder _sessions = new(zone => Aion2Zones.IsInstance(zone) ? SessionKind.Instance : SessionKind.Map);

    /// <summary>What the live grid follows: the stay the player is in (cleared at every map change), or everything together.</summary>
    private enum SessionView { Current, All }

    private SessionView _sessionView = SessionView.Current;

    /// <summary>The session on show in a replay (null for the live views and for a stored fight).</summary>
    private int? _viewedSessionNumber;

    private readonly UploadMenuFlags _uploadFlags = new();
    private CharacterWindow? _characterWindow;
    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>Five minutes, per the user. GitHub's anonymous API allows 60 requests an hour per
    /// IP, so 12 is comfortably inside it even with a second client running alongside.</summary>
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromMinutes(5) };

    /// <summary>Asks aiondps.com every half hour for the newest announcement (a single number) to decide whether the "New" badge shows.</summary>
    private readonly DispatcherTimer _announcementsTimer = new() { Interval = TimeSpan.FromMinutes(30) };

    /// <summary>The posting time of the newest announcement the server named last; the click on the icon stores it as "seen".</summary>
    private long? _latestAnnouncement;

    /// <summary>The update already downloaded and staged, so a repeating check does not fetch the
    /// same one twelve times an hour -- and so clicking the notice knows what to restart into.
    /// Aliased because Velopack's UpdateInfo would otherwise collide with nothing in particular,
    /// but reads ambiguously next to this project's own update code.</summary>
    private VelopackUpdateInfo? _downloadedUpdate;

    /// <summary>The update the in-app restart card (UpdateRestartOverlay) is currently offering,
    /// so its two buttons know what to act on without re-threading it through the event args.</summary>
    private VelopackUpdateInfo? _pendingRestartUpdate;

    public MainWindow()
    {
        InitializeComponent();
        AppMenu.Tag = _uploadFlags;
        _sessions.Identify = IdentifyForSession;
        _sessions.HpSource = HitPointsOfTarget;

        // Captured now, before anything ever rebuilds MobBossFilter.Items, so
        // ApplyMobBossSearchFilter can keep re-inserting this exact instance rather than a plain
        // "All" it would have to build itself -- this one keeps the XAML {local:Loc Main.FilterAllTargets}
        // binding, which is only set up once, at parse time, on this specific object.
        _mobBossAllItem = (ComboBoxItem)MobBossFilter.Items[0]!;
        _mobBossAllContent = _mobBossAllItem.Content;
        // The XAML item's text is a localisation binding; a fresh item gets the same binding.
        _mobBossAllContentBinding = BindingOperations.GetBindingBase(_mobBossAllItem, ContentControl.ContentProperty);
        _mobBossAllTag = _mobBossAllItem.Tag;
        Loaded += OnWindowLoaded;
        SizeChanged += (_, _) => UpdateOverlayWidth();

        // Version in the title, read back from the assembly rather than typed here a second time:
        // AionDPS.csproj's <Version> is the only place it is written. Needed because builds are
        // handed around the group by hand -- a screenshot from someone else is
        // otherwise impossible to pin to a build, which already cost a round of guesswork once.
        Title = AppVersion.Text.Length > 0 ? $"Aion DPS {AppVersion.Text}" : "Aion DPS";

        PlayersGrid.ItemsSource = _rows;
        OverlayContent.ItemsSource = _rows;
        CompactOverlayRows.ItemsSource = _rows;

        // Per the user: the list must sort itself by damage, highest first, not just show rows in
        // whatever order they were first discovered in. IsLiveSorting (not just SortDescriptions
        // alone) is what keeps it re-sorted as damage keeps changing live -- plain
        // SortDescriptions only sorts once, at binding time; without live sorting the row order
        // would freeze after the initial snapshot and never reflect who's actually ahead now.
        var playersView = (ListCollectionView)CollectionViewSource.GetDefaultView(_rows);
        _playersView = playersView;
        playersView.SortDescriptions.Add(new SortDescription(nameof(PlayerRow.Damage), ListSortDirection.Descending));
        playersView.IsLiveSorting = true;
        playersView.LiveSortingProperties.Add(nameof(PlayerRow.Damage));

        _pollTimer.Tick += OnPollTimerTick;

        // Startup check is announced (per the user: should behave exactly like clicking "Check for
        // updates" in the App menu, not stay silent) -- a launch is never mid-fight, so a message
        // box here costs nothing. The recurring five-minute timer stays silent (see RunUpdateCheck):
        // that one CAN land mid-boss, and a background timer popping a dialog over a fight is
        // exactly what announceResult=false was added to prevent. Fire-and-forget on purpose
        // either way -- an update check must never delay the window appearing.
        // Not in the headless test modes (aion2-ui-test, aion2-ui-live, aion2-upload-dryrun): they build
        // this window without showing it, and the announced check would put a message box on the
        // user's screen - from a build folder, where there is never an Update.exe.
        if (!Headless)
        {
            _updateTimer.Tick += (_, _) => _ = RunUpdateCheck(announceResult: false);
            _updateTimer.Start();
            _announcementsTimer.Tick += (_, _) => { _ = RefreshAnnouncementsBadge(); _ = RefreshTwitchDropsBadge(); };
            _announcementsTimer.Start();
            _ = RefreshAnnouncementsBadge();
            _ = RefreshTwitchDropsBadge();
            // The meter talks to GitHub only while "check for updates" is on in the settings.
            var startupSettings = MeterSettings.Load();
            if (startupSettings.CheckForUpdates)
            {
                _ = RunUpdateCheck(announceResult: true);
            }

            // Only the current version may upload (the server refuses an old one). A meter that uploads by itself asks the community server at
            // once, so an old client learns it now and not with its first fight; one that only uploads on a click learns it from that upload.
            UploadClient.ClientOutdated += required => Dispatcher.BeginInvoke(new Action(() => OnClientOutdated(required)));
            UploadClient.ClientAllowed += () => Dispatcher.BeginInvoke(new Action(OnClientAllowed));
            if (startupSettings.AutoUploadProfile || startupSettings.AutoUploadBoss)
            {
                _ = UploadClient.CheckPolicyAsync(AppVersion.Text);
            }
        }

        var settings = MeterSettings.Load();

        // Until the wizard has been through (see below) nothing is uploaded: an install that updated from a
        // version with the uploads switched on starts from "all off" too, so no upload runs before its
        // owner has made the choice.
        if (!Headless && !settings.SetupCompleted)
        {
            settings.AutoUploadProfile = false;
            settings.AutoUploadBoss = false;
            settings.UploadOtherPlayersProfiles = false;
            settings.Save();
        }

        // Empty means "a fresh install, or a settings file older than this feature" -- leave
        // LocalizationManager on whatever it already auto-detected from the OS at construction
        // time (see its DetectSystemLanguage remarks) rather than forcing English.
        if (settings.Language.Length > 0)
        {
            LocalizationManager.Instance.Language = settings.Language;
        }

        // Same value as the View menu's "Always on top" toggle (see OnAlwaysOnTopClicked) --
        // applied here so a saved preference survives a restart, not just a runtime toggle.
        Topmost = settings.AlwaysOnTopOnStartup;
        AlwaysOnTopMenuItem.IsChecked = settings.AlwaysOnTopOnStartup;

        // The mode last used (damage, heal, taken, all, all compact), see SetMode.
        if (Enum.TryParse(settings.LastMode, out MeterMode savedMode) && savedMode != MeterMode.Damage)
        {
            SetMode(savedMode, save: false);
        }
        else
        {
            ApplyModeButtons();
        }

        RestoreWindowGeometry(settings);
        StartCapture(settings);
        DeleteLegacyFightHistory();
        RefreshCharacterSettings(settings);
        TryRestoreRestartSnapshot();

        // A fresh install, or a lost or broken settings file: the wizard asks for the language and the
        // upload options that are off by default (see WizardWindow). Never in the headless test modes.
        if (!Headless)
        {
            bool wizard = !settings.SetupCompleted;
            Loaded += (_, _) =>
            {
                if (wizard)
                {
                    new WizardWindow(settings, RecognisedCharacterName) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
                }

                AskForNpcapIfMissing();
            };
        }
    }

    /// <summary>The name of the local character once the capture has recognised it (the first-start wizard shows it live), else null.</summary>
    private string? RecognisedCharacterName() =>
        _liveSource?.Entities is Aion2.Aion2EntityDirectory directory && directory.LocalPlayerId >= 0 ? directory.NameFor(directory.LocalPlayerId) : null;

    /// <summary>Without the Npcap driver the meter sees no game traffic at all (no damage, no pet info): says so at the start and offers the download page.</summary>
    private void AskForNpcapIfMissing()
    {
        if (Aion2.Capture.NpcapAvailability.Detect().IsInstalled)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        if (ThemedMessageBox.Show(this, loc["Npcap.Missing.Text"], loc["Npcap.Missing.Title"], MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Aion2.Capture.NpcapAvailability.DownloadUrl) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
            }
        }
    }

    /// <summary>Applies a previously saved size/position, if any -- see SaveWindowGeometry, its
    /// counterpart on close. Left null-checked separately from Width/Height since a user who's
    /// only ever resized (not moved) the window would have one pair set and the other still
    /// null. No longer also restores Name/Damage-DPS column widths -- those two independently-
    /// resizable columns were merged into one Width="*" PlayerColumn (see its own remarks in
    /// MainWindow.xaml) that always fills whatever this window's own persisted width leaves it.</summary>
    private void RestoreWindowGeometry(MeterSettings settings)
    {
        if (settings.WindowWidth is double width && settings.WindowHeight is double height)
        {
            Width = width;
            Height = height;
        }

        if (settings.WindowLeft is double left && settings.WindowTop is double top)
        {
            Left = left;
            Top = top;
        }
    }

    /// <summary>Persists the current size/position so it survives a restart -- found necessary by
    /// the user, who resized the window and had it reset every time. Reads WindowState-independent
    /// values (RestoreBounds instead of Width/Height/Left/Top directly) so a maximized or minimized
    /// window on close doesn't save that transient state as if it were the normal size. See
    /// RestoreWindowGeometry's own remarks for why column widths are no longer part of this.</summary>
    private void SaveWindowGeometry(MeterSettings settings)
    {
        Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        // Closed while the compact overlay is up: the window has the panel's size, not its own.
        settings.WindowWidth = _sizeBeforeCompactOverlay?.Width ?? bounds.Width;
        settings.WindowHeight = _sizeBeforeCompactOverlay?.Height ?? bounds.Height;
        settings.WindowLeft = bounds.X;
        settings.WindowTop = bounds.Y;
    }

    /// <summary>Re-reads the display settings - called once at startup and again after Settings is saved.</summary>
    private void RefreshCharacterSettings(MeterSettings settings)
    {
        _showShareBars = settings.ShowShareBars;
        _showDamageTaken = settings.ShowDamageTaken;
        _showBossHp = settings.ShowBossHp;
        _autoReset = settings.AutoResetEnabled;
        _autoResetIdle = TimeSpan.FromSeconds(Math.Clamp(settings.AutoResetSeconds, 1, 600));
        _scope = Enum.TryParse(settings.ViewScope, true, out MeterScope scope) ? scope : MeterScope.Group;
        UpdateScopeControls();
        _compactOverlay = string.Equals(settings.OverlayStyle, "Compact", StringComparison.OrdinalIgnoreCase);
        _compactClickThrough = settings.CompactClickThrough;
        SetCompactOverlayScale(settings.OverlayScale);
        // Both overlay looks paint their dark backgrounds with this brush (DynamicResource); the
        // theme's own is the same colour at 60 %.
        double opacity = Math.Clamp(double.IsFinite(settings.OverlayOpacity) ? settings.OverlayOpacity : 0.6, 0.2, 1.0);
        var overlayBrush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), 0, 0, 0));
        overlayBrush.Freeze();
        Resources["Brush.OverlayBg"] = overlayBrush;
        if (_hideUiActive)
        {
            ApplyOverlayLook();
        }
    }

    /// <summary>
    /// (Re)starts the Aion 2 packet capture with the given settings (adapter, own character name) -
    /// called once at startup and again after Settings is saved.
    /// </summary>
    private string? _captureAdapterInUse;

    private void StartCapture(MeterSettings settings)
    {
        _captureAdapterInUse = settings.CaptureAdapterId;
        Aion2.Capture.DebugRecording.Enabled = settings.DebugRecording;
        Aion2.Capture.LoginCapture.Enabled = settings.ShareLoginWithLegion;
        _pollTimer.Stop();
        if (_historyMode)
        {
            // A new capture ends whatever replay is on show.
            _historyMode = false;
            _viewedSessionNumber = null;
            HistoryBanner.Visibility = Visibility.Collapsed;
            ClearDamageData();
        }

        if (!ReferenceEquals(_source, _liveSource))
        {
            _source?.Dispose();
        }

        _liveSource?.Dispose();
        _source = null;
        _liveSource = null;

        var source = new Aion2PacketCombatSource(Aion2Protocol.Load(), settings.CaptureAdapterId, null, Aion2CharacterStore.DefaultPath);
        ReplaceSource(source);
        _liveSource = source;
        // The upload entries appear once the own character is known (see RefreshUploadAvailability).
        if (source.Entities is Aion2EntityDirectory entities)
        {
            // A map change announces the own character again under another combat id: one player, one row.
            entities.LocalIdChanged += (oldId, newId) => Dispatcher.BeginInvoke(new Action(() =>
            {
                _sessions.MergeIds(oldId, newId);
                int merged = ViewingLive ? _aggregator.MergeIds(oldId, newId) : 0;
                if (ViewingLive && _selectedTargetId == oldId)
                {
                    _selectedTargetId = newId;
                }

                AutoUploadLog.Write($"own combat id changed {oldId} -> {newId}: {merged} event(s) merged into one row");
            }));
            // Any other player: the same name on the same server under a new combat id is the same player.
            entities.IdentityMoved += (oldId, newId) => Dispatcher.BeginInvoke(new Action(() =>
            {
                _sessions.MergeIds(oldId, newId);
                if (ViewingLive)
                {
                    _aggregator.MergeIds(oldId, newId);
                    if (_selectedTargetId == oldId)
                    {
                        _selectedTargetId = newId;
                    }
                }
            }));
            entities.CharacterChanged += _ => Dispatcher.BeginInvoke(new Action(() =>
            {
                RefreshUploadAvailability();
                ScheduleOwnProfileUpload();
            }));
            // A character window of another player was opened: it goes online by itself a few seconds later (see ScheduleInspectedUpload).
            entities.InspectedChanged += () => Dispatcher.BeginInvoke(new Action(ScheduleInspectedUpload));
        }

        _pollTimer.Start();
    }

    /// <summary>Swaps the combat source. Handlers are subscribed once per source - the source
    /// itself forwards from whatever capture it currently holds, so swapping
    /// the capture underneath never needs this window to re-subscribe.</summary>
    private void ReplaceSource(ICombatSource? source)
    {
        _source?.Dispose();
        _source = source;
        if (source is null)
        {
            return;
        }

        source.StatusChanged += OnSourceStatusChanged;
        source.Start();
    }

    /// <summary>Only states the user can act on reach the status line; "connected" is the normal
    /// case and needs no announcement. Marshalled because a capture-based source reports from its
    /// own thread.</summary>
    private void OnSourceStatusChanged(SourceStatus status)
    {
        // "Connected" is normally silent; the Aion 2 source's live counters (frames/events) are the
        // one exception, since they are how a stalled capture is told apart from a stalled decoder.
        bool aion2Counters = status.State == SourceState.Connected && _source is Aion2PacketCombatSource;
        if (status.State is not (SourceState.Waiting or SourceState.Error) && !aion2Counters)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() => ShowUploadStatus(status.Message)));
    }

    /// <summary>The gear entry of a profile upload, with the mana stones and rolled stats when they were read.</summary>
    private static ProfileGearUpload GearUpload(Aion2.Aion2EquippedItem i) => new(i.SlotIndex, i.ItemId, i.Enchant,
        i.Stones?.Select(k => new ProfileStoneUpload(k.StatId, k.Tier)).ToList(),
        i.Stats?.Select(k => new ProfileStatUpload(k.StatId, k.Value)).ToList(),
        i.GodstoneId > 0 ? i.GodstoneId : null,
        i.SkillBonuses?.Select(k => new ProfileSkillBonusUpload(k.SkillId, k.Level)).ToList());

    /// <summary>
    /// What a global hotkey (see GlobalHotkeys) does. They replace the in-game chat commands, which
    /// cannot work: Aion 2's chat is not readable from the network stream.
    /// </summary>
    private void OnHotkeyPressed(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ToggleHideUi:
                SetHideUi();
                break;
            case HotkeyAction.TogglePause:
                SetPaused(!_paused);
                break;
            case HotkeyAction.CopyDamageRanking:
                CopyChatLineChunk(BuildDmgRankingText(), "No damage has been recorded yet.");
                break;
            case HotkeyAction.ClearDamage:
                ClearDamageData();
                break;
            case HotkeyAction.ToggleTimetable:
                ToggleTimetable();
                break;
            case HotkeyAction.TogglePetMap:
                TogglePetMap();
                break;
            case HotkeyAction.ToggleHotkeyOverlay:
                ToggleHotkeyOverlay();
                break;
            case HotkeyAction.ToggleAllOverlays:
                ToggleAllOverlays();
                break;
            case HotkeyAction.ToggleNetworkOverlay:
                ToggleNetworkOverlay();
                break;
            case HotkeyAction.ToggleCombatOverlay:
                ToggleCombatOverlay();
                break;
            case HotkeyAction.ToggleAssistOverlay:
                ToggleAssistOverlay();
                break;
            case HotkeyAction.ToggleLootOverlay:
                ToggleLootOverlay();
                break;
            case HotkeyAction.ToggleCraftOverlay:
                ToggleCraftOverlay();
                break;
            case HotkeyAction.ToggleWorldBossOverlay:
                ToggleWorldBossOverlay();
                break;
            case HotkeyAction.TogglePetArrows:
                TogglePetArrows();
                break;
            case HotkeyAction.ToggleGatherArrows:
                ToggleGatherArrows();
                break;
            case HotkeyAction.NextMode:
                NextMode();
                break;
            case HotkeyAction.UploadBoss:
                OnUploadCurrentBossClicked(this, new RoutedEventArgs());
                break;
        }
    }

    /// <summary>(Re-)registers the hotkeys from the settings; a combination another program already
    /// owns is reported on the status line.</summary>
    private void ApplyHotkeys(MeterSettings settings)
    {
        if (_hotkeys is null)
        {
            return;
        }

        var failed = _hotkeys.Apply(new Dictionary<HotkeyAction, HotkeyBinding>
        {
            [HotkeyAction.ToggleHideUi] = HotkeyBinding.Parse(settings.HotkeyHideUi),
            [HotkeyAction.TogglePause] = HotkeyBinding.Parse(settings.HotkeyPause),
            [HotkeyAction.CopyDamageRanking] = HotkeyBinding.Parse(settings.HotkeyCopyDamage),
            [HotkeyAction.ClearDamage] = HotkeyBinding.Parse(settings.HotkeyClear),
            [HotkeyAction.UploadBoss] = HotkeyBinding.Parse(settings.HotkeyUploadBoss),
            [HotkeyAction.NextMode] = HotkeyBinding.Parse(settings.HotkeyMode),
            [HotkeyAction.ToggleTimetable] = HotkeyBinding.Parse(settings.HotkeyTimetable),
            [HotkeyAction.TogglePetMap] = HotkeyBinding.Parse(settings.HotkeyPetMap),
            [HotkeyAction.ToggleHotkeyOverlay] = HotkeyBinding.Parse(settings.HotkeyHotkeyOverlay),
            [HotkeyAction.ToggleAllOverlays] = HotkeyBinding.Parse(settings.HotkeyAllOverlays),
            [HotkeyAction.ToggleWorldBossOverlay] = HotkeyBinding.Parse(settings.HotkeyWorldBoss),
            [HotkeyAction.ToggleNetworkOverlay] = HotkeyBinding.Parse(settings.HotkeyNetwork),
            [HotkeyAction.ToggleCombatOverlay] = HotkeyBinding.Parse(settings.HotkeyCombatOverlay),
            [HotkeyAction.ToggleAssistOverlay] = HotkeyBinding.Parse(settings.HotkeyAssist),
            [HotkeyAction.ToggleLootOverlay] = HotkeyBinding.Parse(settings.HotkeyLoot),
            [HotkeyAction.ToggleCraftOverlay] = HotkeyBinding.Parse(settings.HotkeyCraft),
            [HotkeyAction.TogglePetArrows] = HotkeyBinding.Parse(settings.HotkeyPetArrows),
            [HotkeyAction.ToggleGatherArrows] = HotkeyBinding.Parse(settings.HotkeyGatherArrows),
        });
        if (failed.Count > 0)
        {
            ShowUploadStatus("Hotkey already in use by another program: " + string.Join(", ", failed));
        }
    }

    private DateTime _lastPreviewInvalidate = DateTime.MinValue;

    private int _cubeMapArrivals = -1;

    /// <summary>
    /// A Hidden Cube comes into range while the pet map module is switched off: the map (and the arrival chime) come all the same, as the cube's switch says ("show the map when a cube appears";
    /// Stacy's idea, her fork 17 October 2026). With the module on its own controller notices the cube.
    /// </summary>
    private void PollCubeMap()
    {
        if (Headless || _source?.Entities is not Aion2EntityDirectory directory)
        {
            return;
        }

        int before = _cubeMapArrivals;
        _cubeMapArrivals = directory.CubeArrivals;
        if (before < 0 || _cubeMapArrivals <= before || _petMapController is not null)
        {
            return;
        }

        MeterSettings settings = MeterSettings.Load();
        if (settings.ShowPetMap || !settings.ShowMapOnCube || !settings.PetMapGatherItems.Contains("Hidden Cube"))
        {
            return;
        }

        _petMapController = new PetMapController(() => _source?.Entities as Aion2EntityDirectory);
        _petMapController.ApplyLocked(settings.PetFarmLocked);
        _petMapController.OnCubeArrived(settings);
    }

    private void OnPollTimerTick(object? sender, EventArgs e)
    {
        PollCubeMap();
        // The Alt+Tab picture of the overlay (see RenderOverlayPreview) is redrawn on request; saying
        // it is stale every couple of seconds keeps it close to what the overlay shows.
        if (_hideUiActive && DateTime.UtcNow - _lastPreviewInvalidate > TimeSpan.FromSeconds(2))
        {
            _lastPreviewInvalidate = DateTime.UtcNow;
            _overlay?.InvalidatePreview();
        }

        // The live capture is polled whatever the grid shows, so the session recording goes on while a stored session is on view.
        CombatBatch liveBatch = _liveSource?.Poll(_paused) ?? CombatBatch.Empty;
        FeedSessions(liveBatch);
        // A new stay began (map or instance change) while an older session or the all-sessions view is on show: the view follows
        // into the new stay, which the recorder has just opened. The grid is filled from the recorder, so this batch's hits are in it already.
        bool followed = liveBatch.ZoneChanges.Count > 0 && (_historyMode || _sessionView == SessionView.All);
        if (followed)
        {
            _sessionView = SessionView.Current;
            if (_historyMode)
            {
                ExitHistoryMode();
            }
            else
            {
                ShowLiveView(SessionView.Current);
            }
        }

        bool viewingLive = ViewingLive;
        CombatBatch batch = followed
            ? new CombatBatch(Array.Empty<DamageEvent>(), liveBatch.Avoids, liveBatch.Kills)
            : viewingLive ? liveBatch : _source?.Poll(_paused) ?? CombatBatch.Empty;
        if (_menuWatcher is not null && _liveSource is { } liveSource && liveBatch.Damage.Any(ev => !ev.IsHeal && liveSource.Entities.IsLocalPlayer(ev.SourceObjectId)))
        {
            _menuWatcher.NoteOwnHit(); // the control mode is learnt while the player fights
        }

        // Hits a summon dealt before its owner was known (the meter started mid-fight) go to the
        // owner once found, instead of staying on a "Player #id" row.
        if (_liveSource?.Entities is Aion2.Aion2EntityDirectory resolvedIn)
        {
            var resolved = resolvedIn.DrainResolvedOwners();
            foreach ((int summon, int owner) in resolved)
            {
                _sessions.Reattribute(summon, owner);
                if (viewingLive)
                {
                    _aggregator.Reattribute(summon, owner);
                }
            }

            // The batch just polled is only applied to the grid below, so it still holds hits of such a summon under its own id: the owner
            // was found within this very batch (the recorder, fed before, has been handed over above). One burn tick of a Fire Wall
            // once stayed behind as a "Player #id" row of 389.
            if (viewingLive && resolved.Count > 0 && batch.Damage.Count > 0)
            {
                var ownerOf = new Dictionary<int, int>();
                foreach ((int summon, int owner) in resolved)
                {
                    ownerOf[summon] = owner; // the same summon twice: the later owner wins, as in the recorder
                }

                batch = batch with
                {
                    Damage = batch.Damage.Select(ev => ownerOf.TryGetValue(ev.SourceObjectId, out int owner) ? ev with { SourceObjectId = owner } : ev).ToList(),
                };
            }
        }

        if (viewingLive && _sessionView == SessionView.Current && batch.ZoneChanges.Count > 0)
        {
            // A map change cuts the stay in two: what happened before it still belongs to the old view, which is then
            // cleared (the old stay lives on in the session list) before the rest comes in.
            IReadOnlyList<DamageEvent> damage = batch.Damage;
            int from = 0;
            foreach (ZoneChange zone in batch.ZoneChanges)
            {
                int to = from;
                while (to < damage.Count && damage[to].Timestamp < zone.Timestamp)
                {
                    to++;
                }

                ApplyBatch(new CombatBatch(damage.Skip(from).Take(to - from).ToList(), Array.Empty<AvoidEvent>(), Array.Empty<KillEvent>()));
                ClearDamageData();
                from = to;
            }

            if (_sessions.Current is { Away.Ticks: > 0 })
            {
                // Back on a map after an instance run: the recorder picked the earlier stay up again, so its totals come back on show
                // (it already holds this batch's hits after the change).
                ShowLiveView(SessionView.Current);
                NoteBossActivity(damage.Skip(from).ToList());
                ApplyBatch(new CombatBatch(Array.Empty<DamageEvent>(), batch.Avoids, batch.Kills));
            }
            else
            {
                ApplyBatch(new CombatBatch(damage.Skip(from).ToList(), batch.Avoids, batch.Kills));
            }
        }
        else
        {
            ApplyBatch(batch);
        }

        // Once a second, an always-on-top meter goes back to the front: a borderless full-screen
        // game can take the front of the topmost band when it gets the focus back.
        if (Topmost && IsVisible)
        {
            NativeOverlay.KeepOnTop(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        }

        // The timetable overlay always sits above the meter: raised right after it, in the same tick.
        _timetable?.RaiseToFront();
        _hotkeyOverlay?.RaiseToFront();
        _worldBoss?.RaiseToFront();
        _assist?.RaiseToFront();
        _loot?.RaiseToFront();
        _craft?.RaiseToFront();
        _combat?.RaiseToFront();
        _network?.RaiseToFront();
        _dummy?.RaiseToFront();
        _petFarm?.RaiseToFront();
        _petMapController?.RaiseToFront();

        // Every five seconds is plenty: a fight only counts as finished 120 s after its last hit.
        if (++_historyTickCounter >= 5)
        {
            _historyTickCounter = 0;
            _sessions.RefreshNames();
        }
    }

    /// <summary>What the grid does with one batch of the source it shows: kills and avoids, boss tracking, the automatic reset
    /// between fights, then the events themselves.</summary>
    private void ApplyBatch(CombatBatch batch)
    {
        _avoids.AddRange(batch.Avoids);
        _kills.AddRange(batch.Kills);
        NoteBossKills(batch.Kills);
        IReadOnlyList<DamageEvent> events = batch.Damage;
        NoteBossActivity(events);
        NoteDefeatedBosses();

        // The "all sessions" view is a long record by design: the automatic reset between fights would leave only the last one.
        if (_autoReset && !_historyMode && _sessionView == SessionView.Current && (StartsNewFight(events) || StartsNewBossPull(events)))
        {
            // Files the finished fight in the history, then starts from zero.
            ClearDamageData();
        }

        if (events.Count > 0 || batch.Avoids.Count > 0 || batch.Kills.Count > 0)
        {
            if (events.Count > 0)
            {
                _aggregator.IngestEvents(events);
            }

            RefreshRows();
            if (events.Count > 0 && _hideUiActive && _compactOverlay)
            {
                FollowNewestRun();
            }
        }
    }

    private void FeedSessions(CombatBatch batch)
    {
        if (!batch.IsEmpty)
        {
            _sessions.Feed(batch);
            _dummy?.Feed(batch, _liveSource?.Entities);
        }
    }

    /// <summary>What the server said about a target's hit points between two moments, kept with the session for the plausibility check.</summary>
    private TargetHp? HitPointsOfTarget(int id, DateTime from, DateTime to)
    {
        if (_liveSource?.Entities is not Aion2EntityDirectory directory)
        {
            return null;
        }

        IReadOnlyList<HpSample> samples = directory.HitPoints.SamplesAround(id, from, to);
        return samples.Count < 2 ? null : new TargetHp(directory.HitPoints.HighestSeen(id) ?? 0, samples, directory.HitPoints.ResetsOf(id));
    }

    /// <summary>The name and class the live capture knows for an object id right now.</summary>
    private (string? Name, string? Class) IdentifyForSession(int id)
    {
        if (_liveSource?.Entities is not Aion2EntityDirectory directory)
        {
            return (null, null);
        }

        return (directory.NameFor(id) ?? directory.MonsterNameFor(id), directory.ClassOf(id));
    }

    /// <summary>
    /// Whether this batch's first damage comes <see cref="_autoResetIdle"/> or more after the last
    /// damage on record - by the events' own times, so a replay behaves like the live game. Never
    /// while a boss fight is unfinished (a boss seen hurt but alive): a phase where nobody can hit
    /// it must not cut it in two. A wipe resets the boss to full health, which ends that fight.
    /// </summary>
    private bool StartsNewFight(IReadOnlyList<DamageEvent> batch)
    {
        DateTime? first = null;
        foreach (DamageEvent ev in batch)
        {
            if (!ev.IsHeal && (first is null || ev.Timestamp < first))
            {
                first = ev.Timestamp;
            }
        }

        if (first is not DateTime start)
        {
            return false;
        }

        IReadOnlyList<DamageEvent> recorded = _aggregator.Events;
        DateTime? last = null;
        for (int i = recorded.Count - 1; i >= 0; i--)
        {
            if (!recorded[i].IsHeal)
            {
                last = recorded[i].Timestamp;
                break;
            }
        }

        return last is DateTime end && start - end >= _autoResetIdle && !BossFightUnfinished();
    }

    /// <summary>
    /// The first hit on a boss (or on a boss reset to full health by a wipe) starts the meter from
    /// zero: the trash before it is filed in the history and the fight on screen is the pull. See
    /// <see cref="BossFight.StartsNewPull"/>.
    /// </summary>
    private bool StartsNewBossPull(IReadOnlyList<DamageEvent> batch) =>
        _source?.Entities is Aion2.Aion2EntityDirectory directory
        && BossFight.StartsNewPull(batch, _aggregator.Events,
            id => directory.BossNpcIdOf(id) is not null,
            id => directory.HitPoints.ResetsOf(id) is { Count: > 0 } resets ? resets[^1] : null,
            id => BossFightUnfinished(except: id));

    private bool BossFightUnfinished(int? except = null)
    {
        if (_source?.Entities is not Aion2.Aion2EntityDirectory directory)
        {
            return false;
        }

        foreach ((int entityId, _) in directory.KnownBosses())
        {
            if (entityId != except && directory.HitPoints.Latest(entityId) is { } hp && hp.Hp > 0
                && hp.Hp < (directory.HitPoints.HighestSeen(entityId) ?? 0) * 0.99
                && _aggregator.Events.Any(ev => !ev.IsHeal && ev.TargetObjectId == entityId))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The old per-fight history (fights.db) is gone for good - sessions replaced it - so a file left by an earlier version is removed.</summary>
    private static void DeleteLegacyFightHistory()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aion DPS Meter");
        foreach (string name in new[] { "fights.db", "fights.db-wal", "fights.db-shm" })
        {
            try
            {
                File.Delete(Path.Combine(folder, name));
            }
            catch (Exception)
            {
                // Locked or read-only: harmless, it is tried again at the next start.
            }
        }
    }

    private void OnAppMenuClicked(object sender, RoutedEventArgs e)
    {
        // AppMenu has no child MenuItems anymore (AppMenuFlyoutStyle hand-authors the Popup
        // content instead), so WPF assigns it MenuItemRole.TopLevelItem instead of
        // TopLevelHeader and never opens IsSubmenuOpen on click by itself - toggle it here.
        AppMenu.IsSubmenuOpen = !AppMenu.IsSubmenuOpen;
        e.Handled = true;
    }

    private void OnHistoryBannerClicked(object sender, MouseButtonEventArgs e) => OnSessionBannerLeave();

    private void ExitHistoryMode()
    {
        if (!_historyMode)
        {
            return;
        }

        _historyMode = false;
        _viewedSessionNumber = null;
        _historyExtras = null;
        HistoryBanner.Visibility = Visibility.Collapsed;
        if (_liveSource is null)
        {
            ClearDamageData();
            StartCapture(MeterSettings.Load());
            RefreshRows();
            return;
        }

        // The capture never stopped: drop the replay and show the live view again.
        if (!ReferenceEquals(_source, _liveSource))
        {
            _source?.Dispose();
        }

        _source = _liveSource;
        ShowLiveView(_sessionView);
    }

    /// <summary>Shows a stored fight or session from a replay source while the live capture goes on in the background (see <see cref="_liveSource"/>).</summary>
    private void ShowReplay(ICombatSource replay)
    {
        if (!ReferenceEquals(_source, _liveSource))
        {
            _source?.Dispose();
        }

        _source = replay;
        replay.StatusChanged += OnSourceStatusChanged;
        replay.Start();
    }

    /// <summary>The live grid again, filled for <paramref name="view"/>: the stay in progress so far, or everything recorded.</summary>
    private void ShowLiveView(SessionView view)
    {
        _sessionView = view;
        ClearDamageData();
        if (view == SessionView.All)
        {
            _aggregator.IngestEvents(_sessions.AllEvents().Concat(_sessions.AllShields()));
            HistoryBanner.Text = LocalizationManager.Instance["Main.SessionAllBanner"];
            HistoryBanner.Visibility = Visibility.Visible;
        }
        else
        {
            if (_sessions.Current is { } stay)
            {
                _aggregator.IngestEvents(stay.Data.Events.Concat(stay.Data.Shields));
            }

            HistoryBanner.Visibility = Visibility.Collapsed;
        }

        RefreshRows();
    }

    /// <summary>A banner click: back to the live view of the current stay (from a stored session, a fight, or the all-sessions view).</summary>
    private void OnSessionBannerLeave()
    {
        if (_historyMode)
        {
            _sessionView = SessionView.Current;
            ExitHistoryMode();
        }
        else if (_sessionView == SessionView.All)
        {
            ShowLiveView(SessionView.Current);
        }
    }

    /// <summary>The name the packet stream gave the object (player name, boss name from its NPC id,
    /// or "Class #id" until a player's name arrives); its hex id when nothing is known.</summary>
    private string ResolveDisplayName(int objectId) =>
        _source?.Entities.NameFor(objectId) ?? MonsterName(objectId) ?? $"0x{objectId:X8}";

    /// <summary>A monster's name from the NPC table, for the targets in the lists and the details windows.</summary>
    private string? MonsterName(int objectId) => (_source?.Entities as Aion2.Aion2EntityDirectory)?.MonsterNameFor(objectId);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _overlay = new NativeOverlay(this);
        CreateTrayIcon();
        _hotkeys = new GlobalHotkeys(this);
        _hotkeys.Pressed += action => Dispatcher.Invoke(() => OnHotkeyPressed(action), System.Windows.Threading.DispatcherPriority.Input);
        ApplyHotkeys(MeterSettings.Load());
        ApplyTimetable(MeterSettings.Load());
        _timetableReminder ??= new TimetableReminder();
        ApplyPetFarm(MeterSettings.Load());
        ApplyHotkeyOverlay(MeterSettings.Load());
        if (!Headless)
        {
            // the world boss overlay and sounds: the lists the game sends are kept in a file, and the controller reads the settings every second
            Aion2FieldBosses.EnablePersistence();
            Aion2Tickets.EnablePersistence();
            Aion2Tickets.Changed += () => Aion2Tickets.Flush();
            _worldBoss ??= new WorldBossController();
            _assist ??= new AssistController(() => _liveSource?.Entities as Aion2.Aion2EntityDirectory);
            _network ??= new NetworkOverlayController();
            Aion2Loot.EnablePersistence();
            _loot ??= new LootOverlayController();
            _craft ??= new CraftOverlayController(() => _liveSource?.Entities as Aion2.Aion2EntityDirectory);
            _dummy ??= new DummyTestController(() => _liveSource?.Entities as Aion2.Aion2EntityDirectory);
            _combat ??= new CombatOverlayController(() => _source?.Entities as Aion2.Aion2EntityDirectory, BossHpCheck);
            _menuWatcher ??= new MenuWatcher();
            _streamTimer.Tick += (_, _) => StreamTick();
            _streamTimer.Start();
            MeterSettings.BackupOnStart(); // after the first Load of this start, which restores a damaged file from the last copy
            if (MeterSettings.RecoveredFromBackup)
            {
                ShowUploadStatus(LocalizationManager.Instance["Main.SettingsRecovered"]);
            }
            _gateTimer.Tick += (_, _) => OnGateTick();
            _gateTimer.Start();
        }
    }

    /// <summary>
    /// Saves window geometry here, not OnClosed -- by the time OnClosed fires the window is
    /// already torn down and its bounds/RestoreBounds are no longer reliable, whereas OnClosing
    /// still runs with a fully intact window. Loads settings fresh from disk rather than reusing
    /// whatever was read at startup, so this can't clobber unrelated fields (Characters, Settings-
    /// dialog choices, etc.) with a stale in-memory snapshot if the Settings dialog saved its own
    /// changes sometime after this window was constructed.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        SaveWindowStateToSettings();
        base.OnClosing(e);
    }

    /// <summary>Extracted from OnClosing so the update restart can reuse it: Velopack's
    /// ApplyUpdatesAndRestart ends the process itself and never reaches OnClosing, which would
    /// silently lose the window position every time someone updated.</summary>
    private void SaveWindowStateToSettings()
    {
        var settings = MeterSettings.Load();
        SaveWindowGeometry(settings);
        settings.Save();
    }

    protected override void OnClosed(EventArgs e)
    {
        _pollTimer.Stop();
        _updateTimer.Stop();
        if (!ReferenceEquals(_source, _liveSource))
        {
            _source?.Dispose();
        }

        _liveSource?.Dispose();
        _overlay?.Dispose();
        _hotkeys?.Dispose();
        _gateTimer.Stop();
        _timetable?.Close();
        _hotkeyOverlay?.Close();
        _worldBoss?.Dispose();
        _assist?.Dispose();
        _loot?.Dispose();
        _craft?.Dispose();
        Aion2Loot.Flush();
        _combat?.Dispose();
        _menuWatcher?.Dispose();
        _streamTimer.Stop();
        _streamServer?.Dispose();
        _network?.Dispose();
        _dummy?.Dispose();
        Aion2FieldBosses.Flush();
        Aion2Tickets.Flush(force: true);
        _timetableReminder?.Dispose();
        _petFarmController?.Dispose();
        _petFarm?.Close();
        _petMapController?.Dispose();
        _trayIcon?.Dispose();
        base.OnClosed(e);
    }

    /// <summary>
    /// Rebuilds the grid from scratch against the current Mob/Boss filter rather than patching
    /// existing rows in place: with a target filter active, a source's presence in the grid
    /// itself depends on the filter (no damage to the selected target -> no row at all), so an
    /// incremental update would need to track and prune rows on every filter change anyway. Small
    /// event counts (a live meter's player list, not a bulk dataset) make the O(n) rescan cheap
    /// enough not to bother.
    /// </summary>
    private void RefreshRows()
    {
        // The index takes in what is new in every view (a few events a second): the target list and the heals are read from it
        // everywhere, and the views that ask nothing of a picked target or the scope take their damage side from it too.
        if (!ForceFullRefresh)
        {
            _index.Update(_aggregator, IsPlayerName);
        }

        // The index answers the damage side of every view that asks nothing of a picked target or of the compact overlay's Group/Corps
        // scope: Damage, PvP, Heal, Taken and All in One. The views of one target's fight and the scoped ones still rescan (a boss
        // fight's list follows rules that look at the whole stretch of events, and the scope at who fought what).
        bool damageIndexed = !ForceFullRefresh && _selectedTargetId is null && !(ScopeActive && _scope != MeterScope.All)
            && (!_pvpOnly || _index.TimeOrdered);

        RefreshMobBossFilterItems(!ForceFullRefresh);

        // !IsHeal here is the fix for a real bug found by terminal_windows against a real
        // heal-bearing session: without it, a pure healer ("Potion" -- a heal-effect name, not a
        // player) showed up as a damage source with a nonsensical "dmg (n/a DPS)" row, because
        // TargetIDps/AllDpsWallClock already filter heals out for the rate but nothing filtered
        // this event set for the totals or the row list itself. Same underlying issue as
        // LiveAggregator.Summarize -- see its remarks.
        var damageOnly = _aggregator.Events.Where(ev => !ev.IsHeal);

        // PVP DMG, per the user: damage against other PLAYERS only - mobs/bosses excluded
        // regardless of the Mob/Boss filter, which only ever lists NPC targets anyway (a specific
        // mob selection means nothing once mobs are out of the picture entirely).
        // A specific numbered run (both _selectedRunWindow* set - see MobBossTag's own remarks)
        // narrows further to that one run's own time span, not the target's whole history.
        Dictionary<int, bool> targetIsPlayer = new(); // the directory lookup takes a lock; once per target, not per hit
        List<DamageEvent> filtered = damageIndexed ? new List<DamageEvent>() : _pvpOnly
            ? damageOnly.Where(ev => targetIsPlayer.TryGetValue(ev.TargetObjectId, out bool known) ? known : targetIsPlayer[ev.TargetObjectId] = IsPlayerName(ev.TargetObjectId)).ToList()
            : _selectedTargetId is int targetId
                ? ShownFightHits(damageOnly, targetId)
                // The own-fight restriction belongs with the compact overlay's Group / Corps view; everywhere else the
                // players around one count too, whatever they are fighting.
                : ScopeActive && _scope != MeterScope.All
                    ? RestrictToEngagedTargets(damageOnly.ToList())
                    : damageOnly.ToList();

        if (_source?.Entities is Aion2.Aion2EntityDirectory rosterDirectory)
        {
            foreach (string member in rosterDirectory.PartyNames)
            {
                _groupNames.Add(member);
            }
        }

        _corpsIds = ScopeActive && _scope != MeterScope.All ? ComputeCorps(filtered) : new HashSet<int>();

        // A pure healer never hit the selected target, so `filtered` holds none of their events, yet
        // they belong in the list (and in an upload's roster). Heals inside the shown window by
        // players the meter has identified add them; bounded to that window so someone who healed
        // an hour ago elsewhere does not come back at a permanent 0.
        long shownCount = damageIndexed ? (_pvpOnly ? _index.PvpCount : _index.DamageCount) : filtered.Count;
        (DateTime, DateTime)? filteredSpan = shownCount == 0 ? null
            : damageIndexed ? (_pvpOnly ? (_index.PvpFirst, _index.PvpLast) : (_index.DamageFirst, _index.DamageLast))
            : (filtered.Min(ev => ev.Timestamp), filtered.Max(ev => ev.Timestamp));
        (int, DateTime, DateTime)? shownSummary = damageIndexed && filteredSpan is (DateTime summaryFrom, DateTime summaryTo)
            ? ((int)Math.Min(shownCount, int.MaxValue), summaryFrom, summaryTo)
            : null;

        if (_healMode && !_pvpOnly)
        {
            RefreshHealRows(filteredSpan);
            RankRows();
            UpdateHpCheck(OnSelectedTarget(filtered));
            UpdateCompactOverlay(filtered, shownSummary);
            return;
        }

        if (_takenMode && !_pvpOnly)
        {
            RefreshTakenRows(filteredSpan);
            RankRows();
            UpdateHpCheck(OnSelectedTarget(filtered));
            UpdateCompactOverlay(filtered, shownSummary);
            return;
        }

        var healSourceIds = filteredSpan is (DateTime spanStart, DateTime spanEnd)
            ? (damageIndexed ? _index.Heals : _aggregator.Events)
                .Where(ev => ev.IsHeal && ev.Timestamp >= spanStart && ev.Timestamp <= spanEnd && IsPlayerName(ev.SourceObjectId))
                .Select(ev => ev.SourceObjectId)
            : Enumerable.Empty<int>();

        // All in One: healing done and damage taken from monsters per player, over the same span as the
        // Heal and Taken modes use, so the three columns agree with those modes' totals.
        Dictionary<int, long> healingById = _allData
            ? HealsInSpan(filteredSpan).GroupBy(ev => ev.SourceObjectId).ToDictionary(g => g.Key, g => g.Sum(ev => ev.Amount))
            : new Dictionary<int, long>();
        Dictionary<int, long> hostileTakenById = _allData
            ? HostileHitsTaken(filteredSpan).GroupBy(ev => ev.TargetObjectId).ToDictionary(g => g.Key, g => g.Sum(ev => ev.Amount))
            : new Dictionary<int, long>();

        // All in One: what the group shields absorbed from monsters, beside the damage taken (blue part of it).
        Dictionary<int, long> absorbedById = _allData
            ? _aggregator.Shields
                .Where(ev => ShieldSkills.IsAbsorb(ev) && IsPlayerName(ev.TargetObjectId) && !IsPlayerName(ev.SourceObjectId)
                    && (filteredSpan is not (DateTime from, DateTime to) || (ev.Timestamp >= from && ev.Timestamp <= to)))
                .GroupBy(ev => ev.TargetObjectId).ToDictionary(g => g.Key, g => g.Sum(ev => ev.Amount))
            : new Dictionary<int, long>();

        var sourceIds = (damageIndexed ? (_pvpOnly ? _index.PvpBySource.Keys : _index.BySource.Keys) : filtered.Select(ev => ev.SourceObjectId).Distinct())
            .Union(healSourceIds)
            .Union(hostileTakenById.Keys)
            .Where(IsPlayerName)
            .ToList();

        // Damage RECEIVED per row (PlayerRow.DamageTaken): everything that hit them inside the
        // shown window - from the selected target only when one is picked, so the figure answers
        // "how much did this boss put on whom", the same scope BuildEncounterUpload's damageTaken
        // uses for the website's distribution chart.
        IEnumerable<DamageEvent> takenWindow = damageIndexed ? Enumerable.Empty<DamageEvent>()
            : filteredSpan is (DateTime takenStart, DateTime takenEnd)
                ? damageOnly.Where(ev => ev.Timestamp >= takenStart && ev.Timestamp <= takenEnd)
                : Enumerable.Empty<DamageEvent>();
        Dictionary<int, long> damageTakenById = takenWindow
            .Where(ev => _pvpOnly || _selectedTargetId is not int selectedAttacker || ev.SourceObjectId == selectedAttacker)
            .GroupBy(ev => ev.TargetObjectId)
            .ToDictionary(g => g.Key, g => g.Sum(ev => ev.Amount));
        long TakenOf(int id) => !damageIndexed ? damageTakenById.GetValueOrDefault(id)
            : filteredSpan is not (DateTime from, DateTime to) ? 0
            : _pvpOnly ? _index.TakenBetween(id, from, to)
            : _index.TakenByTarget.GetValueOrDefault(id);

                // ClassFilter, per the user: was purely decorative until other players' classes started
        // being detected at all (see ResolveClassName) -- now that a class can actually be known
        // for someone besides "You", picking one filters the grid down to it for real. An empty
        // result when nobody of that class is currently present is correct, not a bug.
        if (_selectedClassFilter is string classFilter)
        {
            sourceIds = sourceIds.Where(id => ResolveClassName(id) == classFilter).ToList();
        }

        sourceIds = sourceIds.Where(IsInScope).ToList();

        foreach (int staleId in _rowsByObjectId.Keys.Except(sourceIds).ToList())
        {
            _rows.Remove(_rowsByObjectId[staleId]);
            _rowsByObjectId.Remove(staleId);
        }

        // One pass over the events instead of one per player: in a raid (24 players, hundreds of thousands of
        // hits) the per-player rescans, one of them sorting, froze the meter once a second.
        Dictionary<int, long> damageById = new();
        Dictionary<int, long> estimatedById = new();
        foreach (DamageEvent hit in filtered)
        {
            damageById[hit.SourceObjectId] = damageById.GetValueOrDefault(hit.SourceObjectId) + hit.Amount;
            if (hit.Estimated)
            {
                estimatedById[hit.SourceObjectId] = estimatedById.GetValueOrDefault(hit.SourceObjectId) + hit.Amount;
            }
        }

        Dictionary<int, List<DamageEvent>>? allBySource = null;
        if (!damageIndexed && !_pvpOnly && _selectedTargetId is null)
        {
            allBySource = new();
            foreach (DamageEvent hit in _aggregator.Events)
            {
                if (!allBySource.TryGetValue(hit.SourceObjectId, out var list))
                {
                    allBySource[hit.SourceObjectId] = list = new List<DamageEvent>();
                }

                list.Add(hit);
            }
        }

        Dictionary<int, List<DamageEvent>>? pvpBySource = null;
        if (!damageIndexed && _pvpOnly)
        {
            pvpBySource = filtered.GroupBy(ev => ev.SourceObjectId).ToDictionary(g => g.Key, g => g.ToList());
        }

                // A picked target's DPS is each source's damage over the time of the hits on that target - one span for all rows, so it is
        // worked out once here and not by a pass over all the hits for every row.
        double targetSeconds = _selectedTargetId is int spanTarget && !damageIndexed && !_pvpOnly ? BossFight.SecondsOnTarget(filtered, spanTarget) : 0;

        // Rows are added before the values are set: the sorted view cannot be held back while the list itself changes.
        foreach (int sourceId in sourceIds)
        {
            if (!_rowsByObjectId.ContainsKey(sourceId))
            {
                var added = new PlayerRow(sourceId);
                _rowsByObjectId[sourceId] = added;
                _rows.Add(added);
            }
        }

        // Every row's damage changes in one go; with live sorting on, each change moved the row on its own (2000 rows
        // meant 2000 moves in a list of 2000, every second). Held back, the list is sorted once, when this block ends.
        using (_playersView?.DeferRefresh())
        foreach (int sourceId in sourceIds)
        {
            PlayerRow row = _rowsByObjectId[sourceId];

            // Applied every refresh, not just at creation: an active-character switch detected by
            // UpdateActiveCharacterFromSkill (or a newly detected class from UpdateOtherPlayerClass)
            // must update an already-existing row immediately, not just rows created after the fact.
            ApplyIdentity(row, sourceId);

            row.Damage = damageIndexed ? (_pvpOnly ? _index.PvpBySource : _index.BySource).GetValueOrDefault(sourceId)?.Sum ?? 0 : damageById.GetValueOrDefault(sourceId);
            row.IsEstimated = damageIndexed
                ? (_pvpOnly ? _index.PvpBySource : _index.BySource).GetValueOrDefault(sourceId) is { } indexed && indexed.EstimatedSum * 2 > indexed.Sum
                : estimatedById.GetValueOrDefault(sourceId) * 2 > row.Damage;
            // PVP mode's rate must come from the SAME PVP-only event set row.Damage above was
            // just summed from, not the unfiltered full history - same reasoning as the
            // Mob/Boss-filtered iDPS case below (see BuildEncounterUpload's own remarks on the
            // totalDamage/idps-must-share-one-source-of-truth bug this pattern once caused).
            // `filtered`, not `_aggregator.Events`, in the targeted branch too: a specific
            // numbered run's iDPS must come from that same run's own window, the same
            // one-source-of-truth reasoning the PVP branch above already follows (see
            // BuildEncounterUpload's own remarks on the totalDamage/idps bug this once caused).
            row.Dps = damageIndexed ? (_pvpOnly ? _index.PvpBySource : _index.BySource).GetValueOrDefault(sourceId)?.Rate
                : _pvpOnly
                ? DpsCalculator.AllDpsWallClock(pvpBySource!.GetValueOrDefault(sourceId) ?? new List<DamageEvent>(), sourceId)
                : _selectedTargetId is not null
                    ? (targetSeconds > 0 ? damageById.GetValueOrDefault(sourceId) / targetSeconds : null)
                    : DpsCalculator.AllDpsWallClock(allBySource!.GetValueOrDefault(sourceId) ?? new List<DamageEvent>(), sourceId);
            row.AllMode = _allMode;
            row.AllCompact = _allCompactMode;
            row.Healing = healingById.GetValueOrDefault(sourceId);
            row.Absorbed = absorbedById.GetValueOrDefault(sourceId);
            row.DamageTaken = _allData ? hostileTakenById.GetValueOrDefault(sourceId) : TakenOf(sourceId);
            row.Deaths = 0;
            row.ShowShareBar = _showShareBars;
            row.ShowDamageTaken = _showDamageTaken;

            row.Faction = (_source?.Entities as Aion2.Aion2EntityDirectory)?.FactionOf(sourceId) ?? "";
        }

                // Rank and share are relative to what is on screen, so they are settled once every row's
        // damage for this refresh is known - and by damage, not by the grid's current sort order.
        RankRows();
        UpdateHpCheck(OnSelectedTarget(filtered));
        UpdateCompactOverlay(filtered, shownSummary);
    }

    /// <summary>
    /// The hits shown for the selected target and run: for a boss, the boss and its adds over the
    /// boss fight (see <see cref="BossFight.ShownHits"/>); for anything else, that target alone.
    /// </summary>
    private List<DamageEvent> ShownFightHits(IEnumerable<DamageEvent> damage, int targetId)
    {
        var directory = _source?.Entities as Aion2.Aion2EntityDirectory;
        return BossFight.ShownHits(damage, targetId, _selectedRunWindowStart, _selectedRunWindowEnd,
            id => directory?.BossNpcIdOf(id) is not null,
            id => directory?.IsKnownMonster(id) == true);
    }

    /// <summary>The shown hits that landed on the selected target itself (the boss without its
    /// adds) - what its hit points are held against.</summary>
    private IReadOnlyList<DamageEvent> OnSelectedTarget(IReadOnlyList<DamageEvent> shown) =>
        _selectedTargetId is int id ? shown.Where(ev => ev.TargetObjectId == id).ToList() : shown;

    /// <summary>
    /// Whose rows the scope shows. Group: the local player and the players the party roster names,
    /// nobody else - no stranger around in the open world, named or not yet; before the first
    /// roster frame (a few seconds after joining) that is the local player alone. Corps: the group
    /// and the players who fought the same monsters as it (two parties joined into a corps share
    /// their fight; the roster only names one's own party). All: everybody. PvP shows everyone: the
    /// opponents are the point there.
    /// </summary>
    private bool IsInScope(int sourceId)
    {
        if (!ScopeActive || _scope == MeterScope.All || _pvpOnly || _source?.Entities is not Aion2.Aion2EntityDirectory directory
            || directory.IsLocalPlayer(sourceId) || directory.InferLocalPlayer() == sourceId)
        {
            return true;
        }

        // No roster seen (the meter started after the group formed; the roster is sent once): the
        // group is unknown, and "only me" would be wrong - until it is read, the group is whoever
        // fights the same monsters.
        bool groupKnown = RosterKnown(directory);
        if (_scope == MeterScope.Group && !groupKnown)
        {
            return _corpsIds.Contains(sourceId);
        }

        return IsGroupMember(sourceId, directory) || (_scope == MeterScope.Corps && _corpsIds.Contains(sourceId));
    }

    /// <summary>Whether a party roster naming anyone besides the local player has been read.</summary>
    private bool RosterKnown(Aion2.Aion2EntityDirectory directory)
    {
        string? own = directory.LocalCharacter?.Name;
        return _groupNames.Any(n => n != own) || directory.PartyNames.Any(n => n != own);
    }

    private bool IsGroupMember(int id, Aion2.Aion2EntityDirectory directory)
    {
        if (directory.IsLocalPlayer(id) || directory.InferLocalPlayer() == id)
        {
            return true;
        }

        string name = ResolveDisplayName(id);
        return _groupNames.Contains(name) || directory.PartyNames.Contains(name) || name == directory.LocalCharacter?.Name;
    }

    /// <summary>The players who hit a monster the group also hit (an approximation of the corps until
    /// its own roster is read from the network).</summary>
    private HashSet<int> ComputeCorps(IReadOnlyList<DamageEvent> shown)
    {
        var corps = new HashSet<int>();
        if (_source?.Entities is not Aion2.Aion2EntityDirectory directory)
        {
            return corps;
        }

        var groupTargets = shown.Where(ev => IsGroupMember(ev.SourceObjectId, directory)).Select(ev => ev.TargetObjectId).ToHashSet();
        foreach (DamageEvent ev in shown)
        {
            if (groupTargets.Contains(ev.TargetObjectId) && IsPlayerName(ev.SourceObjectId))
            {
                corps.Add(ev.SourceObjectId);
            }
        }

        return corps;
    }

    private void SetScope(MeterScope scope)
    {
        if (scope == _scope)
        {
            UpdateScopeControls();
            return;
        }

        _scope = scope;
        var settings = MeterSettings.Load();
        settings.ViewScope = scope.ToString();
        settings.Save();
        UpdateScopeControls();
        RefreshRows();
    }

    private void OnOverlayScopeClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse(tag, out MeterScope scope))
        {
            SetScope(scope);
        }

        e.Handled = true;
    }

    /// <summary>Marks the chosen scope on the compact overlay's three-way switch.</summary>
    private void UpdateScopeControls()
    {
        foreach (Border segment in OverlayScopeSwitch.Children.OfType<Border>())
        {
            bool selected = segment.Tag as string == _scope.ToString();
            segment.Background = selected ? new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            if (segment.Child is TextBlock text)
            {
                text.SetResourceReference(TextBlock.ForegroundProperty, selected ? "Brush.OverlayText" : "Brush.TextSubtle");
            }
        }
    }

    /// <summary>A target's heals on itself between two moments - a boss healing itself by the hits it
    /// takes (see Aion2FrameDecoder.DecodeVarintDot), which the HP check has to know about.</summary>
    /// <summary>The HP check for the boss's latest fight: the damage since the last pause of half a minute, against the hit points it lost.</summary>
    private HpCheckResult? BossHpCheck(int bossId)
    {
        if (_source?.Entities is not Aion2.Aion2EntityDirectory directory)
        {
            return null;
        }

        var run = new List<DamageEvent>();
        DateTime? earlier = null;
        foreach (DamageEvent ev in _aggregator.Events.Where(e => !e.IsHeal && e.TargetObjectId == bossId).OrderByDescending(e => e.Timestamp))
        {
            if (earlier is DateTime e2 && (e2 - ev.Timestamp).TotalSeconds > 30)
            {
                break;
            }

            run.Add(ev);
            earlier = ev.Timestamp;
        }

        if (run.Count == 0)
        {
            return null;
        }

        DateTime start = run.Min(h => h.Timestamp), end = run.Max(h => h.Timestamp);
        var readings = directory.HitPoints.SamplesAround(bossId, start, end).Select(s => (s.At, s.Hp)).ToList();
        return readings.Count == 0 ? null : HpCheck.Evaluate(readings, run.Concat(OwnHeals(bossId, start, end)).ToList(), directory.HitPoints.HighestSeen(bossId) ?? 0);
    }

    private IEnumerable<DamageEvent> OwnHeals(int targetId, DateTime start, DateTime end) =>
        _aggregator.Events.Where(ev => ev.IsHeal && ev.SourceObjectId == targetId && ev.TargetObjectId == targetId
            && ev.Timestamp >= start && ev.Timestamp <= end);

    /// <summary>
    /// The status row's guard against wrong totals: for the selected target (one run of it), the
    /// damage counted held against the hit points the server says it lost - see
    /// <see cref="HpCheck"/>. Shown only where the source reports hit points (Aion 2) and a target
    /// is selected; a mismatch is spelled out in the warning colour rather than left for the
    /// numbers above it to be trusted.
    /// </summary>
    private void UpdateHpCheck(IReadOnlyList<DamageEvent> targetHits)
    {
        HpCheckResult? check = null;
        if (!_pvpOnly && _selectedTargetId is int targetId && targetHits.Count > 0
            && _source?.Entities is Aion2.Aion2EntityDirectory directory && !directory.IsKnownPlayer(targetId))
        {
            DateTime start = targetHits.Min(h => h.Timestamp);
            DateTime end = targetHits.Max(h => h.Timestamp);
            var readings = directory.HitPoints.SamplesAround(targetId, start, end).Select(s => (s.At, s.Hp)).ToList();
            check = HpCheck.Evaluate(readings, targetHits.Concat(OwnHeals(targetId, start, end)).ToList(), directory.HitPoints.HighestSeen(targetId) ?? 0);
        }

        _lastHpCheck = check;
        if (check is null)
        {
            HpCheckText.Visibility = Visibility.Collapsed;
            return;
        }

        var loc = LocalizationManager.Instance;
        string percent = check.Ratio.ToString("P1", CultureInfo.CurrentCulture);
        bool warn = check.OverFullHealth || check.Verdict != HpCheckVerdict.Match;
        HpCheckText.Text = check.OverFullHealth
            ? string.Format(loc["Main.HpCheck.OverFull"], check.RunTotal.ToString("N0"), check.Highest.ToString("N0"))
            : check.Verdict switch
            {
                HpCheckVerdict.Missing => string.Format(loc["Main.HpCheck.Missing"], percent, check.Lost.ToString("N0")),
                HpCheckVerdict.Excess => string.Format(loc["Main.HpCheck.Excess"], percent, check.Lost.ToString("N0")),
                _ => string.Format(loc["Main.HpCheck.Match"], percent, check.Lost.ToString("N0")),
            };
        HpCheckText.SetResourceReference(TextBlock.ForegroundProperty, warn ? "Brush.Warning" : "Brush.TextMuted");
        HpCheckText.FontWeight = warn ? FontWeights.Bold : FontWeights.Normal;
        HpCheckText.Visibility = Visibility.Visible;
    }

    private void RankRows()
    {
        // Live sorting alone left the list in the previous mode's order after a switch (PVE -> PVP kept the
        // PVE ranking); sorting explicitly here covers every mode, since each refresh ends in RankRows.
        _playersView?.Refresh();
        long shownTotal = _rows.Sum(r => r.Damage);
        // In Taken mode the bar is drawn to the damage that got through, behind it a blue one to the absorbed
        // damage on top; both scale to the biggest of the two together.
        long shownMax = _rows.Count > 0 ? _rows.Max(r => r.Damage + (_takenMode ? r.Absorbed : 0)) : 0;
        int rank = 0;
        foreach (PlayerRow row in _rows.OrderByDescending(r => r.Damage))
        {
            row.Rank = ++rank;
            row.SharePercent = shownTotal > 0 ? 100.0 * row.Damage / shownTotal : 0;
            row.FillPercent = shownMax > 0 ? 100.0 * row.Damage / shownMax : 0;
            row.AbsorbedFillPercent = _takenMode && shownMax > 0 && row.Absorbed > 0 ? 100.0 * (row.Damage + row.Absorbed) / shownMax : 0;
        }
    }

    /// <summary>
    /// Heal mode: one row per healer with the healing done over the fight on screen (the shown
    /// damage's time span) and HPS over that span. Heals on summoned spirits are left out: a
    /// spirit's spawn arrives as a 100,000 "heal" on itself, and topping up a spirit is not healing
    /// the group.
    /// </summary>
    private void RefreshHealRows((DateTime Start, DateTime End)? span)
    {
        var directory = _source?.Entities as Aion2.Aion2EntityDirectory;
        var heals = HealsInSpan(span);
        double? spanSeconds = span is (DateTime a, DateTime b) && b > a ? (b - a).TotalSeconds : null;

        var sourceIds = heals.Select(ev => ev.SourceObjectId).Distinct().ToList();
        if (_selectedClassFilter is string classFilter)
        {
            sourceIds = sourceIds.Where(id => ResolveClassName(id) == classFilter).ToList();
        }

        sourceIds = sourceIds.Where(IsInScope).ToList();

        foreach (int staleId in _rowsByObjectId.Keys.Except(sourceIds).ToList())
        {
            _rows.Remove(_rowsByObjectId[staleId]);
            _rowsByObjectId.Remove(staleId);
        }

        foreach (int sourceId in sourceIds)
        {
            if (!_rowsByObjectId.TryGetValue(sourceId, out var row))
            {
                row = new PlayerRow(sourceId);
                _rowsByObjectId[sourceId] = row;
                _rows.Add(row);
            }

            ApplyIdentity(row, sourceId);
            var mine = heals.Where(ev => ev.SourceObjectId == sourceId).ToList();
            row.IsEstimated = false;
            row.Damage = mine.Sum(ev => ev.Amount);
            double seconds = spanSeconds ?? (mine.Max(ev => ev.Timestamp) - mine.Min(ev => ev.Timestamp)).TotalSeconds;
            row.Dps = seconds > 0 ? row.Damage / seconds : null;
            row.DamageTaken = 0;
            row.Absorbed = 0;
            row.AllMode = false;
            row.AllCompact = false;
            row.Healing = 0;
            row.Deaths = 0;
            row.ShowShareBar = _showShareBars;
            row.ShowDamageTaken = false;
            row.Faction = directory?.FactionOf(sourceId) ?? "";
        }
    }

    /// <summary>The heals by players inside a span, without those on summoned spirits (see <see cref="RefreshHealRows"/>).</summary>
    private List<DamageEvent> HealsInSpan((DateTime Start, DateTime End)? span)
    {
        var directory = _source?.Entities as Aion2.Aion2EntityDirectory;
        // The index keeps the heals apart (RefreshRows has just taken in the new events): the damage hits, most of the list, are not walked.
        return (ForceFullRefresh ? _aggregator.Events : _index.Heals)
            .Where(ev => ev.IsHeal && IsPlayerName(ev.SourceObjectId)
                && directory?.SummonOwnerOf(ev.TargetObjectId) is null
                && (span is not (DateTime from, DateTime to) || (ev.Timestamp >= from && ev.Timestamp <= to)))
            .ToList();
    }

    /// <summary>The hostile hits players took inside a span: from a monster, not from a player (a
    /// Sorcerer's Absorb Essence costs their own hit points; PvP has its own mode).</summary>
    private List<DamageEvent> HostileHitsTaken((DateTime Start, DateTime End)? span) =>
        _aggregator.Events
            .Where(ev => !ev.IsHeal && IsPlayerName(ev.TargetObjectId) && !IsPlayerName(ev.SourceObjectId)
                && (span is not (DateTime from, DateTime to) || (ev.Timestamp >= from && ev.Timestamp <= to)))
            .ToList();

    /// <summary>
    /// Taken mode: one row per player with the damage monsters dealt them over the fight on screen,
    /// per second of it, and their deaths there (see <see cref="Deaths"/>).
    /// </summary>
    private void RefreshTakenRows((DateTime Start, DateTime End)? span)
    {
        var directory = _source?.Entities as Aion2.Aion2EntityDirectory;
        var hits = HostileHitsTaken(span);
        // What the group shields absorbed from monsters in that span (see ShieldSkills.IsAbsorb).
        var absorbs = _aggregator.Shields
            .Where(ev => ShieldSkills.IsAbsorb(ev) && IsPlayerName(ev.TargetObjectId) && !IsPlayerName(ev.SourceObjectId)
                && (span is not (DateTime from, DateTime to) || (ev.Timestamp >= from && ev.Timestamp <= to)))
            .ToList();
        double? spanSeconds = span is (DateTime a, DateTime b) && b > a ? (b - a).TotalSeconds : null;

        var targetIds = hits.Select(ev => ev.TargetObjectId).Distinct().ToList();
        if (_selectedClassFilter is string classFilter)
        {
            targetIds = targetIds.Where(id => ResolveClassName(id) == classFilter).ToList();
        }

        targetIds = targetIds.Where(IsInScope).ToList();

        foreach (int staleId in _rowsByObjectId.Keys.Except(targetIds).ToList())
        {
            _rows.Remove(_rowsByObjectId[staleId]);
            _rowsByObjectId.Remove(staleId);
        }

        _deathsById.Clear();
        _takenSpan = span;
        foreach (int targetId in targetIds)
        {
            if (!_rowsByObjectId.TryGetValue(targetId, out var row))
            {
                row = new PlayerRow(targetId);
                _rowsByObjectId[targetId] = row;
                _rows.Add(row);
            }

            ApplyIdentity(row, targetId);
            var mine = hits.Where(ev => ev.TargetObjectId == targetId).ToList();
            row.IsEstimated = false;
            row.Damage = mine.Sum(ev => ev.Amount);
            row.Absorbed = absorbs.Where(ev => ev.TargetObjectId == targetId).Sum(ev => ev.Amount);
            double seconds = spanSeconds ?? (mine.Max(ev => ev.Timestamp) - mine.Min(ev => ev.Timestamp)).TotalSeconds;
            row.Dps = seconds > 0 ? row.Damage / seconds : null;

            var readings = directory is null || span is not (DateTime from, DateTime to)
                ? new List<(DateTime, long)>()
                : directory.HitPoints.SamplesAround(targetId, from, to + Deaths.BlowWindow).Select(s => (s.At, s.Hp)).ToList();
            var deaths = Deaths.Find(targetId, readings, mine);
            _deathsById[targetId] = deaths;
            row.Deaths = deaths.Count;
            row.DamageTaken = 0;
            row.Absorbed = 0;
            row.AllMode = false;
            row.AllCompact = false;
            row.Healing = 0;
            row.ShowShareBar = _showShareBars;
            row.ShowDamageTaken = false;
            row.Faction = directory?.FactionOf(targetId) ?? "";
        }
    }


    /// <summary>A player is an object the meter has seen casting a class skill or whose "appeared"
    /// frame it read; everything else is an NPC (shown by its hex id or boss name).</summary>
    private bool IsPlayerName(int id) =>
        _source?.Entities is Aion2.Aion2EntityDirectory entities && entities.IsKnownPlayer(id)
        // a stored session or fight runs on a replay source that knows no players: the identities it came with (name and class are only known for players) tell instead, so its PvP view (the arena) is not empty
        || (_historyMode && _playerIdentities.ContainsKey(id));

    /// <summary>Sets Name/ClassName/Level for one row: from the stored identity when a past fight is
    /// shown, else from the packet stream - the name and class the directory has learned for the id.</summary>
    private void ApplyIdentity(PlayerRow row, int sourceId)
    {
        if (_playerIdentities.TryGetValue(sourceId, out var identity))
        {
            row.Name = identity.Name;
            row.ClassName = identity.ClassName;
            row.Level = identity.Level;
            return;
        }

        row.Name = ResolveDisplayName(sourceId);
        row.ClassName = ResolveClassName(sourceId);
    }

    /// <summary>The class an object has been seen casting a skill of (skill id prefix), keyed by the
    /// object id - not by name, which changes once the real name is learned ("Gladiator #331" ->
    /// "Aahz") and used to drop the class icon along with it. "?" while unknown.</summary>
    private string ResolveClassName(int sourceId) =>
        (_source?.Entities as Aion2EntityDirectory)?.ClassOf(sourceId) ?? "?";

    /// <summary>Adds any newly-seen DAMAGE targets to the dropdown (never removes - only Clear does
    /// that); "All" is the one entry with no Tag, everything else carries its target object id. Heal
    /// targets are excluded on purpose (a healed party member is no "Mob/Boss"). A non-player target
    /// only gets an entry when it is a boss - one whose NPC id the game announced and the boss catalog
    /// knows (see <see cref="IsKnownBoss"/>); a player target (PVP) always does.</summary>
    private void RefreshMobBossFilterItems(bool indexed = false)
    {
        var knownIds = _mobBossEntries.Select(entry => entry.TargetId).ToHashSet();
        bool added = false;

        // The index has the distinct targets already; the scan is for the views that did not feed it.
        IEnumerable<int> targets = indexed ? _index.Targets.ToList() : _aggregator.Events.Where(ev => !ev.IsHeal).Select(ev => ev.TargetObjectId).Distinct();
        foreach (int targetId in targets)
        {
            bool isPlayerTarget = IsPlayerName(targetId);
            if (knownIds.Contains(targetId))
            {
                continue;
            }

            string name = _targetNames.TryGetValue(targetId, out string? n) ? n : ResolveDisplayName(targetId);
            if (!isPlayerTarget && !IsKnownBoss(name, targetId))
            {
                continue;
            }

            _mobBossEntries.Add((targetId, name));
            added = true;
        }

        if (added || _mobBossFilterNeedsRebuild)
        {
            ApplyMobBossSearchFilter();
            RefreshUploadAvailability();
        }
    }

    /// <summary>
    /// Per the user: the upload entry points (the Session menu's two items, and the Damage view's
    /// own button) should not be offered at all until there is something real to upload - shows
    /// them the moment the Mob/Boss filter has at least one real target, hides them again after
    /// Clear. UploadBossButton additionally stays Loot-view-collapsed regardless (a target filter
    /// has no meaning there), so its visibility is never JUST "has data" the way the two menu
    /// items' is.
    /// </summary>
    private void RefreshUploadAvailability()
    {
        bool hasBossData = _mobBossEntries.Count > 0;
        // Aion 2 can also upload players without any boss fight (their character profiles), so the
        // single-upload entries stay available as soon as the own character is known.
        bool hasProfiles = _source?.Entities is Aion2.Aion2EntityDirectory { LocalCharacter: not null };
        _uploadFlags.ShowBoss = hasBossData || hasProfiles;
        _uploadFlags.ShowRun = hasBossData;
        UploadBossButton.Visibility = (hasBossData || hasProfiles) && PlayersGrid.Visibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// Rebuilds MobBossFilter's VISIBLE Items from <see cref="_mobBossEntries"/>, keeping only
    /// names that CONTAIN the search box's current text - substring, case-insensitive ("like
    /// '%word%'", per the user, not WPF's own built-in type-ahead, which only matches from the
    /// start). "All" is always shown regardless of the search text - it isn't a target name to
    /// search for at all. Re-selects whichever target was selected before the rebuild, if it
    /// survived the filter: every rebuild creates new ComboBoxItem instances, so without this the
    /// selection (and _selectedTargetId with it) would reset on every keystroke.
    /// </summary>
    /// <summary>One dropdown entry's Tag - null WindowStart/End means "this target's whole
    /// history", a real pair means one specific numbered run (see MobBossTagsFor).</summary>
    private readonly record struct MobBossTag(int TargetId, DateTime? WindowStart, DateTime? WindowEnd);

    /// <summary>Gap between hits, in seconds, past which two hits on the same target count as
    /// separate runs rather than one continuous fight - same default the CLI's headless clustered
    /// upload already uses (see Program.cs), reused here so the dropdown's own split agrees with
    /// what "AionDPS upload" would produce for the same session.</summary>
    private const double RunClusterGapSeconds = 120;

    /// <summary>
    /// One or more (Tag, DisplayName, Damage) rows for a single Mob/Boss entry - per the user, a
    /// boss farmed repeatedly in one session (their example: Raksang Boilheart, five kills in a
    /// row) must appear as "Name #1".."Name #N" so each run stays individually selectable
    /// afterward, not just as one entry that silently combines every kill's numbers together. A
    /// target hit only once (the common case) still gets exactly one row, unnumbered and with a
    /// null window - identical to this method's pre-existing behavior for that case.
    /// </summary>
    private IEnumerable<(MobBossTag Tag, string Name, long Damage)> MobBossRowsFor(int targetId, string name)
    {
        var hits = _aggregator.Events
            .Where(ev => !ev.IsHeal && ev.TargetObjectId == targetId)
            .OrderBy(ev => ev.Timestamp)
            .ToList();
        if (hits.Count == 0)
        {
            yield return (new MobBossTag(targetId, null, null), name, 0);
            yield break;
        }

        // A wipe and retry keeps the boss's entity id and can come within the gap below, so the
        // moments the server put the boss back to full health cut the hits as well. A run that
        // ended in such a reset is a wipe and carries a skull.
        IReadOnlyList<DateTime> resets = (_source?.Entities as Aion2.Aion2EntityDirectory)?.HitPoints.ResetsOf(targetId)
            ?? Array.Empty<DateTime>();
        var clusters = new List<List<DamageEvent>>();
        var wiped = new List<bool>();
        foreach (DamageEvent hit in hits)
        {
            if (clusters.Count > 0)
            {
                DateTime previous = clusters[^1][^1].Timestamp;
                bool reset = resets.Any(at => at > previous && at <= hit.Timestamp);
                if (reset || (hit.Timestamp - previous).TotalSeconds > RunClusterGapSeconds)
                {
                    wiped[^1] = reset;
                    clusters.Add(new List<DamageEvent> { hit });
                    wiped.Add(false);
                    continue;
                }

                clusters[^1].Add(hit);
            }
            else
            {
                clusters.Add(new List<DamageEvent> { hit });
                wiped.Add(false);
            }
        }

        // The last run is a wipe too when the boss went back to full health after its final hit,
        // even if nobody pulled it again.
        DateTime lastHit = clusters[^1][^1].Timestamp;
        wiped[^1] = resets.Any(at => at > lastHit);

        if (clusters.Count == 1 && !wiped[0])
        {
            yield return (new MobBossTag(targetId, null, null), name, clusters[0].Sum(e => e.Amount));
            yield break;
        }

        for (int i = 0; i < clusters.Count; i++)
        {
            List<DamageEvent> cluster = clusters[i];
            var tag = new MobBossTag(targetId, cluster[0].Timestamp, cluster[^1].Timestamp);
            yield return (tag, $"{name} #{i + 1}{(wiped[i] ? " \U0001F480" : "")}", cluster.Sum(e => e.Amount));
        }
    }

    private void ApplyMobBossSearchFilter()
    {
        MobBossTag? previouslySelected = _selectedTargetId is int previousTargetId
            ? new MobBossTag(previousTargetId, _selectedRunWindowStart, _selectedRunWindowEnd)
            : null;
        string search = _mobBossSearchBox?.Text ?? "";
        // TextChanged rebuilds Items below, which regenerates the ComboBox's item containers and,
        // as a side effect, steals keyboard focus away from PART_SearchBox back to the ComboBox
        // itself - found from a real report: typing more than one letter required clicking back
        // into the search box after every single character. Caret position is saved too. Restored
        // via BeginInvoke at Input priority, same timing reasoning as DropDownOpened's own focus
        // hand-off a few lines below - the rebuilt containers aren't necessarily focusable yet on
        // this exact call frame.
        bool searchBoxHadFocus = _mobBossSearchBox?.IsFocused ?? false;
        int caretIndex = _mobBossSearchBox?.CaretIndex ?? 0;

        // Per the user: a real boss should always sort above trash mobs, by however much combined
        // damage the group actually did to it - not alphabetically, and not by discovery order.
        // Descending by damage puts a boss (tens of thousands of hits) far above a random trash
        // mob (a few hits in passing) without needing any curated "this is a real boss" list.
        //
        // _mobBossEntries is append-only for the whole session and mixes mob/boss targets with
        // player targets (a PVP fight's TargetObjectId points at another player, not a mob) - per
        // the user, PVE mode's list should only ever offer mobs/bosses, PVP mode's only players.
        // Filtering here (rather than in RefreshMobBossFilterItems) keeps both sets around so
        // switching modes mid-session doesn't lose entries seen while the other mode was active.
        var rows = _mobBossEntries
            .Where(entry => IsPlayerName(entry.TargetId) == _pvpOnly)
            .SelectMany(entry => MobBossRowsFor(entry.TargetId, entry.Name))
            .Where(row => search.Length == 0 || row.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(row => row.Damage)
            .ToList();

        try
        {
            MobBossFilter.Items.Clear();
            _mobBossAllItem = new ComboBoxItem { Tag = _mobBossAllTag };
            if (_mobBossAllContentBinding is not null)
            {
                BindingOperations.SetBinding(_mobBossAllItem, ContentControl.ContentProperty, _mobBossAllContentBinding);
            }
            else
            {
                _mobBossAllItem.Content = _mobBossAllContent;
            }

            MobBossFilter.Items.Add(_mobBossAllItem);
            foreach (var row in rows)
            {
                MobBossFilter.Items.Add(new ComboBoxItem { Content = row.Name, Tag = row.Tag });
            }
        }
        catch (InvalidOperationException)
        {
            // A WPF container hiccup while the dropdown is busy: not worth ending the meter over;
            // the next refresh tick rebuilds the list again.
            _mobBossFilterNeedsRebuild = true;
            return;
        }

        _mobBossFilterNeedsRebuild = false;

        // Only reassign SelectedItem when the previous selection actually survived the filter -
        // per the user, forcing it back to "All" (the first item, whenever nothing survives)
        // on every keystroke was what visibly stole focus from the search box mid-word, jumping
        // the dropdown to its first entry after each letter typed. Leaving SelectedItem alone
        // when nothing matches (WPF already cleared it via Items.Clear() above) means the box
        // simply shows no selection while a search is narrowing the list, instead of fighting the
        // user's typing for keyboard focus.
        var stillPresent = MobBossFilter.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => Equals(item.Tag as MobBossTag?, previouslySelected));
        if (stillPresent is not null)
        {
            MobBossFilter.SelectedItem = stillPresent;
        }

        if (searchBoxHadFocus && _mobBossSearchBox is not null)
        {
            TextBox searchBox = _mobBossSearchBox;
            Dispatcher.BeginInvoke(() =>
            {
                searchBox.Focus();
                searchBox.CaretIndex = Math.Min(caretIndex, searchBox.Text.Length);
            }, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    /// <summary>
    /// Wires up the SearchableComboBox template's PART_SearchBox - has to wait for the real
    /// visual tree (Loaded), since a ComboBox's template isn't materialized yet at construction
    /// time and Template.FindName needs it to be. Clearing the search text on every DropDownOpened
    /// (not just once here) is what keeps a stale search from an earlier session hiding the target
    /// the user wants next time they open the list.
    /// </summary>
    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        ApplyClassFilterAvailability();
        LocalizationManager.Instance.PropertyChanged += (_, _) => LocalizeClassFilter();

        // See AppIconImage's own XAML remarks: a plain pack://siteoforigin Source silently
        // rendered nothing for this specific .ico, so it's decoded via GDI instead - the same
        // path Explorer itself uses for any .ico, rather than WPF's own (apparently less
        // reliable, for this file) BitmapImage/pack-URI decoding.
        try
        {
            using var appIcon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "assets", "app", "aiondps.ico"));
            AppIconImage.Source = Imaging.CreateBitmapSourceFromHIcon(
                appIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            // Missing/corrupt icon file must not stop the window from opening - the titlebar
            // just shows no icon at all, same as before this existed.
        }

        MobBossFilter.ApplyTemplate();
        _mobBossSearchBox = MobBossFilter.Template.FindName("PART_SearchBox", MobBossFilter) as TextBox;
        if (_mobBossSearchBox is null)
        {
            return;
        }

        _mobBossSearchBox.TextChanged += (_, _) => ApplyMobBossSearchFilter();
        MobBossFilter.DropDownOpened += (_, _) =>
        {
            _mobBossSearchBox.Text = "";
            // BeginInvoke, not a direct call: the popup's own content isn't reliably focusable
            // yet at the instant DropDownOpened fires (same class of timing issue as focusing
            // anything else inside a just-opened Popup) -- Input priority runs it right after the
            // popup finishes opening, not on some arbitrary later frame.
            Dispatcher.BeginInvoke(() => _mobBossSearchBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        };
    }

    private void OnMobBossFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        // MobBossFilter's XAML sets SelectedIndex="0", which makes WPF fire this handler from
        // inside InitializeComponent itself -- while the rest of the tree (including
        // DpsHeaderText, declared further down in the XAML) hasn't been built yet. Found the hard
        // way: every single GUI launch crashed with a NullReferenceException before a window ever
        // appeared (back when this read DpsColumn, the merged column's predecessor). IsInitialized
        // only becomes true once the whole tree exists.
        if (!IsInitialized)
        {
            return;
        }

        if ((MobBossFilter.SelectedItem as ComboBoxItem)?.Tag is MobBossTag tag)
        {
            _selectedTargetId = tag.TargetId;
            _selectedRunWindowStart = tag.WindowStart;
            _selectedRunWindowEnd = tag.WindowEnd;
        }
        else
        {
            _selectedTargetId = null;
            _selectedRunWindowStart = null;
            _selectedRunWindowEnd = null;
        }

        UpdateDpsColumnHeader();
        RefreshRows();
    }

    /// <summary>Per the user: a two-way segmented toggle (PVE/PVP), not an independent checkbox -
    /// exactly one side is active, enforced here rather than via RadioButton's own chrome (see the
    /// style comment in the XAML). PVP restricts the whole grid to damage against other PLAYERS
    /// only (Arena/Abyss/GvG), mobs and bosses excluded entirely, and is mutually exclusive with
    /// the Mob/Boss filter (see RefreshRows) - reset to "All" here so the dropdown doesn't keep
    /// showing a now-irrelevant mob selection while PVP mode is active.</summary>
    private void OnPvpModeToggleClicked(object sender, RoutedEventArgs e)
    {
        _pvpOnly = ReferenceEquals(sender, PvpModeButton);
        PveModeButton.IsChecked = !_pvpOnly;
        PvpModeButton.IsChecked = _pvpOnly;

        if (_pvpOnly)
        {
            _selectedTargetId = null;
            _selectedRunWindowStart = null;
            _selectedRunWindowEnd = null;
            MobBossFilter.SelectedItem = _mobBossAllItem;
        }

        ApplyMobBossSearchFilter();
        UpdateDpsColumnHeader();
        RefreshRows();
    }

    private void UpdateDpsColumnHeader()
    {
        DpsHeaderText.Text = _healMode && !_pvpOnly ? "Healing / HPS"
            : _takenMode && !_pvpOnly ? "Taken / DTPS"
            : _allMode ? "Taken / Heal / Damage"
            : _pvpOnly ? "Damage / DPS (PvP)" : _selectedTargetId is int ? "Damage / iDPS" : "Damage / DPS";
    }

    // ---- the stream overlay (OBS browser source) ------------------------------------------------------------

    /// <summary>Four times a second: starts, stops or restarts the server as the settings say, and sends the state when it changed - only while a browser source is connected.</summary>
    private void StreamTick()
    {
        if (Headless)
        {
            return;
        }

        if ((DateTime.UtcNow - _streamSettingsAt).TotalSeconds >= 1)
        {
            _streamSettingsAt = DateTime.UtcNow;
            _streamSettings = MeterSettings.Load();
            if (_streamSettings.StreamOverlayOn && _streamSettings.StreamOverlayKey.Length == 0)
            {
                _streamSettings.StreamOverlayKey = Streaming.StreamOverlayServer.NewKey();
                var onDisk = MeterSettings.Load();
                onDisk.StreamOverlayKey = _streamSettings.StreamOverlayKey;
                onDisk.Save();
            }

            bool wanted = _streamSettings.StreamOverlayOn;
            if (!wanted)
            {
                _streamServer?.Dispose();
                _streamServer = null;
            }
            else if (_streamServer is null || !_streamServer.Running || _streamServer.Url?.Contains("/" + _streamSettings.StreamOverlayKey + "/") != true
                || _streamServer.Network != _streamSettings.StreamOverlayNetwork
                || _streamServer.Port != Math.Clamp(_streamSettings.StreamOverlayPort, 1024, 65000) && !_streamServer.Running)
            {
                _streamServer?.Dispose();
                _streamServer = new Streaming.StreamOverlayServer();
                int port = Math.Clamp(_streamSettings.StreamOverlayPort, 1024, 65000);
                if (!_streamServer.Start(port, _streamSettings.StreamOverlayKey, _streamSettings.StreamOverlayNetwork))
                {
                    ShowUploadStatus(LocalizationManager.Instance["Stream.NoPort"]);
                    _streamServer.Dispose();
                    _streamServer = null;
                }
            }
        }

        if (_streamServer is not { Running: true, ClientCount: > 0 })
        {
            return;
        }

        string json = BuildStreamState(_streamSettings.StreamShowNames);
        if (json != _streamLast)
        {
            _streamLast = json;
            _streamServer.Publish(json);
        }
    }

    /// <summary>What the browser source draws now: the rows as the current view shows them, and the boss's health.</summary>
    private string BuildStreamState(bool showNames)
    {
        var ordered = _rows.OrderBy(r => r.Rank > 0 ? r.Rank : int.MaxValue).ThenByDescending(r => r.Damage).ToList();
        string label;
        string[] headers;
        Func<PlayerRow, (string[] Cells, string? Sub)> cells;
        switch (_mode)
        {
            case MeterMode.Heal:
                (label, headers) = ("HEAL", new[] { "HPS", "HEAL", "%" });
                cells = r => (new[] { r.DpsCompact, r.DamageCompact, r.ShareDisplay }, null);
                break;
            case MeterMode.Taken:
                (label, headers) = ("TAKEN", new[] { "DTPS", "TAKEN", "%" });
                cells = r => (new[] { r.DpsCompact, r.DamageCompact, r.ShareDisplay }, null);
                break;
            case MeterMode.All:
                (label, headers) = ("ALL", new[] { "↓", "+", "DMG" });
                cells = r => (new[] { r.TakenCompact, r.HealCompact, r.DamageCompact }, null);
                break;
            case MeterMode.AllCompact:
                (label, headers) = ("ALL+", new[] { "DPS", "DMG" });
                cells = r => (new[] { r.DpsCompact, r.DamageCompact }, $"↓ {r.TakenCompact}  + {r.HealCompact}");
                break;
            default:
                (label, headers) = ("DMG", new[] { "DPS", "DMG", "%" });
                cells = r => (new[] { r.DpsCompact, r.DamageCompact, r.ShareDisplay }, null);
                break;
        }

        var rows = ordered.Select(r =>
        {
            var (values, sub) = cells(r);
            return new Streaming.StreamRowData(r.Rank, r.Name, r.ClassName, r.FillPercent / 100.0, _source?.Entities.IsLocalPlayer(r.ObjectId) == true, values, sub);
        }).ToList();
        return Streaming.StreamSnapshot.ToJson(label, headers, showNames ? rows : Streaming.StreamSnapshot.HideNames(rows), BuildStreamBoss(), 8);
    }

    private Streaming.StreamBoss? BuildStreamBoss()
    {
        if (_pvpOnly || _selectedTargetId is not int targetId
            || _source?.Entities is not Aion2.Aion2EntityDirectory directory || directory.IsKnownPlayer(targetId))
        {
            return null;
        }

        var onTarget = _lastShownHits.Where(ev => ev.TargetObjectId == targetId).ToList();
        long highest = directory.HitPoints.HighestSeen(targetId) ?? 0;
        if (onTarget.Count == 0 || highest <= 0)
        {
            return null;
        }

        var samples = directory.HitPoints.SamplesAround(targetId, onTarget.Min(h => h.Timestamp), onTarget.Max(h => h.Timestamp));
        if (samples.Count == 0)
        {
            return null;
        }

        long hp = samples[^1].Hp;
        double percent = 100.0 * hp / highest;
        string name = _targetNames.TryGetValue(targetId, out string? known) ? known : ResolveDisplayName(targetId);
        return new Streaming.StreamBoss(name, percent, $"{PlayerRow.Compact(hp)} / {PlayerRow.Compact(highest)} · {percent.ToString("0", CultureInfo.InvariantCulture)}%");
    }

    /// <summary>The link for the browser source, on the clipboard.</summary>
    private void CopyStreamLink()
    {
        var loc = LocalizationManager.Instance;
        if (_streamServer?.Url is not string url)
        {
            ShowUploadStatus(loc["Stream.NotRunning"]);
            return;
        }

        try
        {
            Clipboard.SetText(url);
            ShowUploadStatus(loc["Stream.LinkCopied"]);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            ShowUploadStatus(url); // the clipboard was busy: the link itself is shown instead
        }
    }

    // ---- the replay map ----------------------------------------------------------------------------------------

    /// <summary>The directory the replay data is read from: the live capture, also while a stored session is on view (its ids are the live ids).</summary>
    private Aion2.Aion2EntityDirectory? ReplayDirectory => (_historyMode ? _liveSource : _source)?.Entities as Aion2.Aion2EntityDirectory;

    /// <summary>The combat analyzer for one player of a session: the same details as a double click in the meter, but on the hits of that session only,
    /// whatever the meter itself shows; skills, rotation, effects and the replay map.</summary>
    /// <summary>The deaths of a session with the revive: from the file it came from, else from the live capture.</summary>
    private IReadOnlyList<DeathRecord>? DeathsForSession(SessionInfo session)
    {
        if (session.Extras?.Deaths is { } stored)
        {
            return stored;
        }

        return _liveSource?.Entities is Aion2.Aion2EntityDirectory dir
            ? dir.Combat.DeathsIn(session.Start, session.End ?? DateTime.Now, id => session.Classes.ContainsKey(id) || dir.IsKnownPlayer(id))
            : null;
    }

    private UIElement? BuildSessionAnalyzer(SessionInfo session, SessionManagerWindow.PlayerRow row, SessionManagerWindow.PlayerMode mode, SessionManagerWindow.BossRow? boss)
    {
        if (session.Data.Events.Count == 0)
        {
            return null;
        }

        // The picked boss narrows everything to its attempt (its time, and in damage mode the hits on it); no boss: the whole stay.
        IReadOnlyList<DamageEvent> scoped = SessionManagerWindow.ScopedEvents(session, boss?.Attempt, mode);
        var all = scoped.Where(ev => !ev.IsHeal).ToList();
        string title = $"{boss?.Name ?? LocalizationManager.Instance["Sessions.Boss.Total"]}  \u203A  {row.Name}";
        Func<int, string?> nameOf = id => session.Names.GetValueOrDefault(id) ?? ResolveDisplayName(id);
        if (mode == SessionManagerWindow.PlayerMode.Taken)
        {
            // what hit this player in the session, one row per attacker and attack
            var hits = all.Where(ev => ev.TargetObjectId == row.Id && !session.Classes.ContainsKey(ev.SourceObjectId)).ToList();
            var deaths = (DeathsForSession(session) ?? Array.Empty<DeathRecord>()).Where(d => d.Entity == row.Id)
                .Select(d => new Death(d.At, d.Entity, hits.Where(h => h.Timestamp <= d.At && h.Timestamp >= d.At - Deaths.BlowWindow).Cast<DamageEvent?>().LastOrDefault(), d.RevivedAt)).ToList();
            return new PlayerDetailsView(title, row.ClassName, "", hits, nameOf, deaths: deaths, taken: true);
        }

        if (mode == SessionManagerWindow.PlayerMode.Heal)
        {
            var heals = scoped.Where(ev => ev.IsHeal).ToList();
            return new PlayerDetailsView(title, row.ClassName, "", heals.Where(ev => ev.SourceObjectId == row.Id).ToList(), nameOf, heals: true, fightEvents: heals);
        }

        var mine = scoped.Where(ev => ev.SourceObjectId == row.Id).ToList();
        DateTime from = boss?.Attempt?.Start ?? (all.Count > 0 ? all.Min(ev => ev.Timestamp) : session.Start);
        DateTime to = boss?.Attempt?.End ?? (all.Count > 0 ? all.Max(ev => ev.Timestamp) : session.End ?? session.Start);
        FightExtras? extras = session.Extras ?? ExtrasForSession(session);
        return new PlayerDetailsView(title, row.ClassName, "", mine, nameOf, fightEvents: all, extras: extras, playerId: row.Id, fightStart: from, fightEnd: to,
            replay: BuildSessionReplay(session, all, extras, from, to));
    }

    /// <summary>The replay of the picked boss (or the whole stay), shown by the button of the combat analyzer; null when there is no replay data.</summary>
    private ReplayData? BuildSessionReplay(SessionInfo session, List<DamageEvent> all, FightExtras? extras, DateTime from, DateTime to)
    {
        if (extras is null || all.Count == 0)
        {
            return null;
        }

        Func<int, string?> nameOf = id => session.Names.GetValueOrDefault(id) ?? ResolveDisplayName(id);
        bool IsPlayer(int id) => session.Classes.Count == 0 || session.Classes.ContainsKey(id);
        return ReplayBuilder.Build(SessionLabels.NameOf(session), extras, all, from, to,
            id => (nameOf(id) ?? "", session.Classes.GetValueOrDefault(id) ?? "", IsPlayer(id) && session.Classes.ContainsKey(id)), SkillRadius,
            id => Aion2.Protocol.Aion2SkillNames.Display(Aion2.Protocol.Aion2SkillNames.NameOf(id)));
    }

    /// <summary>The rotation, effect, stagger and replay data of a session, read from the live capture, for saving it with the file: the players of the session and the bosses.</summary>
    private FightExtras? ExtrasForSession(SessionInfo session)
    {
        if (_liveSource?.Entities is not Aion2.Aion2EntityDirectory dir)
        {
            return null;
        }

        FightExtras extras = dir.Combat.Slice(session.Start, session.End ?? DateTime.Now, id => dir.NameFor(id) ?? dir.MonsterNameFor(id),
            id => session.Classes.ContainsKey(id) || dir.IsKnownPlayer(id) || dir.BossNpcIdOf(id) is not null,
            (id, from, to) => HpSeriesOf(dir, id, from, to, dir.IsKnownPlayer(id)));
        return extras.IsEmpty ? null : extras;
    }

    /// <summary>An id of the live capture as the grid knows it (the same, except while a session is on view).</summary>
    private int GridId(int liveId) => _historyMode ? _historyReplayIds.GetValueOrDefault(liveId, -1) : liveId;

    /// <summary>Whose positions the replay keeps: the group (the players in the rows) and the bosses.</summary>
    private bool ReplayKeeps(int id)
    {
        int gridId = GridId(id);
        return _rowsByObjectId.ContainsKey(gridId) && IsPlayerName(gridId)
            || ReplayDirectory?.BossNpcIdOf(id) is not null
            || _selectedTargetId == gridId;
    }

    /// <summary>A boss's health over the fight as thousandths of its highest reading, one reading a second at most.</summary>
    private HpSeries? ReplayHp(int id, DateTime from, DateTime to) =>
        ReplayDirectory is { } directory ? HpSeriesOf(directory, id, from, to, IsPlayerName(GridId(id))) : null;

    private static HpSeries? HpSeriesOf(Aion2.Aion2EntityDirectory directory, int id, DateTime from, DateTime to, bool isPlayer)
    {
        if (isPlayer || (directory.HitPoints.HighestSeen(id) ?? 0) <= 0)
        {
            return null;
        }

        long highest = directory.HitPoints.HighestSeen(id)!.Value;
        var samples = directory.HitPoints.SamplesAround(id, from, to);
        var thinned = new List<HpSample>();
        foreach (HpSample sample in samples)
        {
            if (thinned.Count == 0 || (sample.At - thinned[^1].At).TotalSeconds >= 1)
            {
                thinned.Add(sample);
            }
        }

        if (thinned.Count < 2)
        {
            return null;
        }

        DateTime origin = thinned[0].At;
        return new HpSeries(id, origin, thinned.Select(s => (int)(s.At - origin).TotalMilliseconds).ToArray(),
            thinned.Select(s => (short)Math.Clamp(s.Hp * 1000 / highest, 0, 1000)).ToArray());
    }

    /// <summary>The skills learnt so far, also while a stored fight is shown (that has no live directory): from the file the learning is kept in.</summary>
    private Aion2.Aion2CombatState? _lessonFile;

    private double? SkillRadius(int skillId)
    {
        if (ReplayDirectory?.Combat.LessonOf(skillId)?.RadiusCm is int live)
        {
            return live;
        }

        if (_lessonFile is null)
        {
            _lessonFile = new Aion2.Aion2CombatState { FilePath = System.IO.Path.Combine(MeterSettings.DataDirectory, "cast-skills.json") };
            _lessonFile.Load();
        }

        return _lessonFile.LessonOf(skillId)?.RadiusCm;
    }

    private ReplayData BuildReplayData(FightExtras? extras, DateTime? start, DateTime? end, IReadOnlyList<DamageEvent> events)
    {
        if (extras is null || start is not DateTime from || end is not DateTime to)
        {
            return ReplayBuilder.Build("", null, events, DateTime.Now, DateTime.Now, _ => ("", "", false), _ => null, _ => "");
        }

        string title = (_selectedTargetId is int target ? ResolveDisplayName(target) : "") + " " + from.ToString("g");
        if (_historyMode)
        {
            // the extras speak in ids of the live capture, the shown session in replay ids
            events = events.Select(ev => ev with
            {
                SourceObjectId = _historyLiveIds.GetValueOrDefault(ev.SourceObjectId, ev.SourceObjectId),
                TargetObjectId = _historyLiveIds.GetValueOrDefault(ev.TargetObjectId, ev.TargetObjectId),
            }).ToList();
        }

        return ReplayBuilder.Build(title.Trim(), extras, events, from, to,
            id => (ResolveDisplayName(GridId(id)), ResolveClassName(GridId(id)), IsPlayerName(GridId(id))), SkillRadius, id => Aion2.Protocol.Aion2SkillNames.Display(Aion2.Protocol.Aion2SkillNames.NameOf(id)));
    }

    /// <summary>
    /// Builds the upload payload for whichever target <paramref name="targetId"/> refers to, from
    /// <see cref="_rows"/> as it stands right now -- so the caller must have already pointed
    /// <see cref="_selectedTargetId"/> at this target and called <see cref="RefreshRows"/>, the same
    /// way the grid itself gets Name/ClassName/Faction resolved (see ApplyIdentity/ApplySide). This
    /// deliberately reuses that already-correct state rather than re-deriving faction/class logic a
    /// second time here. Returns null when there is nothing to upload (target never hit, or no
    /// player rows survived the "players only" filter).
    ///
    /// Scoped to one specific numbered run when <see cref="_selectedRunWindowStart"/>/
    /// <see cref="_selectedRunWindowEnd"/> are set (the dropdown's "#N" entries - see MobBossTag's
    /// own remarks), otherwise spans the target's whole history exactly as before.
    /// </summary>
    private EncounterUploadRequest? BuildEncounterUpload(int targetId, string serverFingerprint, string? serverName)
    {
        // The payload's participants come from the rows on screen, which in heal or taken mode are
        // the healers or the damaged - build it from the damage rows, then put the mode back.
        MeterMode mode = _mode;
        if (mode == MeterMode.Damage)
        {
            return BuildEncounterUploadCore(targetId, serverFingerprint, serverName);
        }

        _mode = MeterMode.Damage;
        RefreshRows();
        try
        {
            return BuildEncounterUploadCore(targetId, serverFingerprint, serverName);
        }
        finally
        {
            _mode = mode;
            RefreshRows();
        }
    }

    private EncounterUploadRequest? BuildEncounterUploadCore(int targetId, string serverFingerprint, string? serverName)
    {
        var allTargetHits = _aggregator.Events.Where(e => e.TargetObjectId == targetId && !e.IsHeal);
        var targetHits = _selectedRunWindowStart is DateTime runStart
            ? allTargetHits.Where(e => e.Timestamp >= runStart && e.Timestamp <= _selectedRunWindowEnd).ToList()
            : allTargetHits.ToList();
        if (targetHits.Count == 0)
        {
            return null;
        }

        // A boss keeps its entity id across wipes and retries, so a whole-history upload would merge
        // every attempt into one fight. The server's hit points tell them apart: each time the boss
        // came back to full health starts a new attempt, and only the last one - the one that ended
        // the fight - is uploaded (a Draupnir run with two wipes before the kill, 2026-10-03).
        if (_selectedRunWindowStart is null
            && (_source?.Entities as Aion2.Aion2EntityDirectory)?.HitPoints.ResetsOf(targetId) is { Count: > 0 } resets)
        {
            DateTime lastAttempt = resets[^1];
            var lastAttemptHits = targetHits.Where(e => e.Timestamp >= lastAttempt).ToList();
            if (lastAttemptHits.Count > 0)
            {
                targetHits = lastAttemptHits;
            }
        }

        // Only a whole fight is uploaded (the server rejects anything else as "boss_not_killed"): the
        // boss must have been seen dying, and the damage dealt has to cover its hit points - else two
        // failed attempts would pass as one kill. The hit-point frames are the only fight-end signal
        // the protocol decodes so far.
        if (_source?.Entities is Aion2EntityDirectory directory && BossNotKilledReason(directory, targetId, targetHits) is { } notKilled)
        {
            AutoUploadLog.Write($"boss {targetId}: not uploaded - {notKilled}");
            return null;
        }

        // Kept separate from the UTC startedAt/endedAt below (those are for the outgoing payload's
        // own fields): heals never target the boss, so they can only be scoped to this encounter by
        // time window, and that window has to be compared against DamageEvent.Timestamp's own
        // (local) DateTimeKind, not a UTC-converted copy.
        DateTime windowStart = targetHits.Min(e => e.Timestamp);
        DateTime windowEnd = targetHits.Max(e => e.Timestamp);
        return BuildEncounterUpload(targetId, serverFingerprint, serverName, targetHits, windowStart, windowEnd);
    }

    /// <summary>
    /// Fills in the damage the group shields absorbed per player (see <see cref="ShieldSkills"/>), over the same
    /// window and from the same attacker as <c>DamageTaken</c>, so the website can draw the taken-damage bar
    /// part red (got through) and part blue (absorbed). The shield names no caster, so nobody is credited with it.
    /// </summary>
    private List<ParticipantUpload> AttachAbsorbed(List<ParticipantUpload> participants, int attackerId, DateTime windowStart, DateTime windowEnd)
    {
        var byObject = _rows.GroupBy(r => r.ObjectId).ToDictionary(g => g.Key, g => g.First().Name);
        var absorbed = new Dictionary<string, long>();
        foreach (DamageEvent shield in _aggregator.Shields)
        {
            if (!ShieldSkills.IsAbsorb(shield) || shield.SourceObjectId != attackerId
                || shield.Timestamp < windowStart || shield.Timestamp > windowEnd
                || !byObject.TryGetValue(shield.TargetObjectId, out string? name))
            {
                continue;
            }

            absorbed[name] = absorbed.GetValueOrDefault(name) + shield.Amount;
        }

        return absorbed.Count == 0
            ? participants
            : participants.Select(p => absorbed.TryGetValue(p.Name, out long amount) ? p with { DamageAbsorbed = amount } : p).ToList();
    }

    /// <summary>The backend's ceiling for the players of one upload (uploadSchema.ts). Not applied to a world boss: the whole map is on it, and a cut would drop the players who matter for the damage total.</summary>
    private const int MaxUploadParticipants = 300;

    /// <summary>Whether the target is a world boss of the field maps (see Aion2HitAverages.IsEstimatedBoss): proven by its NPC id from the game's own traffic.</summary>
    private bool IsWorldBossTarget(int targetId) =>
        _source?.Entities is Aion2.Aion2EntityDirectory directory && directory.BossNpcIdOf(targetId) is int npc && Aion2.Protocol.Aion2HitAverages.IsEstimatedBoss(npc);

    /// <summary>How many of the players who hit the target have no name or class yet (their "appeared" or cast frames have not been read). Such rows are left out of the upload.</summary>
    private int UnresolvedAttackers(int targetId)
    {
        if (_source?.Entities is not Aion2.Aion2EntityDirectory directory)
        {
            return 0;
        }

        var unresolved = new HashSet<int>();
        var seen = new HashSet<int>();
        foreach (DamageEvent e in _aggregator.Events)
        {
            if (e.TargetObjectId != targetId || e.IsHeal || !seen.Add(e.SourceObjectId))
            {
                continue;
            }

            string? name = directory.NameFor(e.SourceObjectId);
            if (name is null || name.StartsWith("Player #", StringComparison.Ordinal) || name.StartsWith("0x", StringComparison.Ordinal) || !ClassCatalog.IsKnownClass(directory.ClassOf(e.SourceObjectId) ?? ""))
            {
                unresolved.Add(e.SourceObjectId);
            }
        }

        return unresolved.Count;
    }

    /// <summary>
    /// A world boss is uploaded only when the data is complete: the players who hit it all have a name and a class (else the upload leaves them out, as happened
    /// with 5 of 1000 players). Waits while that number goes down; gives up when it stands still for 30 s or after 3 minutes.
    /// </summary>
    private async Task WaitForCompleteWorldBossAsync(int targetId)
    {
        DateTime start = DateTime.UtcNow, lastProgress = start;
        int best = int.MaxValue;
        while (true)
        {
            int open = UnresolvedAttackers(targetId);
            if (open < best)
            {
                best = open;
                lastProgress = DateTime.UtcNow;
            }

            if (open == 0 || DateTime.UtcNow - lastProgress >= TimeSpan.FromSeconds(30) || DateTime.UtcNow - start >= TimeSpan.FromMinutes(3))
            {
                AutoUploadLog.Write($"boss {targetId}: world boss data {(open == 0 ? "complete" : $"as complete as it gets ({open} players without name or class)")} after {(DateTime.UtcNow - start).TotalSeconds:N0} s");
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

    /// <summary>
    /// Same payload construction, but with the target's hits and the time window supplied by the
    /// caller instead of always spanning that target id's ENTIRE history. Needed because
    /// a boss farmed multiple times in one session can keep the same target id across every
    /// kill - a headless clustered upload (see RunHeadlessClusteredUploadAsync) has to pass in one kill-cluster's hits/window at
    /// a time so each real fight gets its own upload instead of one merged across the whole file.
    /// The 3-argument overload above is just this one, called with that target's full history.
    /// </summary>
    /// <summary>More rows than this (a crowded world boss) and the upload carries no buff table.</summary>
    private const int MaxBuffRows = 60;

    private EncounterUploadRequest? BuildEncounterUpload(
        int targetId, string serverFingerprint, string? serverName,
        IReadOnlyList<DamageEvent> targetHits, DateTime windowStart, DateTime windowEnd)
    {
        if (targetHits.Count == 0 || _rows.Count == 0)
        {
            return null;
        }

        double durationSeconds = Math.Max((windowEnd - windowStart).TotalSeconds, 0.001);

        DateTime startedAt = windowStart.ToUniversalTime();
        DateTime endedAt = windowEnd.ToUniversalTime();
        string bossName = _targetNames.TryGetValue(targetId, out string? n) ? n : ResolveDisplayName(targetId);

        // Only a boss the game announced and the catalog knows may be uploaded.
        if (!IsKnownBoss(bossName, targetId))
        {
            return null;
        }

        // A world boss that was seen dying lost all its hit points: the estimated hits are scaled once to that (see EstimateRescale).
        if (targetHits.Any(e => e.Estimated) && _source?.Entities is Aion2EntityDirectory bossDirectory
            && bossDirectory.HitPoints.HighestSeen(targetId) is (> 0 and long bossLost) && bossDirectory.HitPoints.Latest(targetId) is { } bossLatest
            && BossDied(bossDirectory, bossDirectory.HitPoints, targetId, bossLatest)
            && EstimateRescale.FactorFor(targetHits, bossLost) is double rescale)
        {
            targetHits = EstimateRescale.Apply(targetHits, rescale);
            AutoUploadLog.Write($"boss {targetId}: estimated hits scaled by {rescale:N3} to the {bossLost:N0} hit points the boss lost");
        }

        var participants = new List<ParticipantUpload>();
        // Aion 2: the name only exists for players whose "appeared" frame the client saw, and the
        // class for those seen casting - an unnamed "Player #id" or a row without a class the backend
        // knows would make the backend reject the whole upload (class check) or store a nameless
        // player, so only complete rows go in. The own character is told by its name too: the game
        // gives a new object id on a map change, and the old one stops being "local".
        var aion2Directory = _source?.Entities as Aion2.Aion2EntityDirectory;
        string? ownName = aion2Directory?.LocalCharacter?.Name;
        // Profile and legion of the OTHER players only go along when the player agreed to it.
        bool shareOthers = MeterSettings.Load().UploadOtherPlayersProfiles;
        // The effects on every player over this fight, cut once (a crowded world boss has hundreds of rows: no buff table for it, the work would grow with them).
        var buffExtras = aion2Directory is not null && _rows.Count <= MaxBuffRows
            ? aion2Directory.Combat.Slice(windowStart, windowEnd, id => aion2Directory.NameFor(id))
            : null;
        // the boss's health over the fight, for the website's group timeline
        IReadOnlyList<int>? bossHp = buffExtras is not null ? TimelineBuilder.BossHp(buffExtras, targetId, windowStart, windowEnd) : null;

        // One pass over the events instead of one per player: a world boss has a thousand of them, and each rescan of the
        // whole history per row made building the upload take minutes.
        var hitsBySource = targetHits.Where(e => !e.IsHeal).GroupBy(e => e.SourceObjectId).ToDictionary(g => g.Key, g => g.ToList());
        var healsBySource = new Dictionary<int, List<DamageEvent>>();
        var takenByTarget = new Dictionary<int, long>();
        foreach (DamageEvent e in _aggregator.Events)
        {
            if (e.Timestamp < windowStart || e.Timestamp > windowEnd)
            {
                continue;
            }

            if (e.IsHeal)
            {
                if (!healsBySource.TryGetValue(e.SourceObjectId, out List<DamageEvent>? heals))
                {
                    healsBySource[e.SourceObjectId] = heals = new List<DamageEvent>();
                }

                heals.Add(e);
            }
            else if (e.SourceObjectId == targetId)
            {
                takenByTarget[e.TargetObjectId] = takenByTarget.GetValueOrDefault(e.TargetObjectId) + e.Amount;
            }
        }

        var emptyEvents = new List<DamageEvent>();
        var targetSpan = targetHits.Where(e => !e.IsHeal).ToList();
        double fightSeconds = targetSpan.Count == 0 ? 0 : (targetSpan.Max(e => e.Timestamp) - targetSpan.Min(e => e.Timestamp)).TotalSeconds;

        // More players than the upload takes (a world boss): the biggest by damage and the own character are worked out in full, nobody else.
        IEnumerable<PlayerRow> uploadRows = _rows;
        if (_rows.Count > MaxUploadParticipants && !IsWorldBossTarget(targetId))
        {
            uploadRows = _rows.Where(r => _source?.Entities.IsLocalPlayer(r.ObjectId) == true || (ownName is not null && r.Name == ownName))
                .Concat(_rows.Where(r => !(_source?.Entities.IsLocalPlayer(r.ObjectId) == true || (ownName is not null && r.Name == ownName)))
                    .OrderByDescending(r => hitsBySource.TryGetValue(r.ObjectId, out List<DamageEvent>? h) ? h.Sum(e => e.Amount) : 0)
                    .Take(MaxUploadParticipants))
                .ToList();
        }

        foreach (PlayerRow row in uploadRows)
        {
            if (aion2Directory is not null
                && (row.Name.StartsWith("Player #", StringComparison.Ordinal)
                    || row.Name.StartsWith("0x", StringComparison.Ordinal)
                    || !ClassCatalog.IsKnownClass(row.ClassName)))
            {
                continue;
            }

            List<DamageEvent> hitsOnBoss = hitsBySource.GetValueOrDefault(row.ObjectId) ?? emptyEvents;
            // Per the user: heals must be uploaded alongside damage - a pure healer who never hit
            // the boss would otherwise be silently dropped from the roster entirely, so a row
            // survives on EITHER contribution, not damage alone.
            List<DamageEvent> healsBySelf = healsBySource.GetValueOrDefault(row.ObjectId) ?? emptyEvents;
            // Per the user: a group member (row.IsEnemy false - see ApplySide/FactionResolver)
            // must always appear in the roster, even at 0, not just when they happen to land a
            // hit or heal inside this exact fight's tight damage-derived time window - found from
            // a real healer who buffed/healed the group only before this window started and
            // landed no heal inside it, so she was silently missing from the whole roster. An
            // actual enemy (or anyone whose side is still unresolved either way) with no
            // contribution to this target is still skipped - only a known ally survives on
            // presence alone.
            if (hitsOnBoss.Count == 0 && healsBySelf.Count == 0 && row.IsEnemy)
            {
                continue;
            }

            // Aion 2 has no side to tell allies from bystanders, so "an ally survives on presence
            // alone" would put every player standing nearby into the roster - and the backend takes
            // at most 24. A player counts when they hit or healed in this fight or took hits from the
            // boss; the own character always does.
            if (aion2Directory is not null && hitsOnBoss.Count == 0 && healsBySelf.Count == 0
                && !(ownName is not null && row.Name == ownName)
                && !takenByTarget.ContainsKey(row.ObjectId))
            {
                continue;
            }

            bool isSelf = _source?.Entities.IsLocalPlayer(row.ObjectId) == true || (ownName is not null && row.Name == ownName);
            // targetHits, not _aggregator.Events: TargetIDps derives its own duration from
            // whichever events it's given, so passing the full history back in here would silently
            // widen a clustered upload's iDPS window back out to the target id's entire history -
            // targetHits is already scoped to exactly this fight (see the two BuildEncounterUpload
            // overloads above).
            double idps = fightSeconds > 0 && hitsOnBoss.Count > 0 ? hitsOnBoss.Sum(e => e.Amount) / fightSeconds : 0; // as DpsCalculator.TargetIDps, with the fight's length worked out once
            // Most of the damage is a guess (a crowded world boss, see DamageEvent.Estimated): the row says so, and its crit hits are not known.
            bool estimated = hitsOnBoss.Count > 0 && hitsOnBoss.Count(e => e.Estimated) * 2 > hitsOnBoss.Count;
            var skills = SkillBreakdown.For(hitsOnBoss)
                .Select(s => new SkillUsageUpload(s.Skill, s.Hits, estimated ? 0 : s.CritHits, s.Total, s.Min, s.Max))
                .ToList();
            var healSkills = SkillBreakdown.For(healsBySelf, heals: true)
                .Select(s => new SkillUsageUpload(s.Skill, s.Hits, s.CritHits, s.Total, s.Min, s.Max))
                .ToList();

            long totalHealing = healsBySelf.Sum(e => e.Amount);
            double hps = totalHealing / durationSeconds;
            // NOT row.Damage: that field tracks whatever the Mob/Boss filter currently has
            // selected (see RefreshRows), so it silently went stale/wrong whenever an upload ran
            // while a different filter was active than when the fight itself happened. hitsOnBoss
            // is already independently scoped to exactly this targetId, the same data idps/skills
            // above are already computed from - a real upload bug (two uploads for the same fight
            // reporting wildly different totalDamage but identical, correct idps) traced back to
            // this exact line.
            long totalDamage = hitsOnBoss.Sum(e => e.Amount);

            // The opposite direction from hitsOnBoss above: what the BOSS did to this row, over the
            // same window - answers "who ate the boss's damage" (aggro/tank checks), which
            // totalDamage (dealt TO the boss) cannot. Windowed the same way healsBySelf is, not
            // targetHits-derived, since the boss is the SOURCE here, not the target.
            long damageTaken = takenByTarget.GetValueOrDefault(row.ObjectId);

            // The helpful effects that ran on this player (uptime, how often, who gave them): the website's buff columns.
            var buffs = buffExtras is not null && aion2Directory is not null
                ? BuffUpload.For(buffExtras, row.ObjectId, windowStart, windowEnd, id => aion2Directory.NameFor(id)).ToList()
                : new List<BuffUsageUpload>();

            participants.Add(new ParticipantUpload(
                row.Name, row.ClassName, row.Faction, isSelf,
                totalDamage, idps, idps, totalHealing, hps, skills, healSkills, damageTaken, buffs,
                shareOthers || isSelf ? aion2Directory?.GuildOf(ProfileIdOf(row), row.Name) : null,
                shareOthers || isSelf ? BuildProfileUpload(ProfileIdOf(row)) : null,
                ServerId: aion2Directory?.ServerIdOf(ProfileIdOf(row)),
                Estimated: estimated,
                CharacterId: CharacterIdFor(aion2Directory, row.Name, aion2Directory?.ServerIdOf(ProfileIdOf(row))),
                Timeline: buffExtras is not null && aion2Directory is not null
                    ? TimelineBuilder.For(buffExtras, row.ObjectId, windowStart, windowEnd, id => aion2Directory.NameFor(id))
                    : null));
        }

        // The backend takes at most 300 players of an instance fight: the biggest by damage, the own character always. A world boss has no cut.
        if (participants.Count > MaxUploadParticipants && !IsWorldBossTarget(targetId))
        {
            participants = participants.Where(p => p.IsSelf).Concat(participants.Where(p => !p.IsSelf).OrderByDescending(p => p.TotalDamage))
                .Take(MaxUploadParticipants).ToList();
        }

        // an effect whose giver is nobody of this fight came from a monster: not a buff (the givers kept are the fight's players)
        var participantNames = participants.Select(p => p.Name).ToHashSet();
        participants = participants.Select(p => p with { Buffs = BuffUpload.KeepGroupSources(p.Buffs ?? Array.Empty<BuffUsageUpload>(), participantNames) }).ToList();

        participants = AttachAbsorbed(participants, targetId, windowStart, windowEnd);

        // Two rows of the own character (the object id changed during the run) would both be "self";
        // the backend wants exactly one, and merges same-named participants itself afterwards.
        if (participants.Count(p => p.IsSelf) > 1)
        {
            ParticipantUpload keep = participants.Where(p => p.IsSelf).MaxBy(p => p.TotalDamage)!;
            participants = participants.Select(p => p.IsSelf && !ReferenceEquals(p, keep) ? p with { IsSelf = false } : p).ToList();
        }

        // The backend requires exactly one isSelf participant per upload (see uploadSchema.ts) -
        // always true for a fight the local player took part in, but a target only a group member
        // hit (e.g. someone else's solo Training Dummy check) still gets tracked here since combat
        // log lines for the whole group are tracked in the same session. Uploading it anyway would
        // just get rejected with a 400 every time - same "nothing to upload" signal as an empty
        // participants list.
        if (participants.Count == 0 || !participants.Any(p => p.IsSelf))
        {
            return null;
        }

        return new EncounterUploadRequest(
            AppVersion.Text, bossName, startedAt, endedAt, participants, serverFingerprint, serverName,
            Game: "aion2",
            BossNpcId: BossNpcIdOf(targetId),
            BossMaxHp: BossMaxHpOf(targetId),
            ServerId: Aion2OwnServerId(),
            GameServer: Aion2GameServerAddress(),
            BossLevel: (_source?.Entities as Aion2EntityDirectory)?.NpcLevelOf(targetId),
            BossHp: bossHp);
    }

    /// <summary>Hides the Class dropdown's entries that are no Aion 2 class (the static XAML list is
    /// wider than the game's roster); an unknown selection falls back to "All".</summary>
    /// <summary>The class names in the class filter, in the UI language as the game client spells them.</summary>
    private void LocalizeClassFilter()
    {
        string language = LocalizationManager.Instance.Language;
        foreach (ComboBoxItem item in ClassFilter.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string className && item.Content is StackPanel { Children: { Count: > 0 } children })
            {
                string shown = ClassCatalog.DisplayName(className, language);
                if (children.OfType<TextBlock>().FirstOrDefault() is { } text)
                {
                    text.Text = shown;
                }

                System.Windows.Automation.AutomationProperties.SetName(item, shown);
            }
        }
    }

    private void ApplyClassFilterAvailability()
    {
        LocalizeClassFilter();
        bool selectedClassHidden = false;
        foreach (ComboBoxItem item in ClassFilter.Items.OfType<ComboBoxItem>())
        {
            bool hide = item.Tag is string className && !ClassCatalog.IsKnownClass(className);
            item.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
            if (hide && ReferenceEquals(item, ClassFilter.SelectedItem))
            {
                selectedClassHidden = true;
            }
        }

        if (selectedClassHidden)
        {
            ClassFilter.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// The Aion 2 server the uploader plays on, from the game itself: the own character's record
    /// carries a server id (see <see cref="Aion2Servers"/>), which is a stable identity - unlike the
    /// game server's IP, which changed between two sessions of the same character. Null until the
    /// game has sent the record (log in or change map with the meter running).
    /// </summary>
    private (string Fingerprint, string? DisplayName)? ResolveServerIdentity() =>
        Aion2ServerName() is string name ? ("aion2:" + ServerSlug(name), name) : null;

    private static string ServerSlug(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    private int? Aion2OwnServerId() =>
        (_source?.Entities as Aion2EntityDirectory)?.LocalCharacter is { ServerId: > 0 } own ? own.ServerId : null;

    /// <summary>IP:port of the game server the capture is attached to ("193.202.112.97:13328"), the only
    /// thing that tells the regions apart - a server id is reused by every region.</summary>
    private string? Aion2GameServerAddress() =>
        (_source as Aion2.Aion2PacketCombatSource)?.ServerFingerprint is string f && f.StartsWith("aion2:") ? f["aion2:".Length..] : null;

    private string? Aion2ServerName() =>
        (_source?.Entities as Aion2EntityDirectory)?.LocalCharacter is { ServerId: > 0 } own ? Aion2Servers.NameOf(own.ServerId) : null;

    private const string ServerNotIdentified =
        "The game has not told the meter your Aion 2 server yet - log in (or change map) with the meter running, then try again. Upload refused rather than file your run under the wrong server.";

    /// <summary>Whether a target is a boss: its NPC id was announced by the game and is in the boss
    /// catalog (<see cref="Aion2BossCatalog"/>).</summary>
    private bool IsKnownBoss(string name, int targetId) =>
        (_source?.Entities as Aion2EntityDirectory)?.BossNpcIdOf(targetId) is not null;

    /// <summary>The NPC id of the boss behind a target, for the upload.</summary>
    private int? BossNpcIdOf(int targetId) =>
        (_source?.Entities as Aion2EntityDirectory)?.BossNpcIdOf(targetId);

    /// <summary>The highest hit-point reading the game sent for a boss, for the upload.</summary>
    private long? BossMaxHpOf(int targetId) =>
        (_source?.Entities as Aion2EntityDirectory)?.HitPoints.HighestSeen(targetId) is > 0 and long hp ? hp : null;

    /// <summary>For each boss target, the time of the last hit it took that already went online by
    /// the hotkey - so the next press sends only what is new (see <see cref="PendingBossTargets"/>).</summary>
    private readonly Dictionary<int, DateTime> _bossUploadedUntil = new();

    /// <summary>
    /// The boss targets with hits the hotkey has not sent yet, oldest first. A boss counts as new
    /// again when it took a hit after the last upload (a later attempt on the same entity). The
    /// selected target is always included, sent again on purpose when the user picked it.
    /// </summary>
    private List<int> PendingBossTargets()
    {
        var knownIds = _mobBossEntries.Select(entry => entry.TargetId).ToHashSet();
        var lastHit = new Dictionary<int, (DateTime First, DateTime Last)>();
        foreach (DamageEvent ev in _aggregator.Events)
        {
            if (ev.IsHeal || !knownIds.Contains(ev.TargetObjectId))
            {
                continue;
            }

            lastHit[ev.TargetObjectId] = lastHit.TryGetValue(ev.TargetObjectId, out var seen)
                ? (ev.Timestamp < seen.First ? ev.Timestamp : seen.First, ev.Timestamp > seen.Last ? ev.Timestamp : seen.Last)
                : (ev.Timestamp, ev.Timestamp);
        }

        var pending = lastHit
            .Where(kv => !_bossUploadedUntil.TryGetValue(kv.Key, out DateTime sent) || kv.Value.Last > sent)
            .Where(kv => BossFightFinished(kv.Key))
            .OrderBy(kv => kv.Value.First)
            .Select(kv => kv.Key)
            .ToList();
        if (_selectedTargetId is int selected && knownIds.Contains(selected) && !pending.Contains(selected) && BossFightFinished(selected))
        {
            pending.Add(selected);
        }

        return pending;
    }

    /// <summary>
    /// The upload hotkey / button: sends EVERY boss fight not sent yet, not just one. It used to send a
    /// single boss - the one picked in the Mob/Boss filter or else the one hit last - so a fight
    /// the filter was not pointing at (Thamon, the second boss of a Vakron Sky Island run, went missing
    /// from three runs on 2026-10-04) simply never went online. The server merges what the group's
    /// members send, so sending a fight twice is harmless; it is just not done without a new hit.
    /// </summary>
    private async void OnUploadCurrentBossClicked(object sender, RoutedEventArgs e)
    {
        AppMenu.IsSubmenuOpen = false;
        List<int> targetIds = PendingBossTargets();
        if (targetIds.Count == 0)
        {
            // No boss fight, but Aion 2 still has characters worth uploading (per the user).
            if (_source?.Entities is Aion2.Aion2EntityDirectory)
            {
                await UploadPlayersOnlyAsync();
                return;
            }

            ShowUploadStatus(_mobBossEntries.Count == 0 ? "No boss fights recorded yet."
                : _mobBossEntries.Any(entry => !BossFightFinished(entry.TargetId)) ? "Nothing to upload - the boss is still alive (a failed attempt is not uploaded)."
                : "Every boss fight is already uploaded.");
            return;
        }

        if (ResolveServerIdentity() is not (string fingerprint, var displayName))
        {
            ShowUploadStatus(ServerNotIdentified);
            return;
        }

        ShowUploadStatus(targetIds.Count == 1 ? "Uploading..." : $"Uploading {targetIds.Count} boss fights...");

        // The payload is built from the rows the grid shows for the selected boss, so the selection
        // is pointed at each boss in turn and put back afterwards (as in OnUploadLastRunClicked).
        int? previousTarget = _selectedTargetId;
        DateTime? previousRunWindowStart = _selectedRunWindowStart;
        DateTime? previousRunWindowEnd = _selectedRunWindowEnd;
        var uploadedNames = new List<string>();
        string? lastError = null;
        bool nothingRecorded = false;
        bool first = true;
        foreach (int targetId in targetIds)
        {
            if (!first)
            {
                await Task.Delay(100);
            }

            first = false;
            _selectedTargetId = targetId;
            _selectedRunWindowStart = null;
            _selectedRunWindowEnd = null;
            RefreshRows();
            var payload = BuildEncounterUpload(targetId, fingerprint, displayName);
            if (payload is null)
            {
                nothingRecorded = true;
                continue;
            }

            UploadResult result = await UploadClient.SendAsync(payload);
            if (result.Success)
            {
                uploadedNames.Add(payload.BossNpcName);
                _bossUploadedUntil[targetId] = _aggregator.Events
                    .Where(ev => !ev.IsHeal && ev.TargetObjectId == targetId)
                    .Max(ev => ev.Timestamp);
            }
            else
            {
                lastError = result.Error;
            }
        }

        _selectedTargetId = previousTarget;
        _selectedRunWindowStart = previousRunWindowStart;
        _selectedRunWindowEnd = previousRunWindowEnd;
        RefreshRows();

        if (uploadedNames.Count > 0)
        {
            ShowUploadStatus(uploadedNames.Count == 1
                ? "Uploaded."
                : $"Uploaded {uploadedNames.Count} boss fights: {string.Join(", ", uploadedNames)}.");
        }
        else
        {
            ShowUploadStatus(lastError is not null ? $"Upload failed: {lastError}" : nothingRecorded ? "Nothing recorded for this boss yet." : "Nothing to upload.");
        }
    }

    /// <summary>Aion 2 without a boss fight: uploads the own character and every other player whose
    /// name, class and equipment were read from the network, as profiles only (no encounter).</summary>
    private async Task UploadPlayersOnlyAsync()
    {
        if (ResolveServerIdentity() is not (string fingerprint, var displayName))
        {
            ShowUploadStatus(ServerNotIdentified);
            return;
        }

        var payload = BuildProfilesUpload(fingerprint, displayName);
        if (payload is null)
        {
            ShowUploadStatus("No character data to upload yet - log in once with the meter running.");
            return;
        }

        ShowUploadStatus($"Uploading {payload.Participants.Count} character(s)...");
        UploadResult result = await UploadClient.SendProfilesAsync(payload);
        ShowUploadStatus(result.Success ? $"Uploaded {payload.Participants.Count} character(s)." : $"Upload failed: {result.Error}");
    }

    // ---- the own Aion 2 profile goes online by itself -------------------------------------------
    // Per the user: the player must be findable on the site as a profile, without any boss fight and
    // without a click. The game sends the character record (equipment, skills, Daevanion) at login and
    // at map changes, in a burst; once it has been quiet for a few seconds the own profile is uploaded -
    // only the own one (other players' profiles still go with a boss upload or the manual upload), and
    // only when it differs from what was last sent this session.

    private System.Windows.Threading.DispatcherTimer? _profileUploadTimer;
    private string? _lastProfileUploadHash;
    private bool _profileUploadHintShown;

    private void ScheduleOwnProfileUpload()
    {
        // Always runs (but never in a headless mode): besides the upload it keeps the "legion member" flag of the settings up to date.
        if (Headless)
        {
            return;
        }

        StartLoginDumpTimer();
        if (_profileUploadTimer is null)
        {
            _profileUploadTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _profileUploadTimer.Tick += async (_, _) =>
            {
                _profileUploadTimer.Stop();
                UpdateLoginShareEligibility();
                if (MeterSettings.Load().AutoUploadProfile)
                {
                    await UploadOwnProfileAsync();
                    await UploadInspectedAsync();
                }

            };
        }

        _profileUploadTimer.Stop();
        _profileUploadTimer.Start();
    }

    // ---- the login of a legion member (opt-in, hidden for everybody else) ------------------------
    // Members of Akatsuki on Kaisinel get a switch in Settings > General. With it on, the whole login traffic of the character (what the game server
    // sent while it logged in) goes once per login to the community server, which keeps it for a short time to find what ties the characters of one
    // player together. The switch is hidden, the capture is not even started and nothing is sent for anybody else - the server checks the legion again.

    private const string LoginShareLegion = "Akatsuki";
    private const int LoginShareServerId = 1304; // Europe - Kaisinel (Elyos)

    private bool IsLoginShareEligible() =>
        _source?.Entities is Aion2.Aion2EntityDirectory { LocalCharacter: { } own } directory
        && own.ServerId == LoginShareServerId
        && string.Equals((directory.GuildOf(own.CombatId, own.Name) ?? directory.AccountLegionOf(own.Name))?.Trim(), LoginShareLegion, StringComparison.OrdinalIgnoreCase);

    /// <summary>Remembers in the settings whether the logged-in character is in that legion (decides whether the settings show the switch).</summary>
    private void UpdateLoginShareEligibility()
    {
        if (_source?.Entities is not Aion2.Aion2EntityDirectory { LocalCharacter: not null })
        {
            return;
        }

        bool eligible = IsLoginShareEligible();
        var settings = MeterSettings.Load();
        if (settings.LoginShareEligible != eligible)
        {
            settings.LoginShareEligible = eligible;
            settings.Save();
        }
    }

    private System.Windows.Threading.DispatcherTimer? _loginDumpTimer;
    private bool _loginDumpBusy;

    /// <summary>Its own timer: the profile upload waits for a quiet moment, which an active player never gives it (stat frames keep coming).</summary>
    private void StartLoginDumpTimer()
    {
        if (_loginDumpTimer is not null)
        {
            return;
        }

        _loginDumpTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _loginDumpTimer.Tick += async (_, _) => await UploadLoginDumpAsync();
        _loginDumpTimer.Start();
    }

    private async Task UploadLoginDumpAsync()
    {
        if (_loginDumpBusy || !Aion2.Capture.LoginCapture.Pending)
        {
            return;
        }

        _loginDumpBusy = true;
        try
        {
            if (!MeterSettings.Load().ShareLoginWithLegion)
            {
                return;
            }

            Aion2.Capture.LoginCapture.Flush();
            while (Aion2.Capture.LoginCapture.Next() is { } item)
            {
                // Only for a character the login's character list put in that legion, on that server; anything else is dropped unsent.
                if (_source?.Entities is not Aion2.Aion2EntityDirectory { LocalCharacter: { } own } directory
                    || own.ServerId != LoginShareServerId
                    || !string.Equals(directory.AccountLegionOf(item.Name)?.Trim(), LoginShareLegion, StringComparison.OrdinalIgnoreCase))
                {
                    Aion2.Capture.LoginCapture.Done(item.Name);
                    continue;
                }

                var request = new LoginDumpUploadRequest(AppVersion.Text, item.Name, LoginShareLegion, own.ServerId, Aion2GameServerAddress(), Convert.ToBase64String(item.Gzip));
                UploadResult result = await UploadClient.SendLoginDumpAsync(request);
                AutoUploadLog.Write(result.Success ? $"login of {item.Name}: {item.Gzip.Length / 1024} KB sent to the legion analysis" : $"login of {item.Name}: not sent - {result.Error}");
                if (!result.Success)
                {
                    break; // tried again with the next tick
                }

                Aion2.Capture.LoginCapture.Done(item.Name);
            }
        }
        finally
        {
            _loginDumpBusy = false;
        }
    }

    // ---- a boss fight goes online by itself (opt-in) --------------------------------------------
    // A boss that died is uploaded a few seconds later, so the last hits and the kill frame have all
    // arrived. Same payload as the Upload button; the server merges what the group's members send.

    private readonly HashSet<int> _bossesToUpload = new();
    private System.Windows.Threading.DispatcherTimer? _bossUploadTimer;

    private void NoteBossKills(IReadOnlyList<KillEvent> kills)
    {
        NoteBossesToUpload(kills.Where(kill => !kill.VictimIsPlayer && BossNpcIdOf(kill.VictimObjectId) is not null).Select(kill => kill.VictimObjectId));
    }

    /// <summary>Bosses already queued after a defeat; a boss that is back above zero (a new attempt) may be queued again.</summary>
    private readonly HashSet<int> _defeatNoted = new();

    /// <summary>
    /// Aion 2 sends no kill frame, but the boss's hit points reach 0 when it dies (checked on a
    /// Fire Temple run, 2026-10-05: every boss's last reading is 0). A boss at 0 is queued for the
    /// automatic upload; one that wiped the group (back at full health) never is.
    /// </summary>
    private void NoteDefeatedBosses()
    {
        if (Headless || _historyMode || _source?.Entities is not Aion2.Aion2EntityDirectory directory)
        {
            return;
        }

        var defeated = new List<int>();
        DateTime now = DateTime.Now;
        foreach ((int entityId, _) in directory.KnownBosses())
        {
            bool quietLong = _bossActivity.TryGetValue(entityId, out (DateTime First, DateTime Last) activity)
                && now - activity.Last >= BossQuietBeforeUpload
                && activity.Last - activity.First >= BossMinFightLength;
            if (directory.HitPoints.Latest(entityId) is not { } hp)
            {
                // No hit-point reading ever came for this boss (some never send one): all that says
                // it is over is that nobody has hit it for a while after a real fight. Without this
                // such a boss was never uploaded by itself and had to be sent by hand.
                if (quietLong)
                {
                    AutoUploadLog.Write($"boss {entityId}: no HP reading, quiet for {BossQuietBeforeUpload.TotalSeconds:0} s after a fight");
                    defeated.Add(entityId);
                    _bossActivity.Remove(entityId);
                }

                continue;
            }

            if (hp.Hp > 0)
            {
                _defeatNoted.Remove(entityId);
                // The last reading before the kill can stop just short of 0 (the final frame is
                // lost or never sent): within 0.5 % and quiet for a few seconds counts as dead.
                if (hp.Hp <= (directory.HitPoints.HighestSeen(entityId) ?? 0) * 0.005
                    && activity.Last != default && now - activity.Last >= TimeSpan.FromSeconds(5) && _defeatNoted.Add(entityId))
                {
                    defeated.Add(entityId);
                }
            }
            else if (_defeatNoted.Add(entityId))
            {
                defeated.Add(entityId);
            }
        }

        NoteBossesToUpload(defeated);
    }

    /// <summary>First and latest hit on each known boss in the current stretch of fighting.</summary>
    private readonly Dictionary<int, (DateTime First, DateTime Last)> _bossActivity = new();

    private static readonly TimeSpan BossQuietBeforeUpload = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan BossMinFightLength = TimeSpan.FromSeconds(10);

    private void NoteBossActivity(IReadOnlyList<DamageEvent> events)
    {
        if (events.Count == 0 || _source?.Entities is not Aion2.Aion2EntityDirectory directory)
        {
            return;
        }

        foreach (DamageEvent e in events)
        {
            if (e.IsHeal || directory.BossNpcIdOf(e.TargetObjectId) is null)
            {
                continue;
            }

            _bossActivity[e.TargetObjectId] = _bossActivity.TryGetValue(e.TargetObjectId, out var a)
                // A pause this long means a new stretch (second kill of the same boss entity).
                && e.Timestamp - a.Last < BossQuietBeforeUpload + BossQuietBeforeUpload
                ? (a.First, e.Timestamp > a.Last ? e.Timestamp : a.Last)
                : (e.Timestamp, e.Timestamp);
        }
    }

    /// <summary>
    /// Whether a boss fight can be uploaded: the boss is dead (hit points 0, or within 0.5 % of it when
    /// the last frame was lost), or no hit-point reading exists to say otherwise. A boss that is still
    /// alive is a failed attempt - the group wiped or left - and goes nowhere.
    /// </summary>
    private bool BossFightFinished(int targetId)
    {
        if ((_source?.Entities as Aion2EntityDirectory)?.HitPoints is not { } hitPoints || hitPoints.Latest(targetId) is not { } latest)
        {
            return true;
        }

        return BossDied(_source!.Entities as Aion2EntityDirectory, hitPoints, targetId, latest);
    }

    /// <summary>The boss died: its last hit-point reading is 0 (or within 0.5 % of it), or the server removed it
    /// as dead (reason 3) - which covers a lost final reading. A boss that read 100 % again or vanished alive
    /// (reason 7, the instance was closed) did not die.</summary>
    private static bool BossDied(Aion2EntityDirectory? directory, Aion2HitPoints hitPoints, int targetId, HpSample latest)
    {
        if (latest.Hp <= (hitPoints.HighestSeen(targetId) ?? 0) * 0.005)
        {
            return true;
        }

        return directory?.RemovalOf(targetId) is { Reason: Aion2EntityDirectory.RemovedDead };
    }

    /// <summary>Same limit the server applies (constants.ts MIN_KILL_DAMAGE_SHARE).</summary>
    private const double MinKillDamageShare = 0.9;

    /// <summary>
    /// Why the fight does not count as a kill, or null when it does: the boss must have been seen dying
    /// (see <see cref="BossDied"/>) and the hits on it must add up to at least <see cref="MinKillDamageShare"/>
    /// of its highest reading. No reading at all counts as "not proven" - an empty run slipped through
    /// that way (Run 421).
    /// </summary>
    private static string? BossNotKilledReason(Aion2EntityDirectory directory, int targetId, List<DamageEvent> targetHits)
    {
        Aion2HitPoints hitPoints = directory.HitPoints;
        if (hitPoints.HighestSeen(targetId) is not (> 0 and long maxHp) || hitPoints.Latest(targetId) is not { } latest)
        {
            return "no hit-point reading of the boss";
        }

        if (!BossDied(directory, hitPoints, targetId, latest))
        {
            return $"the boss was not seen dying (last reading {latest.Hp} of {maxHp})";
        }

        if (targetHits.Any(e => e.Estimated))
        {
            // A crowded world boss: the damage of the others is estimated and cannot prove the kill by its sum (the server sends only the hits' notices).
            // The boss was seen dying above. The estimate never adds up to the whole, so the line is lower: 75 % of the hit points (constants.ts MIN_WORLD_BOSS_DAMAGE_SHARE).
            double estimatedDamage = targetHits.Sum(e => (double)e.Amount);
            return estimatedDamage >= maxHp * 0.75 ? null : $"world boss: damage {estimatedDamage:N0} covers less than 75 % of the boss's {maxHp:N0} hit points";
        }

        double damage = targetHits.Sum(e => (double)e.Amount);
        return damage >= maxHp * MinKillDamageShare ? null : $"damage {damage:N0} covers less than {MinKillDamageShare:P0} of the boss's {maxHp:N0} hit points";
    }

    private void NoteBossesToUpload(IEnumerable<int> bossEntityIds)
    {
        if (Headless || _historyMode || !MeterSettings.Load().AutoUploadBoss)
        {
            return;
        }

        bool added = false;
        foreach (int entityId in bossEntityIds)
        {
            if (_bossesToUpload.Add(entityId))
            {
                added = true;
                AutoUploadLog.Write($"boss {entityId} queued for the automatic upload");
            }
        }

        if (_bossesToUpload.Count == 0)
        {
            return;
        }

        if (_bossUploadTimer is null)
        {
            _bossUploadTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _bossUploadTimer.Tick += async (_, _) =>
            {
                _bossUploadTimer.Stop();
                await UploadKilledBossesAsync();
            };
        }

        // Restart the 4 s wait only when something new was queued. This runs on every poll tick;
        // restarting it on each of them (while a boss was waiting) pushed the upload back forever,
        // so the automatic upload fired only now and then.
        if (added || !_bossUploadTimer.IsEnabled)
        {
            _bossUploadTimer.Stop();
            _bossUploadTimer.Start();
        }
    }

    private async Task UploadKilledBossesAsync()
    {
        var targetIds = _bossesToUpload.ToList();
        _bossesToUpload.Clear();
        AutoUploadLog.Write($"automatic upload starts for {targetIds.Count} boss(es)");
        if (ResolveServerIdentity() is not (string fingerprint, var displayName))
        {
            AutoUploadLog.Write("server not identified - nothing sent");
            ShowUploadStatus(ServerNotIdentified);
            return;
        }

        // The payload is built from the rows the grid shows for the selected boss (as in
        // OnUploadLastRunClicked), so the selection is pointed at the dead boss for a moment and put
        // back afterwards.
        int? previousTarget = _selectedTargetId;
        DateTime? previousRunWindowStart = _selectedRunWindowStart;
        DateTime? previousRunWindowEnd = _selectedRunWindowEnd;
        int uploaded = 0;
        string? lastError = null;
        foreach (int targetId in targetIds)
        {
            if (IsWorldBossTarget(targetId))
            {
                await WaitForCompleteWorldBossAsync(targetId);
            }

            _selectedTargetId = targetId;
            _selectedRunWindowStart = null;
            _selectedRunWindowEnd = null;
            RefreshRows();
            var payload = BuildEncounterUpload(targetId, fingerprint, displayName);
            if (payload is null)
            {
                AutoUploadLog.Write($"boss {targetId}: no payload (no hits, unknown boss or no own character)");
                continue;
            }

            UploadResult result = await UploadClient.SendAsync(payload);
            AutoUploadLog.Write($"boss {targetId} '{payload.BossNpcName}': {(result.Success ? "uploaded" : "failed: " + result.Error)}");
            if (result.Success)
            {
                uploaded++;
            }
            else
            {
                lastError = result.Error;
            }
        }

        _selectedTargetId = previousTarget;
        _selectedRunWindowStart = previousRunWindowStart;
        _selectedRunWindowEnd = previousRunWindowEnd;
        RefreshRows();

        if (uploaded > 0)
        {
            ShowUploadStatus(uploaded == 1 ? "Boss fight uploaded." : $"{uploaded} boss fights uploaded.");
        }
        else if (lastError is not null)
        {
            ShowUploadStatus($"Upload failed: {lastError}");
        }
    }

    private async Task UploadOwnProfileAsync()
    {
        if (_source?.Entities is not Aion2.Aion2EntityDirectory { LocalCharacter: { Restored: false } own } directory
            || directory.LocalEquipment.Count == 0)
        {
            return;
        }

        if (ResolveServerIdentity() is not (string fingerprint, var displayName))
        {
            if (!_profileUploadHintShown)
            {
                _profileUploadHintShown = true;
                ShowUploadStatus(ServerNotIdentified);
            }

            return;
        }

        ProfilesUploadRequest? payload = BuildProfilesUpload(fingerprint, displayName, ownOnly: true);
        if (payload is null)
        {
            return;
        }

        string hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
            System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(payload.Participants))));
        if (hash == _lastProfileUploadHash)
        {
            return;
        }

        UploadResult result = await UploadClient.SendProfilesAsync(payload);
        if (result.Success)
        {
            _lastProfileUploadHash = hash;
            ShowUploadStatus($"Profile of {own.Name} uploaded ({displayName}).");
        }
        else
        {
            ShowUploadStatus($"Profile upload failed: {result.Error}");
        }
    }

    // ---- the character windows of other players go online by themselves ---------------------------
    // Per the user: a player whose character window was opened (an arena opponent, someone met on the road) is meant to be found on the site, and
    // having to remember a manual upload before the next restart lost him (one such player was lost with the crash that followed). Only with the
    // settings "profiles of other players" and "upload my profile" both on; every window goes once. One the community server would turn away
    // (no faction or server known yet) waits and is tried again with the next window or the next character record.

    private System.Windows.Threading.DispatcherTimer? _inspectedUploadTimer;
    private bool _inspectedUploadBusy;

    private void ScheduleInspectedUpload()
    {
        if (Headless)
        {
            return;
        }

        var settings = MeterSettings.Load();
        if (!settings.AutoUploadProfile || !settings.UploadOtherPlayersProfiles)
        {
            return;
        }

        if (_inspectedUploadTimer is null)
        {
            _inspectedUploadTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _inspectedUploadTimer.Tick += async (_, _) =>
            {
                _inspectedUploadTimer.Stop();
                await UploadInspectedAsync();
            };
        }

        _inspectedUploadTimer.Stop();
        _inspectedUploadTimer.Start();
    }

    private async Task UploadInspectedAsync()
    {
        var settings = MeterSettings.Load();
        if (_inspectedUploadBusy || !settings.AutoUploadProfile || !settings.UploadOtherPlayersProfiles
            || _source?.Entities is not Aion2.Aion2EntityDirectory { LocalCharacter: { Restored: false } } directory
            || directory.LocalEquipment.Count == 0
            || ResolveServerIdentity() is not (string fingerprint, var displayName))
        {
            return;
        }

        // The windows not yet sent (a newer window of the same name counts as new); older ones than a day are left alone, so the first start
        // after an update does not send what was looked at weeks ago.
        var pending = directory.InspectedPlayers()
            .Where(p => p.UploadedAt is null && p.ReceivedAt > DateTime.Now.AddHours(-24))
            .ToDictionary(p => p.Name, p => p.ReceivedAt, StringComparer.Ordinal);
        if (pending.Count == 0)
        {
            return;
        }

        ProfilesUploadRequest? request = BuildProfilesUpload(fingerprint, displayName, onlyNames: pending.Keys.ToHashSet(StringComparer.Ordinal));
        if (request is null)
        {
            return;
        }

        // The server turns away a player without faction or server; those stay pending.
        var ready = request.Participants.Where(p => p.IsSelf || (p.Faction.Length > 0 && p.ServerId is > 0)).ToList();
        var names = ready.Where(p => !p.IsSelf).Select(p => p.Name).ToList();
        if (names.Count == 0)
        {
            return;
        }

        _inspectedUploadBusy = true;
        try
        {
            UploadResult result = await UploadClient.SendProfilesAsync(request with { Participants = ready });
            if (result.Success)
            {
                foreach (string name in names)
                {
                    directory.MarkInspectedUploaded(name, pending[name]);
                }

                ShowUploadStatus($"Profile of {string.Join(", ", names)} uploaded.");
                AutoUploadLog.Write($"profile upload of looked-at players: {string.Join(", ", names)}");
            }
            else
            {
                AutoUploadLog.Write($"profile upload of looked-at players failed: {result.Error}");
            }
        }
        finally
        {
            _inspectedUploadBusy = false;
        }
    }

    private ProfilesUploadRequest? BuildProfilesUpload(string fingerprint, string? displayName, bool ownOnly = false, IReadOnlySet<string>? onlyNames = null)
    {
        if (_source?.Entities is not Aion2.Aion2EntityDirectory directory || directory.LocalCharacter is not { } local)
        {
            return null;
        }

        // Other players' profiles only go online when the player agreed to it (Settings / first-start wizard).
        ownOnly = ownOnly || !MeterSettings.Load().UploadOtherPlayersProfiles;
        var participants = new List<ProfileParticipantUpload>();
        void Add(int id, string name, string className, bool isSelf)
        {
            if (BuildProfileUpload(id) is { } profile)
            {
                participants.Add(new ProfileParticipantUpload(name, className, directory.FactionOf(id) ?? "", isSelf, directory.GuildOf(id, name), profile, directory.ServerIdOf(id), CharacterIdFor(directory, name, directory.ServerIdOf(id))));
            }
        }

        string localClass = directory.ClassOf(local.CombatId) ?? Aion2.Protocol.Aion2SkillNames.ClassFromCode(local.ClassCode) ?? "";
        if (localClass.Length == 0)
        {
            return null;
        }

        Add(local.CombatId, local.Name, localClass, true);

        // Character windows of other players the local player opened (see Aion2FrameDecoder.DecodeInspect):
        // level and the full equipment with enchants, richer than what the "appeared" frame shows, so they
        // win over the seen profile of the same name.
        var inspectedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var inspected in ownOnly ? Array.Empty<Aion2.Aion2InspectedPlayer>() : directory.InspectedPlayers())
        {
            string? inspectedClass = Aion2.Protocol.Aion2SkillNames.ClassFromCode(inspected.ClassCode);
            if (inspectedClass is null || inspected.Name == local.Name || (onlyNames is not null && !onlyNames.Contains(inspected.Name)))
            {
                continue;
            }

            inspectedNames.Add(inspected.Name);
            participants.Add(new ProfileParticipantUpload(
                inspected.Name,
                inspectedClass,
                directory.FactionOfName(inspected.Name) ?? inspected.Faction ?? "",
                false,
                inspected.Guild,
                new ProfileUpload(
                    "seen",
                    inspected.Level,
                    Aion2.Protocol.Aion2SkillNames.ClassIdOfCode(inspected.ClassCode),
                    Aion2.Protocol.Aion2SkillNames.ClassBitsOfCode(inspected.ClassCode),
                    inspected.Gear.Select(GearUpload).ToList(),
                    Array.Empty<ProfileSkillUpload>(),
                    Array.Empty<ProfileBoardUpload>(),
                    GearScore: inspected.GearScore > 0 ? inspected.GearScore : null,
                    Titles: inspected.Titles?.Select(t => new ProfileTitleUpload(t.Slot, t.TitleId)).ToList(),
                    Pets: inspected.Pets?.Select(k => new ProfilePetUpload(k.SpeciesId, k.Level, k.Kinds)).ToList(),
                    BoardCounts: inspected.BoardCounts?.Select(b => new ProfileBoardCountUpload(b.BoardId, b.Count)).ToList()),
                directory.ServerIdOfName(inspected.Name) ?? inspected.ServerId));
        }

        foreach (int id in ownOnly || onlyNames is not null ? Array.Empty<int>() : directory.SeenProfileIds())
        {
            string? name = directory.NameFor(id);
            string? className = directory.ClassOf(id)
                ?? (directory.SeenProfileOf(id) is { ClassId: int classId } ? Aion2.Protocol.Aion2SkillNames.ClassFromCode(classId * 4 + 1) : null);
            // Anonymous "Player #id" entries have no name to attach a profile to.
            if (id == local.CombatId || name is null || name.StartsWith("Player #") || className is null || inspectedNames.Contains(name))
            {
                continue;
            }

            Add(id, name, className, false);
        }

        // The other characters of the uploader's own account (the character list of the login): the basics only - class, level, faction, legion, server and
        // the character id the game's own search knows; the server fetches the rest from there. They are the uploader's own characters, so they go with the own profile.
        foreach (Aion2.AccountCharacter alt in onlyNames is not null ? Array.Empty<Aion2.AccountCharacter>() : directory.AccountCharacters())
        {
            if (alt.Name == local.Name || participants.Any(p => p.Name == alt.Name))
            {
                continue;
            }

            string? altClass = Aion2.Protocol.Aion2SkillNames.ClassFromCode(alt.ClassCode);
            if (altClass is null || alt.ServerId <= 0)
            {
                continue;
            }

            participants.Add(new ProfileParticipantUpload(
                alt.Name,
                altClass,
                alt.Faction == 2 ? "Asmodian" : "Elyos",
                false,
                alt.Legion,
                new ProfileUpload(
                    "seen",
                    alt.Level,
                    Aion2.Protocol.Aion2SkillNames.ClassIdOfCode(alt.ClassCode),
                    Aion2.Protocol.Aion2SkillNames.ClassBitsOfCode(alt.ClassCode),
                    Array.Empty<ProfileGearUpload>(),
                    Array.Empty<ProfileSkillUpload>(),
                    Array.Empty<ProfileBoardUpload>()),
                alt.ServerId,
                alt.CharacterId > 0 ? alt.CharacterId : null));
        }

        return participants.Count == 0
            ? null
            : new ProfilesUploadRequest(AppVersion.Text, fingerprint, displayName, participants.Take(60).ToList(), ServerId: Aion2OwnServerId(), GameServer: Aion2GameServerAddress());
    }

    /// <summary>
    /// Uploads every boss the Mob/Boss filter has accumulated since the last Clear, one request
    /// each - the server recognizes fights uploaded by different group members as the same run
    /// itself (see Backend/src/matching/merge.ts), so this does not need to know whether anyone
    /// else in the group already uploaded. Temporarily drives the same _selectedTargetId/RefreshRows
    /// state the filter dropdown itself uses for each boss in turn, then restores whatever the user
    /// had selected - visibly flipping the grid through each boss while it runs, which is expected
    /// for a menu action the user triggered on purpose (not a background operation).
    /// </summary>
    private async void OnUploadLastRunClicked(object sender, RoutedEventArgs e)
    {
        AppMenu.IsSubmenuOpen = false;
        int? previousTarget = _selectedTargetId;
        DateTime? previousRunWindowStart = _selectedRunWindowStart;
        DateTime? previousRunWindowEnd = _selectedRunWindowEnd;
        // The full, unfiltered set, not whatever the dropdown happens to be showing under an
        // active search right now (see _mobBossEntries's own remarks) - a stale search must never
        // silently shrink how many fights this uploads.
        var targetIds = _mobBossEntries.Select(entry => entry.TargetId).ToList();

        if (targetIds.Count == 0)
        {
            ShowUploadStatus("No boss fights recorded since the last Clear.");
            return;
        }

        if (ResolveServerIdentity() is not (string fingerprint, var displayName))
        {
            ShowUploadStatus(ServerNotIdentified);
            return;
        }

        ShowUploadStatus($"Uploading {targetIds.Count} boss fight(s)...");
        int uploaded = 0;
        string? lastError = null;
        bool first = true;
        foreach (int targetId in targetIds)
        {
            // A big replay can surface dozens of distinct bosses/mobs at once -
            // found live when a real batch this size tripped the backend's own upload rate limit
            // (see Backend/src/server.ts). A small gap between requests keeps even a very large
            // batch comfortably under it instead of firing every request back-to-back.
            if (!first)
            {
                await Task.Delay(100);
            }
            first = false;

            // Full history, not whatever specific run happened to be selected in the UI before
            // this ran - "upload every boss since Clear" means every kill of each one, combined.
            _selectedTargetId = targetId;
            _selectedRunWindowStart = null;
            _selectedRunWindowEnd = null;
            RefreshRows();
            var payload = BuildEncounterUpload(targetId, fingerprint, displayName);
            if (payload is null)
            {
                continue;
            }

            UploadResult result = await UploadClient.SendAsync(payload);
            if (result.Success)
            {
                uploaded++;
            }
            else
            {
                lastError = result.Error;
            }
        }

        _selectedTargetId = previousTarget;
        _selectedRunWindowStart = previousRunWindowStart;
        _selectedRunWindowEnd = previousRunWindowEnd;
        RefreshRows();

        ShowUploadStatus(uploaded > 0
            ? $"Uploaded {uploaded} of {targetIds.Count} boss fight(s)."
            : $"Upload failed: {lastError}");
    }

    private void ShowUploadStatus(string message)
    {
        UploadStatusText.Text = message;
        UploadStatusText.Visibility = Visibility.Visible;
    }

    private void OnClassFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        // Same ordering trap as OnMobBossFilterChanged -- ClassFilter's SelectedIndex="0" fires
        // this from inside InitializeComponent, before the rest of the tree exists.
        if (!IsInitialized)
        {
            return;
        }

        _selectedClassFilter = (ClassFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        RefreshRows();
    }

    private void OnLoadSessionClicked(object sender, RoutedEventArgs e)
    {
        AppMenu.IsSubmenuOpen = false;
        Directory.CreateDirectory(SessionFile.DefaultDirectory);
        var dialog = new Microsoft.Win32.OpenFileDialog
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
            SessionFile.LoadedSession session = SessionFile.Load(dialog.FileName);
            LoadSessionIntoMeter(session);
            ShowUploadStatus($"Loaded session from {session.SavedAt:g} ({session.Events.Count} event(s)).");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            ThemedMessageBox.Show(this, $"Could not load this session file.\n\n{ex.Message}", "Load Session",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Per the user: Load/Save Session are a portable, manual snapshot of the WHOLE
    /// current session (every damage/avoid/kill event, plus personal stat totals) - independent
    /// of the live capture, and deliberately separate from Fight History (FightStore), which auto-records
    /// individual finished fights on its own. Loot isn't included yet - see SessionFile's own
    /// remarks on why.</summary>
    private void OnSaveSessionClicked(object sender, RoutedEventArgs e)
    {
        AppMenu.IsSubmenuOpen = false;
        if (_aggregator.Events.Count == 0)
        {
            ShowUploadStatus("Nothing to save yet - no damage has been recorded this session.");
            return;
        }

        Directory.CreateDirectory(SessionFile.DefaultDirectory);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            InitialDirectory = SessionFile.DefaultDirectory,
            FileName = $"Session {DateTime.Now:yyyy-MM-dd HH-mm-ss}{SessionFile.Extension}",
            Filter = $"Aion DPS session (*{SessionFile.Extension})|*{SessionFile.Extension}",
            DefaultExt = SessionFile.Extension,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        Dictionary<int, string> names = CollectSessionNames();

        try
        {
            SessionFile.Save(dialog.FileName, _aggregator.Events, _avoids, _kills, names);
            ShowUploadStatus($"Session saved to {Path.GetFileName(dialog.FileName)}.");
        }
        catch (IOException ex)
        {
            ThemedMessageBox.Show(this, $"Could not save the session.\n\n{ex.Message}", "Save Session",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Every object id the events/avoids/kills can reference, resolved to a real name now while the
    /// live source can still answer it - a session file has no other way to carry identity.</summary>
    private Dictionary<int, string> CollectSessionNames()
    {
        var ids = new HashSet<int>();
        foreach (DamageEvent ev in _aggregator.Events)
        {
            ids.Add(ev.SourceObjectId);
            ids.Add(ev.TargetObjectId);
        }

        foreach (AvoidEvent av in _avoids)
        {
            ids.Add(av.SourceObjectId);
            ids.Add(av.TargetObjectId);
        }

        foreach (KillEvent k in _kills)
        {
            ids.Add(k.VictimObjectId);
            if (k.KillerObjectId is int killer)
            {
                ids.Add(killer);
            }
        }

        return ids.ToDictionary(id => id, ResolveDisplayName);
    }

    // ---- the session survives a restart of the client ---------------------------------------------
    // Per the user: on a restart (the update's "restart now", or App > Restart client) the running session
    // is written to a temp file and read back on the next start. Unlike a saved session file this goes into
    // the LIVE meter (the capture keeps running), and it carries what the directory had learned, since the
    // game does not announce players again who are already in view.

    private static string RestartSnapshotPath => Path.Combine(Path.GetTempPath(), "AionDPS", "restart-session.aiondps");

    /// <summary>A snapshot older than this is a leftover of some earlier run, not a restart.</summary>
    private static readonly TimeSpan RestartSnapshotMaxAge = TimeSpan.FromMinutes(15);

    private void SaveRestartSnapshot()
    {
        if (_historyMode || _aggregator.Events.Count == 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RestartSnapshotPath)!);
            SessionFile.Save(RestartSnapshotPath, _aggregator.Events, _avoids, _kills, CollectSessionNames(),
                (_source?.Entities as Aion2EntityDirectory)?.Snapshot());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A restart must never be blocked by a session that could not be kept.
        }
    }

    private void TryRestoreRestartSnapshot()
    {
        string path = RestartSnapshotPath;
        if (Headless || !File.Exists(path))
        {
            return;
        }

        try
        {
            SessionFile.LoadedSession session = SessionFile.Load(path);
            File.Delete(path);
            if (DateTime.Now - session.SavedAt > RestartSnapshotMaxAge || session.Events.Count == 0)
            {
                return;
            }

            if (session.Directory is { } directory && _source?.Entities is Aion2EntityDirectory live)
            {
                live.RestoreFrom(directory);
            }

            _avoids.AddRange(session.Avoids);
            _kills.AddRange(session.Kills);
            _aggregator.IngestEvents(session.Events);
            _sessions.Feed(new CombatBatch(session.Events, Array.Empty<AvoidEvent>(), Array.Empty<KillEvent>()));
            RefreshRows();
            ShowUploadStatus($"Resumed {session.Events.Count:N0} event(s) from before the restart.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            // Unreadable leftover: ignore it, the meter starts fresh.
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    /// <summary>App &gt; Restart client: keeps the session, starts a fresh process, ends this one.</summary>
    private void OnRestartClientClicked(object sender, RoutedEventArgs e)
    {
        AppMenu.IsSubmenuOpen = false;
        SaveRestartSnapshot();
        SaveWindowStateToSettings();
        string? exe = Environment.ProcessPath;
        if (exe is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        Application.Current.Shutdown();
    }

    /// <summary>Replays a loaded session the same way EnterHistoryMode replays one stored fight -
    /// a FakeCombatSource so the live capture doesn't interfere, object ids remapped through
    /// it by name so PlayerRow/aggregator identity works exactly like a live session's would.
    /// Restores avoids/kills too (EnterHistoryMode doesn't - FightStore's own single-fight replay
    /// never needed defense/PVP stats to survive a reload) and re-runs class detection from each
    /// event's own Skill field (OnSkillUsed) since a session file has no separate per-participant
    /// class table the way FightStore's SQLite schema does.</summary>
    private void LoadSessionIntoMeter(SessionFile.LoadedSession session)
    {
        _historyMode = true;

        var replay = new FakeCombatSource();
        var idMap = session.Names.ToDictionary(kv => kv.Key, kv => replay.Entities.GetOrAssignId(kv.Value));
        int Remap(int id) => idMap.GetValueOrDefault(id, id);

        ShowReplay(replay);
        ClearDamageData();

        var events = session.Events
            .Select(ev => ev with { SourceObjectId = Remap(ev.SourceObjectId), TargetObjectId = Remap(ev.TargetObjectId) })
            .ToList();
        _avoids.AddRange(session.Avoids.Select(a => a with { SourceObjectId = Remap(a.SourceObjectId), TargetObjectId = Remap(a.TargetObjectId) }));
        _kills.AddRange(session.Kills.Select(k => k with
        {
            KillerObjectId = k.KillerObjectId is int killerId ? Remap(killerId) : null,
            VictimObjectId = Remap(k.VictimObjectId),
        }));

        _aggregator.IngestEvents(events);

        HistoryBanner.Text = string.Format(LocalizationManager.Instance["Main.SessionBanner"], session.SavedAt.ToString("g"));
        HistoryBanner.Visibility = Visibility.Visible;
        RefreshRows();
        Activate();
    }

    private const int RecentSessionsShown = 5;

    /// <summary>Fills the Session menu each time it opens: "All", the stay the player is in, and the last few finished ones.</summary>
    private void OnSessionMenuOpened(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, SessionMenu))
        {
            return;
        }

        LocalizationManager loc = LocalizationManager.Instance;
        bool live = ViewingLive;
        SessionMenu.Items.Clear();
        SessionMenu.Items.Add(SessionMenuItem(loc["Sessions.All"], loc["Sessions.AllHint"], null, live && _sessionView == SessionView.All,
            () => ShowLiveViewFromMenu(SessionView.All)));
        if (_sessions.Current is { } now)
        {
            SessionMenu.Items.Add(SessionMenuItem(SessionLabels.NameOf(now), SessionLabels.TimeSpanOf(now), SessionLabels.ColorOf(now.Kind),
                live && _sessionView == SessionView.Current, () => ShowLiveViewFromMenu(SessionView.Current)));
        }

        SessionMenu.Items.Add(new Separator());
        SessionMenu.Items.Add(new MenuItem { Header = loc["Sessions.Recent"], IsEnabled = false });
        var recent = _sessions.Sessions.Where(session => !session.IsOpen).Reverse().Take(RecentSessionsShown).ToList();
        if (recent.Count == 0)
        {
            SessionMenu.Items.Add(new MenuItem { Header = loc["Sessions.None"], IsEnabled = false });
        }

        foreach (SessionInfo session in recent)
        {
            SessionInfo shown = session;
            SessionMenu.Items.Add(SessionMenuItem(SessionLabels.NameOf(session), SessionLabels.TimeSpanOf(session), SessionLabels.ColorOf(session.Kind),
                _historyMode && _viewedSessionNumber == session.Number, () => ShowPastSession(shown)));
        }

        SessionMenu.Items.Add(new Separator());
        var manager = new MenuItem { Header = loc["Sessions.OpenManager"] };
        manager.Click += (_, _) => OpenSessionManager();
        SessionMenu.Items.Add(manager);
        var folder = new MenuItem { Header = loc["Main.MenuApp.OpenSessionsFolder"] };
        folder.Click += OnOpenSessionsFolderClicked;
        SessionMenu.Items.Add(folder);
    }

    private DummyTestWindow? _dummyWindow;
    private ThemeEditorWindow? _themeEditorWindow;

    private void OnOpenThemeEditorClicked(object sender, RoutedEventArgs e)
    {
        if (_themeEditorWindow is not null)
        {
            _themeEditorWindow.Activate();
            return;
        }

        _themeEditorWindow = new ThemeEditorWindow(MeterSettings.Load()) { Owner = this };
        _themeEditorWindow.Saved += settings =>
        {
            var current = MeterSettings.Load();
            current.Theme = settings.Theme;
            current.Save();
            ApplySettingsEverywhere(current);
        };
        _themeEditorWindow.Closed += (_, _) => _themeEditorWindow = null;
        _themeEditorWindow.Show();
    }
    private ChecklistWindow? _checklistWindow;

    private void OnOpenChecklistClicked(object sender, RoutedEventArgs e)
    {
        if (_checklistWindow is not null)
        {
            _checklistWindow.Activate();
            return;
        }

        _checklistWindow = new ChecklistWindow { Owner = this };
        _checklistWindow.Closed += (_, _) => _checklistWindow = null;
        _checklistWindow.Show();
    }

    private AssistWindow? _assistWindow;

    private void OnOpenAssistClicked(object sender, RoutedEventArgs e)
    {
        if (_assistWindow is not null)
        {
            _assistWindow.Activate();
            return;
        }

        _assistWindow = new AssistWindow(() => _liveSource?.Entities as Aion2EntityDirectory) { Owner = this };
        _assistWindow.Closed += (_, _) => _assistWindow = null;
        _assistWindow.Show();
    }

    /// <summary>Tools menu: the dummy test window (one at a time).</summary>
    private void OnOpenDummyTestClicked(object sender, RoutedEventArgs e)
    {
        if (_dummy is null)
        {
            return;
        }

        if (_dummyWindow is not null)
        {
            _dummyWindow.Activate();
            return;
        }

        _dummyWindow = new DummyTestWindow(_dummy, ToggleAllOverlays) { Owner = this };
        _dummyWindow.Closed += (_, _) => _dummyWindow = null;
        _dummyWindow.Show();
    }

    private void OnOpenSessionManagerClicked(object sender, RoutedEventArgs e) => OpenSessionManager();

    private SessionManagerWindow? _sessionManager;

    private void OpenSessionManager()
    {
        if (_sessionManager is not null)
        {
            _sessionManager.Activate();
            return;
        }

        _sessionManager = new SessionManagerWindow(_sessions) { Owner = this };
        _sessionManager.ExtrasFor = ExtrasForSession;
        _sessionManager.AnalyzerFactory = BuildSessionAnalyzer;
        _sessionManager.DeathsFor = DeathsForSession;
        _sessionManager.Closed += (_, _) => _sessionManager = null;
        _sessionManager.Show();
    }

    private MenuItem SessionMenuItem(string name, string detail, System.Windows.Media.Color? dot, bool isChecked, Action click)
    {
        var grid = new Grid { MinWidth = 260 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (dot is System.Windows.Media.Color color)
        {
            grid.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        else
        {
            var all = new TextBlock { Text = "\u221E", VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(all);
        }

        var nameText = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(nameText, 1);
        grid.Children.Add(nameText);
        var detailText = new TextBlock { Text = detail, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.65 };
        Grid.SetColumn(detailText, 2);
        grid.Children.Add(detailText);

        var item = new MenuItem { Header = grid, IsChecked = isChecked };
        item.Click += (_, _) => click();
        return item;
    }

    private void ShowLiveViewFromMenu(SessionView view)
    {
        if (_historyMode)
        {
            _sessionView = view;
            ExitHistoryMode();
        }
        else
        {
            ShowLiveView(view);
        }
    }

    /// <summary>Puts a finished session into the grid. Like a stored fight it runs on a replay source (ids remapped by name), but the
    /// live capture keeps going and recording behind it; the banner is the way back.</summary>
    private void ShowPastSession(SessionInfo session)
    {
        _historyMode = true;
        _viewedSessionNumber = session.Number;

        var replay = new FakeCombatSource();
        var idMap = session.Names.ToDictionary(kv => kv.Key, kv => replay.Entities.GetOrAssignId(kv.Value));
        int Remap(int id)
        {
            if (!idMap.TryGetValue(id, out int mapped))
            {
                // No name was known for this id: a stand-in name keeps it apart from every real one.
                idMap[id] = mapped = replay.Entities.GetOrAssignId($"0x{id:X8}");
            }

            return mapped;
        }

        ShowReplay(replay);
        ClearDamageData();

        IEnumerable<DamageEvent> events = session.Data.Events.Concat(session.Data.Shields)
            .Select(ev => ev with { SourceObjectId = Remap(ev.SourceObjectId), TargetObjectId = Remap(ev.TargetObjectId) })
            .ToList();
        foreach ((int id, string className) in session.Classes)
        {
            if (session.Names.TryGetValue(id, out string? name))
            {
                _playerIdentities[Remap(id)] = (name, className, 0);
            }
        }

        _aggregator.IngestEvents(events);

        // two live ids with the same name share one replay id (the replay merges by name), so the way back keeps the first of them
        _historyLiveIds = new Dictionary<int, int>();
        foreach (var (live, mapped) in idMap)
        {
            _historyLiveIds.TryAdd(mapped, live);
        }

        _historyReplayIds = new Dictionary<int, int>(idMap);
        _historyExtras = session.Extras ?? ((_liveSource?.Entities as Aion2.Aion2EntityDirectory) is { } liveDir
            ? liveDir.Combat.Slice(session.Start, session.End ?? DateTime.Now, id => liveDir.NameFor(id), ReplayKeeps, ReplayHp)
            : null);

        HistoryBanner.Text = string.Format(LocalizationManager.Instance["Main.SessionViewBanner"], SessionLabels.NameOf(session), SessionLabels.TimeSpanOf(session));
        HistoryBanner.Visibility = Visibility.Visible;
        RefreshRows();
    }

    private void OnOpenSessionsFolderClicked(object sender, RoutedEventArgs e)
    {
        AppMenu.IsSubmenuOpen = false;
        Directory.CreateDirectory(SessionFile.DefaultDirectory);
        Process.Start(new ProcessStartInfo(SessionFile.DefaultDirectory) { UseShellExecute = true });
    }

    /// <summary>The tray icon with its right-click menu; there for as long as the meter runs (not in
    /// the headless test modes, which must not put anything on the user's screen).</summary>
    private void CreateTrayIcon()
    {
        if (Headless)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        try
        {
            _trayIcon = new TrayIcon(this,
                Path.Combine(AppContext.BaseDirectory, "assets", "app", "aiondps.ico"),
                "Aion DPS Meter " + AppVersion.Text,
                new (string?, Action?)[]
                {
                    (loc["Main.Tray.Show"], () => _trayIcon?.Restore()),
                    (loc["Main.Tray.ToggleUi"], SetHideUi),
                    (loc["Main.Tray.StreamLink"], CopyStreamLink),
                    (loc["Main.MenuApp.CheckForUpdates"], () => _ = RunUpdateCheck(announceResult: true)),
                    (null, null),
                    (loc["Main.Tray.Exit"], Close),
                });
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            // No icon file: the meter works without a tray icon.
        }
    }

    private void OnMinimizeToTrayClicked(object sender, RoutedEventArgs e)
    {
        AppMenu.IsSubmenuOpen = false;
        _trayIcon?.HideWindow();
    }

    /// <summary>
    /// Asks GitHub whether a newer release exists and, if so, downloads it in the background and
    /// says so in the status row. Nothing is swapped while the meter runs: Velopack stages the new
    /// version and it becomes active on the next start, so an update never interrupts a fight.
    ///
    /// <paramref name="announceResult"/> separates the two callers. The automatic checks (startup
    /// and the five-minute timer) pass false: they swallow every failure, because a machine that is
    /// offline, or a GitHub that is rate-limiting, would otherwise produce a message box on top of
    /// a boss fight every five minutes. The menu item passes true, because a check the user just
    /// asked for that silently does nothing is indistinguishable from "you are up to date", which
    /// is the one answer it must not fake.
    /// </summary>
    /// <summary>The server will not take uploads from this version: say so once, in the status row and the badge (no pop-up: it came at the worst moments). With "check for
    /// updates" on, the update is fetched now; with it off the meter does not talk to GitHub.</summary>
    private void OnClientOutdated(string requiredVersion)
    {
        var loc = LocalizationManager.Instance;
        ShowUploadStatus(loc["Upload.ClientOutdatedStatus"]);
        _clientOutdated = true;
        RefreshUpdateBadge();
        bool automatic = MeterSettings.Load().CheckForUpdates;
        if (automatic)
        {
            _ = RunUpdateCheck(announceResult: false);
        }
    }

    /// <summary>The server allows this version after all: take the warning back.</summary>
    private void OnClientAllowed()
    {
        _clientOutdated = false;
        UploadStatusText.Visibility = Visibility.Collapsed;
        RefreshUpdateBadge();
    }

    private bool _updateCheckRunning;

    /// <summary>One check at a time: the start-up check, the timer, the menu and a refused upload all come here, and two downloads of the same
    /// update at once would only fight over Velopack's staging folder. A call that arrives while one is running is dropped; the running one does the work.</summary>
    private async Task RunUpdateCheck(bool announceResult)
    {
        if (_updateCheckRunning)
        {
            return;
        }

        _updateCheckRunning = true;
        try
        {
            await RunUpdateCheckCore(announceResult);
        }
        finally
        {
            _updateCheckRunning = false;
        }
    }

    private async Task RefreshAnnouncementsBadge()
    {
        long? latest = await Announcements.AnnouncementsClient.GetLatestAsync();
        if (latest is null)
        {
            return;
        }

        // The first call comes from the constructor, before app.Run: there is no UI synchronization context yet, so the code after the await
        // runs on a pool thread and must not touch the window itself. The dispatcher takes the update and does it once the window runs.
        await Dispatcher.InvokeAsync(() =>
        {
            _latestAnnouncement = latest;
            var settings = MeterSettings.Load();
            if (settings.AnnouncementsSeen == 0)
            {
                // A fresh install (or the first run with this feature) starts from "everything seen": the badge means "something new since you last looked".
                settings.AnnouncementsSeen = latest.Value;
                settings.Save();
            }

            AnnouncementsNewBadge.Visibility = Announcements.AnnouncementsClient.IsNew(latest, settings.AnnouncementsSeen) ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    /// <summary>The newest campaign start the server named last; the click on the Twitch icon stores it as "seen".</summary>
    private long _latestTwitchDrops;

    /// <summary>Shows the Twitch icon while a Twitch Drops campaign runs, with "New" while its start is newer than the one seen at the last click.</summary>
    private async Task RefreshTwitchDropsBadge()
    {
        var status = await Announcements.TwitchDropsClient.GetStatusAsync();
        if (status is null)
        {
            return;
        }

        // Same as in RefreshAnnouncementsBadge: the first call is made before app.Run, so the window is only touched on its dispatcher.
        var (running, latest) = status.Value;
        await Dispatcher.InvokeAsync(() =>
        {
            _latestTwitchDrops = latest;
            TwitchDropsButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            TwitchDropsNewBadge.Visibility = Announcements.TwitchDropsClient.IsNew(running, latest, MeterSettings.Load().TwitchDropsSeen) ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private void OnTwitchDropsClicked(object sender, RoutedEventArgs e)
    {
        if (_latestTwitchDrops > 0)
        {
            var settings = MeterSettings.Load();
            settings.TwitchDropsSeen = _latestTwitchDrops;
            settings.Save();
        }

        TwitchDropsNewBadge.Visibility = Visibility.Collapsed;
        try
        {
            Process.Start(new ProcessStartInfo(Announcements.TwitchDropsClient.PageUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // no browser to open: nothing else to do
        }
    }

    private void OnAnnouncementsClicked(object sender, RoutedEventArgs e)
    {
        if (_latestAnnouncement is long latest)
        {
            var settings = MeterSettings.Load();
            settings.AnnouncementsSeen = latest;
            settings.Save();
        }

        AnnouncementsNewBadge.Visibility = Visibility.Collapsed;
        try
        {
            Process.Start(new ProcessStartInfo(Announcements.AnnouncementsClient.PageUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // no browser to open: nothing else to do
        }
    }

    private async Task RunUpdateCheckCore(bool announceResult)
    {
        // The automatic checks respect the setting; the menu item ignores it, since clicking it IS
        // the consent that setting stands in for.
        if (!announceResult && !MeterSettings.Load().CheckForUpdates)
        {
            return;
        }

        if (!UpdateService.CanUpdate)
        {
            if (announceResult)
            {
                ThemedMessageBox.Show(this,
                    "This copy was not installed by the updater, so it cannot update itself.\n\n" +
                    "That is normal for a build run straight from source or unzipped by hand. " +
                    "Installed copies update themselves silently.\n\n" +
                    UpdateDiagnostics(),
                    "Check for updates", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return;
        }

        VelopackUpdateInfo? update;
        try
        {
            update = await UpdateService.CheckAsync();
        }
        catch (Exception ex)
        {
            if (announceResult)
            {
                ThemedMessageBox.Show(this, $"Could not reach GitHub to check for updates.\n\n{ex.Message}",
                    "Check for updates", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        if (update is null)
        {
            UpdateNotice.Visibility = Visibility.Collapsed;
            SetUpdateStatus(null, null);
            if (announceResult)
            {
                ThemedMessageBox.Show(this, $"You are running the latest version ({AppVersion.Text}).",
                    "Check for updates", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return;
        }

        string version = update.TargetFullRelease.Version.ToString();

        // Downloading the same update again on every timer tick would re-fetch it twelve times an
        // hour for as long as the meter stays open. A newer one than the version waiting replaces
        // it: kept, the older one was what got installed, and the newest then took a second round
        // (0.9.34 -> 0.9.35 -> 0.9.36 on the StacysView fork, with 0.9.36 already out).
        if (_downloadedUpdate is { } staged && staged.TargetFullRelease.Version.CompareTo(update.TargetFullRelease.Version) >= 0)
        {
            if (announceResult)
            {
                OfferRestart(staged, staged.TargetFullRelease.Version.ToString());
            }

            return;
        }

        UpdateNotice.Text = $"Downloading {version}...";
        UpdateNotice.Visibility = Visibility.Visible;
        SetUpdateStatus(string.Format(LocalizationManager.Instance["Overlay.UpdateDownloading"], version), "↓ " + version);

        try
        {
            await UpdateService.DownloadAsync(update);
        }
        catch (Exception ex)
        {
            UpdateNotice.Visibility = Visibility.Collapsed;
            SetUpdateStatus(null, null);
            if (announceResult)
            {
                ThemedMessageBox.Show(this, $"The update could not be downloaded.\n\n{ex.Message}",
                    "Check for updates", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        _downloadedUpdate = update;
        UpdateNotice.Text = $"Update {version} ready - click to restart";
        SetUpdateStatus(string.Format(LocalizationManager.Instance["Overlay.UpdateReady"], version), "↑ " + version);

        if (announceResult)
        {
            OfferRestart(update, version);
        }
    }

    /// <summary>Where this copy runs and whether the updater sits next to it, so a "not installed by
    /// the updater" message says why (an installed copy keeps Update.exe one folder above its
    /// "current" folder).</summary>
    private static string UpdateDiagnostics()
    {
        string folder = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string? parent = Path.GetDirectoryName(folder);
        string updater = parent is null ? "" : Path.Combine(parent, "Update.exe");
        return $"Running from: {folder}\nUpdate.exe expected at: {updater}\n"
            + (File.Exists(updater) ? "Update.exe: found." : "Update.exe: MISSING.");
    }

    private void OnCheckForUpdatesClicked(object sender, RoutedEventArgs e)
    {
        AppMenu.IsSubmenuOpen = false;
        _ = RunUpdateCheck(announceResult: true);
    }

    private void OnUpdateNoticeClicked(object sender, MouseButtonEventArgs e)
    {
        if (_downloadedUpdate is { } update)
        {
            OfferRestart(update, update.TargetFullRelease.Version.ToString());
        }
    }

    /// <summary>
    /// The update is already on disk by this point; all that is left is a restart, which is quick
    /// and needs no installer and no elevation. Still asked rather than done: the meter is being
    /// watched during a fight, and deciding on its own to disappear and come back mid-boss is not
    /// its call to make. Declining costs nothing -- the staged version applies on the next normal
    /// start anyway.
    ///
    /// <para>
    /// Shown as an in-window, app-themed card (UpdateRestartOverlay) rather than MessageBox: this
    /// window draws its own chrome everywhere else (WindowStyle="None"), and a native OS dialog
    /// looked out of place glued onto it (per the user).
    /// </para>
    /// </summary>
    private void OfferRestart(VelopackUpdateInfo update, string version)
    {
        _pendingRestartUpdate = update;
        UpdateRestartText.Text = string.Format(
            LocalizationManager.Instance["Main.UpdateReady.Message"], version, AppVersion.Text);
        UpdateRestartOverlay.Visibility = Visibility.Visible;
    }

    private void OnUpdateRestartNowClicked(object sender, RoutedEventArgs e)
    {
        UpdateRestartOverlay.Visibility = Visibility.Collapsed;
        if (_pendingRestartUpdate is not { } update)
        {
            return;
        }

        _pendingRestartUpdate = null;

        // Window geometry and settings are saved in OnClosing, which ApplyAndRestart never reaches
        // because it ends the process itself -- so save first, then hand over.
        SaveWindowStateToSettings();
        SaveRestartSnapshot();

        UpdateService.ApplyAndRestart(update);
    }

    private void OnUpdateRestartLaterClicked(object sender, RoutedEventArgs e)
    {
        UpdateRestartOverlay.Visibility = Visibility.Collapsed;
        _pendingRestartUpdate = null;
    }

    /// <summary>Double-click on a player's row opens the same details as the context menu. Only a
    /// click on a row counts - not the scroll bar or the empty area below the rows.</summary>
    private void OnPlayersGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source is not null and not DataGridRow)
        {
            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        if (source is DataGridRow row)
        {
            PlayersGrid.SelectedItem = row.Item;
            OnShowPlayerDetailsClicked(sender, e);
        }
    }

    private void OnShowPlayerDetailsClicked(object sender, RoutedEventArgs e)
    {
        if (PlayersGrid.SelectedItem is PlayerRow row)
        {
            ShowPlayerDetails(row);
        }
    }

    private void ShowPlayerDetails(PlayerRow row)
    {
        // Taken mode: what hit this player over the fight on screen, and how they died.
        if (_takenMode && !_pvpOnly)
        {
            var hits = HostileHitsTaken(_takenSpan).Where(ev => ev.TargetObjectId == row.ObjectId).ToList();
            new PlayerDetailsWindow(row.Name, row.ClassName, row.Faction, hits,
                id => _source?.Entities.NameFor(id) ?? MonsterName(id), deaths: _deathsById.GetValueOrDefault(row.ObjectId), taken: true)
            {
                Owner = this,
            }.Show();
            return;
        }

        var mine = _aggregator.Events.Where(ev => ev.SourceObjectId == row.ObjectId).ToList();

        DateTime? fightStart = _aggregator.Events.Count > 0 ? _aggregator.Events.Min(ev => ev.Timestamp) : null;
        DateTime? fightEnd = _aggregator.Events.Count > 0 ? _aggregator.Events.Max(ev => ev.Timestamp) : null;
        FightExtras? extras = _historyMode
            ? _historyExtras
            : fightStart is DateTime from && fightEnd is DateTime to
                ? ReplayDirectory?.Combat.Slice(from, to, ResolveDisplayName, ReplayKeeps, ReplayHp)
                : null;
        new PlayerDetailsWindow(row.Name, row.ClassName, row.Faction, mine,
            id => _source?.Entities.NameFor(id) ?? MonsterName(id) ?? ResolveDisplayName(id), heals: _healMode && !_pvpOnly, fightEvents: _aggregator.Events,
            extras: extras, playerId: _historyMode ? _historyLiveIds.GetValueOrDefault(row.ObjectId, row.ObjectId) : row.ObjectId, fightStart: fightStart, fightEnd: fightEnd,
            replay: BuildReplayData(extras, fightStart, fightEnd, _aggregator.Events))
        {
            Owner = this,
        }.Show();
    }

    private void OnClearClicked(object sender, RoutedEventArgs e)
    {
        AppMenu.IsSubmenuOpen = false;
        ClearDamageData();
    }

    /// <summary>Delegates to Combat/EngagedTargets, which is where the two failure modes this
    /// filter exists for are documented and tested.</summary>
    private List<DamageEvent> RestrictToEngagedTargets(List<DamageEvent> damageEvents)
    {
        // No source yet (e.g. before Settings names an install folder) means no "You" id to anchor on.
        return _source is null
            ? damageEvents
            : EngagedTargets.Filter(damageEvents, _source.Entities.LocalPlayerId);
    }

    private void ClearDamageData()
    {
        _aggregator.Clear();
        _groupNames.Clear();
        _avoids.Clear();
        _kills.Clear();
        _rows.Clear();
        _rowsByObjectId.Clear();
        _targetNames.Clear();
        _playerIdentities.Clear();
        _selectedTargetId = null;
        _selectedRunWindowStart = null;
        _selectedRunWindowEnd = null;
        UpdateDpsColumnHeader();

        _mobBossEntries.Clear();
        _bossUploadedUntil.Clear();
        ApplyMobBossSearchFilter();
        RefreshUploadAvailability();
    }

    // Copy/CopyAll are shared between the Damage and Loot views (see OnShowDamageView/
    // OnShowLootView) rather than adding a second pair of buttons just for Loot -- whichever
    // grid is currently visible decides what gets copied. In BOTH views the two buttons follow
    // the same split, which is what their "String"/"Table" labels have always promised: Copy
    // produces the one-line string meant for pasting into a chat box (Aion chat here, the
    // ".loot" payload in the Loot view), CopyAll produces the multi-line table meant for reading
    // outside the game (tab-separated here, Discord Markdown in the Loot view). The Damage view
    // used to hand BOTH buttons the same tab-separated table -- reported by the user, who
    // expected a postable string from the first one.
    private void OnCopyClicked(object sender, RoutedEventArgs e) =>
        CopyChatLineChunk(BuildDmgChatLine(), "No damage has been recorded yet.");

    private void OnCopyAllClicked(object sender, RoutedEventArgs e) => CopyRowsToClipboard();

    /// <summary>
    /// The Damage view's "Table" payload: a fixed-width table in a code fence, ready to paste into
    /// Discord. Was tab-separated, which Discord collapses into an unreadable run of text -- per
    /// the user, this needs to arrive as an aligned ASCII table.
    /// </summary>
    private void CopyRowsToClipboard()
    {
        var ranked = _rows.OrderByDescending(r => r.Damage).ToList();
        var rows = ranked
            .Select(r => (IReadOnlyList<string>)new[]
            {
                r.Name,
                r.ClassName,
                r.Level > 0 ? r.Level.ToString() : "",
                r.Damage.ToString("N0", DotGroupedNumberFormat),
                r.DpsDisplay,
            })
            .ToList();

        string table = AsciiTable.Render(
            new[] { "Name", "Class", "Lvl", "Damage", "DPS" },
            rows,
            new[] { false, false, true, true, true });

        CopyTextToClipboardIfAny(table, "No damage has been recorded yet.");
    }

    /// <summary>Guards Clipboard.SetText against an empty result -- shared by CopyRowsToClipboard
    /// and the ".dmg" in-game command, same as the pre-existing "if (sb.Length > 0)" check.</summary>
    private void CopyTextToClipboardIfAny(string text, string whatWasEmpty)
    {
        if (text.Length == 0)
        {
            // Reported by the user as "clicking String puts nothing on the clipboard". It did
            // exactly what it was told to -- the loot summary only counts Unique and above, so a
            // run without such a drop produces an empty string -- but silently doing nothing is
            // indistinguishable from a broken button. Say which, instead.
            ThemedMessageBox.Show(this, whatWasEmpty, "Nothing to copy",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            // The clipboard is a single system-wide resource and any other process can hold it
            // open for a moment; SetText then throws instead of waiting. Unhandled, that took the
            // whole meter down mid-raid for something as minor as a failed copy.
            ThemedMessageBox.Show(this, $"The clipboard was busy and the copy failed.\n\n{ex.Message}",
                "Copy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Which chunk (see ChunkChatLineParts) CopyChatLineChunk hands out next, and for
    /// which exact chunk set - reset to 0 the moment the underlying text changes (more damage
    /// since the last copy, a different target selected, etc.) rather than silently continuing a
    /// stale cycle against numbers that no longer match what's on screen.</summary>
    private List<string> _lastChatChunks = new();
    private int _nextChatChunkIndex;

    /// <summary>Copies ONE chat-line chunk per call, cycling back to the first after the last -
    /// per the user, reported twice: even a single joined multi-line clipboard payload (each line
    /// individually well under ChatLineCharLimit) still failed to paste into Aion chat at all the
    /// moment it contained more than one line, exactly like the earlier over-length single-line
    /// case did. Aion's chat box apparently rejects (or otherwise cannot handle) a pasted string
    /// containing a newline at all, not just an over-long one - so each clipboard payload here is
    /// now genuinely a single line, the one shape already confirmed to work, and the user pastes
    /// each part in turn instead of hoping the game splits a multi-line paste into several
    /// messages on its own.</summary>
    private void CopyChatLineChunk(List<string> chunks, string whatWasEmpty)
    {
        if (chunks.Count == 0)
        {
            ThemedMessageBox.Show(this, whatWasEmpty, "Nothing to copy",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!chunks.SequenceEqual(_lastChatChunks))
        {
            _lastChatChunks = chunks;
            _nextChatChunkIndex = 0;
        }

        int index = _nextChatChunkIndex % chunks.Count;
        try
        {
            Clipboard.SetText(chunks[index]);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            ThemedMessageBox.Show(this, $"The clipboard was busy and the copy failed.\n\n{ex.Message}",
                "Copy", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _nextChatChunkIndex = index + 1;
        ShowUploadStatus(chunks.Count > 1
            ? $"Copied part {index + 1}/{chunks.Count} - paste it, then click Copy again for the next part."
            : "Copied to clipboard.");
    }

    /// <summary>
    /// The ".dmg" in-game command's clipboard payload, exact format specified by the user: a
    /// single comma-separated run (not one row per line) of "name, total [dps]" tuples,
    /// e.g. "Mitzuhiko, 3.123.456 [3.145], Mitzuhiki, 3.123.455 [3.144]", ranked by damage
    /// descending -- independent of whatever order/filter the grid itself is currently showing.
    /// Both the damage total and the DPS figure use Aion's own "." thousands-grouping style for visual consistency with what the game itself
    /// would show, not because DPS is naturally an integer -- it's rounded to match.
    /// </summary>
    /// <summary>Aion's own chat box rejects a paste outright once the resulting line would be too
    /// long, rather than truncating it - reported by the user as "pasting did nothing at all".
    /// Empirically confirmed by the user: a manually-typed 255-character line went through fine,
    /// a pasted ~275-character line didn't. Per the user, 255 exactly - the confirmed-working
    /// boundary itself, not a margin below it.</summary>
    private const int ChatLineCharLimit = 255;

    /// <summary>Splits already-formatted "one entry per row" strings into lines that each fit
    /// under <see cref="ChatLineCharLimit"/> (joined by ", " within a line, same as the
    /// unsplit format before this existed) - multiple lines are meant to be pasted into Aion
    /// chat and sent one at a time, not as a single paste.</summary>
    private static List<string> ChunkChatLineParts(IReadOnlyList<string> parts, int limit)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (string part in parts)
        {
            int prospectiveLength = current.Length == 0 ? part.Length : current.Length + 2 + part.Length;
            if (current.Length > 0 && prospectiveLength > limit)
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(", ");
            }

            current.Append(part);
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return lines;
    }

    private List<string> BuildDmgRankingText()
    {
        var ranked = _rows.OrderByDescending(r => r.Damage).ToList();
        var parts = new List<string>(ranked.Count);
        for (int i = 0; i < ranked.Count; i++)
        {
            PlayerRow row = ranked[i];
            string damageText = row.Damage.ToString("N0", DotGroupedNumberFormat);
            string dpsText = row.Dps is double dps
                ? Math.Round(dps).ToString("N0", DotGroupedNumberFormat)
                : "n/a";
            parts.Add($"{row.Name}, {damageText} [{dpsText}]");
        }

        return ChunkChatLineParts(parts, ChatLineCharLimit);
    }

    /// <summary>
    /// Copy's payload in the Damage view: one line, "Name Damage (DPS)" per entry, entries joined
    /// by ", " and ranked by damage descending regardless of how the grid is currently sorted --
    /// format specified by the user for pasting straight into the Aion chat box. Deliberately
    /// leaner than BuildDmgRankingText (the hotkey's payload, which brackets the DPS): both stay as their own specified formats rather than
    /// one being bent into the other. Numbers use Aion's own "." thousands grouping, and a row
    /// whose DPS is undefined (single hit, no elapsed time -- see DpsCalculator) shows "n/a"
    /// rather than a fabricated rate.
    /// </summary>
    /// <summary>
    /// Per the user: when the Mob/Boss filter has a specific target selected, the copied line must
    /// lead with that target's name -- otherwise a line pasted into Aion chat carries damage numbers
    /// with no indication of which fight they're from.
    /// </summary>
    private List<string> BuildDmgChatLine()
    {
        var ranked = _rows.OrderByDescending(r => r.Damage).ToList();
        var parts = new List<string>(ranked.Count);
        foreach (PlayerRow row in ranked)
        {
            string damageText = row.Damage.ToString("N0", DotGroupedNumberFormat);
            string dpsText = row.Dps is double dps
                ? Math.Round(dps).ToString("N0", DotGroupedNumberFormat)
                : "n/a";
            parts.Add($"{row.Name} {damageText} ({dpsText})");
        }

        if (parts.Count == 0)
        {
            return new List<string>();
        }

        List<string> lines = ChunkChatLineParts(parts, ChatLineCharLimit);
        if (_selectedTargetId is int targetId)
        {
            string bossName = _targetNames.TryGetValue(targetId, out string? n) ? n : ResolveDisplayName(targetId);
            lines[0] = $"{bossName}: {lines[0]}";
        }

        return lines;
    }

    private static readonly NumberFormatInfo DotGroupedNumberFormat = new() { NumberGroupSeparator = "." };

    // aiontools.com's U+E000-U+E06F in-game chat icons (see memopad_icons.png, sent to the user
    // for reference) -- these three positions were picked by the user directly from that image
    // ("Orange: Reihe 2 - letztes Bild", "Gold: Reihe 3 Bild 2", "Lila: Reihe 3 Bild 7"). The
    // reference image wraps a single flat sequence starting right after the visible title text
    // (5 icons trail the title on its own line before the first full 20-icon row begins) purely
    // because of the container's width, not real row breaks -- pixel-row-isolated and counted
    // directly against the decoded PNG (not eyeballed at native resolution) to convert "row/
    // position" into an absolute index, then into a codepoint: title-line icons are index 0-4,
    // "Reihe 1" 5-24, "Reihe 2" 25-44, "Reihe 3" 45-64 (all rows measured 20 icons wide). Cross-
    // checked against color: the computed position for "Orange" rendered as a rust-orange sphere,
    // "Gold" as a gold sphere, "Lila" as a purple ring -- matching the user's own color naming,
    // not just the position count alone.

    // Aion's own chat glyphs for the four item-quality tiers, taken from characters the user
    // pasted straight out of the client. Written as escapes rather than literal characters on
    // purpose: they live in the Unicode Private Use Area, so they are invisible in every editor
    // and diff outside the game, and a literal would be silently lost or mangled by anything that
    // strips PUA -- which is exactly what happened trying to send them through chat.
    //
    // Two of these were WRONG until now, and invisibly so: gold was U+E02E and epic U+E02C, which
    // are some other glyph entirely, so every loot summary posted to the group showed the wrong
    // symbols. Only mythic was right. Verified against the client's own output, tier by tier.
    private const string LegendIcon = "\ue036";   // blue
    private const string UniqueIcon = "\ue038";   // gold
    private const string EpicIcon = "\ue03e";     // orange
    private const string MythicIcon = "\ue033";   // purple

    private const int MaxIconsPerColor = 30;

    private void OnPauseClicked(object sender, RoutedEventArgs e) => SetPaused(!_paused);

    /// <summary>Shared by the toolbar Pause/Resume button and the pause hotkey.</summary>
    private void SetPaused(bool paused)
    {
        _paused = paused;
        PauseIcon.Visibility = _paused ? Visibility.Collapsed : Visibility.Visible;
        PlayIcon.Visibility = _paused ? Visibility.Visible : Visibility.Collapsed;
        PauseButton.ToolTip = _paused ? "Resume recording." : "Pause recording.";
    }

    /// <summary>Tracked so a second click on "App Settings" while one is already open focuses the
    /// existing window instead of opening a confusing second editor on the same settings file --
    /// see OnSettingsClicked. Cleared in the window's Closed handler.</summary>
    private SettingsWindow? _settingsWindow;
    private FeedbackWindow? _feedbackWindow;

    private void OnFeedbackClicked(object sender, RoutedEventArgs e)
    {
        if (_feedbackWindow is not null)
        {
            _feedbackWindow.Activate();
            return;
        }

        _feedbackWindow = new FeedbackWindow(MeterSettings.Load()) { Owner = this };
        _feedbackWindow.Closed += (_, _) => _feedbackWindow = null;
        _feedbackWindow.Show();
    }

    /// <summary>
    /// Non-modal per the user's request ("Settings bitte als 2. Fenster öffnen") -- Show(), not
    /// ShowDialog(), so MainWindow stays interactive while Settings is open. SettingsWindow no
    /// longer uses DialogResult for this reason (see its Saved event remarks); applying the
    /// settings here happens from that event instead of a ShowDialog() return value.
    /// </summary>
    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.ShowModule(null);
            _settingsWindow.Activate();
            return;
        }

        OpenSettings(null);
    }

    private ToolsWindow? _toolsWindow;

    /// <summary>A tool of the Tools menu: all tools share one window, the tools in the order of the menu on the left, the page of the chosen one on the right.</summary>
    private void OnOpenModuleClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            OpenTool(id);
        }
    }

    private void OpenTool(string id)
    {
        if (_toolsWindow is null)
        {
            var loc = LocalizationManager.Instance;
            var groups = ToolsWindow.Groups();
            _toolsWindow = new ToolsWindow(groups, BuildToolPage, tool =>
            {
                if (tool == "analyzer")
                {
                    OpenSessionManager();
                }
            }) { Owner = this };
            _toolsWindow.Closed += (_, _) => _toolsWindow = null;
            _toolsWindow.Show();
        }

        _toolsWindow.Select(id);
        _toolsWindow.Activate();
    }

    private void OpenSettingsPage(string module)
    {
        if (_settingsWindow is null)
        {
            OpenSettings(null);
        }

        if (module.Length == 0)
        {
            _settingsWindow?.ShowHotkeysPage();
        }
        else
        {
            _settingsWindow?.ShowModule(module);
        }

        _settingsWindow?.Activate();
    }

    /// <summary>The page of one tool for the tools window; null when the tool is not available (headless, no capture yet).</summary>
    private ToolPage? BuildToolPage(string module)
    {
        var loc = LocalizationManager.Instance;
        Action<MeterSettings> applied = ApplySettingsEverywhere;
        Action hotkeys = () => OpenSettingsPage("");
        Action settingsPage = () => OpenSettingsPage(module);
        // a tool that is a window of its own is shown by its content: the window is never shown and closed when the page goes away
        ToolPage Hosted(Window window) => new() { Content = ThemedChrome.TakeContent(window) ?? new Grid(), Cleanup = window.Close };
        switch (module)
        {
            case "checklist":
                return Hosted(new ChecklistWindow());
            case "attributes":
            {
                var attributesPage = new AttributesPage(() => _liveSource?.Entities as Aion2EntityDirectory);
                var attributesTool = new ToolPage { Content = attributesPage, Cleanup = attributesPage.Stop, CurrentTab = () => attributesPage.Tab };
                attributesPage.TabChanged += attributesTool.RaiseTabChanged;
                return attributesTool;
            }

            case "statcalc":
            {
                var calcPage = new StatCalcPage(() => _liveSource?.Entities as Aion2EntityDirectory, () => _aggregator.Events);
                return new ToolPage { Content = calcPage, Cleanup = calcPage.Stop };
            }

            case "loot":
            {
                var lootPage = new LootPage();
                var page = new ModulePage(loc["Main.MenuTools.Loot"], lootPage, s => s.ShowLootOverlay, (s, v) => s.ShowLootOverlay = v, s => s.HotkeyLoot, hotkeys, settingsPage, applied);
                return new ToolPage { Content = page, Cleanup = () => { page.Stop(); lootPage.Stop(); } };
            }

            case "craft":
            {
                var craftPage = new CraftPage(() => _liveSource?.Entities as Aion2EntityDirectory);
                CraftConfigWindow? craftConfig = null;
                Action openCraftConfig = () =>
                {
                    if (craftConfig is { IsLoaded: true })
                    {
                        craftConfig.Activate();
                        return;
                    }

                    craftConfig = new CraftConfigWindow(() => _liveSource?.Entities as Aion2EntityDirectory) { Owner = (Window?)_toolsWindow ?? this };
                    craftConfig.Closed += (_, _) => craftConfig = null;
                    craftConfig.Show();
                };
                var craftModule = new ModulePage("", craftPage, s => s.ShowCraftOverlay, (s, v) => s.ShowCraftOverlay = v, s => s.HotkeyCraft, hotkeys, openCraftConfig, applied, null, craftPage.ModeBar);
                return new ToolPage { Content = craftModule, Cleanup = () => { craftModule.Stop(); craftPage.Stop(); } };
            }

            case "assist":
                return Hosted(new AssistWindow(() => _liveSource?.Entities as Aion2EntityDirectory));
            case "dummy" when _dummy is not null:
            {
                var dummyWindow = new DummyTestWindow(_dummy, ToggleAllOverlays);
                var dummyTool = new ToolPage { Content = ThemedChrome.TakeContent(dummyWindow) ?? new Grid(), Cleanup = dummyWindow.Close, CurrentTab = () => dummyWindow.Tab };
                dummyWindow.TabChanged += dummyTool.RaiseTabChanged;
                return dummyTool;
            }

            case "theme":
            {
                var editor = new ThemeEditorWindow(MeterSettings.Load());
                editor.Saved += settings =>
                {
                    var current = MeterSettings.Load();
                    current.Theme = settings.Theme;
                    current.Save();
                    ApplySettingsEverywhere(current);
                };
                return Hosted(editor);
            }

            case "network" when _network is not null:
            {
                var view = _network.CreateView();
                var networkBody = new StackPanel();
                networkBody.Children.Add(view.DetachBody()!);
                networkBody.Children.Add(new NetworkReportPanel());
                var page = new ModulePage(loc["Settings.Nav.Network"], networkBody, s => s.ShowNetworkOverlay, (s, v) => s.ShowNetworkOverlay = v, s => s.HotkeyNetwork, hotkeys, settingsPage, applied);
                return new ToolPage { Content = page, Cleanup = () => { page.Stop(); _network?.RemoveView(view); } };
            }

            case "boss" when _worldBoss is not null:
            {
                var view = _worldBoss.CreateView();
                var page = new ModulePage(loc["Settings.Nav.Boss"], view.DetachBody()!, s => s.ShowBossOverlay, (s, v) => s.ShowBossOverlay = v, s => s.HotkeyWorldBoss, hotkeys, settingsPage, applied);
                return new ToolPage { Content = page, Cleanup = () => { page.Stop(); _worldBoss?.RemoveView(view); } };
            }

            case "timetable":
            {
                var view = new TimetableWindow();
                UIElement body = view.DetachView();
                view.Refresh();
                var page = new ModulePage(loc["Settings.Nav.Timetable"], body, s => s.ShowTimetable, (s, v) => s.ShowTimetable = v, s => s.HotkeyTimetable, hotkeys, settingsPage, applied, view.Refresh);
                return new ToolPage { Content = page, Cleanup = () => { page.Stop(); view.Close(); } };
            }

            case "combat" when _combat is not null:
            {
                var views = _combat.CreateViews();
                var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
                stack.Children.Add(ModulePage.Hint(loc["Combat.Page.SampleHint"]));
                var elements = new List<(CheckBox Box, UIElement Body, Func<MeterSettings, bool> Get)>();
                foreach (var (titleKey, view) in views)
                {
                    // each element's own switch sits at its caption and counts at once (the page and the overlay read the settings from disk)
                    (Func<MeterSettings, bool> get, Action<MeterSettings, bool> set) = view.PartKind switch
                    {
                        CombatOverlayWindow.Part.Hp => ((Func<MeterSettings, bool>)(s => s.ShowBossHp), (Action<MeterSettings, bool>)((s, v) => s.ShowBossHp = v)),
                        CombatOverlayWindow.Part.Cast => (s => s.CombatShowBossCast, (s, v) => s.CombatShowBossCast = v),
                        _ => (s => s.CombatShowTargetOfTarget, (s, v) => s.CombatShowTargetOfTarget = v),
                    };
                    var (row, box) = ModulePage.SwitchCaption(loc[titleKey], loc["Settings.Overlay.ShowAs"]);
                    box.Click += (_, _) =>
                    {
                        MeterSettings s = MeterSettings.Load();
                        set(s, box.IsChecked == true);
                        s.Save();
                        applied(s);
                    };
                    UIElement body = view.DetachBody()!;
                    elements.Add((box, body, get));
                    stack.Children.Add(row);
                    stack.Children.Add(body);
                }

                void SyncElements()
                {
                    MeterSettings s = MeterSettings.Load();
                    foreach (var (box, body, get) in elements)
                    {
                        bool on = get(s);
                        box.IsChecked = on;
                        body.Opacity = on ? 1.0 : 0.45; // switched off: the sample stays, dimmed, so the page still shows what the display would be
                    }
                }

                SyncElements();
                var page = new ModulePage(loc["Settings.Nav.Combat"], stack, s => s.ShowCombatOverlay, (s, v) => s.ShowCombatOverlay = v, s => s.HotkeyCombatOverlay, hotkeys, settingsPage, applied, SyncElements);
                return new ToolPage { Content = page, Cleanup = () => { page.Stop(); _combat?.RemoveViews(views.Select(v => v.View)); } };
            }

            case "petmap":
            {
                _petMapController ??= new PetMapController(() => _source?.Entities as Aion2EntityDirectory);
                var (map, list) = _petMapController.CreateViews();
                var stack = new StackPanel();
                stack.Children.Add(ModulePage.Caption(loc["Settings.PetMap.CardMap"]));
                stack.Children.Add(map.DetachBody()!);
                stack.Children.Add(ModulePage.Caption(loc["Settings.PetMap.CardList"]));
                stack.Children.Add(list.DetachBody()!);
                var page = new ModulePage(loc["Settings.Nav.PetMap"], stack, s => s.ShowPetMap, (s, v) => s.ShowPetMap = v, s => s.HotkeyPetMap, hotkeys, settingsPage, applied);
                return new ToolPage
                {
                    Content = page,
                    Cleanup = () =>
                    {
                        page.Stop();
                        _petMapController?.RemoveViews(map, list);
                        if (!MeterSettings.Load().ShowPetMap && _petMapController is { HasViews: false, CubeMapOverride: false })
                        {
                            _petMapController.Dispose();
                            _petMapController = null;
                        }
                    },
                };
            }

            case "petmap.pets":
            case "petmap.gather":
            {
                // the settings of the map's pets / collectibles: the panel of the settings window (never shown) as a page here; every change is kept a moment later
                string panelName = module == "petmap.pets" ? "PanelPets" : "PanelGather";
                var window = new SettingsWindow(MeterSettings.Load());
                if (window.TakePanel(panelName) is not { } panel)
                {
                    window.Close();
                    return null;
                }

                var body = new StackPanel { MinWidth = 560 };
                body.Resources.MergedDictionaries.Add(window.Resources);
                body.Children.Add(panel);
                var save = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                void Save()
                {
                    save.Stop();
                    var s = MeterSettings.Load();
                    window.ReadPanel(panelName, s);
                    s.Save();
                    ApplySettingsEverywhere(s);
                }

                save.Tick += (_, _) => Save();
                void Changed(object sender, RoutedEventArgs e)
                {
                    save.Stop();
                    save.Start();
                }

                foreach (var routed in new[] { System.Windows.Controls.Primitives.ButtonBase.ClickEvent, System.Windows.Controls.Primitives.ToggleButton.CheckedEvent,
                    System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent, System.Windows.Controls.Primitives.RangeBase.ValueChangedEvent,
                    System.Windows.Controls.Primitives.Selector.SelectionChangedEvent })
                {
                    panel.AddHandler(routed, new RoutedEventHandler(Changed));
                }

                var page = new ModulePage(module == "petmap.pets" ? loc["Settings.Nav.Pets"] : loc["Settings.Nav.Gather"], body, s => s.ShowPetMap, (s, v) => s.ShowPetMap = v, s => s.HotkeyPetMap, hotkeys, null, applied);
                return new ToolPage
                {
                    Content = page,
                    Cleanup = () =>
                    {
                        page.Stop();
                        if (save.IsEnabled)
                        {
                            Save();
                        }

                        window.Close();
                    },
                };
            }

            case "stream":
            {
                var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
                status.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
                var copy = new Button { Content = loc["Settings.Stream.Copy"], HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 2, 10, 2) };
                copy.Click += (_, _) => CopyStreamLink();
                var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
                stack.Children.Add(status);
                stack.Children.Add(copy);
                void Show()
                {
                    status.Text = _streamServer is { Running: true } server
                        ? server.Url + "\n" + string.Format(loc["Stream.Clients"], server.ClientCount)
                        : loc["Stream.NotRunning"];
                    copy.IsEnabled = _streamServer is { Running: true };
                }

                Show();
                var page = new ModulePage(loc["Settings.Nav.Stream"], stack, s => s.StreamOverlayOn, (s, v) => s.StreamOverlayOn = v, null, hotkeys, settingsPage, applied, Show);
                return new ToolPage { Content = page, Cleanup = page.Stop };
            }
        }

        return null;
    }

    /// <summary>Opens the settings window; <paramref name="undoUntil"/> adds "Undo reset" to it (after a reset).</summary>
    private void OpenSettings(DateTime? undoUntil)
    {
        var settings = MeterSettings.Load();
        string openedTheme = settings.Theme;
        _settingsWindow = new SettingsWindow(settings, undoUntil) { Owner = this };
        _settingsWindow.Saved += () =>
        {
            // the theme editor may have chosen a theme since this window opened
            var onDisk = MeterSettings.Load();
            if (settings.Theme == openedTheme)
            {
                settings.Theme = onDisk.Theme; // not changed in this window: keep what the theme editor saved meanwhile
            }

            settings.Save();
            ApplySettingsEverywhere(settings);
        };
        _settingsWindow.ResetRequested += () =>
        {
            MeterSettings.ResetToDefaults();
            ApplySettingsEverywhere(MeterSettings.Load());
            ReopenSettings(DateTime.UtcNow.AddSeconds(30));
        };
        _settingsWindow.UndoRequested += () =>
        {
            if (MeterSettings.UndoReset())
            {
                ApplySettingsEverywhere(MeterSettings.Load());
            }
            ReopenSettings(null);
        };
        _settingsWindow.CopyStreamLinkRequested += CopyStreamLink;
        _settingsWindow.NewStreamKeyRequested += () =>
        {
            var current = MeterSettings.Load();
            current.StreamOverlayKey = Streaming.StreamOverlayServer.NewKey();
            current.Save();
            _streamSettingsAt = DateTime.MinValue; // the server restarts with the new key at the next look
            StreamTick();
            CopyStreamLink();
        };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void ReopenSettings(DateTime? undoUntil)
    {
        _settingsWindow?.Close();
        OpenSettings(undoUntil);
    }

    /// <summary>What has to follow when the settings change: the look, the hotkeys, the overlays and the capture.</summary>
    private void ApplySettingsEverywhere(MeterSettings settings)
    {
        if (settings.Language.Length > 0)
        {
            LocalizationManager.Instance.Language = settings.Language;
        }
        else
        {
            LocalizationManager.Instance.UseSystemLanguage();
        }

        Aion2.Capture.DebugRecording.Enabled = settings.DebugRecording;
        Aion2.Capture.LoginCapture.Enabled = settings.ShareLoginWithLegion;
        ThemeManager.Apply(Application.Current, settings); // repaints every open window
        foreach (PlayerRow row in _rows)
        {
            row.RefreshClassBrush(); // the own bar colour may have changed
        }
        ApplyHotkeys(settings); // possibly changed keys
        ApplyTimetable(settings);
        ApplyPetFarm(settings);
        ApplyHotkeyOverlay(settings);
        ExitHistoryMode(); // a viewed past fight must not survive a source change underneath it
        if (settings.CaptureAdapterId != _captureAdapterInUse)
        {
            StartCapture(settings); // only a new network adapter needs a new capture: a restart forgets the map, the position and the pets of the running game
        }
        RefreshCharacterSettings(settings); // possibly a new/changed character list or active one
        ApplyClassFilterAvailability(); // possibly a new/changed install folder or server display name
        RefreshRows();
    }

    /// <summary>
    /// Briefly also duplicated onto a toolbar checkbox, which promptly overflowed the toolbar
    /// past the window's edge (see terminal_windows's measurements) -- reverted back to living
    /// only here. IsCheckable="True" already renders its own checkmark when active, which is
    /// exactly what the user asked for ("in einem Unter-Menü... mit Haken Symbol davor").
    /// </summary>
    private void OnAlwaysOnTopClicked(object sender, RoutedEventArgs e)
    {
        Topmost = AlwaysOnTopMenuItem.IsChecked;

        // The menu toggle and MeterSettings.AlwaysOnTopOnStartup are the same value now, not two
        // independent switches (see that property's remarks) -- saved immediately, same as
        // CheckForUpdates, so "how I last left it" is what the next launch restores.
        var settings = MeterSettings.Load();
        settings.AlwaysOnTopOnStartup = Topmost;
        settings.Save();
    }

    private void OnModeButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse(tag, out MeterMode mode))
        {
            SetMode(mode);
        }
    }

    private static readonly Dictionary<string, ImageSource?> ModeIcons = new();

    /// <summary>One of the mode pictures in assets/modes (null when the file is missing; the button then shows nothing).</summary>
    private static ImageSource? ModeIcon(string name)
    {
        if (ModeIcons.TryGetValue(name, out ImageSource? known))
        {
            return known;
        }

        ImageSource? image = null;
        try
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "modes", name + ".png");
            if (System.IO.File.Exists(path))
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                image = bitmap;
            }
        }
        catch
        {
        }

        ModeIcons[name] = image;
        return image;
    }

    private static SolidColorBrush ModeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static readonly SolidColorBrush ModeGreen = ModeBrush(0x69, 0xE1, 0x5E);
    private static readonly SolidColorBrush ModeRed = ModeBrush(0xFF, 0x6B, 0x5E);
    private static readonly SolidColorBrush ModeAllActive = ModeBrush(0x2C, 0x4A, 0x60);

    /// <summary>The five mode buttons: the chosen one is filled (yellow for damage, green for heal, red for damage taken, blue-grey for the two
    /// All in One modes, whose picture is multicoloured), the others show their picture on the plain button background.</summary>
    private void ApplyModeButtons()
    {
        Brush plain = (Brush)FindResource("Brush.Control");
        void Single(Button button, Image image, string key, bool active, Brush activeBackground)
        {
            button.Background = active ? activeBackground : plain;
            image.Source = ModeIcon("mode-" + key + (active ? "-on" : "-off"));
        }

        Single(ModeDamageButton, ModeDamageImage, "damage", _mode == MeterMode.Damage, (Brush)FindResource("Brush.Accent"));
        Single(ModeHealButton, ModeHealImage, "heal", _mode == MeterMode.Heal, ModeGreen);
        Single(ModeTakenButton, ModeTakenImage, "taken", _mode == MeterMode.Taken, ModeRed);
        ModeAllButton.Background = _mode == MeterMode.All ? ModeAllActive : plain;
        ModeAllImage.Source = ModeIcon("mode-all");
        ModeAllPlusButton.Background = _mode == MeterMode.AllCompact ? ModeAllActive : plain;
        ModeAllPlusImage.Source = ModeIcon("mode-all-plus");
    }

    private void SetMode(MeterMode mode, bool save = true)
    {
        _mode = mode;
        ApplyModeButtons();
        UpdateDpsColumnHeader();
        RefreshRows();
        if (save && !Headless)
        {
            var settings = MeterSettings.Load();
            settings.LastMode = mode.ToString();
            settings.Save();
        }
    }

    /// <summary>Damage, then heal, taken, all in one, all in one compact, then damage again.</summary>
    private void NextMode() => SetMode(_mode switch
    {
        MeterMode.Damage => MeterMode.Heal,
        MeterMode.Heal => MeterMode.Taken,
        MeterMode.Taken => MeterMode.All,
        MeterMode.All => MeterMode.AllCompact,
        _ => MeterMode.Damage,
    });

    private void OnOverlayModeClicked(object sender, MouseButtonEventArgs e)
    {
        NextMode();
        e.Handled = true;
    }

    /// <summary>The id to read a row's guild and profile under: the own character's row may carry an
    /// older object id than the one the game currently knows it by.</summary>
    /// <summary>The game's permanent character id of a player for the upload (a hint for the website to recognise a renamed character), null when no player list named him.</summary>
    private static int? CharacterIdFor(Aion2.Aion2EntityDirectory? directory, string name, int? serverId) =>
        directory is not null && serverId is int server && directory.CharacterIdOf(name, server) is > 0 and int id ? id : null;

    private int ProfileIdOf(PlayerRow row) =>
        _source?.Entities is Aion2.Aion2EntityDirectory { LocalCharacter: { } own } && row.Name == own.Name
            ? own.CombatId
            : row.ObjectId;

    /// <summary>The Aion 2 character profile to attach to a participant: the full own record for the
    /// local player (also when it was restored from disk), what the "player appeared" frame showed
    /// for everyone else; null when there is nothing to say (no frame seen).</summary>
    private ProfileUpload? BuildProfileUpload(int objectId)
    {
        if (_source?.Entities is not Aion2.Aion2EntityDirectory directory)
        {
            return null;
        }

        if (directory.IsLocalPlayer(objectId))
        {
            // Only a record the game sent in this session, for this very name. What was read back from the file of an
            // earlier session (maybe another character's) goes nowhere until the game sends the real one.
            if (directory.LocalCharacter is not { Restored: false } live
                || !string.Equals(live.Name, directory.NameFor(objectId), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            Aion2CharacterInfo character = live;
            int code = character.ClassCode;
            bool known = Aion2.Protocol.Aion2SkillNames.ClassIdOfCode(code) is >= 1 and <= 8;
            return new ProfileUpload(
                "self",
                character.Level,
                known ? Aion2.Protocol.Aion2SkillNames.ClassIdOfCode(code) : null,
                known ? Aion2.Protocol.Aion2SkillNames.ClassBitsOfCode(code) : null,
                directory.LocalEquipment.Select(GearUpload).ToList(),
                directory.LocalSkills.Select(s => new ProfileSkillUpload(s.SkillId, s.Level, s.BaseLevel, s.Stigma, s.Equipped, s.Stones)).ToList(),
                directory.LocalDaevanion.Select(b => new ProfileBoardUpload(b.BoardId, b.NodeIds.ToList())).ToList(),
                directory.LocalSpecies.Select(k => new ProfileSpeciesUpload(k.SpeciesId, k.Level, k.Progress,
                    k.Effects.Select(e => new ProfileSpeciesEffectUpload(e.Page, e.Slot, e.StatId, e.Value, e.Kind)).ToList())).ToList(),
                Titles: directory.LocalTitles.Select(t => new ProfileTitleUpload(t.Slot, t.TitleId)).ToList(),
                Pets: directory.LocalPets.Select(k => new ProfilePetUpload(k.SpeciesId, k.Level, k.Kinds)).ToList(),
                Attributes: directory.LocalAttributes,
                WingId: directory.LocalWing?.WingId,
                WingSkinId: directory.LocalWing is { SkinId: > 0 } wing ? wing.SkinId : null,
                ActivePet: directory.LocalActivePet,
                ActivePetLevel: directory.LocalActivePet is > 0 and int activePet
                    ? directory.LocalPetStates.Where(p => p.PetId == activePet).Select(p => (int?)p.Level).FirstOrDefault()
                    : null,
                Presets: directory.LocalPresets.Select(x => new ProfilePresetUpload(x.Index, x.Active, x.SeenAt.ToUniversalTime(),
                    x.Skills.Select(k => new ProfileSkillUpload(k.SkillId, k.Level, k.BaseLevel, k.Stigma, k.Equipped, k.Stones)).ToList())).ToList(),
                AccountKey: directory.AccountKeyHash());
        }

        // A character window the local player opened for this player: level and the full equipment with
        // enchants, richer than the "appeared" frame below.
        if (directory.NameFor(objectId) is string windowName
            && directory.InspectedPlayers().FirstOrDefault(p => p.Name == windowName) is { } window)
        {
            return new ProfileUpload(
                "seen",
                window.Level,
                Aion2.Protocol.Aion2SkillNames.ClassIdOfCode(window.ClassCode),
                Aion2.Protocol.Aion2SkillNames.ClassBitsOfCode(window.ClassCode),
                window.Gear.Select(GearUpload).ToList(),
                Array.Empty<ProfileSkillUpload>(),
                Array.Empty<ProfileBoardUpload>(),
                GearScore: window.GearScore > 0 ? window.GearScore : null,
                Titles: window.Titles?.Select(t => new ProfileTitleUpload(t.Slot, t.TitleId)).ToList(),
                Pets: window.Pets?.Select(k => new ProfilePetUpload(k.SpeciesId, k.Level, k.Kinds)).ToList(),
                BoardCounts: window.BoardCounts?.Select(b => new ProfileBoardCountUpload(b.BoardId, b.Count)).ToList());
        }

        if (directory.SeenProfileOf(objectId) is { } seen)
        {
            return new ProfileUpload(
                "seen",
                null,
                seen.ClassId,
                seen.ClassBits,
                seen.Gear.Select(GearUpload).ToList(),
                Array.Empty<ProfileSkillUpload>(),
                Array.Empty<ProfileBoardUpload>());
        }

        return null;
    }

    /// <summary>Opens (or brings forward) the character window beside the meter. It lives in its own
    /// window, wider than the meter, and redraws itself when the game re-sends the record.</summary>
    private void OnShowCharacterView(object sender, RoutedEventArgs e)
    {
        if (_source?.Entities is not Aion2.Aion2EntityDirectory directory)
        {
            return;
        }

        if (_characterWindow is { IsLoaded: true })
        {
            _characterWindow.Activate();
            return;
        }

        _characterWindow = new CharacterWindow(directory) { Owner = this };
        _characterWindow.Closed += (_, _) => _characterWindow = null;
        _characterWindow.PlaceBeside(this);
        _characterWindow.Show();
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        AppMenu.IsSubmenuOpen = false;
        Close();
    }

    private void OnMinimizeClicked(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnHideUiClicked(object sender, RoutedEventArgs e)
    {
        var settings = MeterSettings.Load();
        if (!_hideUiActive && !settings.HideUiHintDismissed)
        {
            // the window is about to become see-through and click-through: say how to get it back
            var loc = LocalizationManager.Instance;
            string key = HotkeyBinding.Parse(settings.HotkeyHideUi).ToString();
            string text = string.IsNullOrWhiteSpace(settings.HotkeyHideUi) ? loc["Main.HideUiHint.NoHotkey"] : string.Format(loc["Main.HideUiHint"], key);
            ThemedMessageBox.Show(this, text, loc["Main.HideUiHint.Title"], MessageBoxButton.OK, MessageBoxImage.Information,
                loc["Main.HideUiHint.DontShow"], dismissed =>
                {
                    if (dismissed)
                    {
                        var s = MeterSettings.Load();
                        s.HideUiHintDismissed = true;
                        s.Save();
                    }
                });
        }

        SetHideUi();
    }

    /// <summary>
    /// Found by the user, comparing against their Timetable project's overlay: click-through
    /// alone isn't enough for an overlay that's meant to sit on top of the game. A click-through
    /// window that isn't also topmost can end up BEHIND the game, at which point it may as well
    /// not exist; hiding the taskbar entry while it's active matches the same "this is an overlay
    /// right now, not a normal window" framing. Both are restored to whatever they were before the
    /// moment Hide UI is toggled back off, rather than forced permanently.
    /// </summary>
    /// <summary>The picture Windows shows for the transparent overlay in Alt+Tab and the taskbar: the
    /// overlay's chips on the theme's window colour, scaled to fit the size asked for (0 = actual size).</summary>
    private System.Drawing.Bitmap? RenderOverlayPreview(int maxWidth, int maxHeight)
    {
        try
        {
            int width = Math.Max((int)Math.Ceiling(ActualWidth), 1);
            int height = Math.Max((int)Math.Ceiling(ActualHeight), 1);
            double scale = maxWidth > 0 && maxHeight > 0 ? Math.Min(Math.Min((double)maxWidth / width, (double)maxHeight / height), 1.0) : 1.0;
            int outWidth = Math.Max((int)(width * scale), 1);
            int outHeight = Math.Max((int)(height * scale), 1);

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.DrawRectangle((Brush)FindResource("Brush.Window"), null, new Rect(0, 0, width, height));
                dc.DrawRectangle(new VisualBrush(_compactOverlay ? CompactOverlayPanel : OverlayContent) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                    null, new Rect(0, 0, width, height));
            }

            var target = new RenderTargetBitmap(outWidth, outHeight, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            stream.Position = 0;
            using var decoded = new System.Drawing.Bitmap(stream);
            return new System.Drawing.Bitmap(decoded);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ExternalException or ResourceReferenceKeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>In overlay mode the chips keep the player list's left edge and width, but move up to the
    /// window's top edge, where the title bar ("Aion DPS" + version) was; switching drops the frame and
    /// nothing changes size. Measured while the normal content is still visible; kept for the time it is hidden.</summary>
    private double _overlayLeft, _overlayRightInset;

    private void UpdateOverlayWidth()
    {
        if (NormalContent.Visibility == Visibility.Visible && PlayersGrid.IsVisible && PlayersGrid.ActualWidth > 0)
        {
            Point origin = PlayersGrid.TranslatePoint(new Point(0, 0), this);
            _overlayLeft = origin.X;
            _overlayRightInset = Math.Max(0, ActualWidth - origin.X - PlayersGrid.ActualWidth);
        }

        OverlayContent.Margin = new Thickness(_overlayLeft, 4, 0, 0);
        OverlayContent.Width = Math.Max(ActualWidth - _overlayLeft - _overlayRightInset, 120);
    }

    private void SetHideUi()
    {
        UpdateOverlayWidth();
        _hideUiActive = !_hideUiActive;
        NormalContent.Visibility = _hideUiActive ? Visibility.Collapsed : Visibility.Visible;
        ShowOverlayPanels();
        if (_hideUiActive && _compactOverlay)
        {
            FollowNewestRun();
        }

        // The scope filter only applies to the compact overlay, so the rows are redone on every switch.
        RefreshRows();

        _overlay?.SetIconicPreview(_hideUiActive ? RenderOverlayPreview : null);

        // Per the user: the corner resize-grip glyph (from the window's own
        // ResizeMode="CanResizeWithGrip", not anything drawn by NormalContent) stayed visible even
        // once the rest of the chrome vanished, floating over the game with nothing around it to
        // explain what it was. NoResize removes the glyph along with the ability to drag-resize,
        // which is fine here: the window is click-through while this mode is active, so a mouse
        // couldn't reach the grip to drag it anyway.
        ResizeMode = _hideUiActive ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;

        if (_hideUiActive)
        {
            _topmostBeforeHideUi = Topmost;
            Topmost = true;
            // Stays in the taskbar in both looks: its right-click "Close" and a click on it are
            // ways to end the meter or bring it back besides the hotkey.
            ShowInTaskbar = true;
        }
        else
        {
            Topmost = _topmostBeforeHideUi;
            ShowInTaskbar = true;
        }

        FitWindowToCompactOverlay(_hideUiActive && _compactOverlay);
    }

    /// <summary>Builds the hotkey overlay from the settings (startup, after Settings); whether it is on screen right now is up to <see cref="OnGateTick"/>.</summary>
    private void ApplyHotkeyOverlay(MeterSettings settings)
    {
        if (Headless)
        {
            return;
        }

        if (!settings.ShowHotkeyOverlay)
        {
            _hotkeyOverlay?.ShowOverlay(false);
            return;
        }

        _hotkeyOverlay ??= new HotkeyOverlayWindow();
        _hotkeyOverlay.ApplyLocked(settings.HotkeyOverlayLocked ?? settings.PetFarmLocked);
        _hotkeyOverlay.Render(HotkeyOverlayWindow.RowsOf(settings));
        OnGateTick();
    }

    private DateTime _gateSettingsAt = DateTime.MinValue;
    private MeterSettings _gateSettings = new();

    /// <summary>A few times a second: the hotkey overlay and the DPS overlay (Hide UI) are on screen only while the game is in front
    /// (the hotkey overlay also while the overlays are unlocked, so it can be placed).</summary>
    private void OnGateTick()
    {
        bool inGame = GameWindow.ForegroundIsGameOrSelf();
        if (inGame != _gameGateOpen)
        {
            _gameGateOpen = inGame;
            ShowOverlayPanels();
        }

        if (_hotkeyOverlay is null)
        {
            return;
        }

        if ((DateTime.UtcNow - _gateSettingsAt).TotalSeconds >= 1)
        {
            _gateSettingsAt = DateTime.UtcNow;
            _gateSettings = MeterSettings.Load();
        }

        var s = _gateSettings;
        _hotkeyOverlay.ApplyLockedIfChanged(s.HotkeyOverlayLocked ?? s.PetFarmLocked);
        _hotkeyOverlay.ShowOverlay(s.ShowHotkeyOverlay && (!(s.HotkeyOverlayLocked ?? s.PetFarmLocked) || GameWindow.ForegroundClientArea() is not null));
    }

    /// <summary>Shows or hides the timetable overlay as Settings say (startup, after Settings, hotkey).</summary>
    private void ApplyTimetable(MeterSettings settings)
    {
        if (Headless)
        {
            return;
        }

        if (!settings.ShowTimetable)
        {
            _timetable?.Hide();
            return;
        }

        _timetable ??= new TimetableWindow();
        _timetable.ApplyOpacity(settings.TimetableOpacity ?? settings.OverlayOpacity);
        _timetable.ApplyLookahead(settings.TimetableLookaheadMinutes);
        _timetable.ApplyLockedIfChanged(settings.TimetableLocked);
        if (!_timetable.IsVisible)
        {
            _timetable.Show();
            _timetable.ApplyLocked(settings.TimetableLocked); // the click-through style is set again after the first show
        }

        _timetable.Refresh();
    }

    /// <summary>Starts or stops the pet farming overlay as Settings say (see PetFarmWindow).</summary>
    private void ApplyPetFarm(MeterSettings settings)
    {
        if (Headless)
        {
            return;
        }

        _petMapController?.ClearCubeOverride(); // the settings were applied: a map shown for a cube goes back to what they say
        // the pet map (with its list) has its own switch
        if (settings.ShowPetMap)
        {
            _petMapController ??= new PetMapController(() => _source?.Entities as Aion2EntityDirectory);
            _petMapController.ApplyLocked(settings.PetFarmLocked);
        }
        else if (_petMapController is not null && !_petMapController.HasViews)
        {
            _petMapController.Dispose();
            _petMapController = null;
        }

        if (!settings.ShowPetFarm)
        {
            _petFarmController?.Dispose();
            _petFarmController = null;
            _petFarm?.Hide();
            return;
        }

        _petFarm ??= new PetFarmWindow();
        _petFarm.ApplyOpacity(settings.OverlayOpacities.GetValueOrDefault(OverlayPlaces.PetInfo, settings.OverlayOpacity));
        _petFarm.ApplyLocked(settings.PetInfoLocked);
        _petFarmController ??= new PetFarmController(_petFarm, () => _source?.Entities as Aion2EntityDirectory);
    }

    private void TogglePetMap()
    {
        var settings = MeterSettings.Load();
        settings.ShowPetMap = !settings.ShowPetMap;
        settings.Save();
        ApplyPetFarm(settings);
        _settingsWindow?.SyncOverlaySwitches(settings);
    }

    private void ToggleTimetable()
    {
        var settings = MeterSettings.Load();
        settings.ShowTimetable = !(_timetable?.IsVisible ?? false);
        settings.Save();
        ApplyTimetable(settings);
        _settingsWindow?.SyncOverlaySwitches(settings);
    }

    private void ToggleHotkeyOverlay()
    {
        var settings = MeterSettings.Load();
        settings.ShowHotkeyOverlay = !settings.ShowHotkeyOverlay;
        settings.Save();
        ApplyHotkeyOverlay(settings);
        _settingsWindow?.SyncOverlaySwitches(settings);
    }

    /// <summary>The map controller reads the settings every second, so the switch is all it takes.</summary>
    private void TogglePetArrows()
    {
        var settings = MeterSettings.Load();
        settings.ShowPetArrows = !settings.ShowPetArrows;
        settings.Save();
        _settingsWindow?.SyncOverlaySwitches(settings);
    }

    private void ToggleGatherArrows()
    {
        var settings = MeterSettings.Load();
        settings.ShowGatherArrows = !settings.ShowGatherArrows;
        settings.Save();
        _settingsWindow?.SyncOverlaySwitches(settings);
    }

    /// <summary>The world boss controller reads the settings every second, so the switch is all it takes.</summary>
    private void ToggleWorldBossOverlay()
    {
        var settings = MeterSettings.Load();
        settings.ShowBossOverlay = !settings.ShowBossOverlay;
        settings.Save();
        _settingsWindow?.SyncOverlaySwitches(settings);
    }

    /// <summary>The network controller reads the settings every second, so the switch is all it takes.</summary>
    private void ToggleNetworkOverlay()
    {
        var settings = MeterSettings.Load();
        settings.ShowNetworkOverlay = !settings.ShowNetworkOverlay;
        settings.Save();
        _settingsWindow?.SyncOverlaySwitches(settings);
    }

    /// <summary>The assist controller reads the settings every second, so the switch is all it takes.</summary>
    private void ToggleAssistOverlay()
    {
        var settings = MeterSettings.Load();
        settings.ShowAssistOverlay = !settings.ShowAssistOverlay;
        settings.Save();
        _settingsWindow?.SyncOverlaySwitches(settings);
    }

    /// <summary>The loot controller reads the settings every second, so the switch is all it takes.</summary>
    private void ToggleLootOverlay()
    {
        var settings = MeterSettings.Load();
        settings.ShowLootOverlay = !settings.ShowLootOverlay;
        settings.Save();
        _settingsWindow?.SyncOverlaySwitches(settings);
    }

    /// <summary>The crafting controller reads the settings every second, so the switch is all it takes.</summary>
    private void ToggleCraftOverlay()
    {
        var settings = MeterSettings.Load();
        settings.ShowCraftOverlay = !settings.ShowCraftOverlay;
        settings.Save();
        _settingsWindow?.SyncOverlaySwitches(settings);
    }

    /// <summary>The combat controller reads the settings every second, so the switch is all it takes.</summary>
    private void ToggleCombatOverlay()
    {
        var settings = MeterSettings.Load();
        settings.ShowCombatOverlay = !settings.ShowCombatOverlay;
        settings.Save();
        _settingsWindow?.SyncOverlaySwitches(settings);
    }

    /// <summary>Which overlays the "all overlays" hotkey switched off, so the next press brings back exactly those.</summary>
    private (bool Hotkeys, bool Timetable, bool PetMap, bool PetFarm, bool WorldBoss, bool Network, bool Combat, bool Dummy, bool Assist, bool Loot)? _overlaysSwitchedOff;

    /// <summary>One press switches every overlay that is on off (hotkey list, timetable, interactive map, pet info); the next brings the same ones back.
    /// The DPS overlay is not among them - it is the Hide UI mode with its own hotkey.</summary>
    private void ToggleAllOverlays()
    {
        var settings = MeterSettings.Load();
        bool anyOn = settings.ShowHotkeyOverlay || settings.ShowTimetable || settings.ShowPetMap || settings.ShowPetFarm || settings.ShowBossOverlay || settings.ShowNetworkOverlay || settings.ShowCombatOverlay || settings.ShowDummyOverlay || settings.ShowAssistOverlay || settings.ShowLootOverlay;
        if (anyOn)
        {
            _overlaysSwitchedOff = (settings.ShowHotkeyOverlay, settings.ShowTimetable, settings.ShowPetMap, settings.ShowPetFarm, settings.ShowBossOverlay, settings.ShowNetworkOverlay, settings.ShowCombatOverlay, settings.ShowDummyOverlay, settings.ShowAssistOverlay, settings.ShowLootOverlay);
            settings.ShowHotkeyOverlay = settings.ShowTimetable = settings.ShowPetMap = settings.ShowPetFarm = settings.ShowBossOverlay = settings.ShowNetworkOverlay = settings.ShowCombatOverlay = settings.ShowDummyOverlay = settings.ShowAssistOverlay = settings.ShowLootOverlay = false;
        }
        else
        {
            var back = _overlaysSwitchedOff ?? (true, true, true, false, true, true, true, true, false, false);
            (settings.ShowHotkeyOverlay, settings.ShowTimetable, settings.ShowPetMap, settings.ShowPetFarm, settings.ShowBossOverlay, settings.ShowNetworkOverlay, settings.ShowCombatOverlay, settings.ShowDummyOverlay, settings.ShowAssistOverlay, settings.ShowLootOverlay) = back;
            _overlaysSwitchedOff = null;
        }

        settings.Save();
        ApplyHotkeyOverlay(settings);
        ApplyTimetable(settings);
        ApplyPetFarm(settings);
        _settingsWindow?.SyncOverlaySwitches(settings);
    }

    /// <summary>Settings changed while Hide UI is up: switches to the look now chosen.</summary>
    private void ApplyOverlayLook()
    {
        ShowOverlayPanels();
        ShowInTaskbar = true;
        FitWindowToCompactOverlay(_compactOverlay);
        if (_compactOverlay)
        {
            FollowNewestRun();
        }

        RefreshRows();
    }

    /// <summary>Which Hide-UI look is up: one chip per player or the compact panel (Settings).</summary>
    private void ShowOverlayPanels()
    {
        // outside the game (a browser in front, the desktop) the overlay is not drawn; its window stays, so the taskbar button still brings it back
        OverlayContent.Visibility = _hideUiActive && !_compactOverlay && _gameGateOpen ? Visibility.Visible : Visibility.Collapsed;
        CompactOverlayPanel.Visibility = _hideUiActive && _compactOverlay && _gameGateOpen ? Visibility.Visible : Visibility.Collapsed;

        // The chips let every click through to the game. The compact panel takes clicks (a player's
        // skills, dragging it into place) unless "click-through" is chosen in Settings; the window is
        // truly transparent around it, so the rest of the screen still reaches the game.
        _overlay?.SetClickThrough(_hideUiActive && (!_compactOverlay || _compactClickThrough));
        RefreshUpdateBadge();
    }

    // --- The update hint in the transparent (Hide UI) modes: the status row with its notice is hidden there. ---
    private string? _updateBadgeFull;
    private string? _updateBadgeShort;
    private bool _clientOutdated;

    /// <summary>Remembers what the update check says (null = nothing to show) and puts it on the overlays.</summary>
    private void SetUpdateStatus(string? fullText, string? shortText)
    {
        _updateBadgeFull = fullText;
        _updateBadgeShort = shortText;
        RefreshUpdateBadge();
    }

    private void RefreshUpdateBadge()
    {
        var loc = LocalizationManager.Instance;
        // Only an outdated client (it cannot upload until it is updated) is worth a badge on the
        // transparent overlay; a merely available update stays quiet there while the client is
        // still new enough, the status row and the restart prompt carry it.
        string? full = _clientOutdated ? loc["Overlay.OutdatedBadge"] : null;
        string? small = _clientOutdated ? loc["Overlay.OutdatedBadgeShort"] : null;
        bool show = full is not null && _hideUiActive && _gameGateOpen;
        var brush = (Brush)FindResource(_clientOutdated ? "Brush.Warning" : "Brush.Accent");
        OverlayUpdateBadge.Visibility = show && !_compactOverlay ? Visibility.Visible : Visibility.Collapsed;
        CompactUpdateBadge.Visibility = show && _compactOverlay ? Visibility.Visible : Visibility.Collapsed;
        OverlayUpdateBadge.Background = brush;
        CompactUpdateBadge.Background = brush;
        OverlayUpdateBadgeText.Text = full ?? "";
        CompactUpdateBadgeText.Text = small ?? "";
        CompactUpdateBadge.ToolTip = full;
    }

    private const double MinOverlayScale = 0.7, MaxOverlayScale = 2.0;

    private void SetCompactOverlayScale(double scale)
    {
        scale = Math.Clamp(double.IsFinite(scale) ? scale : 1.0, MinOverlayScale, MaxOverlayScale);
        CompactOverlayScale.ScaleX = scale;
        CompactOverlayScale.ScaleY = scale;
    }

    /// <summary>The compact overlay's corner grip: dragging right or down grows the whole panel.</summary>
    private void OnCompactOverlayResize(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        double scale = CompactOverlayScale.ScaleX;
        SetCompactOverlayScale((CompactOverlayPanel.Width * scale + Math.Max(e.HorizontalChange, e.VerticalChange)) / CompactOverlayPanel.Width);
    }

    private void OnCompactOverlayResized(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        var settings = MeterSettings.Load();
        settings.OverlayScale = CompactOverlayScale.ScaleX;
        settings.Save();
    }

    private void OnCompactOverlayDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    /// <summary>A click on a player's line in the compact overlay: that player's skill breakdown.</summary>
    private void OnCompactOverlayRowClicked(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PlayerRow row)
        {
            ShowPlayerDetails(row);
        }
    }

    private void OnOverlaySettingsClicked(object sender, MouseButtonEventArgs e)
    {
        OnSettingsClicked(sender, new RoutedEventArgs());
        e.Handled = true;
    }

    private void OnOverlayCopyClicked(object sender, MouseButtonEventArgs e)
    {
        CopyRowsToClipboard();
        e.Handled = true;
    }

    private void OnOverlayResetClicked(object sender, MouseButtonEventArgs e)
    {
        ClearDamageData();
        RefreshRows();
        e.Handled = true;
    }

    private void OnOverlayMinimizeClicked(object sender, MouseButtonEventArgs e)
    {
        WindowState = WindowState.Minimized;
        e.Handled = true;
    }

    private void OnOverlayCloseClicked(object sender, MouseButtonEventArgs e)
    {
        Close();
        e.Handled = true;
    }

    /// <summary>The compact overlay's header: the target shown (or "All targets"), how long the
    /// fight has run, and whether the capture is running.</summary>
    private void UpdateCompactOverlay(IReadOnlyList<DamageEvent> shownHits, (int Count, DateTime First, DateTime Last)? summary = null)
    {
        if (!_compactOverlay)
        {
            return;
        }

        OverlayTargetText.Text = _pvpOnly ? "PvP"
            : _selectedTargetId is int targetId ? ResolveDisplayName(targetId)
            : LocalizationManager.Instance["Main.FilterAllTargets"];
        var strings = LocalizationManager.Instance;
        OverlayModeText.Text = strings[_healMode ? "Main.Overlay.ModeHeal" : _takenMode ? "Main.Overlay.ModeTaken" : _allMode ? "Main.Overlay.ModeAll" : _allCompactMode ? "Main.Overlay.ModeAllCompact" : "Main.Overlay.ModeDamage"];
        OverlayRateHeader.Text = _allMode ? strings["Main.Overlay.ColDamage"] : _healMode ? "HPS" : _takenMode ? "DTPS" : "DPS";
        OverlayShareHeader.Text = _allMode ? strings["Main.Overlay.ColHeal"] : "%";
        OverlayTotalHeader.Text = _allMode ? strings["Main.Overlay.ColTaken"] : strings["Main.Overlay.ColTotal"];
        bool capturing = (_source as Aion2.Aion2PacketCombatSource)?.ServerFingerprint is not null;
        OverlayStateDot.Fill = _paused ? Brushes.Orange : capturing ? Brushes.LimeGreen : Brushes.Gray;
        OverlayTimeText.Text = summary is { Count: > 1 } sum ? (sum.Last - sum.First).ToString(@"m\:ss")
            : summary is null && shownHits.Count > 1 ? (shownHits.Max(h => h.Timestamp) - shownHits.Min(h => h.Timestamp)).ToString(@"m\:ss")
            : "";
        _lastShownHits = shownHits;
        OverlayHpBlock.Visibility = Visibility.Collapsed; // the boss's health is a window of its own now (CombatOverlayWindow.Part.Hp)
    }

    /// <summary>The fought boss's health in the compact overlay, with the HP check's verdict on the
    /// damage counted against it (see <see cref="HpCheck"/>). Only where the source reports hit
    /// points (Aion 2), for a selected target that is no player, and when Settings ask for it.</summary>
    private void UpdateOverlayBossHp(IReadOnlyList<DamageEvent> shownHits)
    {
        if (!_showBossHp || _pvpOnly || _selectedTargetId is not int targetId
            || _source?.Entities is not Aion2.Aion2EntityDirectory directory || directory.IsKnownPlayer(targetId))
        {
            OverlayHpBlock.Visibility = Visibility.Collapsed;
            return;
        }

        var onTarget = shownHits.Where(ev => ev.TargetObjectId == targetId).ToList();
        long highest = directory.HitPoints.HighestSeen(targetId) ?? 0;
        if (onTarget.Count == 0 || highest <= 0)
        {
            OverlayHpBlock.Visibility = Visibility.Collapsed;
            return;
        }

        DateTime start = onTarget.Min(h => h.Timestamp);
        DateTime end = onTarget.Max(h => h.Timestamp);
        var samples = directory.HitPoints.SamplesAround(targetId, start, end);
        if (samples.Count == 0)
        {
            OverlayHpBlock.Visibility = Visibility.Collapsed;
            return;
        }

        long hp = samples[^1].Hp;
        double percent = 100.0 * hp / highest;
        OverlayHpBar.Value = percent;
        OverlayHpText.Text = $"{PlayerRow.Compact(hp)} / {PlayerRow.Compact(highest)} · {percent.ToString("0", CultureInfo.CurrentCulture)}%";
        HpCheckResult? check = HpCheck.Evaluate(samples.Select(s => (s.At, s.Hp)).ToList(), onTarget.Concat(OwnHeals(targetId, start, end)).ToList(), highest);
        OverlayHpCheckText.Text = check is null ? ""
            : check.OverFullHealth || check.Verdict != HpCheckVerdict.Match ? "⚠ " + check.Ratio.ToString("P0", CultureInfo.CurrentCulture)
            : "✓";
        OverlayHpBlock.Visibility = Visibility.Visible;
    }

    /// <summary>While the compact overlay is up, keeps it on the newest fight in the Mob/Boss list
    /// (nobody can pick a target on it): the run whose latest hit is the most recent wins, a boss
    /// staying on screen while its adds die around it.</summary>
    private void FollowNewestRun()
    {
        if (_pvpOnly)
        {
            return;
        }

        ComboBoxItem? newest = null, newestBoss = null;
        DateTime newestHit = DateTime.MinValue, newestBossHit = DateTime.MinValue;
        var directory = _source?.Entities as Aion2.Aion2EntityDirectory;

        // One pass over the events for all entries of the list (the list may hold a dozen targets and runs): each event is matched against
        // the entries of its own target only, instead of every entry scanning every event.
        var entries = new List<(ComboBoxItem Item, MobBossTag Tag)>();
        var byTarget = new Dictionary<int, List<int>>();
        foreach (ComboBoxItem item in MobBossFilter.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is MobBossTag entryTag)
            {
                if (!byTarget.TryGetValue(entryTag.TargetId, out List<int>? slots))
                {
                    byTarget[entryTag.TargetId] = slots = new List<int>();
                }

                slots.Add(entries.Count);
                entries.Add((item, entryTag));
            }
        }

        var lastHits = new DateTime[entries.Count];
        foreach (DamageEvent ev in _aggregator.Events)
        {
            if (ev.IsHeal || !byTarget.TryGetValue(ev.TargetObjectId, out List<int>? slots))
            {
                continue;
            }

            foreach (int slot in slots)
            {
                MobBossTag tag = entries[slot].Tag;
                if (ev.Timestamp > lastHits[slot]
                    && (tag.WindowStart is not DateTime from || (ev.Timestamp >= from && ev.Timestamp <= tag.WindowEnd)))
                {
                    lastHits[slot] = ev.Timestamp;
                }
            }
        }

        for (int i = 0; i < entries.Count; i++)
        {
            (ComboBoxItem item, MobBossTag tag) = entries[i];
            DateTime last = lastHits[i];

            if (last > newestHit)
            {
                newestHit = last;
                newest = item;
            }

            if (last > newestBossHit && directory?.BossNpcIdOf(tag.TargetId) is not null)
            {
                newestBossHit = last;
                newestBoss = item;
            }
        }

        if (newestBoss is not null && newestBossHit >= newestHit - TimeSpan.FromSeconds(RunClusterGapSeconds))
        {
            newest = newestBoss;
        }

        if (newest is not null && !ReferenceEquals(newest, MobBossFilter.SelectedItem))
        {
            MobBossFilter.SelectedItem = newest;
        }
    }

    private (double Width, double Height, double MinWidth, double MinHeight)? _sizeBeforeCompactOverlay;

    /// <summary>While the compact overlay is up the window takes exactly the panel's size, so the
    /// panel can be scaled up past the normal window and nothing invisible sits over the game; the
    /// normal size comes back with the full window.</summary>
    private void FitWindowToCompactOverlay(bool fit)
    {
        if (fit && _sizeBeforeCompactOverlay is null)
        {
            _sizeBeforeCompactOverlay = (Width, Height, MinWidth, MinHeight);
            MinWidth = 0;
            MinHeight = 0;
            SizeToContent = SizeToContent.WidthAndHeight;
        }
        else if (!fit && _sizeBeforeCompactOverlay is { } before)
        {
            SizeToContent = SizeToContent.Manual;
            MinWidth = before.MinWidth;
            MinHeight = before.MinHeight;
            Width = before.Width;
            Height = before.Height;
            _sizeBeforeCompactOverlay = null;
        }
    }
}
