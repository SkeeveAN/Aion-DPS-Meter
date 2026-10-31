using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AionDPS.Update;

/// <summary>The outcome of one run of <see cref="DataUpdateClient.RefreshAsync"/>.</summary>
public sealed record DataUpdateResult(bool Reached, int Taken, int Removed, int Failed);

/// <summary>
/// Takes the data the server has newer than the client's own set (see <see cref="AssetStore"/>): asks aiondps.com for the list of its data files (path, SHA-256, size,
/// layout number), leaves every file that equals the shipped one or the one already taken, loads the others, checks size and hash (and that a .json reads as JSON) and
/// puts them in the data folder through a temporary name. A file the list no longer names is dropped from the folder, so the shipped one counts again. A plain GET
/// without any identifier of the player; it belongs to the update function and is made whenever the update check is on, also when the upload consent was declined.
/// Never throws; a failure just leaves the data as it was.
/// </summary>
public static class DataUpdateClient
{
    private const string BaseUrl = "https://aiondps.com/api/data";
    private const long MaxFileBytes = 20L * 1024 * 1024;
    private const int MaxFiles = 6000;
    private static readonly Regex AllowedPath = new(@"^(aion2|i18n)/[A-Za-z0-9_][A-Za-z0-9_.\-/]*\.(json|png|jpg|wav)$", RegexOptions.Compiled);
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(40) }; // no redirects: the host is fixed
    private static int _running;

    public static bool IsAllowed(string relative) => AllowedPath.IsMatch(relative) && !relative.Split('/').Any(p => p is "." or "..");

    /// <summary>One run: list, compare, take what differs. Only one at a time.</summary>
    public static async Task<DataUpdateResult> RefreshAsync(string baseUrl = BaseUrl)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return new DataUpdateResult(false, 0, 0, 0);
        }

        try
        {
            return await RefreshCoreAsync(baseUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new DataUpdateResult(false, 0, 0, 0);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private static async Task<DataUpdateResult> RefreshCoreAsync(string baseUrl)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/manifest");
        string etag = AssetStore.ManifestEtag;
        if (etag.Length > 0)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        using HttpResponseMessage response = await Http.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
        {
            return new DataUpdateResult(true, 0, 0, 0);
        }

        if (!response.IsSuccessStatusCode)
        {
            return new DataUpdateResult(false, 0, 0, 0);
        }

        string newEtag = response.Headers.ETag?.Tag ?? "";
        IReadOnlyList<ManifestFile> files = ParseManifest(await response.Content.ReadAsStringAsync());
        if (files.Count == 0)
        {
            return new DataUpdateResult(false, 0, 0, 0); // not a list: nothing is changed
        }

        // the files the shipped set does not have or has differently, json first (a map's tables before its pictures)
        var wanted = new List<ManifestFile>();
        foreach (ManifestFile file in files)
        {
            if (file.Format > AssetStore.SupportedFormat(file.Path))
            {
                continue; // a layout this client cannot read
            }

            AssetStore.Entry? have = AssetStore.EntryOf(file.Path);
            if (have is not null && have.Sha256 == file.Sha256 && File.Exists(AssetStore.OverlayPath(file.Path)) && new FileInfo(AssetStore.OverlayPath(file.Path)).Length == file.Size)
            {
                continue;
            }

            if (AssetStore.ShippedSha256(file.Path) == file.Sha256)
            {
                continue;
            }

            wanted.Add(file);
        }

        int taken = 0, failed = 0;
        using var gate = new SemaphoreSlim(3);
        await Task.WhenAll(wanted.OrderBy(f => f.Path.EndsWith(".json", StringComparison.Ordinal) ? 0 : 1).ThenBy(f => f.Path, StringComparer.Ordinal).Select(async file =>
        {
            await gate.WaitAsync();
            try
            {
                if (await TakeAsync(baseUrl, file))
                {
                    Interlocked.Increment(ref taken);
                }
                else
                {
                    Interlocked.Increment(ref failed);
                }
            }
            finally
            {
                gate.Release();
            }
        }));

        // what was taken once and is the shipped file now (a new release) or no longer in the list: the folder gives it up
        int removed = 0;
        var listed = files.ToDictionary(f => f.Path, f => f);
        foreach (string path in AssetStore.IndexedPaths())
        {
            if (!listed.TryGetValue(path, out ManifestFile? file) || (AssetStore.ShippedSha256(path) is { } shipped && shipped == file.Sha256))
            {
                AssetStore.RemoveEntry(path);
                TryDelete(AssetStore.OverlayPath(path));
                removed++;
            }
        }

        // the list counts as taken in only when nothing is missing, otherwise the next run asks again
        AssetStore.ManifestEtag = failed == 0 ? newEtag : "";
        AssetStore.Save();
        return new DataUpdateResult(true, taken, removed, failed);
    }

    private static async Task<bool> TakeAsync(string baseUrl, ManifestFile file)
    {
        string target = AssetStore.OverlayPath(file.Path);
        string temp = target + ".part";
        try
        {
            using HttpResponseMessage response = await Http.GetAsync($"{baseUrl}/file?path={Uri.EscapeDataString(file.Path)}", HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length != file.Size || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (file.Path.EndsWith(".json", StringComparison.Ordinal))
            {
                using JsonDocument _ = JsonDocument.Parse(bytes); // a table that does not read is never taken in
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(temp, bytes);
            File.Move(temp, target, overwrite: true);
            AssetStore.SetEntry(file.Path, new AssetStore.Entry(file.Sha256.ToLowerInvariant(), file.Size, file.Format));
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // stays behind: harmless, the index does not list it
        }
    }

    internal sealed record ManifestFile(string Path, string Sha256, long Size, int Format);

    /// <summary>The list <c>{"version","files":[{"path","sha256","size","format"}]}</c>; an entry that is not as expected (a path outside the data, a size above the limit) is left out.</summary>
    internal static IReadOnlyList<ManifestFile> ParseManifest(string json)
    {
        var list = new List<ManifestFile>();
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("files", out JsonElement files) || files.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (JsonElement f in files.EnumerateArray())
        {
            string? path = f.TryGetProperty("path", out JsonElement p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            string? sha = f.TryGetProperty("sha256", out JsonElement s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            long size = f.TryGetProperty("size", out JsonElement z) && z.TryGetInt64(out long zz) ? zz : -1;
            int format = f.TryGetProperty("format", out JsonElement m) && m.TryGetInt32(out int mm) ? mm : 1;
            if (path is null || sha is null || !IsAllowed(path) || !Regex.IsMatch(sha, "^[0-9a-fA-F]{64}$") || size <= 0 || size > MaxFileBytes)
            {
                continue;
            }

            list.Add(new ManifestFile(path, sha.ToLowerInvariant(), size, format));
            if (list.Count >= MaxFiles)
            {
                break;
            }
        }

        return list;
    }
}
