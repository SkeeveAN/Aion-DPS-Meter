using System.IO;
using System.Text.Json;

namespace AionDPS.Ui;

/// <summary>
/// Settings for the meter UI. Scoped deliberately to what the tool actually has a data source for.
/// </summary>
public sealed class MeterSettings
{

    // User interface
    public string Theme { get; set; } = "Dark";
    public string FontSize { get; set; } = "Medium";

    // "Dark", or "File:name" for a theme saved from the theme editor (see ThemeFile).

    /// <summary>Share-of-group bar under each row's Damage/DPS line (see PlayerRow.SharePercent).</summary>
    public bool ShowShareBars { get; set; } = true;

    /// <summary>"↓ N" damage-received figure on each row's second line (see PlayerRow.DamageTaken).
    /// Off by default, per the user: most players never want this second line at all, so the row
    /// stays at its narrower single-line height until someone opts in.</summary>
    public bool ShowDamageTaken { get; set; }



    /// <summary>GUI display language, an ISO 639-1 code from LocalizationManager.SupportedLanguages
    /// (e.g. "de"), or "" on a fresh install to mean "use whatever LocalizationManager already
    /// auto-detected from the OS at startup, and don't overwrite it here." Independent of the game client's
    /// language (see Localization.cs) -- this is purely which language the meter's OWN menus/buttons/labels render in.</summary>
    public string Language { get; set; } = "";

    /// <summary>Posting time (epoch seconds) of the newest AION 2 announcement seen when the announcements icon was last clicked;
    /// the "New" badge shows while the server knows a newer one. 0 = never, which the first check fills in silently.</summary>
    public long AnnouncementsSeen { get; set; }

    /// <summary>Start time (epoch seconds) of the newest Twitch Drops campaign seen when the Twitch icon was last clicked;
    /// the "New" badge shows while a newer campaign is running. 0 = never, which counts as new (campaigns are time limited).</summary>
    public long TwitchDropsSeen { get; set; }

    /// <summary>The meter mode last used (Damage, Heal, Taken, All, AllCompact), restored on the next
    /// launch. Saved on every change, like the always-on-top toggle.</summary>
    public string LastMode { get; set; } = "Damage";

    /// <summary>Whether the meter window starts pinned above every other window, including the game
    /// itself. Defaults to off -- per the user, existing behaviour (an ordinary window, until turned
    /// on from the View menu's "Always on top" toggle) should not change for anyone who never asked
    /// for this. That toggle and this setting are the same value now, not two independent switches:
    /// checking one updates the other and saves immediately, the same save-on-change treatment
    /// CheckForUpdates already gets, so "how I last left it" is what a fresh launch restores.</summary>
    public bool AlwaysOnTopOnStartup { get; set; }

    /// <summary>MainWindow's size/position, saved on close and restored on next launch -- found
    /// necessary by the user, who resized the window and had it reset every restart. All four
    /// null (fresh install / older settings file) means "use the XAML default", not "0x0 at the
    /// origin".</summary>
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    /// <summary>Settings window's own size, saved on close - per the user, who resized it (the
    /// Characters tab needs real room) and had it reset every time. Position isn't remembered on
    /// purpose: it always opens CenterOwner'd on MainWindow instead, which stays correct
    /// regardless of where MainWindow itself currently is; only WindowWidth/Height above (the
    /// MAIN window) also remember position, since that one has nothing to center against.</summary>
    public double? SettingsWindowWidth { get; set; }
    public double? SettingsWindowHeight { get; set; }


    /// <summary>Whether the meter asks GitHub for a newer release -- at startup and every five
    /// minutes while it runs (see MainWindow's update timer). Default on, but a real switch and
    /// not a decorative one: this is the program's only outbound network call, and the README
    /// promises that nothing leaves the machine, so anyone who wants that promise kept literally
    /// can turn it off. The "Check for updates" menu item still works when it is off -- that one
    /// is the user asking, not the program deciding.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Last name / e-mail typed into the feedback window, so a second report is not retyped.</summary>
    public string FeedbackName { get; set; } = "";

