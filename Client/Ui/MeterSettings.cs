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
    /// auto-detected from the OS at startup, and don't overwrite it here." Independent of Chat.log's
    /// own language (see ChatLogParser's multi-language remarks and Localization.cs) -- this is
    /// purely which language the meter's OWN menus/buttons/labels render in.</summary>
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

    /// <summary>Whether the own character profile is uploaded by itself a few seconds after a login
    /// (see MainWindow.ScheduleOwnProfileUpload), so the player can be found on the website. On by
    /// default; the manual upload button works either way.</summary>
    public bool AutoUploadProfile { get; set; } = true;

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
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Aion DPS Meter", "meter-settings.json");

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
                var loaded = JsonSerializer.Deserialize<MeterSettings>(File.ReadAllText(path));
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
