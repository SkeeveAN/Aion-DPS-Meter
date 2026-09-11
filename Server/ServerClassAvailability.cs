namespace AionSniffer.Server;

/// <summary>
/// Which classes exist on a given private server - per the user, the Class dropdown should not
/// offer one that cannot actually appear on whichever server this install is pointed at, e.g.
/// Origin Aion is 4.6 with the newer classes (Aethertech/Bard/Gunner/Painter) removed entirely,
/// while other 4.6 servers keep some of those and only actually lack Painter (a genuinely
/// post-4.6 class kept in the dropdown only "for parity with the myaion.eu reference" per
/// MainWindow.xaml's own remarks).
///
/// Keyed by <see cref="ServerIdentity"/>'s fingerprint ("IP:PORT") where a real one is known -
/// confirmed for Origin Aion via its own backend's servers table (fingerprint "70.0.0.150:10241",
/// display name "Origin Aion"). Servers whose fingerprint has never been seen fall back to a
/// case-insensitive match on the user's own free-text ServerDisplayName setting instead - lower
/// confidence (a user could type anything there), but the only signal available before a real
/// upload has ever confirmed that server's fingerprint. An unrecognized server (neither lookup
/// matches) shows every class, same as today - never guess a server's own patch/class roster from
/// nothing.
/// </summary>
public static class ServerClassAvailability
{
    // Sourced from web research (nostalgic.gg, originaion.com, EuroAion's own forum), not from
    // this project's own client-string extraction - these are facts about the SERVER'S ruleset,
    // not about Aion's client text, so that method does not apply here.
    private static readonly Dictionary<string, string[]> ExcludedClassesByFingerprint = new(StringComparer.Ordinal)
    {
        // Origin Aion: 4.6 with Aethertech/Bard/Gunner removed entirely (the "original 8 classes"
        // ruleset) - Painter never existed at 4.6 regardless of server, so it is excluded too.
        ["70.0.0.150:10241"] = new[] { "Aethertech", "Bard", "Gunner", "Painter" },
    };

    // Fallback for a server never yet seen by fingerprint - matched against MeterSettings'
    // free-text ServerDisplayName, case-insensitively.
    private static readonly Dictionary<string, string[]> ExcludedClassesByDisplayName = new(StringComparer.OrdinalIgnoreCase)
    {
        // EuroAion: 4.6 keeping Aethertech/Bard(Songweaver)/Gunner, missing only Painter.
        ["euroaion"] = new[] { "Painter" },
    };

    /// <summary>Class names (matching the dropdown's own Tag values) that do not exist on this
    /// server - empty when the server is unrecognized by either fingerprint or display name.</summary>
    public static IReadOnlySet<string> ExcludedClassesFor(string? fingerprint, string? displayName)
    {
        if (fingerprint is not null && ExcludedClassesByFingerprint.TryGetValue(fingerprint, out string[]? byFingerprint))
        {
            return byFingerprint.ToHashSet();
        }

        if (displayName is not null)
        {
            foreach ((string needle, string[] excluded) in ExcludedClassesByDisplayName)
            {
                if (displayName.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    return excluded.ToHashSet();
                }
            }
        }

        return new HashSet<string>();
    }
}