    public string FeedbackEmail { get; set; } = "";

    /// <summary>Whether the own character profile is uploaded by itself a few seconds after a login
    /// (see MainWindow.ScheduleOwnProfileUpload), so the player can be found on the website. Off by
    /// default, like the boss upload: without an opt-in no game data leaves the PC. The manual upload
    /// button works either way.</summary>
    public bool AutoUploadProfile { get; set; }

    /// <summary>System-wide hotkeys (see GlobalHotkeys), written "Ctrl+Alt+H"; empty = off. They stand
    /// in for the chat commands, which cannot work because Aion 2's chat is not readable.</summary>
    public string HotkeyHideUi { get; set; } = "Ctrl+Alt+H";

    public string HotkeyPause { get; set; } = "Ctrl+Alt+P";

    public string HotkeyCopyDamage { get; set; } = "Ctrl+Alt+D";

    public string HotkeyClear { get; set; } = "Ctrl+Alt+Shift+X";

    /// <summary>Uploads the current boss, like the Upload button (see MainWindow.OnUploadCurrentBossClicked).</summary>
    public string HotkeyUploadBoss { get; set; } = "Ctrl+Alt+U";

    /// <summary>Whether a boss fight is uploaded by itself a few seconds after the boss died (see
    /// MainWindow.ScheduleBossUpload). Off by default: unlike the own profile, it sends fight data
    /// without a click, so the player opts in.</summary>
    public bool AutoUploadBoss { get; set; }

    /// <summary>Whether the game-server traffic is also written to a file on this PC (see DebugRecording), so it can be sent
    /// to the developer by hand. Off by default; nothing is sent by the program.</summary>
    public bool DebugRecording { get; set; }

    /// <summary>Whether the profiles of OTHER players the meter has seen (name, class, level, gear, legion)
    /// go online with the own profile upload, the Upload button and a boss upload. Off by default: it is
    /// data about people who did not agree to anything. With it off only the own character is sent.</summary>
    public bool UploadOtherPlayersProfiles { get; set; }

    /// <summary>False until the first-start wizard (WizardWindow) has been through: a fresh install, or a
    /// settings file that was lost or broken. A settings file from before the wizard existed does not have
    /// the field either, so every install sees the wizard once after updating to the version that brought it
    /// (0.11.0) - on purpose, so everybody makes the privacy choices (all off by default) once.</summary>
    public bool SetupCompleted { get; set; }

    /// <summary>The look of Hide UI: "Chips" (a click-through chip per player) or "Compact" (one
    /// transparent panel with a header, the fought boss and a dense line per player; it takes
    /// clicks, so it can be dragged, scaled and a player opened).</summary>
    public string OverlayStyle { get; set; } = "Chips";

    /// <summary>How opaque the overlay's dark backgrounds are, 0.2 to 1 (both looks).</summary>
    public double OverlayOpacity { get; set; } = 0.6;

    /// <summary>How opaque the timetable overlay's background is, 0.2 to 1. Null (a settings file from before
    /// this existed) follows <see cref="OverlayOpacity"/>, which is what the timetable used until then.</summary>
    public double? TimetableOpacity { get; set; }

    /// <summary>How many minutes ahead the timetable overlay lists events that are about to start, 0 to 60 (0 = none).</summary>
    public int TimetableLookaheadMinutes { get; set; } = 60;

    /// <summary>The world boss overlay (see WorldBossOverlayWindow): the bosses switched on in the World boss settings with the time until they are there.
    /// Off until switched on.</summary>
    public bool ShowBossOverlay { get; set; }

    /// <summary>The network overlay (see NetworkOverlayWindow): ping, traffic and connection quality of the game connection. Off until switched on;
    /// the ids of the lines switched off in the Network analysis settings are kept in <see cref="NetworkOverlayHidden"/> (everything shows by default).</summary>
    public bool ShowNetworkOverlay { get; set; }

