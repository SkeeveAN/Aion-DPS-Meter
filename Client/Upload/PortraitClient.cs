using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AionDPS.Upload;

/// <summary>
/// Fetches the player's portrait (the cut-out character picture the website shows on the profile) for the
/// character window. It talks to the server only when the caller says the profile upload is on (the player
/// can only be found there then), uses the same base URL as <see cref="UploadClient"/>, and never throws:
/// any failure just means no portrait. The picture is cached in the data folder for seven days, and the
/// server is asked at most once per character and session.
/// </summary>
public static class PortraitClient
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan CacheLife = TimeSpan.FromDays(7);
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) }; // no redirects: the host is fixed
    private static readonly HashSet<string> Asked = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    private static string CacheDirectory => Path.Combine(
        Environment.GetEnvironmentVariable("AIONDPS_DATA_DIR") is { Length: > 0 } testDir
            ? testDir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aion DPS Meter"),
        "portraits");

    /// <summary>The path of a PNG with the character's portrait, or null (not on the server, no portrait yet,
    /// offline, already asked this session). <paramref name="serverFingerprint"/> is the "aion2:..." key the
    /// uploads are filed under; only a player of that server counts as a match.</summary>
    public static async Task<string?> GetAsync(string name, string serverFingerprint)
    {
        try
        {
            string key = serverFingerprint + "/" + name.ToLowerInvariant();
            string file = Path.Combine(CacheDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24] + ".png");
            bool fresh = File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < CacheLife;
            if (fresh)
            {
                return file;
            }

            lock (Gate)
            {
                if (!Asked.Add(key))
                {
                    return File.Exists(file) ? file : null;
                }
            }

            string? url = await FindPortraitUrlAsync(name, serverFingerprint);
            if (url is not null && await DownloadAsync(url, file))
            {
                return file;
            }

            return File.Exists(file) ? file : null; // an outdated copy is better than none
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static async Task<string?> FindPortraitUrlAsync(string name, string serverFingerprint)
    {
        using JsonDocument search = JsonDocument.Parse(await Http.GetStringAsync($"{UploadClient.ApiBaseUrl}/api/players/search?q={Uri.EscapeDataString(name)}"));
        if (search.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        int? id = null;
        foreach (JsonElement row in search.RootElement.EnumerateArray())
        {
            if (row.TryGetProperty("serverFingerprint", out JsonElement fp) && fp.GetString() == serverFingerprint
                && row.TryGetProperty("name", out JsonElement n) && string.Equals(n.GetString(), name, StringComparison.OrdinalIgnoreCase)
                && row.TryGetProperty("id", out JsonElement i) && i.TryGetInt32(out int value))
            {
                id = value;
                break;
            }
        }

        if (id is null)
        {
            return null;
        }

        using JsonDocument detail = JsonDocument.Parse(await Http.GetStringAsync($"{UploadClient.ApiBaseUrl}/api/players/{id}"));
        return detail.RootElement.TryGetProperty("player", out JsonElement player)
            && player.TryGetProperty("portraitUrl", out JsonElement url) && url.ValueKind == JsonValueKind.String
            ? url.GetString() : null;
    }

    private static async Task<bool> DownloadAsync(string portraitUrl, string file)
    {
        // Only the server we upload to, only over https: the URL comes from a response, so it is checked, not trusted.
        var baseUri = new Uri(UploadClient.ApiBaseUrl);
        if (!Uri.TryCreate(baseUri, portraitUrl, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != baseUri.Host)
        {
            return false;
        }

        using HttpResponseMessage response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes)
        {
            return false;
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(chunk)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBytes)
            {
                return false;
            }
        }

        byte[] data = buffer.ToArray();
        byte[] png = { 0x89, 0x50, 0x4E, 0x47 };
        if (data.Length < 8 || !data.AsSpan(0, 4).SequenceEqual(png))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temp = file + ".tmp";
        await File.WriteAllBytesAsync(temp, data);
        File.Move(temp, file, true);
        return true;
    }
}
