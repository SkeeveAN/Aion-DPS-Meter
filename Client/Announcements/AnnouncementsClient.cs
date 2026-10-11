using System.Net.Http;
using System.Text.Json;

namespace AionDPS.Announcements;

/// <summary>
/// The "New" badge of the announcements icon. The announcements themselves are only shown on the website
/// (<see cref="PageUrl"/>); the meter asks aiondps.com for one number, the posting time of the newest
/// announcement, and compares it with the one it saw when the icon was clicked last. Nothing is sent to the
/// server: a plain GET without any identifier of the player. Never throws; a failure just means no badge.
/// </summary>
public static class AnnouncementsClient
{
    public const string PageUrl = "https://aiondps.com/announcements";
    private const string LatestUrl = "https://aiondps.com/api/announcements/latest";
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) }; // no redirects: the host is fixed

    /// <summary>The posting time (epoch seconds) of the newest announcement, or null (offline, server has none yet).</summary>
    public static async Task<long?> GetLatestAsync()
    {
        try
        {
            using HttpResponseMessage response = await Http.GetAsync(LatestUrl);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("latest", out JsonElement latest) && latest.ValueKind == JsonValueKind.Number && latest.TryGetInt64(out long value) && value > 0
                ? value
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Whether the badge shows: something newer than what was seen at the last click. <paramref name="seen"/> = 0 is a
    /// fresh install, which starts from "everything seen" instead of greeting every new user with a badge (see the caller).</summary>
    public static bool IsNew(long? latest, long seen) => latest is long l && seen > 0 && l > seen;
}