    /// <summary>The assist overlay (see AssistOverlayWindow): own cooldowns and effects, switched in the Tools > Assist window. Locked (default): click-through; unlocked: it can be moved and scaled.</summary>
    public bool ShowAssistOverlay { get; set; }

    public bool AssistOverlayLocked { get; set; } = true;
    public double? AssistOverlayLeft { get; set; }
    public double? AssistOverlayTop { get; set; }
    public double AssistOverlayScale { get; set; } = 1.0;
    public double? AssistOverlayWidth { get; set; }
    public double? AssistOverlayHeight { get; set; }

    /// <summary>The skills and effects (skill ids) the player unticked in the assist window: the overlay does not show them (everything shows by default).</summary>
    public List<int> AssistOverlayHidden { get; set; } = new();

    public bool NetworkOverlayLocked { get; set; } = true;

    public List<string> NetworkOverlayHidden { get; set; } = new();

    public double? NetworkOverlayLeft { get; set; }
    public double? NetworkOverlayTop { get; set; }
    public double NetworkOverlayScale { get; set; } = 1.0;
    public double? NetworkOverlayWidth { get; set; }
    public double? NetworkOverlayHeight { get; set; }

    /// <summary>The dummy test overlay (see DummyTestOverlayWindow): the countdown of a training-dummy run. Shows only while a run is waiting, running or just finished.</summary>
    public bool ShowDummyOverlay { get; set; } = true;

    public bool DummyOverlayLocked { get; set; } = true;

    public double? DummyOverlayLeft { get; set; }
    public double? DummyOverlayTop { get; set; }
    public double DummyOverlayScale { get; set; } = 1.0;
    public double? DummyOverlayWidth { get; set; }
    public double? DummyOverlayHeight { get; set; }

    /// <summary>The length of the last dummy test run, in seconds (the field in the dummy test window starts with it).</summary>
    public int DummyTestSeconds { get; set; } = 120;

    /// <summary>The combat overlay (see CombatOverlayWindow): the boss cast bar, the target of the target and the healer's target in one frame.
    /// Off until switched on; every element has its own place and size inside the frame (<see cref="CombatElements"/>, in the frame's own units).</summary>
    /// <summary>The overlays step aside while a game menu is open (the game shows the mouse cursor then). Only works in the control mode that hides the cursor while playing; off until switched on.</summary>
    public bool HideDuringMenus { get; set; }

    /// <summary>The stream overlay for OBS and Streamlabs (see StreamOverlayServer): a web page that shows the meter live, for the same PC or (two-PC streaming) others in the own network. Off until switched on.</summary>
    public bool StreamOverlayOn { get; set; }

    public int StreamOverlayPort { get; set; } = 8920;

    /// <summary>The random key in the link; empty until the first start (then made once).</summary>
    public string StreamOverlayKey { get; set; } = "";

    /// <summary>Show player names in the stream. Off by default: everyone is shown by their class, like in the uploads.</summary>
    public bool StreamShowNames { get; set; }

    /// <summary>Saving a session to a file also writes the rotation, effect, stagger and replay data (a few times the size of the damage alone).</summary>
    public bool SessionSaveExtras { get; set; } = true;

    /// <summary>Where the session manager (with the combat analyzer) was left: place, size and the width of its left half.</summary>
    public double? SessionWindowLeft { get; set; }
    public double? SessionWindowTop { get; set; }
    public double? SessionWindowWidth { get; set; }
    public double? SessionWindowHeight { get; set; }
    public double? SessionSplitLeft { get; set; }

    /// <summary>Reachable from other PCs of the network (a streaming PC running OBS); off: this PC only. On by default, the link carries a key and only the own network is answered.</summary>
    public bool StreamOverlayNetwork { get; set; } = true;

