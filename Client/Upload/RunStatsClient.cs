using System.Net.Http;
using System.Text.Json;

namespace AionDPS.Upload;

/// <summary>One fight of a character as aiondps.com holds it (every upload of the community counts).</summary>
public sealed record SiteRun(string Boss, string Instance, string Category, string Mode, DateTime At, double Idps, long Damage);

/// <summary>
/// The runs of a character as the website knows them: all uploads, not only what this computer saw. Two plain GETs (the search by the exact name, then the player's page data)
/// to the fixed host, answered from a cache for ten minutes. Never throws.
/// </summary>
public static class RunStatsClient
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly Dictionary<string, (DateTime At, List<SiteRun>? Runs)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Asking = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the lookup may be made: what other players uploaded about the own characters can always be read (a plain GET by the exact name); only the restricted mode (the privacy question declined) sends nothing at all.</summary>
    public static bool Allowed() => !Ui.PrivacyConsent.Restricted;

    /// <summary>The runs from the cache (null: not asked yet or nothing there); starts the lookup when it is old, <paramref name="done"/> is called when new data came.</summary>
    public static List<SiteRun>? Get(string name, string server, Action? done)
    {
        if (!Allowed() || name.Length == 0)
        {
            return null;
        }

        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var known) && DateTime.UtcNow - known.At < TimeSpan.FromMinutes(10))
            {
                return known.Runs;
            }

            if (!Asking.Add(name))
            {
                return Cache.TryGetValue(name, out var old) ? old.Runs : null;
            }
        }

        _ = Task.Run(async () =>
        {
            List<SiteRun>? runs = null;
            try
            {
                runs = await Fetch(name, server);
            }
            finally
            {
                lock (Cache)
                {
                    Cache[name] = (DateTime.UtcNow, runs);
                    Asking.Remove(name);
                }

                done?.Invoke();
            }
        });
        lock (Cache)
        {
            return Cache.TryGetValue(name, out var stale) ? stale.Runs : null;
        }
    }

    internal static async Task<List<SiteRun>?> Fetch(string name, string server)
    {
        try
        {
            using JsonDocument found = JsonDocument.Parse(await Http.GetStringAsync($"{UploadClient.ApiBaseUrl}/api/players/search?q={Uri.EscapeDataString(name)}"));
            JsonElement pick = default;
            foreach (JsonElement row in found.RootElement.EnumerateArray())
            {
                string? serverName = row.TryGetProperty("serverName", out JsonElement sn) ? sn.GetString() : null;
                if (pick.ValueKind == JsonValueKind.Undefined || (server.Length > 0 && string.Equals(serverName, server, StringComparison.OrdinalIgnoreCase)))
                {
                    pick = row.Clone();
                }
            }

            if (pick.ValueKind == JsonValueKind.Undefined)
            {
                return null;
            }

            int id = pick.GetProperty("id").GetInt32();
            using JsonDocument page = JsonDocument.Parse(await Http.GetStringAsync($"{UploadClient.ApiBaseUrl}/api/players/{id}"));
            var runs = new List<SiteRun>();
            if (page.RootElement.TryGetProperty("history", out JsonElement history))
            {
                foreach (JsonElement h in history.EnumerateArray())
                {
                    if (!DateTime.TryParse(h.GetProperty("startedAt").GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime at))
                    {
                        continue;
                    }

                    runs.Add(new SiteRun(
                        h.GetProperty("bossName").GetString() ?? "", h.GetProperty("instanceName").GetString() ?? "",
                        h.TryGetProperty("instanceCategory", out JsonElement cat) ? cat.GetString() ?? "" : "", h.TryGetProperty("mode", out JsonElement mode) ? mode.GetString() ?? "" : "",
                        at.ToLocalTime(), h.TryGetProperty("idps", out JsonElement i) && i.ValueKind == JsonValueKind.Number ? i.GetDouble() : 0,
                        h.TryGetProperty("totalDamage", out JsonElement d) && d.ValueKind == JsonValueKind.Number ? d.GetInt64() : 0));
                }
            }

            return runs;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
