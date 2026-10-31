using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AionDPS.Update;

/// <summary>The client changes of one release, one short line each.</summary>
public sealed record ChangelogVersion(string Version, string Date, IReadOnlyList<string> Items);

/// <summary>
/// The list of changes the update window shows: what changed in the client between the installed version and the offered one, asked from aiondps.com
/// (<c>/api/changelog</c>, German or English, one short line per change). A plain GET with the two version numbers and the language, nothing that
/// names the player; it belongs to the permission to update, so the restricted mode asks it as well. Never throws; a failure just means the
/// window comes without the list.
/// </summary>
public static class ChangelogClient
{
    private const string BaseUrl = "https://aiondps.com/api/changelog";
    private static readonly Regex VersionPattern = new(@"^\d{1,4}\.\d{1,4}\.\d{1,4}$", RegexOptions.Compiled);
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(6) }; // no redirects: the host is fixed

    /// <summary>The changes after <paramref name="from"/> up to <paramref name="to"/>, newest release first; empty when there is none or the server cannot be reached.</summary>
    public static async Task<IReadOnlyList<ChangelogVersion>> GetAsync(string from, string to, string language)
    {
        if (!VersionPattern.IsMatch(from) || !VersionPattern.IsMatch(to))
        {
            return Array.Empty<ChangelogVersion>();
        }

        try
        {
            string url = $"{BaseUrl}?from={from}&to={to}&lang={(language == "de" ? "de" : "en")}";
            using HttpResponseMessage response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<ChangelogVersion>();
            }

            return Parse(await response.Content.ReadAsStringAsync());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return Array.Empty<ChangelogVersion>();
        }
    }

    /// <summary>The answer <c>{"versions":[{"version","date","items":[...]}]}</c>; entries that are not as expected are left out.</summary>
    public static IReadOnlyList<ChangelogVersion> Parse(string json)
    {
        var list = new List<ChangelogVersion>();
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("versions", out JsonElement versions) || versions.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (JsonElement entry in versions.EnumerateArray())
        {
            string? version = entry.TryGetProperty("version", out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            string date = entry.TryGetProperty("date", out JsonElement d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : "";
            if (version is null || !VersionPattern.IsMatch(version) || !entry.TryGetProperty("items", out JsonElement items) || items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            List<string> lines = items.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString() ?? "").Where(s => s.Length > 0).Take(60).ToList();
            if (lines.Count > 0)
            {
                list.Add(new ChangelogVersion(version, date, lines));
            }
        }

        return list.Take(30).ToList();
    }
}