    /// <summary>The control mode learnt so far: "Unknown", "Hidden" (the cursor is hidden while playing) or "Classic" (always shown; menus cannot be told apart).</summary>
    public string MenuControlMode { get; set; } = "Unknown";

    /// <summary>Shows the overlays for the menu that is open, or hides them again.</summary>
    public string HotkeyShowMenu { get; set; } = "Ctrl+Alt+J";

    public bool ShowCombatOverlay { get; set; }

    public bool CombatOverlayLocked { get; set; } = true;

    public double? CombatOverlayLeft { get; set; }
    public double? CombatOverlayTop { get; set; }
    public double CombatOverlayScale { get; set; } = 1.0;
    public double? CombatOverlayWidth { get; set; }
    public double? CombatOverlayHeight { get; set; }

    /// <summary>The boss cast bar is a window of its own, placed and scaled apart from the target of the target (whose frame is the CombatOverlay* values).</summary>
    public double? CombatCastOverlayLeft { get; set; }
    public double? CombatCastOverlayTop { get; set; }
    public double CombatCastOverlayScale { get; set; } = 1.0;
    public double? CombatCastOverlayWidth { get; set; }
    public double? CombatCastOverlayHeight { get; set; }

    /// <summary>The target's health bar is a window of its own as well.</summary>
    public double? CombatHpOverlayLeft { get; set; }
    public double? CombatHpOverlayTop { get; set; }
    public double CombatHpOverlayScale { get; set; } = 1.0;
    public double? CombatHpOverlayWidth { get; set; }
    public double? CombatHpOverlayHeight { get; set; }

    /// <summary>Place, size and scale of each element by its id ("BossCast", "TargetOfTarget", "Healer").</summary>
    public Dictionary<string, CombatElementLayout> CombatElements { get; set; } = new();

    public bool CombatShowBossCast { get; set; } = true;
    public bool CombatShowTargetOfTarget { get; set; } = true;

    /// <summary>The healer's target: its name, hit points and effects. Off until switched on.</summary>
    public bool CombatShowHealer { get; set; }
    public bool CombatHealerHpBar { get; set; } = true;
    public bool CombatHealerEffects { get; set; } = true;


    /// <summary>Which casts the bar shows: "all", "area" (at you and area attacks) or "you" (only at you).</summary>
    public string CombatCastFilter { get; set; } = "all";

    /// <summary>The most casts shown at once (1 to 3).</summary>
    public int CombatMaxCasts { get; set; } = 2;

    /// <summary>Casts of monsters that are not bosses too.</summary>
    public bool CombatCastsOfAllMonsters { get; set; }

    /// <summary>Master switch of the world boss sounds: off = no boss plays its sound. Off until switched on.</summary>
    public bool BossNotify { get; set; }

    /// <summary>Per boss (NPC id as text): watched or not, and the sound with its volume that plays when it is back. A boss without an entry is not watched.</summary>
    public Dictionary<string, BossAlertSetting> BossAlerts { get; set; } = new();

    /// <summary>Place, scale and size of the world boss overlay.</summary>
    public double? BossOverlayLeft { get; set; }
    public double? BossOverlayTop { get; set; }
    public double BossOverlayScale { get; set; } = 1.0;
    public double? BossOverlayWidth { get; set; }
    public double? BossOverlayHeight { get; set; }

    /// <summary>Master switch of the timetable reminder sounds: off = no event plays its sound (the choices below are kept).</summary>
    public bool TimetableNotify { get; set; } = false;

    /// <summary>How many minutes before an event starts its sound plays, 0 to 60 (the same for every event).</summary>
    public int TimetableSoundMinutes { get; set; } = 5;

    /// <summary>Per event id: shown in the timetable overlay or not, and the reminder sound (empty = none) with its volume. An event without an entry is shown, without sound.</summary>
    public Dictionary<string, TimetableEventSetting> TimetableEvents { get; set; } = new();

