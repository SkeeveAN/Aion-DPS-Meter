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
    private bool _showBossHp;
    private TimetableWindow? _timetable;
    private bool _autoReset = true;
    /// <summary>Whose rows are shown: everybody, the own group, or the corps (two groups together).</summary>
    private enum MeterScope { All, Group, Corps }

    private MeterScope _scope = MeterScope.Group;

    // The players who fought the same monsters as the group, for the corps scope (see ComputeCorps).
    private HashSet<int> _corpsIds = new();
    private HpCheckResult? _lastHpCheck;

    /// <summary>Silence after which the next damage starts a new fight (MeterSettings.AutoReset and
    /// AutoResetSeconds).</summary>
    private TimeSpan _autoResetIdle = TimeSpan.FromSeconds(10);

    /// <summary>What the rows show: damage dealt, healing done, or damage taken (with deaths) -
    /// the Mode menu, the compact overlay's badge or the mode hotkey go round the three.</summary>
    private enum MeterMode { Damage, Heal, Taken }

    private MeterMode _mode;

    private bool _healMode => _mode == MeterMode.Heal;

    private bool _takenMode => _mode == MeterMode.Taken;

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


    // Local fight history (History/). The store is opened once and shared between the recorder
    // (files finished fights from the live event list) and the history window. _historyMode is
    // true while a past fight is loaded into the grid instead of the live session - the recorder
    // stays quiet then, and the banner offers the way back.
    private FightStore? _fightStore;
    private FightRecorder? _fightRecorder;
    private FightHistoryWindow? _fightHistoryWindow;
    private bool _recordFightHistory = true;
    private bool _historyMode;

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
    private CharacterWindow? _characterWindow;
    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>Five minutes, per the user. GitHub's anonymous API allows 60 requests an hour per
    /// IP, so 12 is comfortably inside it even with a second client running alongside.</summary>
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromMinutes(5) };

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
            _ = RunUpdateCheck(announceResult: true);
        }

        var settings = MeterSettings.Load();

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

        RestoreWindowGeometry(settings);
        StartCapture(settings);
        InitializeFightHistory(settings);
        RefreshCharacterSettings(settings);
        TryRestoreRestartSnapshot();
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
        _autoReset = settings.AutoReset;
        _autoResetIdle = TimeSpan.FromSeconds(Math.Clamp(settings.AutoResetSeconds, 1, 600));
        _scope = Enum.TryParse(settings.ViewScope, true, out MeterScope scope) ? scope : MeterScope.Group;
        UpdateScopeControls();
        _compactOverlay = string.Equals(settings.OverlayStyle, "Compact", StringComparison.OrdinalIgnoreCase);
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
    private void StartCapture(MeterSettings settings)
    {
        _pollTimer.Stop();
        ReplaceSource(null);

        var source = new Aion2PacketCombatSource(Aion2Protocol.Load(), settings.CaptureAdapterId, settings.Aion2CharacterName, Aion2CharacterStore.DefaultPath);
        // Remember the name the stream reveals, so the next (solo) session knows it without a party.
        source.LocalNameLearned += learned =>
        {
            var current = MeterSettings.Load();
            if (string.IsNullOrWhiteSpace(current.Aion2CharacterName))
            {
                current.Aion2CharacterName = learned;
                current.Save();
            }
        };
        ReplaceSource(source);
        // The upload entries appear once the own character is known (see RefreshUploadAvailability).
        if (source.Entities is Aion2EntityDirectory entities)
        {
            entities.CharacterChanged += _ => Dispatcher.BeginInvoke(new Action(() =>
            {
                RefreshUploadAvailability();
                ScheduleOwnProfileUpload();
            }));
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

    // Per fight: which real buffs (not damage/heal skills) each RECIPIENT received, for the web
    // frontend's "Buffs" column (see BuildEncounterUpload) - a parallel side-channel list, same
    // shape/reasoning as _lootRows above, since a buff cast is neither a DamageEvent nor something
    // LiveAggregator's damage/heal model has any use for. Keyed by recipient rather than caster so
    // a Cleric/Chanter's group-wide buff shows up on every party member it actually landed on, not
    // only on whoever cast it.
    private readonly List<(DateTime Timestamp, int RecipientId, string Skill)> _buffCasts = new();

    /// <summary>Individually named items the user wants tracked regardless of grade, beyond the
    /// Godstone/Design/Recipe prefix rule -- exact names, not a broader pattern: e.g. "Bundle" on
    /// its own would also match 1.379 unrelated crafting-material items in ItemDatabase (Log
    /// Bundle, Fiber Bundle, ...), which is exactly the "every single piece of trash loot" the
    /// user asked to stop tracking in the first place.</summary>
    private static readonly HashSet<string> AlwaysTrackedItemNames = new()
    {
        "Veteran's Composite Manastone Bundle",
        // Stahlmauerbastion's own reward box - Legend grade (confirmed in
        // assets/items/items_origincdx_4x.json, id 188052600), so it would otherwise fall below
        // the Unique+ threshold below just like the Manastone Bundle above.
        "Beritran Supply Box",
    };

    /// <summary>Slot categories the user calls "Schmuck" for the 65er/60er jewelry rule above -
    /// English substrings since ItemDatabase's names are all English (see its own remarks).</summary>
    private static readonly string[] JewelrySlotKeywords = { "Belt", "Ring", "Earring", "Necklace", "Helm", "Helmet" };

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
        });
        if (failed.Count > 0)
        {
            ShowUploadStatus("Hotkey already in use by another program: " + string.Join(", ", failed));
        }
    }

    private DateTime _lastPreviewInvalidate = DateTime.MinValue;

    private void OnPollTimerTick(object? sender, EventArgs e)
    {
        // The Alt+Tab picture of the overlay (see RenderOverlayPreview) is redrawn on request; saying
        // it is stale every couple of seconds keeps it close to what the overlay shows.
        if (_hideUiActive && DateTime.UtcNow - _lastPreviewInvalidate > TimeSpan.FromSeconds(2))
        {
            _lastPreviewInvalidate = DateTime.UtcNow;
            _overlay?.InvalidatePreview();
        }

        CombatBatch batch = _source?.Poll(_paused) ?? CombatBatch.Empty;
        _avoids.AddRange(batch.Avoids);
        _kills.AddRange(batch.Kills);
        NoteBossKills(batch.Kills);
        IReadOnlyList<DamageEvent> events = batch.Damage;

        // Hits a summon dealt before its owner was known (the meter started mid-fight) go to the
        // owner once found, instead of staying on a "Player #id" row.
        if (_source?.Entities is Aion2.Aion2EntityDirectory resolvedIn)
        {
            foreach ((int summon, int owner) in resolvedIn.DrainResolvedOwners())
            {
                _aggregator.Reattribute(summon, owner);
            }
        }
        if (_autoReset && !_historyMode && (StartsNewFight(events) || StartsNewBossPull(events)))
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

        // Once a second, an always-on-top meter goes back to the front: a borderless full-screen
        // game can take the front of the topmost band when it gets the focus back.
        if (Topmost && IsVisible)
        {
            NativeOverlay.KeepOnTop(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        }

        // Every five seconds is plenty: a fight only counts as finished 120 s after its last hit.
        if (++_historyTickCounter >= 5)
        {
            _historyTickCounter = 0;
            RecordFinishedFights(flushAll: false);
        }
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

    private void InitializeFightHistory(MeterSettings settings)
    {
        _recordFightHistory = settings.RecordFightHistory;
        if (_fightStore is not null)
        {
            return;
        }

        try
        {
            _fightStore = new FightStore(FightStore.DefaultPath);
            _fightStore.Prune(settings.HistoryRetentionDays, settings.HistoryMaxFights);
            _fightRecorder = new FightRecorder(_fightStore);
        }
        catch (Exception ex)
        {
            // A locked or corrupt history file must never keep the meter itself from running.
            ShowUploadStatus($"Fight history unavailable: {ex.Message}");
        }
    }

    /// <summary>Files every fight that has been silent long enough (or all open ones on flush -
    /// Clear and exit) into the local history. Never while a past fight is being viewed.</summary>
    private void RecordFinishedFights(bool flushAll)
    {
        if (_historyMode || !_recordFightHistory || _fightRecorder is null || _aggregator.Events.Count == 0)
        {
            return;
        }

        try
        {
            int written = _fightRecorder.Tick(_aggregator.Events, DateTime.Now, BuildFightContext(), flushAll);
            if (written > 0 && _fightHistoryWindow is not null)
            {
                _fightHistoryWindow.Refresh();
            }
        }
        catch (Exception ex)
        {
            ShowUploadStatus($"Fight history: {ex.Message}");
        }
    }

    /// <summary>The recorder describes participants exactly the way the grid does - same name,
    /// class and side resolution, handed over as callbacks.</summary>
    private FightContext BuildFightContext() => new(
        NameOf: ResolveDisplayName,
        ClassOf: ResolveClassName,
        FactionOf: id => _rowsByObjectId.GetValueOrDefault(id)?.Faction ?? "",
        IsPlayer: IsPlayerName,
        IsSelf: id => _source?.Entities.IsLocalPlayer(id) == true,
        IsEnemy: id => _rowsByObjectId.GetValueOrDefault(id)?.IsEnemy ?? false,
        // No target is filtered by name: short pulls are kept out by the recorder's minimum
        // duration and the history's retention cap.
        IsIgnoredTarget: _ => false,
        Game: "aion2",
        ServerName: Aion2ServerName());

    private void OnAppMenuClicked(object sender, RoutedEventArgs e)
    {
        // AppMenu has no child MenuItems anymore (AppMenuFlyoutStyle hand-authors the Popup
        // content instead), so WPF assigns it MenuItemRole.TopLevelItem instead of
        // TopLevelHeader and never opens IsSubmenuOpen on click by itself - toggle it here.
        AppMenu.IsSubmenuOpen = !AppMenu.IsSubmenuOpen;
        e.Handled = true;
    }

    private void OnFightHistoryClicked(object sender, RoutedEventArgs e)
    {
        // Closes the App-menu flyout (AppMenuFlyoutStyle) when reached from there - a no-op
        // when this fires from anywhere else, since IsSubmenuOpen is already false then.
        AppMenu.IsSubmenuOpen = false;
        if (_fightStore is null)
        {
            ShowUploadStatus("Fight history is unavailable - see the earlier notice.");
            return;
        }

        if (_fightHistoryWindow is not null)
        {
            _fightHistoryWindow.Refresh();
            _fightHistoryWindow.Activate();
            return;
        }

        _fightHistoryWindow = new FightHistoryWindow(_fightStore) { Owner = this };
        _fightHistoryWindow.LoadRequested += EnterHistoryMode;
        _fightHistoryWindow.Closed += (_, _) => _fightHistoryWindow = null;
        _fightHistoryWindow.Show();
    }

    /// <summary>
    /// Shows a stored fight in the grid instead of the live session. The live source is swapped
    /// for a FakeCombatSource that knows the fight's names, and the stored events are remapped
    /// onto that source's ids (ids are per session, only names travel). Nothing is recorded while
    /// this is on; the banner in the status row is the way back to live.
    /// </summary>
    private void EnterHistoryMode(FightDetail detail)
    {
        RecordFinishedFights(flushAll: true);
        _historyMode = true;

        var replay = new FakeCombatSource();
        var idMap = detail.Names.ToDictionary(kv => kv.Key, kv => replay.Entities.GetOrAssignId(kv.Value));
        ReplaceSource(replay);
        ClearDamageData();

        foreach (FightParticipant participant in detail.Participants)
        {
            _playerIdentities[replay.Entities.GetOrAssignId(participant.Name)] = (participant.Name, participant.ClassName, 0);
        }

        _aggregator.IngestEvents(detail.Events.Select(ev => ev with
        {
            SourceObjectId = idMap.GetValueOrDefault(ev.SourceObjectId, ev.SourceObjectId),
            TargetObjectId = idMap.GetValueOrDefault(ev.TargetObjectId, ev.TargetObjectId),
        }));

        HistoryBanner.Text = string.Format(LocalizationManager.Instance["Main.HistoryBanner"], detail.Summary.TargetName, detail.Summary.StartedAt.ToString("g"));
        HistoryBanner.Visibility = Visibility.Visible;
        RefreshRows();
        Activate();
    }

    private void OnHistoryBannerClicked(object sender, MouseButtonEventArgs e) => ExitHistoryMode();

    private void ExitHistoryMode()
    {
        if (!_historyMode)
        {
            return;
        }

        _historyMode = false;
        HistoryBanner.Visibility = Visibility.Collapsed;
        ClearDamageData();
        StartCapture(MeterSettings.Load());
        RefreshRows();
    }

    /// <summary>The name the packet stream gave the object (player name, boss name from its NPC id,
    /// or "Class #id" until a player's name arrives); its hex id when nothing is known.</summary>
    private string ResolveDisplayName(int objectId) =>
        _source?.Entities.NameFor(objectId) ?? $"0x{objectId:X8}";

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _overlay = new NativeOverlay(this);
        CreateTrayIcon();
        _hotkeys = new GlobalHotkeys(this);
        _hotkeys.Pressed += action => Dispatcher.Invoke(() => OnHotkeyPressed(action), System.Windows.Threading.DispatcherPriority.Input);
        ApplyHotkeys(MeterSettings.Load());
        ApplyTimetable(MeterSettings.Load());
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
        RecordFinishedFights(flushAll: true);
        _fightStore?.Dispose();
        _source?.Dispose();
        _overlay?.Dispose();
        _hotkeys?.Dispose();
        _timetable?.Close();
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
        RefreshMobBossFilterItems();

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
        var filtered = _pvpOnly
            ? damageOnly.Where(ev => IsPlayerName(ev.TargetObjectId)).ToList()
            : _selectedTargetId is int targetId
                ? ShownFightHits(damageOnly, targetId)
                : RestrictToEngagedTargets(damageOnly.ToList());

        _corpsIds = _scope != MeterScope.All ? ComputeCorps(filtered) : new HashSet<int>();

        // A pure healer never hit the selected target, so `filtered` holds none of their events, yet
        // they belong in the list (and in an upload's roster). Heals inside the shown window by
        // players the meter has identified add them; bounded to that window so someone who healed
        // an hour ago elsewhere does not come back at a permanent 0.
        (DateTime, DateTime)? filteredSpan = filtered.Count > 0
            ? (filtered.Min(ev => ev.Timestamp), filtered.Max(ev => ev.Timestamp))
            : null;

        if (_healMode && !_pvpOnly)
        {
            RefreshHealRows(filteredSpan);
            RankRows();
            UpdateHpCheck(OnSelectedTarget(filtered));
            UpdateCompactOverlay(filtered);
            return;
        }

        if (_takenMode && !_pvpOnly)
        {
            RefreshTakenRows(filteredSpan);
            RankRows();
            UpdateHpCheck(OnSelectedTarget(filtered));
            UpdateCompactOverlay(filtered);
            return;
        }

        var healSourceIds = filteredSpan is (DateTime spanStart, DateTime spanEnd)
            ? _aggregator.Events
                .Where(ev => ev.IsHeal && ev.Timestamp >= spanStart && ev.Timestamp <= spanEnd && IsPlayerName(ev.SourceObjectId))
                .Select(ev => ev.SourceObjectId)
            : Enumerable.Empty<int>();

        var sourceIds = filtered.Select(ev => ev.SourceObjectId).Distinct()
            .Union(healSourceIds)
            .Where(IsPlayerName)
            .ToList();

        // Damage RECEIVED per row (PlayerRow.DamageTaken): everything that hit them inside the
        // shown window - from the selected target only when one is picked, so the figure answers
        // "how much did this boss put on whom", the same scope BuildEncounterUpload's damageTaken
        // uses for the website's distribution chart.
        var damageTakenById = (filteredSpan is (DateTime takenStart, DateTime takenEnd)
                ? damageOnly.Where(ev => ev.Timestamp >= takenStart && ev.Timestamp <= takenEnd)
                : Enumerable.Empty<DamageEvent>())
            .Where(ev => _pvpOnly || _selectedTargetId is not int selectedAttacker || ev.SourceObjectId == selectedAttacker)
            .GroupBy(ev => ev.TargetObjectId)
            .ToDictionary(g => g.Key, g => g.Sum(ev => ev.Amount));

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

        foreach (int sourceId in sourceIds)
        {
            if (!_rowsByObjectId.TryGetValue(sourceId, out var row))
            {
                row = new PlayerRow(sourceId);
                _rowsByObjectId[sourceId] = row;
                _rows.Add(row);
            }

            // Applied every refresh, not just at creation: an active-character switch detected by
            // UpdateActiveCharacterFromSkill (or a newly detected class from UpdateOtherPlayerClass)
            // must update an already-existing row immediately, not just rows created after the fact.
            ApplyIdentity(row, sourceId);

            row.Damage = filtered.Where(ev => ev.SourceObjectId == sourceId).Sum(ev => ev.Amount);
            // PVP mode's rate must come from the SAME PVP-only event set row.Damage above was
            // just summed from, not the unfiltered full history - same reasoning as the
            // Mob/Boss-filtered iDPS case below (see BuildEncounterUpload's own remarks on the
            // totalDamage/idps-must-share-one-source-of-truth bug this pattern once caused).
            // `filtered`, not `_aggregator.Events`, in the targeted branch too: a specific
            // numbered run's iDPS must come from that same run's own window, the same
            // one-source-of-truth reasoning the PVP branch above already follows (see
            // BuildEncounterUpload's own remarks on the totalDamage/idps bug this once caused).
            row.Dps = _pvpOnly
                ? DpsCalculator.AllDpsWallClock(filtered, sourceId)
                : _selectedTargetId is int t
                    ? BossFight.Dps(filtered, t, sourceId)
                    : DpsCalculator.AllDpsWallClock(_aggregator.Events, sourceId);
            row.DamageTaken = damageTakenById.GetValueOrDefault(sourceId);
            row.Deaths = 0;
            row.ShowShareBar = _showShareBars;
            row.ShowDamageTaken = _showDamageTaken;

            row.Faction = (_source?.Entities as Aion2.Aion2EntityDirectory)?.FactionOf(sourceId) ?? "";
        }

        // Rank and share are relative to what is on screen, so they are settled once every row's
        // damage for this refresh is known - and by damage, not by the grid's current sort order.
        RankRows();
        UpdateHpCheck(OnSelectedTarget(filtered));
        UpdateCompactOverlay(filtered);
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
        if (_scope == MeterScope.All || _pvpOnly || _source?.Entities is not Aion2.Aion2EntityDirectory directory
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
        return directory.PartyNames.Any(n => n != own);
    }

    private bool IsGroupMember(int id, Aion2.Aion2EntityDirectory directory)
    {
        if (directory.IsLocalPlayer(id) || directory.InferLocalPlayer() == id)
        {
            return true;
        }

        string name = ResolveDisplayName(id);
        return directory.PartyNames.Contains(name) || name == directory.LocalCharacter?.Name;
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

    private void OnScopeMenuClicked(object sender, RoutedEventArgs e) =>
        SetScope(sender == ScopeAllItem ? MeterScope.All : sender == ScopeCorpsItem ? MeterScope.Corps : MeterScope.Group);

    private void OnOverlayScopeClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse(tag, out MeterScope scope))
        {
            SetScope(scope);
        }

        e.Handled = true;
    }

    /// <summary>Marks the chosen scope in the Mode menu and on the overlay's three-way switch.</summary>
    private void UpdateScopeControls()
    {
        ScopeAllItem.IsChecked = _scope == MeterScope.All;
        ScopeGroupItem.IsChecked = _scope == MeterScope.Group;
        ScopeCorpsItem.IsChecked = _scope == MeterScope.Corps;
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
            check = HpCheck.Evaluate(readings, targetHits, directory.HitPoints.HighestSeen(targetId) ?? 0);
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
        long shownTotal = _rows.Sum(r => r.Damage);
        long shownMax = _rows.Count > 0 ? _rows.Max(r => r.Damage) : 0;
        int rank = 0;
        foreach (PlayerRow row in _rows.OrderByDescending(r => r.Damage))
        {
            row.Rank = ++rank;
            row.SharePercent = shownTotal > 0 ? 100.0 * row.Damage / shownTotal : 0;
            row.FillPercent = shownMax > 0 ? 100.0 * row.Damage / shownMax : 0;
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
        var heals = _aggregator.Events
            .Where(ev => ev.IsHeal && IsPlayerName(ev.SourceObjectId)
                && directory?.SummonOwnerOf(ev.TargetObjectId) is null
                && (span is not (DateTime from, DateTime to) || (ev.Timestamp >= from && ev.Timestamp <= to)))
            .ToList();
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
            row.Damage = mine.Sum(ev => ev.Amount);
            double seconds = spanSeconds ?? (mine.Max(ev => ev.Timestamp) - mine.Min(ev => ev.Timestamp)).TotalSeconds;
            row.Dps = seconds > 0 ? row.Damage / seconds : null;
            row.DamageTaken = 0;
            row.Deaths = 0;
            row.ShowShareBar = _showShareBars;
            row.ShowDamageTaken = false;
            row.Faction = directory?.FactionOf(sourceId) ?? "";
        }
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
            row.Damage = mine.Sum(ev => ev.Amount);
            double seconds = spanSeconds ?? (mine.Max(ev => ev.Timestamp) - mine.Min(ev => ev.Timestamp)).TotalSeconds;
            row.Dps = seconds > 0 ? row.Damage / seconds : null;

            var readings = directory is null || span is not (DateTime from, DateTime to)
                ? new List<(DateTime, long)>()
                : directory.HitPoints.SamplesAround(targetId, from, to + Deaths.BlowWindow).Select(s => (s.At, s.Hp)).ToList();
            var deaths = Deaths.Find(targetId, readings, mine);
            _deathsById[targetId] = deaths;
            row.Deaths = deaths.Count;
            row.DamageTaken = 0;
            row.ShowShareBar = _showShareBars;
            row.ShowDamageTaken = false;
            row.Faction = directory?.FactionOf(targetId) ?? "";
        }
    }


    /// <summary>A player is an object the meter has seen casting a class skill or whose "appeared"
    /// frame it read; everything else is an NPC (shown by its hex id or boss name).</summary>
    private bool IsPlayerName(int id) =>
        _source?.Entities is Aion2.Aion2EntityDirectory entities && entities.IsKnownPlayer(id);

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
    private void RefreshMobBossFilterItems()
    {
        var knownIds = _mobBossEntries.Select(entry => entry.TargetId).ToHashSet();
        bool added = false;

        foreach (int targetId in _aggregator.Events.Where(ev => !ev.IsHeal).Select(ev => ev.TargetObjectId).Distinct())
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
        UploadBossMenuItem.Visibility = hasBossData || hasProfiles ? Visibility.Visible : Visibility.Collapsed;
        UploadRunMenuItem.Visibility = hasBossData ? Visibility.Visible : Visibility.Collapsed;
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

        var clusters = new List<List<DamageEvent>>();
        foreach (DamageEvent hit in hits)
        {
            if (clusters.Count > 0 && (hit.Timestamp - clusters[^1][^1].Timestamp).TotalSeconds <= RunClusterGapSeconds)
            {
                clusters[^1].Add(hit);
            }
            else
            {
                clusters.Add(new List<DamageEvent> { hit });
            }
        }

        if (clusters.Count == 1)
        {
            yield return (new MobBossTag(targetId, null, null), name, clusters[0].Sum(e => e.Amount));
            yield break;
        }

        for (int i = 0; i < clusters.Count; i++)
        {
            List<DamageEvent> cluster = clusters[i];
            var tag = new MobBossTag(targetId, cluster[0].Timestamp, cluster[^1].Timestamp);
            yield return (tag, $"{name} #{i + 1}", cluster.Sum(e => e.Amount));
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
            : _pvpOnly ? "Damage / DPS (PvP)" : _selectedTargetId is int ? "Damage / iDPS" : "Damage / DPS";
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

        // Kept separate from the UTC startedAt/endedAt below (those are for the outgoing payload's
        // own fields): heals never target the boss, so they can only be scoped to this encounter by
        // time window, and that window has to be compared against DamageEvent.Timestamp's own
        // (local) DateTimeKind, not a UTC-converted copy.
        DateTime windowStart = targetHits.Min(e => e.Timestamp);
        DateTime windowEnd = targetHits.Max(e => e.Timestamp);
        return BuildEncounterUpload(targetId, serverFingerprint, serverName, targetHits, windowStart, windowEnd);
    }

    /// <summary>
    /// Same payload construction, but with the target's hits and the time window supplied by the
    /// caller instead of always spanning that target id's ENTIRE history. Needed because
    /// a boss farmed multiple times in one session can keep the same target id across every
    /// kill - a headless clustered upload (see RunHeadlessClusteredUploadAsync) has to pass in one kill-cluster's hits/window at
    /// a time so each real fight gets its own upload instead of one merged across the whole file.
    /// The 3-argument overload above is just this one, called with that target's full history.
    /// </summary>
    private static readonly TimeSpan BuffPrePullGrace = TimeSpan.FromSeconds(30);

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

        var participants = new List<ParticipantUpload>();
        // Aion 2: the name only exists for players whose "appeared" frame the client saw, and the
        // class for those seen casting - an unnamed "Player #id" or a row without a class the backend
        // knows would make the backend reject the whole upload (class check) or store a nameless
        // player, so only complete rows go in. The own character is told by its name too: the game
        // gives a new object id on a map change, and the old one stops being "local".
        var aion2Directory = _source?.Entities as Aion2.Aion2EntityDirectory;
        string? ownName = aion2Directory?.LocalCharacter?.Name;
        foreach (PlayerRow row in _rows)
        {
            if (aion2Directory is not null
                && (row.Name.StartsWith("Player #", StringComparison.Ordinal)
                    || row.Name.StartsWith("0x", StringComparison.Ordinal)
                    || !ClassCatalog.IsKnownClass(row.ClassName)))
            {
                continue;
            }

            var hitsOnBoss = targetHits.Where(e => e.SourceObjectId == row.ObjectId).ToList();
            // Per the user: heals must be uploaded alongside damage - a pure healer who never hit
            // the boss would otherwise be silently dropped from the roster entirely, so a row
            // survives on EITHER contribution, not damage alone.
            var healsBySelf = _aggregator.Events
                .Where(e => e.IsHeal && e.SourceObjectId == row.ObjectId
                    && e.Timestamp >= windowStart && e.Timestamp <= windowEnd)
                .ToList();
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
                && !_aggregator.Events.Any(e => !e.IsHeal && e.SourceObjectId == targetId && e.TargetObjectId == row.ObjectId
                    && e.Timestamp >= windowStart && e.Timestamp <= windowEnd))
            {
                continue;
            }

            bool isSelf = _source?.Entities.IsLocalPlayer(row.ObjectId) == true || (ownName is not null && row.Name == ownName);
            // targetHits, not _aggregator.Events: TargetIDps derives its own duration from
            // whichever events it's given, so passing the full history back in here would silently
            // widen a clustered upload's iDPS window back out to the target id's entire history -
            // targetHits is already scoped to exactly this fight (see the two BuildEncounterUpload
            // overloads above).
            double idps = DpsCalculator.TargetIDps(targetHits, targetId, row.ObjectId) ?? 0;
            var skills = SkillBreakdown.For(hitsOnBoss)
                .Select(s => new SkillUsageUpload(s.Skill, s.Hits, s.CritHits, s.Total, s.Min, s.Max))
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
            long damageTaken = _aggregator.Events
                .Where(e => !e.IsHeal && e.SourceObjectId == targetId && e.TargetObjectId == row.ObjectId
                    && e.Timestamp >= windowStart && e.Timestamp <= windowEnd)
                .Sum(e => e.Amount);

            // Real reinforcements this row RECEIVED during the fight -
            // capped and ranked by count, same "top N" shape SkillBreakdown already uses for
            // damage/heal skills above, just counting applications instead of summing damage. Keyed
            // by recipient, not caster, so a Cleric/Chanter group buff shows up on every party
            // member's own row, not only on whoever cast it.
            //
            // The lower bound is windowStart minus BuffPrePullGrace, not windowStart itself - found
            // from a real run where a pre-pull buff (Daevic Fury I) landed one second before the
            // first recorded hit and was silently dropped from the upload entirely (not just
            // filtered by isLongLastingBuff server-side - it never made it into the payload at
            // all). Buffing right as you engage, a fraction of a second before your first hit
            // actually lands, is completely normal, so the strict windowStart the damage/heal
            // events above use is too tight specifically for buffs. Not unbounded, though: this
            // target id persists across every future kill of the same boss in this session (see
            // RunHeadlessClusteredUploadAsync's own remarks), so a buff from a genuinely separate,
            // much earlier kill must not bleed into this one - 30s is generous enough for any real
            // pre-pull buff sequence without reaching back that far.
            var buffs = _buffCasts
                .Where(b => b.RecipientId == row.ObjectId
                    && b.Timestamp >= windowStart - BuffPrePullGrace && b.Timestamp <= windowEnd)
                .GroupBy(b => b.Skill)
                .Select(g => new BuffUsageUpload(g.Key, g.Count()))
                .OrderByDescending(b => b.Casts)
                .Take(8)
                .ToList();

            participants.Add(new ParticipantUpload(
                row.Name, row.ClassName, row.Faction, isSelf,
                totalDamage, idps, idps, totalHealing, hps, skills, healSkills, damageTaken, buffs,
                aion2Directory?.GuildOf(ProfileIdOf(row)),
                BuildProfileUpload(ProfileIdOf(row))));
        }

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
            BossNpcId: BossNpcIdOf(targetId));
    }

    /// <summary>Hides the Class dropdown's entries that are no Aion 2 class (the static XAML list is
    /// wider than the game's roster); an unknown selection falls back to "All".</summary>
    private void ApplyClassFilterAvailability()
    {
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

    /// <summary>The Mob/Boss filter target whose last hit is the most recent, i.e. whichever boss
    /// was just fought - used as the "Upload current boss" default when the filter is still on
    /// "All" rather than forcing a manual pick first (see OnUploadCurrentBossClicked).</summary>
    private int? MostRecentlyFoughtTargetId()
    {
        var knownIds = _mobBossEntries.Select(entry => entry.TargetId).ToHashSet();
        return _aggregator.Events
            .Where(ev => !ev.IsHeal && knownIds.Contains(ev.TargetObjectId))
            .GroupBy(ev => ev.TargetObjectId)
            .OrderByDescending(g => g.Max(ev => ev.Timestamp))
            .Select(g => (int?)g.Key)
            .FirstOrDefault();
    }

    /// <summary>Uploads the boss the Mob/Boss filter is currently showing, or - per the user,
    /// "current boss" should mean the one just fought, not force a manual filter pick first -
    /// whichever boss most recently took damage, if the filter is still on "All".</summary>
    private async void OnUploadCurrentBossClicked(object sender, RoutedEventArgs e)
    {
        if ((_selectedTargetId ?? MostRecentlyFoughtTargetId()) is not int targetId)
        {
            // No boss fight, but Aion 2 still has characters worth uploading (per the user).
            if (_source?.Entities is Aion2.Aion2EntityDirectory)
            {
                await UploadPlayersOnlyAsync();
                return;
            }

            ShowUploadStatus("No boss fights recorded yet.");
            return;
        }

        if (ResolveServerIdentity() is not (string fingerprint, var displayName))
        {
            ShowUploadStatus(ServerNotIdentified);
            return;
        }

        var payload = BuildEncounterUpload(targetId, fingerprint, displayName);
        if (payload is null)
        {
            ShowUploadStatus("Nothing recorded for this boss yet.");
            return;
        }

        ShowUploadStatus("Uploading...");
        UploadResult result = await UploadClient.SendAsync(payload);
        ShowUploadStatus(result.Success ? "Uploaded." : $"Upload failed: {result.Error}");
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
        if (Headless || !MeterSettings.Load().AutoUploadProfile)
        {
            return;
        }

        if (_profileUploadTimer is null)
        {
            _profileUploadTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _profileUploadTimer.Tick += async (_, _) =>
            {
                _profileUploadTimer.Stop();
                await UploadOwnProfileAsync();
            };
        }

        _profileUploadTimer.Stop();
        _profileUploadTimer.Start();
    }

    // ---- a boss fight goes online by itself (opt-in) --------------------------------------------
    // A boss that died is uploaded a few seconds later, so the last hits and the kill frame have all
    // arrived. Same payload as the Upload button; the server merges what the group's members send.

    private readonly HashSet<int> _bossesToUpload = new();
    private System.Windows.Threading.DispatcherTimer? _bossUploadTimer;

    private void NoteBossKills(IReadOnlyList<KillEvent> kills)
    {
        if (kills.Count == 0 || Headless || _historyMode || !MeterSettings.Load().AutoUploadBoss)
        {
            return;
        }

        foreach (KillEvent kill in kills)
        {
            if (!kill.VictimIsPlayer && BossNpcIdOf(kill.VictimObjectId) is not null)
            {
                _bossesToUpload.Add(kill.VictimObjectId);
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

        _bossUploadTimer.Stop();
        _bossUploadTimer.Start();
    }

    private async Task UploadKilledBossesAsync()
    {
        var targetIds = _bossesToUpload.ToList();
        _bossesToUpload.Clear();
        if (ResolveServerIdentity() is not (string fingerprint, var displayName))
        {
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

    private ProfilesUploadRequest? BuildProfilesUpload(string fingerprint, string? displayName, bool ownOnly = false)
    {
        if (_source?.Entities is not Aion2.Aion2EntityDirectory directory || directory.LocalCharacter is not { } local)
        {
            return null;
        }

        var participants = new List<ProfileParticipantUpload>();
        void Add(int id, string name, string className, bool isSelf)
        {
            if (BuildProfileUpload(id) is { } profile)
            {
                participants.Add(new ProfileParticipantUpload(name, className, "", isSelf, directory.GuildOf(id), profile));
            }
        }

        string localClass = directory.ClassOf(local.CombatId) ?? Aion2.Protocol.Aion2SkillNames.ClassFromCode(local.ClassCode) ?? "";
        if (localClass.Length == 0)
        {
            return null;
        }

        Add(local.CombatId, local.Name, localClass, true);
        foreach (int id in ownOnly ? Array.Empty<int>() : directory.SeenProfileIds())
        {
            string? name = directory.NameFor(id);
            string? className = directory.ClassOf(id)
                ?? (directory.SeenProfileOf(id) is { ClassId: int classId } ? Aion2.Protocol.Aion2SkillNames.ClassFromCode(classId * 4 + 1) : null);
            // Anonymous "Player #id" entries have no name to attach a profile to.
            if (id == local.CombatId || name is null || name.StartsWith("Player #") || className is null)
            {
                continue;
            }

            Add(id, name, className, false);
        }

        return participants.Count == 0
            ? null
            : new ProfilesUploadRequest(AppVersion.Text, fingerprint, displayName, participants.Take(60).ToList());
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
        RecordFinishedFights(flushAll: true);
        _historyMode = true;

        var replay = new FakeCombatSource();
        var idMap = session.Names.ToDictionary(kv => kv.Key, kv => replay.Entities.GetOrAssignId(kv.Value));
        int Remap(int id) => idMap.GetValueOrDefault(id, id);

        ReplaceSource(replay);
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
    private async Task RunUpdateCheck(bool announceResult)
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

        try
        {
            await UpdateService.DownloadAsync(update);
        }
        catch (Exception ex)
        {
            UpdateNotice.Visibility = Visibility.Collapsed;
            if (announceResult)
            {
                ThemedMessageBox.Show(this, $"The update could not be downloaded.\n\n{ex.Message}",
                    "Check for updates", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        _downloadedUpdate = update;
        UpdateNotice.Text = $"Update {version} ready - click to restart";

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
                id => _source?.Entities.NameFor(id), deaths: _deathsById.GetValueOrDefault(row.ObjectId), taken: true)
            {
                Owner = this,
            }.Show();
            return;
        }

        var mine = _aggregator.Events.Where(ev => ev.SourceObjectId == row.ObjectId).ToList();

        new PlayerDetailsWindow(row.Name, row.ClassName, row.Faction, mine,
            id => _source?.Entities.NameFor(id) ?? ResolveDisplayName(id), heals: _healMode && !_pvpOnly)
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
        // Whatever is still open is over now - file it before it is gone.
        RecordFinishedFights(flushAll: true);
        _fightRecorder?.Reset();

        _aggregator.Clear();
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
    /// single comma-separated run (not one row per line) of "rank, name, total [dps]" tuples,
    /// e.g. "1, Mitzuhiko, 3.123.456 [3.145], 2, Mitzuhiki, 3.123.455 [3.144]", ranked by damage
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
            parts.Add($"{i + 1}, {row.Name}, {damageText} [{dpsText}]");
        }

        return ChunkChatLineParts(parts, ChatLineCharLimit);
    }

    /// <summary>
    /// Copy's payload in the Damage view: one line, "Name Damage (DPS)" per entry, entries joined
    /// by ", " and ranked by damage descending regardless of how the grid is currently sorted --
    /// format specified by the user for pasting straight into the Aion chat box. Deliberately
    /// leaner than BuildDmgRankingText (the ".dmg" command's payload, which prefixes each entry
    /// with its rank and brackets the DPS): both stay as their own specified formats rather than
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
            _settingsWindow.Activate();
            return;
        }

        var settings = MeterSettings.Load();
        _settingsWindow = new SettingsWindow(settings) { Owner = this };
        _settingsWindow.Saved += () =>
        {
            settings.Save();
            ThemeManager.Apply(Application.Current, settings.Theme, settings.FontSize); // repaints every open window
            ApplyHotkeys(settings); // possibly changed keys
            ApplyTimetable(settings);
            ExitHistoryMode(); // a viewed past fight must not survive a source change underneath it
            StartCapture(settings); // possibly a new/changed AionInstallFolder
            InitializeFightHistory(settings); // possibly toggled recording
            RefreshCharacterSettings(settings); // possibly a new/changed character list or active one
            ApplyClassFilterAvailability(); // possibly a new/changed install folder or server display name
            RefreshRows();
        };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
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

    private void OnModeClicked(object sender, RoutedEventArgs e)
    {
        SetMode(sender == HealModeItem ? MeterMode.Heal : sender == TakenModeItem ? MeterMode.Taken : MeterMode.Damage);
    }

    private void SetMode(MeterMode mode)
    {
        _mode = mode;
        DamageModeItem.IsChecked = mode == MeterMode.Damage;
        HealModeItem.IsChecked = mode == MeterMode.Heal;
        TakenModeItem.IsChecked = mode == MeterMode.Taken;
        UpdateDpsColumnHeader();
        RefreshRows();
    }

    /// <summary>Damage, then heal, then taken, then damage again.</summary>
    private void NextMode() => SetMode(_mode switch
    {
        MeterMode.Damage => MeterMode.Heal,
        MeterMode.Heal => MeterMode.Taken,
        _ => MeterMode.Damage,
    });

    private void OnOverlayModeClicked(object sender, MouseButtonEventArgs e)
    {
        NextMode();
        e.Handled = true;
    }

    /// <summary>The id to read a row's guild and profile under: the own character's row may carry an
    /// older object id than the one the game currently knows it by.</summary>
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

        if (directory.IsLocalPlayer(objectId) && directory.LocalCharacter is { } character)
        {
            int code = character.ClassCode;
            bool known = code % 4 is 1 or 2 && code / 4 is >= 1 and <= 8;
            return new ProfileUpload(
                "self",
                character.Level,
                known ? code / 4 : null,
                known ? code % 4 : null,
                directory.LocalEquipment.Select(i => new ProfileGearUpload(i.SlotIndex, i.ItemId, i.Enchant)).ToList(),
                directory.LocalSkills.Select(s => new ProfileSkillUpload(s.SkillId, s.Level, s.BaseLevel)).ToList(),
                directory.LocalDaevanion.Select(b => new ProfileBoardUpload(b.BoardId, b.NodeIds.ToList())).ToList());
        }

        if (directory.SeenProfileOf(objectId) is { } seen)
        {
            return new ProfileUpload(
                "seen",
                null,
                seen.ClassId,
                seen.Faction,
                seen.Gear.Select(i => new ProfileGearUpload(i.SlotIndex, i.ItemId, i.Enchant)).ToList(),
                Array.Empty<ProfileSkillUpload>(),
                Array.Empty<ProfileBoardUpload>());
        }

        return null;
    }

    private void OnShowDamageView(object sender, RoutedEventArgs e)
    {
        PlayersGrid.Visibility = Visibility.Visible;
        SetActiveNavButton(DamageNavButton, CharacterNavButton);
        RefreshUploadAvailability();
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

    /// <summary>Swaps which of the two nav-rail view buttons reads as "active" - an orange fill
    /// (Brush.Accent) with dark-on-orange text (Brush.Window, same contrast pairing as the PVE/PVP
    /// toggle) for the one just selected, back to NavButton's own plain default for the other.
    /// Replaces the old FontWeight-only swap now that both buttons show an icon, not text.</summary>
    private void SetActiveNavButton(Button active, Button inactive)
    {
        active.Background = (Brush)FindResource("Brush.Accent");
        active.Foreground = (Brush)FindResource("Brush.Window");
        inactive.Background = (Brush)FindResource("Brush.Control");
        inactive.Foreground = (Brush)FindResource("Brush.Text");
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

    private void OnHideUiClicked(object sender, RoutedEventArgs e) => SetHideUi();

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

    /// <summary>In overlay mode the bars are three quarters of the window's width (the rest stays
    /// transparent), set whenever the window is resized or the mode is switched on.</summary>
    private const double OverlayWidthShare = 0.75;

    private void UpdateOverlayWidth() => OverlayContent.Width = Math.Max(ActualWidth * OverlayWidthShare, 120);

    private void SetHideUi()
    {
        UpdateOverlayWidth();
        _hideUiActive = !_hideUiActive;
        NormalContent.Visibility = _hideUiActive ? Visibility.Collapsed : Visibility.Visible;
        ShowOverlayPanels();
        if (_hideUiActive && _compactOverlay)
        {
            FollowNewestRun();
            RefreshRows();
        }

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
            // The compact panel stays in the taskbar: its right-click "Close" and a click on it are
            // ways to end the meter or bring it back besides the hotkey.
            ShowInTaskbar = _compactOverlay;
        }
        else
        {
            Topmost = _topmostBeforeHideUi;
            ShowInTaskbar = true;
        }

        FitWindowToCompactOverlay(_hideUiActive && _compactOverlay);
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
        _timetable.ApplyOpacity(settings.OverlayOpacity);
        if (!_timetable.IsVisible)
        {
            _timetable.Show();
        }

        _timetable.Refresh();
    }

    private void ToggleTimetable()
    {
        var settings = MeterSettings.Load();
        settings.ShowTimetable = !(_timetable?.IsVisible ?? false);
        settings.Save();
        ApplyTimetable(settings);
    }

    /// <summary>Settings changed while Hide UI is up: switches to the look now chosen.</summary>
    private void ApplyOverlayLook()
    {
        ShowOverlayPanels();
        ShowInTaskbar = _compactOverlay;
        FitWindowToCompactOverlay(_compactOverlay);
        if (_compactOverlay)
        {
            FollowNewestRun();
            RefreshRows();
        }
    }

    /// <summary>Which Hide-UI look is up: one chip per player or the compact panel (Settings).</summary>
    private void ShowOverlayPanels()
    {
        OverlayContent.Visibility = _hideUiActive && !_compactOverlay ? Visibility.Visible : Visibility.Collapsed;
        CompactOverlayPanel.Visibility = _hideUiActive && _compactOverlay ? Visibility.Visible : Visibility.Collapsed;

        // The chips let every click through to the game. The compact panel takes clicks (a player's
        // skills, dragging it into place); the window is truly transparent around it, so the rest of
        // the screen still reaches the game.
        _overlay?.SetClickThrough(_hideUiActive && !_compactOverlay);
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
    private void UpdateCompactOverlay(IReadOnlyList<DamageEvent> shownHits)
    {
        if (!_compactOverlay)
        {
            return;
        }

        OverlayTargetText.Text = _pvpOnly ? "PvP"
            : _selectedTargetId is int targetId ? ResolveDisplayName(targetId)
            : LocalizationManager.Instance["Main.FilterAllTargets"];
        var strings = LocalizationManager.Instance;
        OverlayModeText.Text = strings[_healMode ? "Main.Overlay.ModeHeal" : _takenMode ? "Main.Overlay.ModeTaken" : "Main.Overlay.ModeDamage"];
        OverlayRateHeader.Text = _healMode ? "HPS" : _takenMode ? "DTPS" : "DPS";
        bool capturing = (_source as Aion2.Aion2PacketCombatSource)?.ServerFingerprint is not null;
        OverlayStateDot.Fill = _paused ? Brushes.Orange : capturing ? Brushes.LimeGreen : Brushes.Gray;
        OverlayTimeText.Text = shownHits.Count > 1
            ? (shownHits.Max(h => h.Timestamp) - shownHits.Min(h => h.Timestamp)).ToString(@"m\:ss")
            : "";
        UpdateOverlayBossHp(shownHits);
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
        HpCheckResult? check = HpCheck.Evaluate(samples.Select(s => (s.At, s.Hp)).ToList(), onTarget, highest);
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
        foreach (ComboBoxItem item in MobBossFilter.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is not MobBossTag tag)
            {
                continue;
            }

            DateTime last = DateTime.MinValue;
            foreach (DamageEvent ev in _aggregator.Events)
            {
                if (!ev.IsHeal && ev.TargetObjectId == tag.TargetId && ev.Timestamp > last
                    && (tag.WindowStart is not DateTime from || (ev.Timestamp >= from && ev.Timestamp <= tag.WindowEnd)))
                {
                    last = ev.Timestamp;
                }
            }

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
