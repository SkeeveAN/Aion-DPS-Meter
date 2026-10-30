using System.IO;
using System.Net.Http;

namespace AionDPS.Schedule;

/// <summary>
/// The event timetable comes from aiondps.com (<see cref="Url"/>), so a changed time only has to be changed there and the meter needs no new release. The
/// meter takes the table at the start and again now and then, keeps the last good answer in its data folder and falls back to the table it ships with
/// (assets/aion2/schedule/events.json) when there is none. Nothing is sent to the server: a plain GET without any identifier of the player. Never throws;
/// a failure just keeps the table in use.
/// </summary>
public static class ScheduleClient
{
    private const string Url = "https://aiondps.com/api/schedule";
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) }; // no redirects: the host is fixed

    public static string CachePath => Path.Combine(
        Environment.GetEnvironmentVariable("AIONDPS_DATA_DIR") is { Length: > 0 } testDir
            ? testDir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aion DPS Meter"),
        "schedule-cache.json");

    /// <summary>Asks the server for the table and takes it in use when it reads as a whole one. True when the table in use is now the server's.
    /// <paramref name="region"/> (the region code of the player's own server, see <see cref="Aion2.Protocol.Aion2Servers.RegionCodeOf"/>) lets the server give the clock of
    /// that region; without one the table is the European one. The region is a plain word of the player's own server, no identifier.</summary>
    public static async Task<bool> RefreshAsync(string? region = null)
    {
        try
        {
            using HttpResponseMessage response = await Http.GetAsync(region is null ? Url : $"{Url}?region={Uri.EscapeDataString(region)}");
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            string json = await response.Content.ReadAsStringAsync();
            IReadOnlyList<ScheduledEvent> events = EventSchedule.Parse(json);
            if (events.Count == 0)
            {
                return false; // not a timetable: keep the one in use
            }

            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            await File.WriteAllTextAsync(CachePath, json);
            EventSchedule.Replace(events);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