    /// <summary>Scale of the compact overlay, 0.7 to 2, set by its corner grip.</summary>
    public double OverlayScale { get; set; } = 1.0;

    /// <summary>The boss's health bar in the compact overlay (current / full, %, HP check mark).</summary>
    public bool ShowBossHp { get; set; }

    /// <summary>How opaque the boss health bar window is (0.2 to 1).</summary>
    public double BossHpOpacity { get; set; } = 1.0;

    /// <summary>How opaque the boss cast bar window is (0.2 to 1).</summary>
    public double CombatCastOpacity { get; set; } = 1.0;

    /// <summary>Start from zero when a fight begins after <see cref="AutoResetSeconds"/> without any
    /// damage, or when a boss is pulled: the meter shows the current fight, the previous one is kept
    /// in the fight history. Off by default - a reset throws the fight on screen away, so it is the
    /// player's choice. Named AutoResetEnabled since 0.10.19: the earlier "AutoReset" (on by default)
    /// was never a deliberate choice, and its saved value is dropped.</summary>
    public bool AutoResetEnabled { get; set; }

    /// <summary>Seconds without damage after which the next fight starts from zero (1 to 600).</summary>
    public int AutoResetSeconds { get; set; } = 10;

    /// <summary>Whose rows the meter shows: "All" (everybody around), "Group" (you and your party) or
    /// "Corps" (your party and the one it is joined with). Switched in the overlay's header or the
    /// Mode menu.</summary>
    public string ViewScope { get; set; } = "Group";

    /// <summary>Goes round all modes: damage, healing, damage taken, All in One, All in One Compact (see MainWindow.NextMode).</summary>
    public string HotkeyMode { get; set; } = "Ctrl+Alt+M";

    /// <summary>The hotkey overlay (see HotkeyOverlayWindow): a movable, transparent list of the hotkeys. Off until switched on; the ids of the
    /// hotkeys the player switched off in the list are kept in <see cref="HotkeyOverlayHidden"/> (everything shows by default).</summary>
    public bool ShowHotkeyOverlay { get; set; }

    public List<string> HotkeyOverlayHidden { get; set; } = new();

    public double? HotkeyOverlayLeft { get; set; }
    public double? HotkeyOverlayTop { get; set; }
    public double HotkeyOverlayScale { get; set; } = 1.0;
    public double? HotkeyOverlayWidth { get; set; }
    public double? HotkeyOverlayHeight { get; set; }

    /// <summary>Shows or hides the timetable overlay (see TimetableWindow).</summary>
    public string HotkeyTimetable { get; set; } = "Ctrl+Alt+T";

    /// <summary>Switches the pet map (with its list) on or off.</summary>
    public string HotkeyPetMap { get; set; } = "Ctrl+Alt+K";
    /// <summary>Hotkey overlay on / off.</summary>
    public string HotkeyHotkeyOverlay { get; set; } = "Ctrl+Alt+O";
    /// <summary>All overlays (hotkey list, timetable, interactive map, pet info) off, and the same ones back on.</summary>
    public string HotkeyAllOverlays { get; set; } = "Ctrl+Alt+A";
    /// <summary>World boss overlay on / off.</summary>
    public string HotkeyWorldBoss { get; set; } = "Ctrl+Alt+B";
    /// <summary>Network overlay on / off.</summary>
    public string HotkeyNetwork { get; set; } = "Ctrl+Alt+N";
    public string HotkeyCombatOverlay { get; set; } = "Ctrl+Alt+C";
    /// <summary>The arrows on the edge of the interactive map pointing at pets / gatherables that are out of view (one switch each); on by default.</summary>
    public bool ShowPetArrows { get; set; } = true;
    public bool ShowGatherArrows { get; set; } = true;
    /// <summary>A sound when a Hidden Cube appears in range (the game announces it only when the player is near): switch, sound id (see <see cref="NotifySounds.All"/>), volume 0 to 100. Off until switched on.</summary>
    public bool CubeSoundEnabled { get; set; }
    public string CubeSound { get; set; } = "ping";
    public int CubeSoundVolume { get; set; } = 70;
    /// <summary>Pet arrows / gather arrows on / off.</summary>
    public string HotkeyPetArrows { get; set; } = "Ctrl+Alt+E";
    public string HotkeyGatherArrows { get; set; } = "Ctrl+Alt+G";

