using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using AionSniffer.ChatLog;
using AionSniffer.Combat;
using AionSniffer.Data;
using AionSniffer.Update;
using AionSniffer.Upload;
using VelopackUpdateInfo = Velopack.UpdateInfo;

namespace AionSniffer.Ui;

/// <summary>
/// The main meter window. Holds its own LiveAggregator, fed entirely from Aion's Chat.log via
/// ChatLogTailer, with "Load Demo Data" as the offline stand-in for checking the UI.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ObservableCollection<PlayerRow> _rows = new();

    /// <summary>Everyone the meter has ever identified, with class and faction, so someone seen in
    /// an earlier session is recognised the moment they appear again. See Ui/KnownPlayers.</summary>
    private readonly KnownPlayers _knownPlayers = KnownPlayers.Load();
    private readonly Dictionary<int, PlayerRow> _rowsByObjectId = new();
    private readonly Dictionary<int, string> _targetNames = new();

    private readonly ObservableCollection<LootRow> _lootRows = new();
    private readonly Dictionary<(string Person, int ItemId), LootRow> _lootRowsByKey = new();

    /// <summary>
    /// Identity (name/class/level) per source object id, independent of PlayerRow. Found by
    /// terminal_windows: RefreshRows removes and later re-creates a row for any source that drops
    /// out of the current Mob/Boss filter, but a fresh PlayerRow only ever gets "0x########" --
    /// the real identity, set once by OnLoadDemoDataClicked directly on the row object, was lost
    /// for good the moment that row got filtered out once. Mirrors _targetNames, which already
    /// solved the identical problem for target display names.
    /// </summary>
    private readonly Dictionary<int, (string Name, string ClassName, int Level)> _playerIdentities = new();

    private readonly LiveAggregator _aggregator = new();
    private NativeOverlay? _overlay;
    private bool _paused;
    private bool _hideUiActive;
    private bool _topmostBeforeHideUi;

    /// <summary>Null = "All" (the Mob/Boss filter's first, always-present entry).</summary>
    private int? _selectedTargetId;

    /// <summary>Null = "All" (ClassFilter's first entry, no Tag). Per the user: was purely
    /// decorative until now (see the XAML comment on ClassFilter's own history) -- expected to
    /// actually filter once other players' classes started being detected at all, and an empty
    /// result when nobody of that class is present is the correct, intended behavior, not a bug.</summary>
    private string? _selectedClassFilter;

    /// <summary>
    /// The user's own characters, from Settings -- see MeterSettings.Characters remarks for why
    /// Chat.log itself can never supply the local player's real name. _activeCharacterName is
    /// which of them "You" currently means; normally set automatically by
    /// UpdateActiveCharacterFromSkill whenever a Chat.log line shows "You" using a skill unique to
    /// one registered character's class, but see _autoDetectActiveCharacter below for when that's
    /// turned off and it's only the Settings dialog's manual picker instead.
    /// </summary>
    private List<CharacterProfile> _characters = new();
    private string? _activeCharacterName;

    /// <summary>Mirrors MeterSettings.AutoDetectActiveCharacter -- see its remarks.
    /// UpdateActiveCharacterFromSkill is a no-op while this is false, and _activeCharacterName
    /// only changes via Settings.</summary>
    private bool _autoDetectActiveCharacter = true;

    // Chat-log live tailing. _chatLogParser is kept alongside the tailer (not just inside it) so
    // RefreshRows/RefreshMobBossFilterItems can resolve real names for ids this window didn't
    // itself assign (see ResolveDisplayName) -- the network path and demo data have their own
    // name sources (_playerIdentities/_targetNames), but ids that arrive purely from Chat.log
    // only exist in this registry.
    private ChatLogParser? _chatLogParser;
    private ChatLogTailer? _chatLogTailer;
    private string? _chatLogPath;
    private readonly DispatcherTimer _chatLogTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>Counts chat-log ticks so the file-size check runs every 30 seconds rather than
    /// every second (see OnChatLogTimerTick).</summary>
    private int _chatLogSizeTickCounter;

    /// <summary>Five minutes, per the user. GitHub's anonymous API allows 60 requests an hour per
    /// IP, so 12 is comfortably inside it even with a second client running alongside.</summary>
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromMinutes(5) };

    /// <summary>The update already downloaded and staged, so a repeating check does not fetch the
    /// same one twelve times an hour -- and so clicking the notice knows what to restart into.
    /// Aliased because Velopack's UpdateInfo would otherwise collide with nothing in particular,
    /// but reads ambiguously next to this project's own update code.</summary>
    private VelopackUpdateInfo? _downloadedUpdate;

    public MainWindow()
    {
        InitializeComponent();

        // Version in the title, read back from the assembly rather than typed here a second time:
        // AionSniffer.csproj's <Version> is the only place it is written. Needed because builds are
        // handed around the group by hand -- a screenshot or a Chat.log recorded by someone else is
        // otherwise impossible to pin to a build, which already cost a round of guesswork once.
        Title = AppVersion.Text.Length > 0 ? $"AionSniffer DMG Meter {AppVersion.Text}" : "AionSniffer DMG Meter";

        PlayersGrid.ItemsSource = _rows;
        LootGrid.ItemsSource = _lootRows;
        OverlayContent.ItemsSource = _rows;

        // Per the user: the list must sort itself by damage, highest first, not just show rows in
        // whatever order they were first discovered in. IsLiveSorting (not just SortDescriptions
        // alone) is what keeps it re-sorted as damage keeps changing live -- plain
        // SortDescriptions only sorts once, at binding time; without live sorting the row order
        // would freeze after the initial snapshot and never reflect who's actually ahead now.
        var playersView = (ListCollectionView)CollectionViewSource.GetDefaultView(_rows);
        playersView.SortDescriptions.Add(new SortDescription(nameof(PlayerRow.Damage), ListSortDirection.Descending));
        playersView.IsLiveSorting = true;
        playersView.LiveSortingProperties.Add(nameof(PlayerRow.Damage));

        // Same live-sorting reasoning as the players list above, applied to loot -- grouped by
        // person first (now that the whole group is tracked, not just "You"), highest quantity
        // first within each person.
        var lootView = (ListCollectionView)CollectionViewSource.GetDefaultView(_lootRows);
        lootView.SortDescriptions.Add(new SortDescription(nameof(LootRow.Person), ListSortDirection.Ascending));
        lootView.SortDescriptions.Add(new SortDescription(nameof(LootRow.Quantity), ListSortDirection.Descending));
        lootView.IsLiveSorting = true;
        lootView.LiveSortingProperties.Add(nameof(LootRow.Quantity));

        _chatLogTimer.Tick += OnChatLogTimerTick;

        // Startup check plus every five minutes after that, both silent about failures (see
        // RunUpdateCheck). Fire-and-forget on purpose: an update check must never delay the window
        // appearing, and there is nothing to wait for -- its only outcome is a line in the footer.
        _updateTimer.Tick += (_, _) => _ = RunUpdateCheck(announceResult: false);
        _updateTimer.Start();
        _ = RunUpdateCheck(announceResult: false);
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
        StartChatLogTailing(settings);
        RefreshCharacterSettings(settings);
    }

    /// <summary>Applies a previously saved size/position, if any -- see SaveWindowGeometry, its
    /// counterpart on close. Left null-checked separately from Width/Height since a user who's
    /// only ever resized (not moved) the window would have one pair set and the other still
    /// null. Also restores the Name/Damage-DPS column widths, same deal -- a DataGridColumn drag-
    /// resize is otherwise not persisted anywhere either.</summary>
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

        if (settings.NameColumnWidth is double nameWidth)
        {
            NameColumn.Width = new DataGridLength(nameWidth);
        }

        if (settings.DpsColumnWidth is double dpsWidth)
        {
            DpsColumn.Width = new DataGridLength(dpsWidth);
        }
    }

    /// <summary>Persists the current size/position so it survives a restart -- found necessary by
    /// the user, who resized the window and had it reset every time. Reads WindowState-independent
    /// values (RestoreBounds instead of Width/Height/Left/Top directly) so a maximized or minimized
    /// window on close doesn't save that transient state as if it were the normal size. Also
    /// persists the current Name/Damage-DPS column widths, same reasoning as RestoreWindowGeometry.</summary>
    private void SaveWindowGeometry(MeterSettings settings)
    {
        Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        settings.WindowWidth = bounds.Width;
        settings.WindowHeight = bounds.Height;
        settings.WindowLeft = bounds.X;
        settings.WindowTop = bounds.Y;

        settings.NameColumnWidth = NameColumn.ActualWidth;
        settings.DpsColumnWidth = DpsColumn.ActualWidth;
    }

    /// <summary>Re-reads Characters/ActiveCharacterName/AutoDetectActiveCharacter from Settings --
    /// called once at startup and again after Settings is saved, alongside StartChatLogTailing.</summary>
    private void RefreshCharacterSettings(MeterSettings settings)
    {
        _characters = settings.Characters;
        _activeCharacterName = settings.ActiveCharacterName;
        _autoDetectActiveCharacter = settings.AutoDetectActiveCharacter;
    }

    /// <summary>Detected class per real player name, from OTHER players' own skill usage (see
    /// UpdateOtherPlayerClass) -- per the user ("es wurde keine Klasse der anderen Spieler
    /// erkannt"), consulted by ApplyIdentity for any row that isn't "You". Session-scoped like
    /// everything else here, not persisted -- a fresh detection per skill use is cheap enough not
    /// to bother, and a class never actually changes mid-session anyway.</summary>
    private readonly Dictionary<string, string> _detectedClassByName = new();

    /// <summary>
    /// Dispatches a skill-usage sighting to whichever of the two things it's useful for: "You"
    /// updates which registered character is active (see UpdateActiveCharacterFromSkill), anyone
    /// else updates that name's detected class for the grid's icon column (see
    /// UpdateOtherPlayerClass). Both need the same skill-name -> class(es) lookup, done once here.
    /// </summary>
    private void OnSkillUsed(string actorName, string skillName)
    {
        // Matches skillName against whichever of the three client languages it's actually in (see
        // SkillDatabase.FindByLocalizedName), exact match preferred over the rank-normalized
        // fallback for the reasons documented there.
        var skillInfo = SkillDatabase.FindByLocalizedName(skillName);
        if (skillInfo is null || skillInfo.Class.Length == 0)
        {
            return;
        }

        // Some DB entries list more than one class for a shared skill (e.g. "Gladiator, Templar")
        // -- found by terminal_windows, ~10% of real mentions even after the rank fix above.
        var classNames = skillInfo.Class.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (actorName == "You")
        {
            UpdateActiveCharacterFromSkill(classNames);
        }
        else
        {
            UpdateOtherPlayerClass(actorName, classNames);
        }
    }

    /// <summary>
    /// Auto-detects which registered character "You" currently is, by matching the just-used
    /// skill's class(es) against the registered CharacterProfiles. Replaces the manual quick-
    /// switch dropdown removed per the user's original request ("YOU + genutzte Skills sollte
    /// ausreichen"). Silently does nothing if no registered character matches, or if two
    /// characters share a class (ambiguous -- leaves whichever was already active rather than
    /// guessing) -- or, per the user's later request, if AutoDetectActiveCharacter has been
    /// turned off in Settings (two clients both fighting at once can otherwise flip this back and
    /// forth between two registered characters with no way to pin it to the one actually meant to
    /// be tracked).
    /// </summary>
    /// <summary>
    /// Fires when Aion announces that a named character has logged in -- see
    /// ChatLogParser.PlayerLoggedIn for why that alone means nothing. Only a REGISTERED character
    /// of the user's own can trigger anything here; a real friend logging in is the common case for
    /// this line and must never be read as a switch.
    ///
    /// <para>Found from a real Chat.log spanning several days on one shared installation: an
    /// Assassin's and a Ranger's own damage, both narrated as "Ihr"/"You" because both were the
    /// same person's characters played at different sittings, summed into one absurd total because
    /// nothing ever told the meter the local identity had changed. This is the closest thing
    /// Chat.log has to that signal -- there is no line that says "you switched characters"
    /// directly, and the connection-status line only fires once per client launch, not per
    /// character-select swap.</para>
    ///
    /// <para>Gated behind the same "Auto-detect active character" setting as the skill-based
    /// detection, and for the same reason it exists: with two clients open at once, whichever one
    /// happens to log a groupmate's login notification must not flip which of the two registered
    /// characters this session believes it is.</para>
    /// </summary>
    private void OnPlayerLoggedIn(string name)
    {
        if (!_autoDetectActiveCharacter || !_characters.Any(c => c.Name == name))
        {
            return;
        }

        // The same character logging back in -- a relog, or simply the first login line of a
        // fresh session -- is not a switch; there is nothing to separate it from.
        if (name == _activeCharacterName)
        {
            return;
        }

        bool switchedFromKnownCharacter = _activeCharacterName is not null;

        _activeCharacterName = name;
        var settings = MeterSettings.Load();
        settings.ActiveCharacterName = name;
        settings.Save();

        if (switchedFromKnownCharacter)
        {
            // Everything recorded so far belongs to whoever was just playing, not to the character
            // that just logged in -- carrying it forward would keep merging two different people's
            // (or, as found, one person's two different characters') damage into one row.
            ClearDamageData();
            ClearLootData();
        }

        RefreshRows();
    }

    private void UpdateActiveCharacterFromSkill(string[] classNames)
    {
        if (!_autoDetectActiveCharacter)
        {
            return;
        }

        // Split and match any of the skill's class(es); still bails if that leaves more than one
        // registered character (genuinely ambiguous), same rule as before this was generalized.
        var matches = _characters.Where(c => classNames.Contains(c.ClassName)).ToList();
        if (matches.Count != 1 || matches[0].Name == _activeCharacterName)
        {
            return;
        }

        _activeCharacterName = matches[0].Name;

        var settings = MeterSettings.Load();
        settings.ActiveCharacterName = _activeCharacterName;
        settings.Save();

        RefreshRows();
    }

    /// <summary>
    /// Records a real (non-"You") player's class from their own skill usage, per the user. Unlike
    /// the "You" case, there's no registered-character list to disambiguate a shared skill against
    /// (see UpdateActiveCharacterFromSkill) -- a skill mapping to more than one class is simply
    /// left unresolved for someone else rather than guessed. A class, once detected, never
    /// actually changes for a given character, so this only does anything on the first sighting
    /// (or if it somehow saw a different class before, which would mean the earlier one was wrong
    /// -- still safer to take the latest than to never correct it).
    /// </summary>
    private void UpdateOtherPlayerClass(string playerName, string[] classNames)
    {
        if (classNames.Length != 1)
        {
            return;
        }

        if (_detectedClassByName.TryGetValue(playerName, out string? existing) && existing == classNames[0])
        {
            return;
        }

        _detectedClassByName[playerName] = classNames[0];
        RefreshRows();
    }

    /// <summary>
    /// (Re)starts chat-log tailing from the given settings' AionInstallFolder, if it looks usable
    /// -- called once at startup and again after Settings is saved with a possibly different
    /// folder. Explicit rule from the user: recording must never look into the past, so a fresh
    /// ChatLogTailer always seeks to the CURRENT end of Chat.log (see its own remarks) -- this is
    /// true both on first startup and when the user points Settings at a different install after
    /// the window is already open; neither case should replay history.
    /// </summary>
    private void StartChatLogTailing(MeterSettings settings)
    {
        _chatLogTimer.Stop();
        _chatLogParser = null;
        _chatLogTailer = null;

        string? folder = settings.AionInstallFolder;
        _chatLogPath = string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, "Chat.log");

        if (_chatLogPath is null)
        {
            return;
        }

        _chatLogParser = new ChatLogParser();
        _chatLogParser.SkillUsed += OnSkillUsed;
        _chatLogParser.CommandReceived += OnChatCommand;
        _chatLogParser.PersonalStatChanged += OnPersonalStatChanged;
        _chatLogParser.LootAcquired += OnLootAcquired;
        _chatLogParser.PlayerLoggedIn += OnPlayerLoggedIn;

        // Chat.log may not exist yet on a client that has never had chat logging (g_chatlog)
        // enabled -- don't gate the timer on the file already being there, or enabling logging
        // later, while this window is already open, would go unnoticed. OnChatLogTimerTick
        // creates the tailer lazily once the file appears.
        if (File.Exists(_chatLogPath))
        {
            _chatLogTailer = new ChatLogTailer(_chatLogPath, _chatLogParser);
        }

        _chatLogTimer.Start();
    }

    // AP earned from looted relics, per person (see Data/RelicApDatabase for why Chat.log can
    // never report this itself). Keyed by the same resolved person name the Loot list uses, so
    // "You" is already mapped to the active character here -- that is what lets a relic picked up
    // by anyone in the group land on their own row, not just the local player's.
    private readonly Dictionary<string, long> _relicApByPerson = new();

    // Running totals for the footer row -- see ChatLogParser.PersonalStatChanged remarks for why
    // these are simple accumulators, not per-row PlayerRow fields like Damage (Exp/AP/GP/Kinah
    // only ever apply to "You", there's no "other player's XP" to track). Session-scoped like
    // damage totals: reset by ClearDamageData, not persisted across restarts.
    private long _totalExp;
    private long _totalAp;
    private long _totalGp;
    private long _totalKinah;

    private void OnPersonalStatChanged(PersonalStatKind kind, long delta)
    {
        switch (kind)
        {
            case PersonalStatKind.Experience:
                _totalExp += delta;
                ExpValueText.Text = _totalExp.ToString("N0");
                break;
            case PersonalStatKind.AbyssPoints:
                _totalAp += delta;
                RefreshApDisplays(); // footer + the "You" row's second AP line, relics included
                break;
            case PersonalStatKind.GloryPoints:
                _totalGp += delta;
                GpValueText.Text = _totalGp.ToString("N0");
                break;
            case PersonalStatKind.Kinah:
                _totalKinah += delta;
                KinahValueText.Text = _totalKinah.ToString("N0");
                break;
        }
    }

    /// <summary>
    /// Tracks the WHOLE group's loot, per the user -- not just "You". A loot line naming a
    /// registered OTHER character (e.g. "Mitzuhiko has acquired [item:...].") is still dropped,
    /// same perspective rule as damage (see IsNamedCopyOfRegisteredCharacter): it's that
    /// character's own client narrating itself in third person, which is guaranteed to be a
    /// duplicate of ITS OWN "You" line elsewhere in the merged log. Anyone else named in third
    /// person -- a real, unregistered group member (or a pet like "Superclyde") -- is the OPPOSITE
    /// case: there is no "their own You line" for us to receive at all (we don't run their
    /// client), so third person is the correct, sole source for them, not a duplicate to discard.
    /// </summary>
    private void OnLootAcquired(LootEvent loot)
    {
        string? person = ResolveLootPerson(loot.Subject);
        if (person is null)
        {
            return;
        }

        // Before the loot-list filter below, deliberately: relics are Rare grade, so IsTrackedLoot
        // drops them from the Loot view as ordinary trash -- correct there, since the user asked
        // not to list every drop, but their AP still has to count. Both facts are true at once.
        if (RelicApDatabase.IsRelic(loot.ItemId))
        {
            _relicApByPerson.TryGetValue(person, out long relicAp);
            _relicApByPerson[person] = relicAp + RelicApDatabase.ApFor(loot.ItemId, loot.Quantity);
            RefreshApDisplays();
        }

        string itemName = ItemDatabase.DisplayName(loot.ItemId);
        if (!IsTrackedLoot(loot.ItemId, itemName))
        {
            return;
        }

        var key = (person, loot.ItemId);
        if (!_lootRowsByKey.TryGetValue(key, out var row))
        {
            row = new LootRow(person, loot.ItemId, itemName, ItemDatabase.GradeOf(loot.ItemId), loot.RawTag);
            _lootRowsByKey[key] = row;
            _lootRows.Add(row);
        }

        row.Quantity += loot.Quantity;
        row.LastTag = loot.RawTag;
    }

    /// <summary>Null return means "drop this line" (see OnLootAcquired remarks) -- everything
    /// else is the real name to attribute the loot to, with "You" resolved to whichever character
    /// is currently active (same convention as ResolveDisplayName/ApplyIdentity use for damage).</summary>
    private string? ResolveLootPerson(string? subject)
    {
        if (subject is null)
        {
            return null;
        }

        if (subject == "You")
        {
            return string.IsNullOrEmpty(_activeCharacterName) ? "You" : _activeCharacterName;
        }

        bool isOtherRegisteredCharacter = subject != _activeCharacterName && _characters.Any(c => c.Name == subject);
        return isOtherRegisteredCharacter ? null : subject;
    }

    /// <summary>Individually named items the user wants tracked regardless of grade, beyond the
    /// Godstone/Design/Recipe prefix rule -- exact names, not a broader pattern: e.g. "Bundle" on
    /// its own would also match 1.379 unrelated crafting-material items in ItemDatabase (Log
    /// Bundle, Fiber Bundle, ...), which is exactly the "every single piece of trash loot" the
    /// user asked to stop tracking in the first place.</summary>
    private static readonly HashSet<string> AlwaysTrackedItemNames = new()
    {
        "Veteran's Composite Manastone Bundle",
    };

    /// <summary>
    /// Per the user ("Bitte nicht jeden Loot berücksichtigen"): Godstones and crafting
    /// Designs/Recipes are always worth tracking regardless of rarity (identified by name prefix
    /// -- aioncodex's own naming convention, not a separate category field the source data
    /// exposes: "Godstone: X", "[Event] Godstone: X", "Design: X", "Balic Design: X", "Recipe: X",
    /// "Balic Recipe: X" all contain the matched substring), and so is anything in
    /// AlwaysTrackedItemNames. Everything else (gear, manastones, ordinary trash) only counts from
    /// Unique (Gold) grade up -- Common/Rare/Hero drops are exactly the "every single piece of
    /// trash loot" the user asked to stop tracking. An unresolved grade (id not in ItemDatabase)
    /// is tracked rather than dropped: silently hiding something we can't even name is worse than
    /// showing "Item #ID" for a rare gap in the data.
    /// </summary>
    private static bool IsTrackedLoot(int itemId, string itemName)
    {
        if (itemName.Contains("Godstone:") || itemName.Contains("Design:") || itemName.Contains("Recipe:")
            || AlwaysTrackedItemNames.Contains(itemName))
        {
            return true;
        }

        return ItemDatabase.GradeOf(itemId) is not ItemGrade grade || grade >= ItemGrade.Unique;
    }

    /// <summary>
    /// AionRainMeter-style in-game commands, per the user's request ("Bitte ingame Befehle
    /// umsetzen") -- typing e.g. ".ui" into any in-game chat box reaches here via
    /// ChatLogParser.CommandReceived. Only the commands actually wired below do anything; every
    /// other word from the reference list (.exp/.gt/.codex/.rank/.item/.url/.google/.yt/.ping/
    /// .iptrace/.report/.check/.timer/.tr/.timerreset/.timerkill/.switch/.alpha/.upload/.ss/.db/
    /// .sort/.sortclear/.hit/.heal) either needs game data this build doesn't have (stats, items,
    /// timers) or a decision on what it should even mean here, and is deliberately left alone
    /// rather than silently doing nothing under a name that implies it works. ".ap" is now wired,
    /// per the user, to the group's relic AP only -- see BuildRelicApText -- not to a general AP
    /// stat dump, since redistributing relics fairly is the actual use case for typing it in Aion.
    ///
    /// speakerName is checked against the locally authorized character and anything else is
    /// silently ignored, INCLUDING a null speaker (an unrecognized line shape) -- fail closed, not
    /// open. Found necessary by terminal_windows running the original, speaker-blind version of
    /// this regex against a real ~69k-line session: 6 real dot-commands from OTHER players turned
    /// up in public LFG chat (".gear" x3, ".l", ".decompose", ".der"), proving a stranger typing
    /// ".cleardmg" in a channel the user might not even be reading would otherwise have silently
    /// wiped their whole session with no visible cause. None of today's four commands happened to
    /// collide, but that was luck, not a guarantee the next one added won't. Live-tested with the
    /// real "[charname:...]" line shape by terminal_windows: a stranger's command is dropped
    /// (confirmed by damage still accumulating through an ignored ".pause"), the owner's own goes
    /// through.
    /// </summary>
    private void OnChatCommand(string? speakerName, string command, string args)
    {
        // _activeCharacterName is null until the skill-based auto-detect has seen a skill (see
        // UpdateActiveCharacterFromSkill) -- with exactly one registered character there's no ambiguity about who
        // "the user" is regardless, so that single name is trusted immediately at startup too.
        // Registering a second character removes this fallback (falls back to strict
        // _activeCharacterName again) rather than guessing which of several is speaking.
        string? authorizedName = _activeCharacterName
            ?? (_characters.Count == 1 ? _characters[0].Name : null);

        // Authorizes by NAME, not identity -- relies on Aion character names being unique
        // per-server (they are), not on any stronger proof this is really the same person. Noted
        // by terminal_windows as a conscious, accepted assumption rather than a gap to fix.
        if (speakerName is null || authorizedName is null || !string.Equals(speakerName, authorizedName, StringComparison.Ordinal))
        {
            return;
        }

        switch (command)
        {
            case "ui":
                SetHideUi();
                break;
            case "pause":
                SetPaused(true);
                break;
            case "resume":
                SetPaused(false);
                break;
            case "dmg":
                CopyTextToClipboardIfAny(BuildDmgRankingText(), "No damage has been recorded yet.");
                break;
            case "cleardmg":
                ClearDamageData();
                break;
            case "clearloot":
                ClearLootData();
                break;
            case "loot":
                CopyTextToClipboardIfAny(BuildLootChatSummary(),
                "No loot of Unique grade or better has dropped yet, and the chat summary only lists "
                + "those. Use the Table button next to it for the full loot list.");
                break;
            case "ap":
                CopyTextToClipboardIfAny(BuildRelicApText(),
                "Nobody in the group has picked up a relic yet.");
                break;
        }
    }

    private void OnChatLogTimerTick(object? sender, EventArgs e)
    {
        if (_chatLogTailer is null && _chatLogParser is not null && _chatLogPath is not null && File.Exists(_chatLogPath))
        {
            _chatLogTailer = new ChatLogTailer(_chatLogPath, _chatLogParser);
        }

        // Once every 30 ticks, not every one: this is a stat() against a file the game is writing
        // to, and the answer changes by kilobytes a second at most.
        if (++_chatLogSizeTickCounter >= 30)
        {
            _chatLogSizeTickCounter = 0;
            RefreshChatLogSizeWarning();
        }

        var events = _chatLogTailer?.Poll(_paused);
        if (events is { Count: > 0 })
        {
            var counted = events
                .Where(ev => !IsNamedCopyOfRegisteredCharacter(ev.SourceObjectId))
                .Select(AttributePetDamageToOwner)
                .ToList();
            if (counted.Count > 0)
            {
                _aggregator.IngestEvents(counted);
            }

            RefreshRows();
        }
    }

    /// <summary>Per the user: a Spiritmaster's summoned pets, from Aion 4.6's four base elemental
    /// spirits ("Bei Beschwörer muss das Pet unbedingt ihm zugerechnet werden") -- there is no
    /// general way to detect "this name is a pet" (unlike NpcDatabase's real monster list, these
    /// aren't a separate category in that data), so this is a short, explicitly user-confirmed
    /// name list rather than a guess from anything broader (plain "contains Spirit" would also
    /// catch hundreds of unrelated hostile mobs, e.g. "Ancient Fire Spirit").</summary>
    private static readonly HashSet<string> SpiritmasterPetNames = new()
    {
        "Water Spirit", "Wind Spirit", "Storm Spirit", "Fire Spirit", "Earth Spirit",
    };

    /// <summary>
    /// Rewrites a pet's damage/heal source to whichever character is currently active, before it
    /// ever reaches the aggregator -- so every downstream calculation (Damage sum, DPS, the AP
    /// second line, everything) treats it exactly like the summoner's own hit, with no separate
    /// merge step needed anywhere else.
    ///
    /// Multiple Spiritmasters in the same group are genuinely ambiguous -- Chat.log never says
    /// whose pet it is, "Water Spirit" reads identically regardless of which of them summoned it,
    /// and there is no structural marker for it the way "[charname:...]" solved this for typed
    /// chat commands. Not solvable from the log alone, so not attempted: only merges when exactly
    /// ONE registered character is a Spiritmaster (per the user's own follow-up, "Das wird
    /// bestimmt zu einem Problem wenn es mehrere SMs mit Pets gibt" / "Wenn es 2 SMs gibt bitte
    /// Pet DMG extra anzeigen") -- with two or more, the event is left untouched instead of
    /// guessed, so it shows up as its own "Water Spirit" row (see the players-only filter
    /// exemption in RefreshRows) rather than being silently dropped or misattributed.
    /// </summary>
    private DamageEvent AttributePetDamageToOwner(DamageEvent ev)
    {
        string? sourceName = _chatLogParser?.Names.NameFor(ev.SourceObjectId);
        if (sourceName is null || !SpiritmasterPetNames.Contains(sourceName))
        {
            return ev;
        }

        if (_characters.Count(c => c.ClassName == "Spiritmaster") != 1)
        {
            return ev;
        }

        // Found by terminal_windows: "Water Spirit"/"Fire Spirit" are ALSO real hostile monster
        // names (they're in NpcDatabase too) -- without this check, a mob by that name hitting
        // the player would get credited as the player's own damage, inflating their total with
        // damage they received rather than dealt. A pet never attacks its own owner, so "this
        // pet-named source hit ME" is, by construction, always the hostile mob instead -- no NPC
        // lookup needed, just the hit's direction.
        int youId = _chatLogParser!.Names.GetOrAssignId("You");
        if (ev.TargetObjectId == youId)
        {
            return ev;
        }

        return ev with { SourceObjectId = youId };
    }

    /// <summary>
    /// Drops damage/heal events whose SOURCE is any registered character's own name -- found
    /// necessary by the user + terminal_windows running two Aion clients at once, both grouped,
    /// both writing into the same shared Chat.log: each client narrates its OWN character's hits
    /// as "You" and its GROUPMATE's hits by name in third person, so the same physical hit lands
    /// in the merged file twice -- once as "You inflicted..." from that character's own client
    /// (mapped here to whichever registered character is currently active), once as
    /// "{Name} inflicted..." from the OTHER client. Counting both double-counts every hit.
    ///
    /// Deliberately does NOT exempt the currently active character's own name -- an earlier
    /// version did (treating it as "not one of the others, so maybe a legitimate mention"), which
    /// was the actual bug: found by terminal_windows testing the case that exemption was blind to
    /// (the active character ALSO showing up by name), producing exactly this method's namesake
    /// symptom, a split "Mitzuhiko" row with and without a class icon for the same person. The
    /// exemption's premise was false -- a client never narrates its own character's actions in
    /// third person, active or not (confirmed: "Katzugawa inflicted..." never once appeared while
    /// Katzugawa was the OTHER character), so ANY named mention of ANY registered character is a
    /// cross-client duplicate, full stop; there is no case where it's legitimately someone else
    /// coincidentally sharing that name once it's registered as one of the user's own.
    ///
    /// This does NOT need to know which physical client wrote which line (confirmed impossible:
    /// terminal_windows found zero client-identifying markers on any real combat/heal line across
    /// a ~74k-line session). The perspective rule alone is enough.
    ///
    /// Deliberate scope: only filters by SOURCE (attacker/healer), not target -- being on the
    /// receiving end of a hit isn't the duplicated-narration case this fixes. Also doesn't touch
    /// the separate same-second-identical-text dedup in ChatLogParser (kept as-is, see its
    /// remarks) -- that one's about literal duplicate broadcasts, not this cross-client
    /// perspective issue, and a real simultaneous multi-hit by one character is not the same
    /// failure mode as two clients both narrating the same hit.
    /// </summary>
    private bool IsNamedCopyOfRegisteredCharacter(int sourceObjectId)
    {
        string? sourceName = _chatLogParser?.Names.NameFor(sourceObjectId);
        return sourceName is not null && _characters.Any(c => c.Name == sourceName);
    }

    /// <summary>Falls back to the chat-log parser's own name registry for ids this window never
    /// assigned an identity/target name for itself -- e.g. every id from live Chat.log tailing.
    /// Demo data and (eventually) the network path keep using _playerIdentities/_targetNames
    /// first; this is only reached when neither of those has an entry. "You" specifically is
    /// remapped to whichever of the user's own characters is currently active (see
    /// _activeCharacterName remarks) -- Chat.log itself never contains a name to use instead.</summary>
    private string ResolveDisplayName(int objectId)
    {
        string? raw = _chatLogParser?.Names.NameFor(objectId);
        if (raw == "You" && !string.IsNullOrEmpty(_activeCharacterName))
        {
            return _activeCharacterName;
        }

        return raw ?? $"0x{objectId:X8}";
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _overlay = new NativeOverlay(this);
        _overlay.HotkeyPressed += () => Dispatcher.Invoke(SetHideUi, System.Windows.Threading.DispatcherPriority.Input);
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
        _knownPlayers.SaveIfChanged();
    }

    protected override void OnClosed(EventArgs e)
    {
        _chatLogTimer.Stop();
        _updateTimer.Stop();
        _overlay?.Dispose();
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

        // !IsHeal here is the fix for a real bug found by terminal_windows against an actual
        // Chat.log session: without it, a pure healer ("Potion" -- a heal-effect name, not a
        // player) showed up as a damage source with a nonsensical "dmg (n/a DPS)" row, because
        // TargetIDps/AllDpsWallClock already filter heals out for the rate but nothing filtered
        // this event set for the totals or the row list itself. Same underlying issue as
        // LiveAggregator.Summarize -- see its remarks.
        var damageOnly = _aggregator.Events.Where(ev => !ev.IsHeal);
        var filtered = _selectedTargetId is int targetId
            ? damageOnly.Where(ev => ev.TargetObjectId == targetId).ToList()
            : RestrictToEngagedTargets(damageOnly.ToList());

        // "Players only", always on per the user's request ("Players only ist IMMER vorhanden.") --
        // no toggle anymore, mobs never show. Real Aion character names never contain a space,
        // verified against a real session -- that covers ordinary multi-word mob names. It does
        // NOT catch single-word named/rank bosses ("Ulsaruk" showed up as a top-damage "player"
        // after a raid, per the user) -- NpcDatabase.IsKnownNpc catches those instead, checked
        // against a real 4.x monster name list rather than guessed. Deliberately a display
        // filter, not a data drop: _aggregator.Events itself is untouched. Spiritmaster pet names
        // are explicitly exempted from BOTH the space check and the NpcDatabase check (some of
        // them, e.g. "Fire Spirit"/"Earth Spirit", are also cataloged real monsters there) -- with
        // exactly one registered Spiritmaster their damage never keeps its own source id at all
        // (see AttributePetDamageToOwner), but with two or more it deliberately does, specifically
        // so it can still show here as its own row instead of vanishing.
        var sourceIds = filtered.Select(ev => ev.SourceObjectId).Distinct()
            .Where(id => SpiritmasterPetNames.Contains(ResolveDisplayName(id))
                || (!ResolveDisplayName(id).Contains(' ') && !NpcDatabase.IsKnownNpc(ResolveDisplayName(id))))
            .ToList();

        // ClassFilter, per the user: was purely decorative until other players' classes started
        // being detected at all (see ResolveClassName) -- now that a class can actually be known
        // for someone besides "You", picking one filters the grid down to it for real. An empty
        // result when nobody of that class is currently present is correct, not a bug.
        if (_selectedClassFilter is string classFilter)
        {
            sourceIds = sourceIds.Where(id => ResolveClassName(id) == classFilter).ToList();
        }

        var sides = ResolveSides();

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
            row.Dps = _selectedTargetId is int t
                ? DpsCalculator.TargetIDps(_aggregator.Events, t, sourceId)
                : DpsCalculator.AllDpsWallClock(_aggregator.Events, sourceId);

            ApplySide(row, sourceId, sides);
        }
    }

    /// <summary>
    /// Works out, for this refresh, who is on which side. Recomputed rather than remembered: a
    /// player only becomes classifiable once they heal someone or trade a hit, which can happen
    /// several minutes into a fight, and a row created before that must pick the answer up when it
    /// arrives.
    /// </summary>
    private IReadOnlyDictionary<int, Side> ResolveSides()
    {
        if (_chatLogParser is null)
        {
            return new Dictionary<int, Side>();
        }

        // Loot lines only ever name your own group, so everyone who looted is on your side --
        // together with the characters registered in Settings, that is what tells the resolver
        // which of the two separated sides is actually yours.
        var anchors = new HashSet<string>(_characters.Select(c => c.Name), StringComparer.Ordinal);
        foreach (LootRow loot in _lootRows)
        {
            anchors.Add(loot.Person);
        }

        return FactionResolver.Resolve(
            _aggregator.Events,
            id => _chatLogParser.Names.NameFor(id),
            IsPlayerName,
            _chatLogParser.Names.GetOrAssignId("You"),
            anchors);
    }

    /// <summary>Same test the grid's "players only" filter uses, so the resolver never tries to
    /// put a mob on a side.</summary>
    private bool IsPlayerName(int id)
    {
        string name = ResolveDisplayName(id);
        return SpiritmasterPetNames.Contains(name)
            || (!name.Contains(' ') && !NpcDatabase.IsKnownNpc(name));
    }

    /// <summary>
    /// Turns a resolved side into what the row shows. The faction NAME can only be filled in when
    /// the user has told the meter their own -- Chat.log states nobody's faction, so with that
    /// unanswered the meter still knows who the enemy is, it just cannot say which banner they
    /// fight under. That is why IsEnemy is set regardless and Faction is left blank.
    /// </summary>
    private void ApplySide(PlayerRow row, int sourceId, IReadOnlyDictionary<int, Side> sides)
    {
        Side side = sides.GetValueOrDefault(sourceId, Side.Unknown);
        row.IsEnemy = side == Side.Enemy;

        // The "?? fallback" this replaced was dead code: a registered active character whose
        // Faction is empty returns "" rather than null, so the fallback never fired and NOBODY got
        // an emblem -- exactly what the user saw after a Sauro run, since characters registered
        // before the faction field existed carry an empty one. Skipping empties instead means one
        // character with a faction set is enough to label the whole run.
        // A hand-set faction is the user's answer and outranks anything derived from the log.
        if (_knownPlayers.Find(row.Name) is { FactionIsManual: true, Faction.Length: > 0 } pinned)
        {
            row.Faction = pinned.Faction;
            _knownPlayers.Remember(row.Name, row.ClassName, null);
            return;
        }

        string own = OwnFaction();

        // In an arena the opponent can be your OWN faction -- Discipline, Harmony, Chaos and Glory
        // all mix them -- so fighting someone there says nothing about their banner. Their faction
        // is left blank rather than derived, and since Remember ignores empty values, nothing wrong
        // is written to the database either. A faction learned elsewhere, or set by hand, still
        // shows: that is real knowledge, and hiding it would be its own kind of wrong.
        bool derivable = side != Side.Unknown && !(side == Side.Enemy && (_chatLogParser?.InArena ?? false));

        row.Faction = own.Length == 0 || !derivable
            ? ""
            : side == Side.Own ? own : Opposite(own);

        // Same rule as the class above: this session's evidence wins, memory fills the gaps. A
        // faction cannot change, so a remembered one stays valid indefinitely -- unlike a class.
        if (row.Faction.Length == 0 && _knownPlayers.Find(row.Name) is { Faction.Length: > 0 } seenBefore)
        {
            row.Faction = seenBefore.Faction;
        }

        // A faction is only ever WRITTEN when it was proven, never when it was merely inferred from
        // fighting someone. Hostility is not evidence of a banner: an arena opponent is frequently
        // your own faction, and the meter cannot reliably tell it is in an arena at all -- the zone
        // line is announced once on entry, so a meter started mid-match never sees it.
        //
        // Deriving for the current session is still useful (a Dredgion enemy really is the other
        // faction), so the row keeps showing it. It simply does not outlive the session, and a
        // wrong guess in an arena cannot poison the database. Own side is a different matter: that
        // is proven by heals, loot and group membership, so it is written.
        bool provenFaction = side == Side.Own || _knownPlayers.Find(row.Name)?.FactionIsManual == true;
        _knownPlayers.Remember(row.Name, row.ClassName, provenFaction ? row.Faction : null);
    }

    /// <summary>The local player's own faction, from the active character or any registered one
    /// that has it set. Everyone else's is derived relative to this.</summary>
    private string OwnFaction() =>
        FirstFaction(_characters.Where(c => c.Name == _activeCharacterName)) ?? FirstFaction(_characters) ?? "";

    private static string? FirstFaction(IEnumerable<CharacterProfile> characters) =>
        characters.Select(c => c.Faction).FirstOrDefault(f => !string.IsNullOrEmpty(f));

    private static string Opposite(string faction) =>
        faction == "Elyos" ? "Asmodian" : faction == "Asmodian" ? "Elyos" : "";

    /// <summary>Sets Name/ClassName/Level for one row from whichever identity source applies:
    /// _playerIdentities (demo data) first, else Chat.log's own name registry with "You" remapped
    /// to the active character (see ResolveDisplayName) and its class resolved via
    /// ResolveClassName -- Chat.log never supplies a class as data, only skill usage to infer it
    /// from.</summary>
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

        // Fill from what an earlier session worked out. Only where this session has nothing: a
        // class detected live is current evidence and outranks a remembered one, which could be
        // from before the player rerolled or transferred.
        if (row.ClassName is "?" or "" && _knownPlayers.Find(row.Name) is { ClassName.Length: > 0 } remembered)
        {
            row.ClassName = remembered.ClassName;
        }

        _relicApByPerson.TryGetValue(row.Name, out long rowRelicAp);
        row.RelicAp = rowRelicAp;
    }

    /// <summary>
    /// The footer's "AP:" counter: the session's own AP counter (local player only -- Chat.log
    /// reports AP gains for nobody else) plus relic AP, which exists for every person in the group
    /// (see Data/RelicApDatabase). Null, not 0, when there is nothing to show. Not used for the
    /// per-row line anymore -- see PlayerRow.RelicAp -- since that one is deliberately relics-only.
    /// </summary>
    private long? ApTotalFor(string personName, bool isLocalPlayer)
    {
        _relicApByPerson.TryGetValue(personName, out long relicAp);
        long total = relicAp + (isLocalPlayer ? _totalAp : 0);
        return isLocalPlayer || relicAp > 0 ? total : null;
    }

    /// <summary>Repaints both places AP appears -- the footer counter and the per-row "AP:" lines
    /// -- after relic loot changed a total. Called from OnLootAcquired, which runs on the chat-log
    /// timer just like damage updates do.</summary>
    private void RefreshApDisplays()
    {
        ApValueText.Text = ApTotalFor(ResolveLootPerson("You") ?? "You", isLocalPlayer: true)?.ToString("N0") ?? "-";
        RefreshRows();
    }

    /// <summary>"You" resolves via the active character's registered profile; anyone else via
    /// UpdateOtherPlayerClass's detections. "?" (never null) for a class Chat.log hasn't revealed
    /// yet -- shared by ApplyIdentity (row display) and RefreshRows (ClassFilter, see its
    /// remarks), so both agree on exactly the same answer for the same id.</summary>
    private string ResolveClassName(int sourceId)
    {
        if (_chatLogParser?.Names.NameFor(sourceId) == "You")
        {
            return _characters.FirstOrDefault(c => c.Name == _activeCharacterName)?.ClassName ?? "?";
        }

        return _detectedClassByName.TryGetValue(ResolveDisplayName(sourceId), out string? detectedClass) ? detectedClass : "?";
    }

    /// <summary>Adds any newly-seen DAMAGE targets to the dropdown (never removes -- only Clear
    /// does that); "All" is the one entry with no Tag, everything else carries its target object
    /// id. Heal targets excluded on purpose -- found against a real Chat.log session where a
    /// healed party member ("Thai", from "... recovered ... HP because Inss used ...") showed up
    /// as a selectable "Mob/Boss", which they plainly aren't (see LiveAggregator.Summarize's
    /// remarks for the same underlying IsHeal-filter gap in a different consumer).</summary>
    private void RefreshMobBossFilterItems()
    {
        var knownIds = MobBossFilter.Items.OfType<ComboBoxItem>()
            .Where(i => i.Tag is int)
            .Select(i => (int)i.Tag!)
            .ToHashSet();

        foreach (int targetId in _aggregator.Events.Where(ev => !ev.IsHeal).Select(ev => ev.TargetObjectId).Distinct())
        {
            if (knownIds.Contains(targetId))
            {
                continue;
            }

            MobBossFilter.Items.Add(new ComboBoxItem
            {
                Content = _targetNames.TryGetValue(targetId, out string? name) ? name : ResolveDisplayName(targetId),
                Tag = targetId,
            });
        }
    }

    private void OnMobBossFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        // MobBossFilter's XAML sets SelectedIndex="0", which makes WPF fire this handler from
        // inside InitializeComponent itself -- while the rest of the tree (including DpsColumn,
        // declared further down in the XAML) hasn't been built yet. Found the hard way: every
        // single GUI launch crashed with a NullReferenceException on DpsColumn before a window
        // ever appeared. IsInitialized only becomes true once the whole tree exists.
        if (!IsInitialized)
        {
            return;
        }

        _selectedTargetId = (MobBossFilter.SelectedItem as ComboBoxItem)?.Tag as int?;
        DpsColumn.Header = _selectedTargetId is int ? "Damage / iDPS" : "Damage / DPS";
        RefreshRows();
    }

    /// <summary>
    /// Builds the upload payload for whichever target <paramref name="targetId"/> refers to, from
    /// <see cref="_rows"/> as it stands right now -- so the caller must have already pointed
    /// <see cref="_selectedTargetId"/> at this target and called <see cref="RefreshRows"/>, the same
    /// way the grid itself gets Name/ClassName/Faction resolved (see ApplyIdentity/ApplySide). This
    /// deliberately reuses that already-correct state rather than re-deriving faction/class logic a
    /// second time here. Returns null when there is nothing to upload (target never hit, or no
    /// player rows survived the "players only" filter).
    /// </summary>
    private EncounterUploadRequest? BuildEncounterUpload(int targetId)
    {
        var targetHits = _aggregator.Events.Where(e => e.TargetObjectId == targetId && !e.IsHeal).ToList();
        if (targetHits.Count == 0 || _rows.Count == 0)
        {
            return null;
        }

        DateTime startedAt = targetHits.Min(e => e.Timestamp).ToUniversalTime();
        DateTime endedAt = targetHits.Max(e => e.Timestamp).ToUniversalTime();
        string bossName = _targetNames.TryGetValue(targetId, out string? n) ? n : ResolveDisplayName(targetId);

        var participants = new List<ParticipantUpload>();
        foreach (PlayerRow row in _rows)
        {
            var hitsOnBoss = targetHits.Where(e => e.SourceObjectId == row.ObjectId).ToList();
            if (hitsOnBoss.Count == 0)
            {
                continue;
            }

            bool isSelf = _chatLogParser?.Names.NameFor(row.ObjectId) == "You";
            double idps = DpsCalculator.TargetIDps(_aggregator.Events, targetId, row.ObjectId) ?? 0;
            var skills = SkillBreakdown.For(hitsOnBoss, trustLoggedFlag: isSelf)
                .Select(s => new SkillUsageUpload(s.Skill, s.Hits, s.CritHits, s.Total, s.Min, s.Max))
                .ToList();

            participants.Add(new ParticipantUpload(
                row.Name, row.ClassName, row.Faction, isSelf,
                row.Damage, idps, idps, ApTotal: null, skills));
        }

        if (participants.Count == 0)
        {
            return null;
        }

        return new EncounterUploadRequest(AppVersion.Text, bossName, startedAt, endedAt, participants);
    }

    /// <summary>Uploads exactly the boss the Mob/Boss filter is currently showing - the Damage
    /// view's Upload button, and the Session menu's "Upload current boss".</summary>
    private async void OnUploadCurrentBossClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedTargetId is not int targetId)
        {
            ShowUploadStatus("Select a boss in the Mob/Boss filter first.");
            return;
        }

        var payload = BuildEncounterUpload(targetId);
        if (payload is null)
        {
            ShowUploadStatus("Nothing recorded for this boss yet.");
            return;
        }

        ShowUploadStatus("Uploading...");
        bool ok = await UploadClient.SendAsync(payload);
        ShowUploadStatus(ok ? "Uploaded." : "Upload failed - dpsmeter.skeeve.tv unreachable.");
    }

    /// <summary>
    /// Uploads every boss the Mob/Boss filter has accumulated since the last Clear, one request
    /// each - the server recognizes fights uploaded by different group members as the same run
    /// itself (see backend/src/matching/merge.ts), so this does not need to know whether anyone
    /// else in the group already uploaded. Temporarily drives the same _selectedTargetId/RefreshRows
    /// state the filter dropdown itself uses for each boss in turn, then restores whatever the user
    /// had selected - visibly flipping the grid through each boss while it runs, which is expected
    /// for a menu action the user triggered on purpose (not a background operation).
    /// </summary>
    private async void OnUploadLastRunClicked(object sender, RoutedEventArgs e)
    {
        int? previousTarget = _selectedTargetId;
        var targetIds = MobBossFilter.Items.OfType<ComboBoxItem>()
            .Where(i => i.Tag is int)
            .Select(i => (int)i.Tag!)
            .ToList();

        if (targetIds.Count == 0)
        {
            ShowUploadStatus("No boss fights recorded since the last Clear.");
            return;
        }

        ShowUploadStatus($"Uploading {targetIds.Count} boss fight(s)...");
        int uploaded = 0;
        foreach (int targetId in targetIds)
        {
            _selectedTargetId = targetId;
            RefreshRows();
            var payload = BuildEncounterUpload(targetId);
            if (payload is null)
            {
                continue;
            }

            if (await UploadClient.SendAsync(payload))
            {
                uploaded++;
            }
        }

        _selectedTargetId = previousTarget;
        RefreshRows();

        ShowUploadStatus(uploaded > 0
            ? $"Uploaded {uploaded} of {targetIds.Count} boss fight(s)."
            : "Upload failed - dpsmeter.skeeve.tv unreachable.");
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

    private void OnLoadDemoDataClicked(object sender, RoutedEventArgs e)
    {
        // Same numbers as SelfCheck's Gladiator/Zauberer scenario -- lets the UI be checked
        // visually without playing, and the DPS column can be eyeballed against the selftest's
        // console output for the same inputs.
        var start = DateTime.UtcNow;
        const int gladiator = 1;
        const int zauberer = 2;
        const int boss = 100;
        const int eliteGuard = 101;

        _targetNames[boss] = "Training Dummy";
        _targetNames[eliteGuard] = "Elite Guard";

        // Identity set BEFORE the row exists, not patched onto it afterwards: a row created by
        // RefreshRows only knows the identity if it's already in _playerIdentities by then (see
        // that dictionary's remarks -- a PlayerRow patched in place loses its name/class/level for
        // good the moment it gets filtered out and later recreated).
        _playerIdentities[gladiator] = ("Strohmie", "Gladiator", 80);
        _playerIdentities[zauberer] = ("Nxrse", "Sorcerer", 80);

        _aggregator.IngestEvents(new[]
        {
            new DamageEvent(start, gladiator, boss, 1000, IsHeal: false),
            new DamageEvent(start, gladiator, boss, 1200, IsHeal: false),
            new DamageEvent(start.AddSeconds(2), gladiator, boss, 1100, IsHeal: false),
            new DamageEvent(start.AddSeconds(1), zauberer, boss, 50_000, IsHeal: false),

            // Second target, hit by only one of the two sources -- so switching the Mob/Boss
            // filter to it visibly changes both which rows appear and what their DPS/iDPS numbers
            // are, instead of just relabeling the same aggregate total.
            new DamageEvent(start.AddSeconds(3), gladiator, eliteGuard, 800, IsHeal: false),
            new DamageEvent(start.AddSeconds(5), gladiator, eliteGuard, 900, IsHeal: false),
        });

        RefreshRows();
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
                MessageBox.Show(this,
                    "This copy was not installed by the updater, so it cannot update itself.\n\n" +
                    "That is normal for a build run straight from source or unzipped by hand. " +
                    "Installed copies update themselves silently.",
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
                MessageBox.Show(this, $"Could not reach GitHub to check for updates.\n\n{ex.Message}",
                    "Check for updates", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        if (update is null)
        {
            UpdateNotice.Visibility = Visibility.Collapsed;
            if (announceResult)
            {
                MessageBox.Show(this, $"You are running the latest version ({AppVersion.Text}).",
                    "Check for updates", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return;
        }

        string version = update.TargetFullRelease.Version.ToString();

        // Downloading the same update again on every timer tick would re-fetch it twelve times an
        // hour for as long as the meter stays open.
        if (_downloadedUpdate is not null)
        {
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
                MessageBox.Show(this, $"The update could not be downloaded.\n\n{ex.Message}",
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

    private void OnCheckForUpdatesClicked(object sender, RoutedEventArgs e) => _ = RunUpdateCheck(announceResult: true);

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
    /// </summary>
    private void OfferRestart(VelopackUpdateInfo update, string version)
    {
        var answer = MessageBox.Show(this,
            $"Version {version} has been downloaded (you have {AppVersion.Text}).\n\n" +
            "Restart the meter now to use it? If you'd rather not, it will be applied the next " +
            "time you start the meter anyway.",
            "Update ready", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        // Window geometry and settings are saved in OnClosing, which ApplyAndRestart never reaches
        // because it ends the process itself -- so save first, then hand over.
        SaveWindowStateToSettings();
        UpdateService.ApplyAndRestart(update);
    }

    /// <summary>
    /// Shows how big Chat.log has got, once it passes the threshold. Aion never rotates or trims
    /// that file -- it only grows, for as long as the client is installed -- so nothing else will
    /// ever tell the user about it.
    /// </summary>
    private void RefreshChatLogSizeWarning()
    {
        long size = _chatLogPath is null ? 0 : ChatLogMaintenance.SizeOf(_chatLogPath);
        if (size < ChatLogMaintenance.WarnThresholdBytes)
        {
            ChatLogSizeWarning.Visibility = Visibility.Collapsed;
            return;
        }

        ChatLogSizeWarning.Text = $"Chat.log {size / (1024.0 * 1024.0):F0} MB - click to empty";
        ChatLogSizeWarning.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Empties Chat.log after asking. Deliberately a confirmation and not a quiet action: this is
    /// the only thing the meter does that writes outside its own settings, and what it discards is
    /// the user's chat history, not the meter's data.
    /// </summary>
    private void OnEmptyChatLogClicked(object sender, RoutedEventArgs e)
    {
        if (_chatLogPath is null || !File.Exists(_chatLogPath))
        {
            MessageBox.Show(this, "No Chat.log found. Set your Aion folder under Settings first.",
                "Empty Chat.log", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        long size = ChatLogMaintenance.SizeOf(_chatLogPath);
        string question =
            $"Empty Chat.log?\n\n{_chatLogPath}\ncurrently {size / (1024.0 * 1024.0):F0} MB\n\n" +
            "The file is emptied, not deleted, and the meter keeps recording. Everything Aion wrote " +
            "into it so far is gone for good - including chat, not just combat lines.";

        // Refused, not merely discouraged. Truncating a file another process holds open does not
        // reclaim anything: the client keeps its write offset, so its next line restores the file
        // to its old length as NUL bytes first. Emptying a 50 MB log with Aion running would leave
        // 50 MB of zeros behind -- the very thing the user is trying to get rid of. Measured, see
        // SelfCheck.RunChatLogMaintenanceScenario.
        if (ChatLogMaintenance.IsHeldByAnotherProcess(_chatLogPath))
        {
            MessageBox.Show(this,
                "Aion has Chat.log open right now, so emptying it would not free anything.\n\n" +
                "The client keeps writing at the position it already reached, so the file would " +
                "immediately grow back to its current size as empty bytes. Close Aion first, then " +
                "empty it - the meter can stay open.",
                "Empty Chat.log", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, question, "Empty Chat.log", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        switch (ChatLogMaintenance.Empty(_chatLogPath))
        {
            case EmptyResult.Emptied:
                RefreshChatLogSizeWarning();
                break;

            case EmptyResult.NoPermission:
                MessageBox.Show(this,
                    "Windows would not let the meter write there.\n\n" +
                    "That happens when Aion is installed under Program Files: the meter runs without " +
                    "administrator rights on purpose, so it cannot modify files in a protected folder. " +
                    "Empty the file by hand, or move the Aion install somewhere in your user profile.",
                    "Empty Chat.log", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;

            case EmptyResult.Failed:
                MessageBox.Show(this, "Chat.log could not be emptied - something is holding it open exclusively.",
                    "Empty Chat.log", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;
        }
    }

    /// <summary>
    /// Flips the selected player between the two factions and pins that choice. Needed because the
    /// log cannot always answer it: an arena opponent is frequently the SAME faction as you, and
    /// trading blows with someone proves only that you are fighting them.
    ///
    /// <para>Written through SetFactionManually rather than Remember, so the resolver's own verdict
    /// never quietly takes it back on the next refresh.</para>
    /// </summary>
    private void OnSwitchFactionClicked(object sender, RoutedEventArgs e)
    {
        if (PlayersGrid.SelectedItem is not PlayerRow row || row.Name.Length == 0)
        {
            return;
        }

        // With nothing known yet, start from whichever faction is not the local player's -- the
        // reason to reach for this menu is almost always an opponent.
        string current = row.Faction;
        string next = current == "Elyos" ? "Asmodian"
            : current == "Asmodian" ? "Elyos"
            : Opposite(OwnFaction()) is { Length: > 0 } opposite ? opposite : "Elyos";

        _knownPlayers.SetFactionManually(row.Name, next);
        _knownPlayers.SaveIfChanged();
        row.Faction = next;
    }

    /// <summary>
    /// Opens the remembered-player list. Reachable from the menu rather than only from a row's
    /// context menu, because the thing most likely to need fixing -- a faction derived wrongly in
    /// an arena -- concerns someone who is no longer in the current session.
    /// </summary>
    private void OnPlayerDatabaseClicked(object sender, RoutedEventArgs e)
    {
        new PlayerDatabaseWindow(_knownPlayers, OwnFaction()) { Owner = this }.ShowDialog();

        // A faction corrected in there has to reach the rows that are on screen right now.
        RefreshRows();
    }

    private void OnShowPlayerDetailsClicked(object sender, RoutedEventArgs e)
    {
        if (PlayersGrid.SelectedItem is not PlayerRow row)
        {
            return;
        }

        bool isLocalPlayer = _chatLogParser?.Names.NameFor(row.ObjectId) == "You";
        var mine = _aggregator.Events.Where(ev => ev.SourceObjectId == row.ObjectId).ToList();

        new PlayerDetailsWindow(row.Name, row.ClassName, row.Faction, isLocalPlayer, mine,
            id => _chatLogParser?.Names.NameFor(id) ?? ResolveDisplayName(id))
        {
            Owner = this,
        }.Show();
    }

    private void OnClearClicked(object sender, RoutedEventArgs e) => ClearActiveView();

    /// <summary>Delegates to Combat/EngagedTargets, which is where the two failure modes this
    /// filter exists for are documented and tested.</summary>
    private List<DamageEvent> RestrictToEngagedTargets(List<DamageEvent> damageEvents)
    {
        // Demo data has no Chat.log name table, so there is no "You" id to anchor on.
        return _chatLogParser is null
            ? damageEvents
            : EngagedTargets.Filter(damageEvents, _chatLogParser.Names.GetOrAssignId("You"));
    }

    /// <summary>Shared by the toolbar Clear button and the ".cleardmg" in-game command.</summary>
    /// <summary>
    /// Clears whichever view is showing, per the user: damage and loot are separate records of the
    /// same session and are wanted separately -- clearing a botched pull should not throw away the
    /// loot that already dropped, and vice versa.
    /// </summary>
    private void ClearActiveView()
    {
        if (LootGrid.Visibility == Visibility.Visible)
        {
            ClearLootData();
        }
        else
        {
            ClearDamageData();
        }
    }

    private void ClearDamageData()
    {
        _aggregator.Clear();
        _rows.Clear();
        _rowsByObjectId.Clear();
        _targetNames.Clear();
        _playerIdentities.Clear();
        _selectedTargetId = null;
        DpsColumn.Header = "Damage / DPS";

        // Personal stats belong to the damage side: they are the run's own counters (XP/AP/GP/Kinah
        // earned while fighting), not a property of the loot table.
        _totalExp = 0;
        _totalAp = 0;
        _totalGp = 0;
        _totalKinah = 0;
        ExpValueText.Text = "-";
        ApValueText.Text = "-";
        GpValueText.Text = "-";
        KinahValueText.Text = "-";

        while (MobBossFilter.Items.Count > 1) // keep the XAML-declared "All" entry, drop the rest
        {
            MobBossFilter.Items.RemoveAt(1);
        }

        MobBossFilter.SelectedIndex = 0;
    }

    private void ClearLootData()
    {
        _lootRows.Clear();
        _lootRowsByKey.Clear();

        // Relic AP is derived purely from looted relics, so it goes with the loot, not the damage.
        _relicApByPerson.Clear();
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
    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        if (LootGrid.Visibility == Visibility.Visible)
        {
            CopyTextToClipboardIfAny(BuildLootChatSummary(),
                "No loot of Unique grade or better has dropped yet, and the chat summary only lists "
                + "those. Use the Table button next to it for the full loot list.");
        }
        else
        {
            CopyTextToClipboardIfAny(BuildDmgChatLine(), "No damage has been recorded yet.");
        }
    }

    private void OnCopyAllClicked(object sender, RoutedEventArgs e)
    {
        if (LootGrid.Visibility == Visibility.Visible)
        {
            CopyTextToClipboardIfAny(BuildLootDiscordTable(), "No loot has been recorded yet.");
        }
        else
        {
            CopyRowsToClipboard();
        }
    }

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
            MessageBox.Show(this, whatWasEmpty, "Nothing to copy",
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
            MessageBox.Show(this, $"The clipboard was busy and the copy failed.\n\n{ex.Message}",
                "Copy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// The ".dmg" in-game command's clipboard payload, exact format specified by the user: a
    /// single comma-separated run (not one row per line) of "rank, name, total [dps]" tuples,
    /// e.g. "1, Mitzuhiko, 3.123.456 [3.145], 2, Mitzuhiki, 3.123.455 [3.144]", ranked by damage
    /// descending -- independent of whatever order/filter the grid itself is currently showing.
    /// Both the damage total and the DPS figure use Aion's own "." thousands-grouping style (see
    /// ChatLogParser's number-format remarks) for visual consistency with what the game itself
    /// would show, not because DPS is naturally an integer -- it's rounded to match.
    /// </summary>
    private string BuildDmgRankingText()
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

        return string.Join(", ", parts);
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
    private string BuildDmgChatLine()
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

        return string.Join(", ", parts);
    }

    private static readonly NumberFormatInfo DotGroupedNumberFormat = new() { NumberGroupSeparator = "." };

    /// <summary>
    /// Fixed-width table in a code fence for Discord (CopyAll's payload while the Loot view is
    /// active -- see OnCopyAllClicked), grouped by person then quantity descending. It used to be a
    /// Markdown table, which Discord does not render at all: the pipes arrived literally and
    /// nothing lined up. Grade is shown as its name
    /// (Common/Rare/Hero/Unique/Legendary/Ultimate) rather than a color, since Discord doesn't
    /// render Aion's in-chat rarity colors; "?" for an id ItemDatabase couldn't resolve.
    /// </summary>
    private string BuildLootDiscordTable()
    {
        var ranked = _lootRows.OrderBy(r => r.Person, StringComparer.Ordinal).ThenByDescending(r => r.Quantity).ToList();
        if (ranked.Count == 0)
        {
            return "";
        }

        return AsciiTable.Render(
            new[] { "Person", "Item", "Qty", "Grade" },
            ranked.Select(r => (IReadOnlyList<string>)new[]
            {
                r.Person,
                r.ItemName,
                r.Quantity.ToString("N0", DotGroupedNumberFormat),
                r.Grade is ItemGrade g ? g.ToString() : "?",
            }).ToList(),
            new[] { false, false, true, false });
    }

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

    /// <summary>
    /// The ".loot" in-game command's clipboard payload: per person, one repeated icon per
    /// Legend(blue)/Unique(gold)/Epic(orange)/Mythic(purple) item quantity they're credited with
    /// this session. Blue was added on the user's request -- the Veteran's Composite Manastone
    /// Bundle that drops in Sauro is a Legend, and leaving it out meant the summary silently
    /// skipped the one drop the group cares most about after the rare top-tier pieces. Godstones/
    /// Designs/Recipes are tracked (see IsTrackedLoot) but deliberately don't contribute here --
    /// this summary is specifically about rarity tier, not about those categories. Capped per
    /// color per person so one freak stack can't produce an unpasteable wall of icons.
    /// </summary>
    private string BuildLootChatSummary()
    {
        var byPerson = _lootRows
            .GroupBy(r => r.Person)
            .Select(g => new
            {
                Person = g.Key,
                Legend = g.Where(r => r.Grade == ItemGrade.Legend).Sum(r => r.Quantity),
                Unique = g.Where(r => r.Grade == ItemGrade.Unique).Sum(r => r.Quantity),
                Epic = g.Where(r => r.Grade == ItemGrade.Epic).Sum(r => r.Quantity),
                Mythic = g.Where(r => r.Grade == ItemGrade.Mythic).Sum(r => r.Quantity),
            })
            .Where(p => p.Legend > 0 || p.Unique > 0 || p.Epic > 0 || p.Mythic > 0)
            .OrderBy(p => p.Person, StringComparer.Ordinal);

        // Ascending rarity, so the rarest sits at the end of each person's run of icons where it
        // is easiest to spot when the line is scanned quickly in chat.
        return string.Join("  ", byPerson.Select(p =>
            $"{p.Person}: {RepeatIcon(LegendIcon, p.Legend)}{RepeatIcon(UniqueIcon, p.Unique)}"
            + $"{RepeatIcon(EpicIcon, p.Epic)}{RepeatIcon(MythicIcon, p.Mythic)}"));
    }

    private const int MaxIconsPerColor = 30;

    private static string RepeatIcon(string icon, long count) =>
        string.Concat(Enumerable.Repeat(icon, (int)Math.Min(count, MaxIconsPerColor)));

    /// <summary>
    /// The ".ap" in-game command's clipboard payload: per person, only what their held relics will
    /// pay out once exchanged (see Data/RelicApDatabase) -- ranked highest first, same "Name AP"
    /// shape as BuildDmgChatLine, joined by ", " for pasting straight into the Aion chat box.
    /// Deliberately excludes the session's real AP total (Chat.log only reports that for the local
    /// player anyway): this line exists so the group can see who's still holding relics and decide
    /// who to route them to next, not to report anyone's overall AP progress.
    /// </summary>
    private string BuildRelicApText()
    {
        var parts = _relicApByPerson
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => $"{kv.Key} {kv.Value.ToString("N0", DotGroupedNumberFormat)}");

        return string.Join(", ", parts);
    }

    private void OnPauseClicked(object sender, RoutedEventArgs e) => SetPaused(!_paused);

    /// <summary>Shared by the toolbar Pause/Resume button and the ".pause"/".resume" in-game
    /// commands -- those set an explicit target state rather than toggling.</summary>
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
            StartChatLogTailing(settings); // possibly a new/changed AionInstallFolder
            RefreshCharacterSettings(settings); // possibly a new/changed character list or active one
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
        PlayersGrid.Visibility = Visibility.Visible;
        LootGrid.Visibility = Visibility.Collapsed;
        DamageNavButton.FontWeight = FontWeights.Bold;
        LootNavButton.FontWeight = FontWeights.Normal;
        UploadBossButton.Visibility = Visibility.Visible;
    }

    private void OnShowLootView(object sender, RoutedEventArgs e)
    {
        PlayersGrid.Visibility = Visibility.Collapsed;
        LootGrid.Visibility = Visibility.Visible;
        DamageNavButton.FontWeight = FontWeights.Normal;
        LootNavButton.FontWeight = FontWeights.Bold;
        // The Mob/Boss filter next to it has no meaning for loot, so neither does uploading "the
        // currently filtered boss" - the Session menu's upload items stay reachable regardless.
        UploadBossButton.Visibility = Visibility.Collapsed;
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

    /// <summary>
    /// Found by the user, comparing against their Timetable project's overlay: click-through
    /// alone isn't enough for an overlay that's meant to sit on top of the game. A click-through
    /// window that isn't also topmost can end up BEHIND the game, at which point it may as well
    /// not exist; hiding the taskbar entry while it's active matches the same "this is an overlay
    /// right now, not a normal window" framing. Both are restored to whatever they were before the
    /// moment Hide UI is toggled back off, rather than forced permanently.
    /// </summary>
    private void SetHideUi()
    {
        _hideUiActive = !_hideUiActive;
        NormalContent.Visibility = _hideUiActive ? Visibility.Collapsed : Visibility.Visible;
        OverlayContent.Visibility = _hideUiActive ? Visibility.Visible : Visibility.Collapsed;
        _overlay?.SetClickThrough(_hideUiActive);

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
            ShowInTaskbar = false;
        }
        else
        {
            Topmost = _topmostBeforeHideUi;
            ShowInTaskbar = true;
        }
    }
}
