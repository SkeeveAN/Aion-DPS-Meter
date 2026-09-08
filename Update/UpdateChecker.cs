using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AionSniffer.Update;

/// <summary>A published release that is newer than the running build.</summary>
public sealed record UpdateInfo(Version Version, string TagName, string ReleasePageUrl, string? MsiUrl);

/// <summary>
/// Asks GitHub whether a newer release exists. This is the only outbound network call the program
/// makes -- everything else it knows comes from a local Chat.log -- and it can be switched off
/// entirely (MeterSettings.CheckForUpdates), because "nothing leaves your machine" is a promise
/// the README makes and a user who wants it kept should not have to take it on trust.
///
/// Deliberately NOT /releases/latest: that endpoint skips prereleases and drafts, and every
/// release of this project so far is marked prerelease, so it answers 404 (verified against the
/// live repo). /releases returns them newest-first instead, and the newest non-draft entry is what
/// "latest" means here.
/// </summary>
public static class UpdateChecker
{
    private const string ReleasesApi = "https://api.github.com/repos/SkeeveAN/Aion-DPS-Meter/releases?per_page=10";
    private const string ReleasesPage = "https://github.com/SkeeveAN/Aion-DPS-Meter/releases";

    /// <summary>
    /// One shared client: a new HttpClient per check exhausts sockets under a repeating timer, and
    /// this one runs every five minutes for as long as the meter is open. GitHub rejects requests
    /// without a User-Agent, so it is set once here rather than per call.
    /// </summary>
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AionDpsMeter", AppVersion.Text));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>
    /// Returns the newest release if it is newer than the running build, null if up to date.
    /// Throws only for genuine failures the caller may want to report (no network, GitHub down,
    /// rate limit) -- the automatic checks swallow those, the manual menu item shows them, since
    /// a background check failing every five minutes is noise while a check the user just asked
    /// for silently doing nothing is a bug they cannot distinguish from "no update".
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(ReleasesApi, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        UpdateInfo? newest = null;
        foreach (JsonElement release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            {
                continue;
            }

            string tag = release.TryGetProperty("tag_name", out var tagName) ? tagName.GetString() ?? "" : "";
            var version = AppVersion.Parse(tag);
            if (version == new Version(0, 0, 0) || (newest is not null && version <= newest.Version))
            {
                continue;
            }

            string page = release.TryGetProperty("html_url", out var html) ? html.GetString() ?? ReleasesPage : ReleasesPage;
            newest = new UpdateInfo(version, tag, page, FindMsiUrl(release));
        }

        return newest is not null && newest.Version > AppVersion.Current ? newest : null;
    }

    private static string? FindMsiUrl(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
                && asset.TryGetProperty("browser_download_url", out var url))
            {
                return url.GetString();
            }
        }

        return null;
    }

    /// <summary>
    /// Downloads the release's MSI into the temp folder and returns its path. Written to a
    /// version-named file so a half-finished download from a previous attempt cannot be mistaken
    /// for a complete one of a different version, and deleted first for the same reason.
    /// </summary>
    public static async Task<string> DownloadMsiAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        if (update.MsiUrl is null)
        {
            throw new InvalidOperationException($"Release {update.TagName} has no .msi asset attached.");
        }

        string path = Path.Combine(Path.GetTempPath(), $"AionDpsMeter-{update.TagName}.msi");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using var response = await Http.GetAsync(update.MsiUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var target = File.Create(path))
        {
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        return path;
    }

    /// <summary>
    /// Hands the downloaded MSI to Windows Installer and returns once it has been started. The
    /// caller is expected to close the meter right after: the installer replaces the very files
    /// this process is running from, and a per-machine MSI will raise its own UAC prompt, which is
    /// why installing is never done behind the user's back.
    /// </summary>
    public static void LaunchInstaller(string msiPath)
    {
        Process.Start(new ProcessStartInfo("msiexec.exe", $"/i \"{msiPath}\"") { UseShellExecute = true });
    }

    /// <summary>Opens the release page in the default browser -- the fallback when a release has
    /// no MSI attached, or when the download failed and the user should just fetch it by hand.</summary>
    public static void OpenReleasePage(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