    /// <summary>The notice that appears when the eye (hide the window) is clicked was dismissed with "do not show again".</summary>
    public bool HideUiHintDismissed { get; set; }

    /// <summary>The timetable overlay: what is active now and what starts within the lookahead (<see cref="TimetableLookaheadMinutes"/>). Its place
    /// and scale are remembered (null left/top = a default corner).</summary>
    public bool ShowTimetable { get; set; } = true;

    public double? TimetableLeft { get; set; }

    public double? TimetableTop { get; set; }

    public double TimetableScale { get; set; } = 1.0;

    /// <summary>The pet farming overlay (see PetFarmWindow): reads the targeted monster's name from the screen and shows its pet's level. Off until switched on.</summary>
    public bool ShowPetFarm { get; set; }

    /// <summary>Locked (default): the pet farming overlay is click-through. Unlocked: it can be clicked, moved and scaled.</summary>
    public bool PetFarmLocked { get; set; } = true;

    /// <summary>Locked (default): the small pet counter of the targeted monster is click-through. Unlocked: it can be moved and scaled. Independent of the map's lock.</summary>
    public bool PetInfoLocked { get; set; } = true;

    /// <summary>Locked (default): the world boss overlay is click-through. Unlocked: it can be clicked, moved and scaled. Independent of the pet overlays' lock.</summary>
    public bool BossOverlayLocked { get; set; } = true;

    /// <summary>Locked (default): the timetable overlay is click-through (no header, no grip). Unlocked: it can be moved and scaled.</summary>
    public bool TimetableLocked { get; set; } = true;

    public double PetFarmScale { get; set; } = 1.0;

    /// <summary>The pet map (see the Pet-Karte settings page): shown or not, how many metres around the player it covers, how opaque it is.</summary>
    public bool ShowPetMap { get; set; }

    public int PetMapRadius { get; set; } = 150;

    public double PetMapOpacity { get; set; } = 0.7;

    /// <summary>The pets whose monsters the pet map shows (pet ids). Empty until the player picks some.</summary>
    public List<int> PetMapPets { get; set; } = new();

    /// <summary>Width of the region list on the Region tab of the pet settings (the player drags the splitter).</summary>
    public double PetRegionListWidth { get; set; } = 185;

    /// <summary>Rare tab of the pet settings: a pet with at most this many spawn points on a map (all its mobs there together) counts as rare on that map.</summary>
    public int PetRareMax { get; set; } = 20;

    /// <summary>Rare tab: from this many spawn points in instances a pet is plentiful there and is not listed on the maps, only under Instances.</summary>
    public int PetRareInstanceMin { get; set; } = 40;

    /// <summary>The collectibles the interactive map shows (by their English name, see the Sammeln page); none until the player picks some.</summary>
    public List<string> PetMapGatherItems { get; set; } = new();

    /// <summary>The map and the position the pet map knew last (kept so it can show them while the position is unknown, e.g. with the game closed).</summary>
    public string? PetMapLastMap { get; set; }
    public double? PetMapLastX { get; set; }
    public double? PetMapLastY { get; set; }

    /// <summary>Also read the name on the target plate from the screen when the game's own mark message has not been seen (off: only the packet is used, nothing is captured).</summary>
    public bool PetFarmScreenFallback { get; set; }

    /// <summary>Place and size of the pet map window and of the pet list window (each on its own).</summary>
    public double? PetMapLeft { get; set; }

    public double? PetMapTop { get; set; }

