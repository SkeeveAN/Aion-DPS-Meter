namespace AionDPS.Server;

/// <summary>
/// Which classes exist on a given private server - per the user, the Class dropdown should not
/// offer one that cannot actually appear on whichever server this install is pointed at, e.g.
/// Origin Aion is 4.6 with the newer classes (Aethertech/Bard/Gunner/Painter) removed entirely,
/// while other 4.6 servers keep some of those and only actually lack Painter (a genuinely
/// post-4.6 class kept in the dropdown only "for parity with the myaion.eu reference" per
/// MainWindow.xaml's own remarks).
///
/// Keyed by <see cref="ServerIdentity"/>'s fingerprint ("IP:PORT") for Origin Aion
/// ("70.0.0.150:10241") and by a case-insensitive match on the user's own free-text
/// ServerDisplayName setting for everything else - checked displayName-FIRST despite fingerprint
/// being the "harder" identifier, because a real report proved <see cref="ServerIdentity"/>'s own
/// claim that it is "stable and unique per private-server operator" false: Aion Riftshade shares
/// Origin Aion's exact fingerprint (same gateway), so a fingerprint-first lookup wrongly applied
/// Origin Aion's exclusions to Riftshade too (see ExcludedClassesFor's own remarks, and
/// MainWindow.xaml.cs's ApplyActiveCharacterForCurrentServer for the same fix applied earlier to a
/// different feature). An unrecognized server (neither lookup matches) shows every class, same as
/// today - never guess a server's own patch/class roster from nothing.
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
        // Aion Riftshade: 4.8, confirmed per the user to keep Aethertech/Bard/Gunner too - unlike
        // Origin Aion's "original 8 classes" 4.6 ruleset above. Painter's status here was not part
        // of that confirmation, so it stays shown rather than guessed at, same as any other
        // not-yet-confirmed class.
        ["riftshade"] = Array.Empty<string>(),
    };

    /// <summary>Class names (matching the dropdown's own Tag values) that do not exist on this
    /// server - empty when the server is unrecognized by either fingerprint or display name.</summary>
    public static IReadOnlySet<string> ExcludedClassesFor(string? fingerprint, string? displayName)
    {
        // displayName checked FIRST, fingerprint only as a fallback - reversed from this method's
        // original order after a real report ("im Dropdown fehlen Aethertech/Gunner/Bard" on
        // Riftshade) traced back to the exact same fingerprint collision already fixed for the
        // backend's own `servers` table and for MainWindow's ApplyActiveCharacterForCurrentServer:
        // Aion Riftshade and Origin Aion share the identical "70.0.0.150:10241" fingerprint (same
        // gateway), so the fingerprint-first lookup below always matched Origin Aion's entry first
        // and wrongly excluded Riftshade's own Aethertech/Bard/Gunner too. displayName is the
        // explicit catalog pick the user actually made (see SettingsWindow's AionInstallServerBox)
        // and, per that same earlier fix, the more trustworthy signal of the two.
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

        if (fingerprint is not null && ExcludedClassesByFingerprint.TryGetValue(fingerprint, out string[]? byFingerprint))
        {
            return byFingerprint.ToHashSet();
        }

        return new HashSet<string>();
    }
}
