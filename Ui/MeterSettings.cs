using System.IO;
using System.Text.Json;

namespace AionSniffer.Ui;

/// <summary>One of the user's own characters, entered by hand in Settings -- see MeterSettings.
/// Characters remarks for why this can't be auto-detected from Chat.log.</summary>
public sealed class CharacterProfile
{
    public string Name { get; set; } = "";
    public string ClassName { get; set; } = "";
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

    /// <summary>Root folder of the Aion client install (e.g. "D:\Spiele\AION\OriginAion"), set in
    /// the Settings dialog. This is where Chat.log lives, and there is no way to auto-discover it,
    /// so the user picks it once. Consumed by MainWindow's ChatLogTailer, restarted on change.</summary>
    public string? AionInstallFolder { get; set; }

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
    /// Per-user settings location, NOT next to the exe. The MSI installs per-machine under
    /// Program Files, which an unprivileged process cannot write to -- and this app stopped
    /// asking for administrator rights when the packet-capture path was removed, since reading
    /// Chat.log needs none. Writing beside the exe would therefore fail silently for every
    /// installed copy: settings would appear to save and be gone again next launch.
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
