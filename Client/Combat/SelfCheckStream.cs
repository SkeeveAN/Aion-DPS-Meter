using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using AionDPS.Streaming;

namespace AionDPS.Combat;

/// <summary>The stream overlay's server on this PC: what it serves, what it refuses, and the live state reaching a browser source.</summary>
public static class SelfCheckStream
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Stream overlay server (OBS browser source):");
        bool ok = true;

        // the state
        var rows = new List<StreamRowData>
        {
            new(1, "Alice", "Cleric", 1.0, true, new[] { "8.3K", "916K", "40%" }),
            new(2, "Bob", "Cleric", 0.5, false, new[] { "4.1K", "458K", "20%" }),
            new(3, "Carol", "Gladiator", 0.4, false, new[] { "3.3K", "366K", "16%" }, "↓ 12K  + 3K"),
        };
        var hidden = StreamSnapshot.HideNames(rows);
        bool names = hidden[0].Name == "Cleric" && hidden[1].Name == "Cleric 2" && hidden[2].Name == "Gladiator";
        using JsonDocument state = JsonDocument.Parse(StreamSnapshot.ToJson("DMG", new[] { "DPS", "DMG", "%" }, hidden, new StreamBoss("Boss", 61.2, "12K / 20K"), 2));
        bool json = state.RootElement.GetProperty("rows").GetArrayLength() == 2 && state.RootElement.GetProperty("rows")[0].GetProperty("self").GetBoolean()
            && state.RootElement.GetProperty("boss").GetProperty("percent").GetDouble() > 61 && state.RootElement.GetProperty("modeLabel").GetString() == "DMG";
        Console.WriteLine($"  -> without names everyone is shown by class ({names}); the state is the JSON the page reads, cut to the rows asked for ({json})");
        ok &= names && json;

        bool peers = StreamOverlayServer.IsLocalPeer(System.Net.IPAddress.Parse("192.168.1.20")) && StreamOverlayServer.IsLocalPeer(System.Net.IPAddress.Parse("10.0.0.5"))
            && StreamOverlayServer.IsLocalPeer(System.Net.IPAddress.Parse("172.20.1.1")) && StreamOverlayServer.IsLocalPeer(System.Net.IPAddress.Loopback)
            && !StreamOverlayServer.IsLocalPeer(System.Net.IPAddress.Parse("8.8.8.8")) && !StreamOverlayServer.IsLocalPeer(System.Net.IPAddress.Parse("172.32.0.1"))
            && !StreamOverlayServer.IsLocalPeer(System.Net.IPAddress.Parse("2001:db8::1"));
        Console.WriteLine($"  -> only this PC and the own network (private ranges) are answered, no address from the internet: {peers}");
        ok &= peers;

        string key = StreamOverlayServer.NewKey();
        using var server = new StreamOverlayServer();
        int port = 30000 + Random.Shared.Next(2000);
        if (!server.Start(port, key))
        {
            Console.WriteLine($"  -> the server could not start on any of ten ports ({server.LastError}): False");
            return false;
        }

        string basePath = server.Url!;
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            bool keyShape = key.Length == 32 && key.All(Uri.IsHexDigit) && NewKeysDiffer();
            HttpResponseMessage page = http.GetAsync(basePath).GetAwaiter().GetResult();
            string html = page.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            bool served = page.StatusCode == HttpStatusCode.OK && html.Contains("overlay.js", StringComparison.Ordinal)
                && page.Headers.TryGetValues("Content-Security-Policy", out var csp) && csp.First().Contains("default-src 'none'", StringComparison.Ordinal)
                && page.Headers.CacheControl?.NoStore == true;
            bool script = http.GetAsync(basePath + "overlay.js").GetAwaiter().GetResult().StatusCode == HttpStatusCode.OK
                && http.GetAsync(basePath + "overlay.css").GetAwaiter().GetResult().StatusCode == HttpStatusCode.OK;
            Console.WriteLine($"  -> a random 32-character key ({keyShape}); the page, its script and style are served with a strict policy ({served && script})");
            ok &= keyShape && served && script;

            HttpStatusCode Code(string url, HttpMethod? method = null, string? host = null)
            {
                var request = new HttpRequestMessage(method ?? HttpMethod.Get, url);
                if (host is not null)
                {
                    request.Headers.Host = host;
                }

                return http.SendAsync(request).GetAwaiter().GetResult().StatusCode;
            }

            string root = $"http://127.0.0.1:{server.Port}/";
            bool refused = Code(root) == HttpStatusCode.NotFound && Code(root + "0000/") == HttpStatusCode.NotFound
                && Code(basePath, host: "evil.example:" + server.Port) == HttpStatusCode.Forbidden && Code(basePath, HttpMethod.Post) == HttpStatusCode.MethodNotAllowed
                && Code(basePath + "nothing.txt") == HttpStatusCode.NotFound;
            bool redirect = Code(root + key) == HttpStatusCode.Found;
            Console.WriteLine($"  -> no page without the key, none for a foreign host name, none for POST, nothing else ({refused}); the link without a slash is sent on ({redirect})");
            ok &= refused && redirect;

            HttpResponseMessage icon = http.GetAsync(basePath + "c/Cleric.png").GetAwaiter().GetResult();
            bool badIcon = http.GetAsync(basePath + "c/..%2F..%2Fsecret.png").GetAwaiter().GetResult().StatusCode == HttpStatusCode.NotFound;
            bool icons = icon.StatusCode == HttpStatusCode.OK && icon.Content.Headers.ContentType?.MediaType == "image/png" && badIcon;
            Console.WriteLine($"  -> a class icon is served, a path out of the folder is not: {icons}");
            ok &= icons;

            // a browser source: the event stream gets the newest state at once and every later one
            server.Publish("{\"first\":1}");
            using HttpResponseMessage stream = http.GetAsync(basePath + "events", HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            using var reader = new StreamReader(stream.Content.ReadAsStreamAsync().GetAwaiter().GetResult());
            string? Line() => reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            var received = new List<string>();
            while (received.Count < 4 && Line() is string line)
            {
                if (line.Length > 0)
                {
                    received.Add(line);
                }

                if (received.Any(l => l.StartsWith("data: ", StringComparison.Ordinal)))
                {
                    break;
                }
            }

            bool firstOk = received.Contains("retry: 2000") && received.Contains("data: {\"first\":1}") && stream.Content.Headers.ContentType?.MediaType == "text/event-stream";
            server.Publish("{\"second\":2}");
            string? next = null;
            for (int i = 0; i < 4 && next is null; i++)
            {
                string? line = Line();
                if (line is not null && line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    next = line;
                }
            }

            bool live = firstOk && next == "data: {\"second\":2}" && server.ClientCount == 1;
            Console.WriteLine($"  -> the event stream sends the newest state at once and every later one, to one connected browser source: {live}");
            ok &= live;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or IOException)
        {
            Console.WriteLine($"  -> the server did not answer: {ex.GetType().Name} {ex.Message}");
            ok = false;
        }

        server.Stop();
        bool stopped = !server.Running;
        Console.WriteLine($"  -> after Stop nobody listens: {stopped}");
        return ok && stopped;
    }

    private static bool NewKeysDiffer() => StreamOverlayServer.NewKey() != StreamOverlayServer.NewKey();
}
