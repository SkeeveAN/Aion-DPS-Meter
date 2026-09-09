using System.IO;
using System.Text.Json;

namespace AionSniffer.Ui;

/// <summary>One of the user's own characters, entered by hand in Settings -- see MeterSettings.
/// Characters remarks for why this can't be auto-detected from Chat.log.</summary>
public sealed class CharacterProfile
{
    public string Name { get; set; } = "";
    public string ClassName { get; set; } = "";

    /// <summary>"Elyos" or "Asmodian". Chat.log never states a faction for anyone, not even the
    /// local player, so this is the one fact the meter cannot derive and has to be told. Everyone
    /// else's faction is then worked out relative to it -- see Combat/FactionResolver. Empty for
    /// characters registered before this field existed; the resolver still separates the two sides
    /// in that case, it just cannot put a name to either.</summary>
    public string Faction { get; set; } = "";

    /// <summary>Per the user: a character belongs to exactly one server, so the list needs to say
    /// which -- someone with characters on two different private servers could otherwise register
    /// the same name twice with no way to tell the entries apart. Stamped automatically from
    /// whatever Server/ServerIdentity.cs currently detects for the configured Aion install folder
    /// (see SettingsWindow's Add/Update handlers), not typed by hand: a character can't actually
    /// exist on a server other than the one its own client connects to, so asking the user to enter
    /// this manually would only add a chance to get it wrong. Null for a character added before
    /// this field existed, or before any Aion folder was configured.</summary>
    public string? ServerFingerprint { get; set; }

    /// <summary>Cosmetic label for <see cref="ServerFingerprint"/>, same snapshot-at-add-time
    /// origin as MeterSettings.ServerDisplayName -- kept alongside the character so the list still
    /// reads as a name (e.g. "Origin Aion") even if the install folder's own label changes later.</summary>
    public string? ServerDisplayName { get; set; }

    /// <summary>What the character list actually displays, parens and all: the friendly name when
    /// there is one, the raw fingerprint as a fallback (still better than nothing), or blank for a
    /// character predating server tracking -- never a fabricated guess. Pre-formatted here rather
    /// than via a XAML converter, same reasoning as PlayerRow.ApDisplay: an empty string renders as
    /// nothing, which is simpler than a StringFormat + visibility-converter pair for the same
    /// result.</summary>
    public string ServerLabel => (ServerDisplayName ?? ServerFingerprint) is string label ? $" ({label})" : "";
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

    /// <summary>PlayersGrid's Name/Damage-DPS column widths, same save-on-close/restore-on-open
    /// deal as the window geometry above -- a DataGridColumn is user-resizable by dragging its
    /// border, but nothing persisted that on its own; found the same way (the user resized both,
    /// found gone again next launch).</summary>
    public double? NameColumnWidth { get; set; }
    public double? DpsColumnWidth { get; set; }

    /// <summary>Whether the meter asks GitHub for a newer release -- at startup and every five
    /// minutes while it runs (see MainWindow's update timer). Default on, but a real switch and
    /// not a decorative one: this is the program's only outbound network call, and the README
    /// promises that nothing leaves the machine, so anyone who wants that promise kept literally
    /// can turn it off. The "Check for updates" menu item still works when it is off -- that one
    /// is the user asking, not the program deciding.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Root folder of the Aion client install (e.g. "D:\Spiele\AION\OriginAion"), set in
    /// the Settings dialog. This is where Chat.log lives, and there is no way to auto-discover it,
    /// so the user picks it once. Consumed by MainWindow's ChatLogTailer, restarted on change.</summary>
    public string? AionInstallFolder { get; set; }

    /// <summary>Friendly label for the server this install connects to (e.g. "Origin Aion",
    /// "EuroAion") -- purely cosmetic, sent alongside the real identifier (see
    /// Server/ServerIdentity.cs) so the community backend's leaderboards show a name instead of a
    /// bare IP:port. Optional: the backend groups correctly by the detected fingerprint alone even
    /// if this is never set, since gear/roster differences between servers mean two servers' runs
    /// must never be merged regardless of whether either has a name attached.</summary>
    public string? ServerDisplayName { get; set; }

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