    public double PetMapScale { get; set; } = 1.0;

    public double? PetListLeft { get; set; }

    public double? PetListTop { get; set; }

    public double PetListScale { get; set; } = 1.0;

    /// <summary>Own width/height of the three overlays when the player pulled an edge (null: as large as the content), before the scale.</summary>
    public double? PetMapWidth { get; set; }
    public double? PetMapHeight { get; set; }
    public double? PetListWidth { get; set; }
    public double? PetListHeight { get; set; }
    public double? PetFarmWidth { get; set; }
    public double? PetFarmHeight { get; set; }

    public double? PetFarmLeft { get; set; }

    public double? PetFarmTop { get; set; }


    /// <summary>Network adapter the Aion 2 packet capture listens on (Aion2/Capture/CaptureAdapters):
    /// null/empty = automatic (the adapter Windows routes internet traffic through), "all" = every
    /// adapter, else a NetworkInterface.Id. Matters when a gaming VPN carries the game's traffic.</summary>
    public string? CaptureAdapterId { get; set; }

    /// <summary>The user's own Aion 2 character name. Aion 2 frames name every player but give no
    /// hint which one is you, so the meter matches this name; it fills it in by itself as soon as
    /// the stream reveals it (a party roster), and Settings lets it be typed for solo play.</summary>
    public string? Aion2CharacterName { get; set; }








    /// <summary>
    /// Per-user settings location, NOT next to the exe. Beside the exe is where a self-updating
    /// install is least safe to keep anything: Velopack swaps the whole application directory when
    /// an update applies, so settings written there would be replaced along with it. (The same
    /// path was already wrong for the older per-machine MSI, which put the exe under Program Files
    /// where an unprivileged process cannot write at all.)
    /// </summary>
    /// <summary>The folder the settings and what the meter learns (cast-skills.json ...) live in.</summary>
    public static string DataDirectory => Path.GetDirectoryName(SettingsPath)!;

    private static string SettingsPath => Path.Combine(
        Environment.GetEnvironmentVariable("AIONDPS_DATA_DIR") is { Length: > 0 } testDir // tests only: a throw-away data folder
            ? testDir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aion DPS Meter"),
        "meter-settings.json");

    /// <summary>Where older, elevated builds kept the file. Read once, on first launch after the
    /// upgrade, so an existing Aion folder and character list survive the move instead of the
    /// user finding an empty settings dialog.</summary>
    private static string LegacySettingsPath => Path.Combine(AppContext.BaseDirectory, "meter-settings.json");

    private static string Sibling(string suffix) => Path.Combine(Path.GetDirectoryName(SettingsPath)!, "meter-settings." + suffix + ".json");

    /// <summary>The copy made at each start from a settings file that read fine (see <see cref="BackupOnStart"/>).</summary>
    private static string BackupPath => Sibling("backup");

    /// <summary>What "reset to defaults" took away, kept so it can be taken back (see <see cref="UndoReset"/>).</summary>
    private static string UndoPath => Sibling("undo");

    /// <summary>True once in a session when the settings file was damaged and the settings came back from the copy of the last start.</summary>
    public static bool RecoveredFromBackup { get; private set; }

    public static MeterSettings Load()
    {
        string path = File.Exists(SettingsPath) ? SettingsPath
            : File.Exists(LegacySettingsPath) ? LegacySettingsPath
            : SettingsPath;
        if (File.Exists(path))
        {
            if (TryRead(path) is { } loaded)
            {
                return loaded;
            }

            // A crash or a power cut while it was being written: keep the damaged file beside the others and go back to the copy of the last start.
            if (path == SettingsPath)
            {
                KeepDamaged(path);
                if (File.Exists(BackupPath) && TryRead(BackupPath) is { } restored)
                {
                    RecoveredFromBackup = true;
                    try
                    {
                        File.Copy(BackupPath, SettingsPath, overwrite: true);
                    }
                    catch (IOException)
                    {
                        // the next Save writes it anyway
                    }

                    return restored;
                }
            }
        }

        return new MeterSettings();
    }

