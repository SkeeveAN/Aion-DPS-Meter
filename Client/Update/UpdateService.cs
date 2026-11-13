using Velopack;
using Velopack.Sources;

namespace AionDPS.Update;

/// <summary>
/// Self-updating, via Velopack. The meter installs per-user under %LOCALAPPDATA%, so it can
/// replace its own files without elevation: an update downloads in the background and is swapped
/// in on the next start, with no installer to run and no UAC prompt. That is the entire reason
/// the WiX/MSI packaging was dropped -- a per-machine install under Program Files is only
/// writable by an elevated process, and this app deliberately stopped asking for that when the
/// packet-capture path was removed.
///
/// <para>
/// Prereleases are included on purpose. Every release of this project so far is marked prerelease,
/// and the default is to skip them -- the same trap that made GitHub's own /releases/latest
/// endpoint answer 404 for this repo. With that flag off, the update check would silently never
/// find anything, which is indistinguishable from being up to date.
/// </para>
/// </summary>
public static class UpdateService
{
    private const string RepositoryUrl = "https://github.com/SkeeveAN/Aion-DPS-Meter";

    private static readonly UpdateManager Manager =
        new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: true));

    /// <summary>
    /// False for a build that Velopack did not install -- a "dotnet run" during development, or a
    /// copy someone unzipped by hand. There is nothing to update in place there, and asking would
    /// only produce errors, so every entry point checks this first.
    /// </summary>
    public static bool CanUpdate => Manager.IsInstalled;

    /// <summary>
    /// The stable channel. A test build (alpha, beta) lives on the test channel, where the stable release would never be offered; it looks at the stable
    /// channel as well, so a tester is led on from the beta to 2.0.0 when it appears (2.0.0 is newer than 2.0.0-beta.N).
    /// </summary>
    private static readonly UpdateManager StableManager =
        new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: true), new UpdateOptions { ExplicitChannel = "win" });

    /// <summary>The manager that found the update last returned: it also downloads and applies it (the two channels have their own files).</summary>
    private static UpdateManager _found = Manager;

    /// <summary>Newest release (of the own channel and, for a test build, of the stable channel) if it is newer than what is running, null if up to date.</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        UpdateInfo? own = await Manager.CheckForUpdatesAsync();
        UpdateInfo? stable = null;
        if (Manager.CurrentVersion?.ToString().Contains('-') == true)
        {
            try
            {
                stable = await StableManager.CheckForUpdatesAsync();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // the stable channel could not be asked: the test channel's answer stands
            }
        }

        if (stable is not null && (own is null || stable.TargetFullRelease.Version > own.TargetFullRelease.Version))
        {
            _found = StableManager;
            return stable;
        }

        _found = Manager;
        return own;
    }

    /// <summary>
    /// Fetches the update into Velopack's staging area. Delta packages mean this is usually a
    /// fraction of the full app, and nothing is swapped until <see cref="ApplyAndRestart"/> runs,
    /// so an interrupted download leaves the installed version untouched.
    /// </summary>
    public static Task DownloadAsync(UpdateInfo update, Action<int>? progress = null) =>
        _found.DownloadUpdatesAsync(update, progress);

    /// <summary>
    /// Swaps in the downloaded version and relaunches. This exits the process, so callers must
    /// treat it as the last thing they do -- anything that still needs saving has to be saved
    /// before the call, not after it.
    /// </summary>
    public static void ApplyAndRestart(UpdateInfo update) => _found.ApplyUpdatesAndRestart(update);
}
