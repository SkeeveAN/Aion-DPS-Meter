using System.IO;
using System.Text.Json;
using AionDPS.Game;

namespace AionDPS.Ui;

/// <summary>One of the user's own characters, entered by hand in Settings -- see MeterSettings.
/// Characters remarks for why this can't be auto-detected from Chat.log.</summary>
public sealed class CharacterProfile
{
    public string Name { get; set; } = "";
    public string ClassName { get; set; } = "";

    /// <summary>Which game this character exists in. Absent in settings files written before Aion 2
    /// support, which can only mean classic Aion.</summary>
    public GameKind Game { get; set; } = GameKind.Aion;

    /// <summary>"Elyos" or "Asmodian". Chat.log never states a faction for anyone, not even the
    /// local player, so this is the one fact the meter cannot derive and has to be told. Everyone
    /// else's faction is then worked out relative to it -- see Combat/FactionResolver. Empty for
    /// characters registered before this field existed; the resolver still separates the two sides
    /// in that case, it just cannot put a name to either.</summary>
    public string Faction { get; set; } = "";

    /// <summary>The technical server identity (see Server/ServerIdentity.cs), auto-stamped from
    /// whatever config.ini currently reports for the configured Aion install folder at the moment
    /// this character is added -- separate from the user's own <see cref="ServerDisplayName"/>/
    /// <see cref="ServerVersion"/> pick below, which is what actually gets shown. Not typed by
    /// hand: a character can't actually exist on a server other than the one its own client
    /// connects to. Null for a character added before this field existed, or before any Aion
    /// folder was configured.</summary>
    public string? ServerFingerprint { get; set; }

    /// <summary>Per the user: a character belongs to exactly one server, and which one is now a
    /// required, explicit choice from the backend's curated server list (GET /api/server-catalog -
    /// see Server/ServerCatalogClient.cs), not free text -- someone with characters on two
    /// different private servers could otherwise register the same name twice with no way to tell
    /// the entries apart, or mistype a name the backend's own list already has the correct spelling
    /// for. Null only for a character registered before this picker existed.</summary>
    public string? ServerDisplayName { get; set; }

    /// <summary>The chosen catalog entry's patch version (e.g. "4.6") - kept alongside the name
    /// since private servers don't share one numbering scheme and the version is exactly the fact
    /// that tells two same-named-era servers apart.</summary>
    public string? ServerVersion { get; set; }

    /// <summary>What the character list actually displays, parens and all: name+version when both
    /// are known (the normal case for anything registered through the catalog picker), the name
    /// alone, the raw technical fingerprint as a last-resort fallback (still better than nothing),
    /// or blank for a character predating server tracking entirely -- never a fabricated guess.
    /// Pre-formatted here rather than via a XAML converter, same reasoning as PlayerRow.ApDisplay:
    /// an empty string renders as nothing, simpler than a StringFormat + visibility-converter pair
    /// for the same result.</summary>
    public string ServerLabel => ServerDisplayName is string name
        ? ServerVersion is string version ? $" ({name} {version})" : $" ({name})"
        : ServerFingerprint is string fingerprint ? $" ({fingerprint})" : "";
}

/// <summary>
/// Settings for the meter UI. Scoped deliberately to what the tool actually has a data source
/// for right now. MyAion's settings dialog (the reference the user shared) has a lot more:
/// legion/position columns, loot+kinah tracking, auto-upload to a backend, a donation goal --
/// none of that has a packet source wired up yet, so it's left out here rather than added as
/// inert checkboxes that would silently do nothing.
/// </summary>
public sealed class MeterSettings
{
    // Targets list filters (which NPC ranks are shown at all)
    public bool ShowPlayers { get; set; } = true;
    public bool ShowMinionNpcs { get; set; } = true;
    public bool ShowCommonNpcs { get; set; } = true;
    public bool ShowEliteNpcs { get; set; } = true;
    public bool ShowHeroicNpcs { get; set; } = true;
    public bool ShowLegendaryNpcs { get; set; } = true;

    // User interface
    public string Theme { get; set; } = "Dark";
    public string FontSize { get; set; } = "Medium";

    /// <summary>Share-of-group bar under each row's Damage/DPS line (see PlayerRow.SharePercent).</summary>
    public bool ShowShareBars { get; set; } = true;

    /// <summary>"↓ N" damage-received figure on each row's second line (see PlayerRow.DamageTaken).
    /// Off by default, per the user: most players never want this second line at all, so the row
    /// stays at its narrower single-line height until someone opts in.</summary>
    public bool ShowDamageTaken { get; set; }

    /// <summary>Dodge/parry/block/resist tally per row (see Combat/DefenseStats). Off by default -
    /// same reasoning as ShowDamageTaken above.</summary>
    public bool ShowDefenseStats { get; set; }

    /// <summary>Relic AP figure on each row's second line (see PlayerRow.ApDisplay). Off by
    /// default - same reasoning as ShowDamageTaken above.</summary>
    public bool ShowRelicAp { get; set; }

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

    /// <summary>Set right before an update-triggered restart (OnUpdateRestartNowClicked) to the
    /// moment Chat.log had been read up to; consumed once by the NEXT startup
    /// (ResumeFromChatLogSince) and cleared immediately after, so an ordinary restart later never
    /// replays it again. Per the user: a self-update ends the process and starts a fresh one
    /// seconds later, which would otherwise silently drop whatever happened in Chat.log during
    /// that gap - unlike an ordinary restart (closing the meter and reopening it later), where
    /// ChatLogTailer's own "never look into the past" rule is exactly what's wanted instead. Local
    /// time, matching Chat.log's own timestamps (see EventBlob's remarks on why those are
    /// Kind-unspecified local values, not UTC).</summary>
    public DateTime? PendingResumeFrom { get; set; }