    private static MeterSettings? TryRead(string path)
    {
        try
        {
            string text = File.ReadAllText(path);
            return text.Trim().Length == 0 ? null : JsonSerializer.Deserialize<MeterSettings>(text);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void KeepDamaged(string path)
    {
        try
        {
            File.Move(path, Sibling("damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")), overwrite: true);
            foreach (string old in Directory.GetFiles(Path.GetDirectoryName(path)!, "meter-settings.damaged-*.json").OrderByDescending(f => f).Skip(3))
            {
                File.Delete(old); // the three newest are enough
            }
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Written to a file of its own first and put in place at once, so a crash or a power cut never leaves half a file.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        string temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, SettingsPath, overwrite: true);
    }

    /// <summary>Copies a settings file that reads fine to the backup, once at each start; a damaged file is never copied over a good backup.</summary>
    public static void BackupOnStart()
    {
        try
        {
            if (File.Exists(SettingsPath) && TryRead(SettingsPath) is not null)
            {
                File.Copy(SettingsPath, BackupPath, overwrite: true);
            }
        }
        catch (IOException)
        {
        }
    }

    /// <summary>What a reset leaves alone: the privacy and upload decisions (a reset must never switch an upload on or off), the first-start wizard, the update check and the own character.</summary>
    private static void KeepAcrossReset(MeterSettings from, MeterSettings to)
    {
        to.SetupCompleted = from.SetupCompleted;
        to.AutoUploadProfile = from.AutoUploadProfile;
        to.AutoUploadBoss = from.AutoUploadBoss;
        to.UploadOtherPlayersProfiles = from.UploadOtherPlayersProfiles;
        to.CheckForUpdates = from.CheckForUpdates;
        to.Aion2CharacterName = from.Aion2CharacterName;
    }

    /// <summary>Puts every setting back to its default except <see cref="KeepAcrossReset"/>; what it replaced is kept for <see cref="UndoReset"/>.</summary>
    public static void ResetToDefaults()
    {
        MeterSettings current = Load();
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(UndoPath, JsonSerializer.Serialize(current, new JsonSerializerOptions { WriteIndented = true }));
        var fresh = new MeterSettings();
        KeepAcrossReset(current, fresh);
        fresh.Save();
    }

    /// <summary>Takes a reset back; false when there is nothing to take back.</summary>
    public static bool UndoReset()
    {
        if (!File.Exists(UndoPath) || TryRead(UndoPath) is not { } before)
        {
            return false;
        }

        before.Save();
        File.Delete(UndoPath);
        return true;
    }
}

/// <summary>What the player chose for one timetable event (see <see cref="MeterSettings.TimetableEvents"/>).</summary>
public sealed class TimetableEventSetting
{
    public bool Show { get; set; } = true;

    /// <summary>The id of the reminder sound (see <see cref="NotifySounds.All"/>); empty = no sound.</summary>
    public string Sound { get; set; } = "";

    /// <summary>0 to 100.</summary>
    public int Volume { get; set; } = 70;
}

/// <summary>What the player chose for one world boss (see <see cref="MeterSettings.BossAlerts"/>).</summary>
public sealed class BossAlertSetting
{
    /// <summary>The switch before the boss's name: the boss is on the overlay and plays its sound.</summary>
    public bool Enabled { get; set; }

    /// <summary>The id of the sound (see <see cref="NotifySounds.All"/>); empty = no sound.</summary>
    public string Sound { get; set; } = "chime";

    /// <summary>0 to 100.</summary>
    public int Volume { get; set; } = 60;
}

/// <summary>Place, size and scale of one element of the combat overlay, in the overlay frame's own units (its scaling excluded).</summary>
public sealed class CombatElementLayout
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Scale { get; set; } = 1.0;
}
