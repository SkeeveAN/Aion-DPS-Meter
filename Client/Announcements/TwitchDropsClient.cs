using System.Net.Http;
using System.Text.Json;

namespace AionDPS.Announcements;

/// <summary>
/// The Twitch icon of the main window. The campaigns themselves are only shown on the website (<see cref="PageUrl"/>);
/// the meter asks aiondps.com for two numbers (is a campaign running, when did the newest one start) and compares the
/// start with the one it saw when the icon was clicked last. Nothing is sent to the server: a plain GET without any
/// identifier of the player. Never throws; a failure just means no icon.
/// </summary>
public static class TwitchDropsClient
{
    public const string PageUrl = "https://aiondps.com/twitch-drops";
    private const string StatusUrl = "https://aiondps.com/api/twitch-drops/latest";
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) }; // no redirects: the host is fixed

    /// <summary>Whether a campaign runs now and the start (epoch seconds) of the newest started one; null when the server cannot be reached.</summary>
    public static async Task<(bool Running, long Latest)?> GetStatusAsync()
    {
        try
        {
            using HttpResponseMessage response = await Http.GetAsync(StatusUrl);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement root = doc.RootElement;
            bool running = root.TryGetProperty("running", out JsonElement r) && r.ValueKind == JsonValueKind.True;
            long latest = root.TryGetProperty("latest", out JsonElement l) && l.ValueKind == JsonValueKind.Number && l.TryGetInt64(out long value) ? value : 0;
            return (running, latest);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The icon shows while a campaign runs; "New" on top while its start is newer than what was seen at the last click
    /// (<paramref name="seen"/> = 0, a fresh install, counts as new).</summary>
    public static bool IsNew(bool running, long latest, long seen) => running && latest > seen;
}