    /// <summary>Whether the meter asks GitHub for a newer release -- at startup and every five
    /// minutes while it runs (see MainWindow's update timer). Default on, but a real switch and
    /// not a decorative one: this is the program's only outbound network call, and the README
    /// promises that nothing leaves the machine, so anyone who wants that promise kept literally
    /// can turn it off. The "Check for updates" menu item still works when it is off -- that one
    /// is the user asking, not the program deciding.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Which game the meter is currently pointed at - decides the combat source MainWindow
    /// builds (Chat.log tailer for classic Aion, packet capture for Aion 2), which class list and
    /// server catalog Settings offer, and what uploads are tagged as. One active game at a time,
    /// same as one active Chat.log; missing in older settings files = classic Aion. While
    /// <see cref="GameDetectionMode"/> is Automatic, MainWindow keeps overwriting this to match
    /// whichever client is actually running (see Game/GameDetector.cs) - same relationship
    /// <see cref="ActiveCharacterName"/> has to <see cref="AutoDetectActiveCharacter"/>.</summary>
    public GameKind Game { get; set; } = GameKind.Aion;

    /// <summary>Whether <see cref="Game"/> is kept in sync with the running client (Automatic, the
    /// default per the user - Aion and Aion 2 should be told apart clearly without having to
    /// remember to flip Settings' Game dropdown) or is a fixed pick Settings' dropdown controls
    /// directly (Manual). Missing in older settings files = Automatic.</summary>
    public GameDetectionMode GameDetectionMode { get; set; } = GameDetectionMode.Automatic;

    /// <summary>Root folder of the Aion client install (e.g. "D:\Spiele\AION\OriginAion"), set in
    /// the Settings dialog. This is where Chat.log lives, and there is no way to auto-discover it,
    /// so the user picks it once. Consumed by MainWindow's ChatLogTailer, restarted on change.
    /// Not needed for Aion 2, whose source captures network traffic rather than reading a file.</summary>
    public string? AionInstallFolder { get; set; }

    /// <summary>Friendly label for the server this install connects to (e.g. "Origin Aion",
    /// "EuroAion") -- purely cosmetic, sent alongside the real identifier (see
    /// Server/ServerIdentity.cs) so the community backend's leaderboards show a name instead of a
    /// bare IP:port. Optional: the backend groups correctly by the detected fingerprint alone even
    /// if this is never set, since gear/roster differences between servers mean two servers' runs
    /// must never be merged regardless of whether either has a name attached.</summary>
    public string? ServerDisplayName { get; set; }

    /// <summary>Per the user: different servers are different Aion installs with different
    /// Chat.log paths (e.g. Origin Aion under "D:\Spiele\AION\OriginAion", Aion Riftshade under
    /// "D:\Spiele\AION\Aion Riftshade") - remembered here, keyed by the same server-catalog display
    /// name as <see cref="ServerDisplayName"/>/<see cref="CharacterProfile.ServerDisplayName"/>, so
    /// picking a known server in Settings recalls its folder instead of having to browse to it
    /// again every time. Purely a convenience cache for the Settings dialog: <see
    /// cref="AionInstallFolder"/> above is still the one, single "currently active" folder
    /// MainWindow's ChatLogTailer actually reads from - this app tails one Chat.log at a time, it
    /// does not watch every known server's install at once.</summary>
    public Dictionary<string, string> ServerInstallFolders { get; set; } = new();

    /// <summary>
    /// The user's own characters (name + class), entered by hand. Chat.log never reveals the
    /// local player's real name -- verified against a real, large session: the active character
    /// is invariably written as the literal string "You", never its own name; "X has logged in"
    /// lines only ever name OTHER people (friend/legion notifications), never the reader. There is
    /// therefore no way to auto-detect this, and no point guessing (a silently wrong guessed name
    /// would corrupt the data without anyone noticing) -- the user must maintain the list, exactly
    /// as they asked for ("mehrere Namen, damit du weisst welchen Namen du eintragen musst").
    /// </summary>
    public List<CharacterProfile> Characters { get; set; } = new();

    /// <summary>Which entry in <see cref="Characters"/> "You" currently means. Null (or a name no
    /// longer in the list) falls back to displaying the literal "You". When
    /// <see cref="AutoDetectActiveCharacter"/> is true (the default), MainWindow's
    /// UpdateActiveCharacterFromSkill keeps overwriting this automatically from whichever skill
    /// "You" was last seen using; when
    /// false, only the Settings dialog's "Active character" picker changes it.</summary>
    public string? ActiveCharacterName { get; set; }

    /// <summary>
    /// Per the user: running two Aion clients at once (see MainWindow.IsNamedCopyOfRegisteredCharacter
    /// remarks) means BOTH registered characters can be generating "You used skill" lines in the
    /// same session, so skill-based auto-detection would otherwise flip ActiveCharacterName back
    /// and forth between them constantly -- exactly the opposite of what's wanted when the whole
    /// point is to pick ONE of the two to track and discard the other's lines as duplicates.
    /// Defaults to true (the original "YOU + genutzte Skills sollte ausreichen" behavior, correct
    /// for the common single-character case); turning it off freezes ActiveCharacterName at
    /// whatever the Settings dialog's picker last set, until turned back on or changed again.
    /// </summary>
    public bool AutoDetectActiveCharacter { get; set; } = true;

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
