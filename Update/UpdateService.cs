using Velopack;
using Velopack.Sources;

namespace AionSniffer.Update;

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

    /// <summary>Newest release if it is newer than what is running, null if up to date.</summary>
    public static Task<UpdateInfo?> CheckAsync() => Manager.CheckForUpdatesAsync();

    /// <summary>
    /// Fetches the update into Velopack's staging area. Delta packages mean this is usually a
    /// fraction of the full app, and nothing is swapped until <see cref="ApplyAndRestart"/> runs,
    /// so an interrupted download leaves the installed version untouched.
    /// </summary>
    public static Task DownloadAsync(UpdateInfo update, Action<int>? progress = null) =>
        Manager.DownloadUpdatesAsync(update, progress);

    /// <summary>
    /// Swaps in the downloaded version and relaunches. This exits the process, so callers must
    /// treat it as the last thing they do -- anything that still needs saving has to be saved
    /// before the call, not after it.
    /// </summary>
    public static void ApplyAndRestart(UpdateInfo update) => Manager.ApplyUpdatesAndRestart(update);
}
