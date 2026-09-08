using System.IO;
using System.Text.Json;

namespace AionSniffer.Ui;

/// <summary>One player the meter has seen before, with whatever it managed to work out about them.</summary>
public sealed class KnownPlayer
{
    public string Name { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string Faction { get; set; } = "";

    /// <summary>Set when the user corrected the faction by hand. The resolver then leaves this
    /// player alone: in an arena the opponent is often the SAME faction, which no amount of
    /// evidence from the log can reveal -- fighting someone proves hostility, never their
    /// banner.</summary>
    public bool FactionIsManual { get; set; }

    /// <summary>When this entry was last confirmed, so a stale record can be judged later. Stored
    /// as UTC: a raid group spans time zones, and a local timestamp would be meaningless the
    /// moment the file is compared with anyone else's.</summary>
    public DateTime LastSeenUtc { get; set; }
}

/// <summary>
/// Remembers everyone the meter has identified, so a player recognised once is known immediately
/// the next time they turn up.
///
/// <para>Both facts it stores are expensive to learn and easy to lose. A class is only detected
/// when that player happens to use a skill the database can attribute (see MainWindow's SkillUsed
/// handler), and a faction only once enough heals, hits or shared targets have accumulated to
/// place them -- which in a short fight may never happen. Neither survives a restart on its own,
/// so without this file the meter starts every session knowing nothing about people it has already
/// spent a raid identifying.</para>
///
/// <para>Deliberately never downgrades: a remembered class or faction is only ever replaced by
/// another real value, never by the empty string or "?" that a fresh, not-yet-identified row
/// carries. Otherwise the first refresh of a new session would wipe everything the file knows.</para>
/// </summary>
public sealed class KnownPlayers
{
    private readonly Dictionary<string, KnownPlayer> _byName = new(StringComparer.Ordinal);
    private bool _dirty;

    private static string Path0 => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Aion DPS Meter", "known-players.json");

    public static KnownPlayers Load()
    {
        var result = new KnownPlayers();
        try
        {
            if (File.Exists(Path0))
            {
                var rows = JsonSerializer.Deserialize<List<KnownPlayer>>(File.ReadAllText(Path0)) ?? new List<KnownPlayer>();
                foreach (KnownPlayer row in rows)
                {
                    if (row.Name.Length > 0)
                    {
                        result._byName[row.Name] = row;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable file must not stop the meter from starting; it just starts
            // out knowing nobody, which is where every install begins anyway.
        }

        return result;
    }

    public KnownPlayer? Find(string name) => _byName.GetValueOrDefault(name);

    /// <summary>Everyone on record, newest confirmation first -- the people from the session that
    /// just ended are the ones most likely to need correcting.</summary>
    public IReadOnlyList<KnownPlayer> All() =>
        _byName.Values.OrderByDescending(p => p.LastSeenUtc).ThenBy(p => p.Name, StringComparer.Ordinal).ToList();

    /// <summary>Forgets a player entirely. For a name that was recorded wrongly, or one that simply
    /// should not be remembered -- deleting is cleaner than leaving a bad entry to be believed.</summary>
    public void Remove(string name)
    {
        if (_byName.Remove(name))
        {
            _dirty = true;
        }
    }

    /// <summary>Drops a hand-set faction and lets the resolver decide again. The way back from a
    /// correction that turned out to be the wrong one.</summary>
    public void ClearManualFaction(string name)
    {
        if (_byName.TryGetValue(name, out var entry) && entry.FactionIsManual)
        {
            entry.FactionIsManual = false;
            entry.Faction = "";
            _dirty = true;
        }
    }

    /// <summary>
    /// Records the user's own answer, which from then on outranks anything derived. Kept apart
    /// from <see cref="Remember"/> so an automatic update cannot quietly overwrite a correction.
    /// </summary>
    public void SetFactionManually(string name, string faction)
    {
        if (name.Length == 0 || name is "You" or "you")
        {
            return;
        }

        if (!_byName.TryGetValue(name, out var entry))
        {
            _byName[name] = entry = new KnownPlayer { Name = name };
        }

        entry.Faction = faction;
        entry.FactionIsManual = true;
        entry.LastSeenUtc = DateTime.UtcNow;
        _dirty = true;
    }

    /// <summary>
    /// Records what is currently known about a player. Empty or placeholder values are ignored
    /// rather than written, so calling this on every grid refresh -- which is what MainWindow does
    /// -- cannot erode the file down to bare names. A faction the user set by hand is never
    /// overwritten here.
    /// </summary>
    public void Remember(string name, string? className, string? faction)
    {
        if (name.Length == 0 || name is "You" or "you")
        {
            return;
        }

        if (!_byName.TryGetValue(name, out var entry))
        {
            _byName[name] = entry = new KnownPlayer { Name = name };
            _dirty = true;
        }

        if (IsReal(className) && entry.ClassName != className)
        {
            entry.ClassName = className!;
            _dirty = true;
        }

        if (IsReal(faction) && entry.Faction != faction && !entry.FactionIsManual)
        {
            entry.Faction = faction!;
            _dirty = true;
        }

        // Only bumped when something was actually learned, so the timestamp means "last confirmed"
        // rather than "last time the meter happened to be open".
        if (_dirty)
        {
            entry.LastSeenUtc = DateTime.UtcNow;
        }
    }

    /// <summary>"?" is what the grid shows for a class it has not worked out yet -- a placeholder,
    /// not an answer, and it must never be written over a real one.</summary>
    private static bool IsReal(string? value) => !string.IsNullOrEmpty(value) && value != "?";

    /// <summary>Writes only when something changed. Called on close rather than on every update:
    /// the grid refreshes about once a second while a fight is running.</summary>
    public void SaveIfChanged()
    {
        if (!_dirty)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path0)!);
            File.WriteAllText(Path0, JsonSerializer.Serialize(
                _byName.Values.OrderBy(p => p.Name, StringComparer.Ordinal).ToList(),
                new JsonSerializerOptions { WriteIndented = true }));
            _dirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the file costs recognition of players, nothing more -- never a reason to
            // interrupt someone mid-raid with a dialog.
        }
    }
}
