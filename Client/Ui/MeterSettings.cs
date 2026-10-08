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

    /// <summary>Share-of-group bar under each row's Damage/DPS line (see PlayerRow.SharePercent).</summary>
    public bool ShowShareBars { get; set; } = true;

    /// <summary>"↓ N" damage-received figure on each row's second line (see PlayerRow.DamageTaken).
    /// Off by default, per the user: most players never want this second line at all, so the row
    /// stays at its narrower single-line height until someone opts in.</summary>
    public bool ShowDamageTaken { get; set; }



    /// <summary>Whether finished fights are filed into the local history (History/FightRecorder,
    /// %AppData%\Aion DPS Meter\fights.db). Local only - nothing about it is ever uploaded.</summary>
    public bool RecordFightHistory { get; set; } = true;

    /// <summary>History retention: fights older than this, or beyond the newest
    /// <see cref="HistoryMaxFights"/>, are pruned at startup.</summary>
    public int HistoryRetentionDays { get; set; } = 90;

    public int HistoryMaxFights { get; set; } = 2000;

    /// <summary>GUI display language, an ISO 639-1 code from LocalizationManager.SupportedLanguages
    /// (e.g. "de"), or "" on a fresh install to mean "use whatever LocalizationManager already
    /// auto-detected from the OS at startup, and don't overwrite it here." Independent of the game client's
    /// language (see Localization.cs) -- this is purely which language the meter's OWN menus/buttons/labels render in.</summary>
    public string Language { get; set; } = "";

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

    /// <summary>Scale of the compact overlay, 0.7 to 2, set by its corner grip.</summary>
    public double OverlayScale { get; set; } = 1.0;

    /// <summary>The boss's health bar in the compact overlay (current / full, %, HP check mark).</summary>
    public bool ShowBossHp { get; set; }

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

    /// <summary>Goes round damage, healing and damage taken (see MainWindow.NextMode).</summary>
    public string HotkeyMode { get; set; } = "Ctrl+Alt+M";

    /// <summary>Shows or hides the timetable overlay (see TimetableWindow).</summary>
    public string HotkeyTimetable { get; set; } = "Ctrl+Alt+T";

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

    public double PetFarmScale { get; set; } = 1.0;

    /// <summary>The pet map (see the Pet-Karte settings page): shown or not, how many metres around the player it covers, how opaque it is.</summary>
    public bool ShowPetMap { get; set; }

    public int PetMapRadius { get; set; } = 150;

    public double PetMapOpacity { get; set; } = 0.7;

    /// <summary>The pets whose monsters the pet map shows (pet ids). Empty until the player picks some.</summary>
    public List<int> PetMapPets { get; set; } = new();

    /// <summary>Also read the name on the target plate from the screen when the game's own mark message has not been seen (off: only the packet is used, nothing is captured).</summary>
    public bool PetFarmScreenFallback { get; set; }

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
    private static string SettingsPath => Path.Combine(
        Environment.GetEnvironmentVariable("AIONDPS_DATA_DIR") is { Length: > 0 } testDir // tests only: a throw-away data folder
            ? testDir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aion DPS Meter"),
        "meter-settings.json");

    /// <summary>Where older, elevated builds kept the file. Read once, on first launch after the
    /// upgrade, so an existing Aion folder and character list survive the move instead of the
    /// user finding an empty settings dialog.</summary>
    private static string LegacySettingsPath => Path.Combine(AppContext.BaseDirectory, "meter-settings.json");

    public static MeterSettings Load()
    {
        try
        {
            string path = File.Exists(SettingsPath) ? SettingsPath
                : File.Exists(LegacySettingsPath) ? LegacySettingsPath
                : SettingsPath;

            if (File.Exists(path))
            {
                string text = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<MeterSettings>(text);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to defaults -- a corrupt settings file should not block the app from starting.
        }

        return new MeterSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
