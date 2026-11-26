using System.Net.Http;
using System.Text.Json.Nodes;

namespace Aion2G13Plugin;

/// <summary>Reads the live stream the DPS tool offers for OBS and reports the class of the own character (the row marked "self").</summary>
public sealed class Watcher
{
    private CancellationTokenSource? _stop;

    public event Action<string>? ClassSeen;
    public event Action<string>? StatusChanged;

    public void Start(string link)
    {
        Stop();
        string events = EventsUrl(link);
        if (events.Length == 0)
        {
            StatusChanged?.Invoke("Bitte den Stream-Link aus dem DPS-Tool eintragen.");
            return;
        }

        _stop = new CancellationTokenSource();
        _ = Task.Run(() => Run(events, _stop.Token));
    }

    public void Stop()
    {
        _stop?.Cancel();
        _stop = null;
    }

    public static string EventsUrl(string link)
    {
        link = link.Trim();
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp)
        {
            return "";
        }

        string path = uri.AbsolutePath.TrimEnd('/');
        if (!path.EndsWith("/events", StringComparison.Ordinal))
        {
            path += "/events";
        }

        return new UriBuilder(uri) { Path = path, Query = "" }.Uri.ToString();
    }

    private async Task Run(string url, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                StatusChanged?.Invoke("Verbunden. Warte auf den eigenen Charakter (erscheint nach dem ersten Kampf).");
                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) is not null)
                {
                    if (line.StartsWith("data:", StringComparison.Ordinal) && FindSelf(line[5..]) is { } cls)
                    {
                        ClassSeen?.Invoke(cls);
                    }
                }

                StatusChanged?.Invoke("Verbindung beendet, versuche es erneut ...");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                StatusChanged?.Invoke("Keine Verbindung zum DPS-Tool (läuft es, ist der Stream-Link an?). " + ex.Message);
            }

            try
            {
                await Task.Delay(3000, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public static string? FindSelf(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is JsonObject { } root && root["rows"] is JsonArray rows)
            {
                foreach (var row in rows)
                {
                    if (row?["self"]?.GetValue<bool>() == true && row["cls"]?.GetValue<string>() is { Length: > 0 } cls)
                    {
                        return cls;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
        }

        return null;
    }
}
