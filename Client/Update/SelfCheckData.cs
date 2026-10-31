using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace AionDPS.Update;

/// <summary>The data the server delivers (DataUpdateClient, AssetStore): a test server with a list, a changed table, a new file, a damaged file and a layout the client does not read.</summary>
public static class SelfCheckData
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Data from the server (shipped set first, taken files win):");
        string dir = Path.Combine(Path.GetTempPath(), "aiondps-selfcheck-" + Guid.NewGuid().ToString("N"));
        string? before = Environment.GetEnvironmentVariable("AIONDPS_DATA_DIR");
        HttpListener? listener = null;
        try
        {
            Directory.CreateDirectory(dir);
            Environment.SetEnvironmentVariable("AIONDPS_DATA_DIR", dir);
            AssetStore.Reset();

            // the shipped regions.json and what the test server offers: the same file (must stay), a changed table, a new file, a damaged file, a layout of the future, a path out of the data
            string regionsShipped = File.ReadAllText(AssetStore.Path("aion2", "pets", "regions.json"));
            byte[] changed = Encoding.UTF8.GetBytes("{\"maps\":{\"test\":{\"title\":\"Test\"}}}");
            byte[] fresh = Encoding.UTF8.GetBytes("{\"new\":true}");
            byte[] damaged = Encoding.UTF8.GetBytes("{\"x\":1}");
            byte[] future = Encoding.UTF8.GetBytes("{\"layout\":2}");
            string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
            string shippedSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(AssetStore.Path("aion2", "pets", "regions.json")))).ToLowerInvariant();
            var served = new Dictionary<string, byte[]>
            {
                ["aion2/maps/maps.json"] = changed,
                ["aion2/maps/brand_new.json"] = fresh,
                ["aion2/pets/damaged.json"] = Encoding.UTF8.GetBytes("{\"x\":2}"), // served other bytes than listed
                ["aion2/pets/future.json"] = future,
            };
            string manifest = "{\"version\":\"t\",\"files\":[" + string.Join(",", new[]
            {
                $"{{\"path\":\"aion2/pets/regions.json\",\"sha256\":\"{shippedSha}\",\"size\":{new FileInfo(AssetStore.Path("aion2", "pets", "regions.json")).Length},\"format\":1}}",
                $"{{\"path\":\"aion2/maps/maps.json\",\"sha256\":\"{Sha(changed)}\",\"size\":{changed.Length},\"format\":1}}",
                $"{{\"path\":\"aion2/maps/brand_new.json\",\"sha256\":\"{Sha(fresh)}\",\"size\":{fresh.Length},\"format\":1}}",
                $"{{\"path\":\"aion2/pets/damaged.json\",\"sha256\":\"{Sha(damaged)}\",\"size\":{damaged.Length},\"format\":1}}",
                $"{{\"path\":\"aion2/pets/future.json\",\"sha256\":\"{Sha(future)}\",\"size\":{future.Length},\"format\":2}}",
                $"{{\"path\":\"aion2/../app/aiondps.ico\",\"sha256\":\"{Sha(fresh)}\",\"size\":{fresh.Length},\"format\":1}}",
                $"{{\"path\":\"feedback/public.pem\",\"sha256\":\"{Sha(fresh)}\",\"size\":{fresh.Length},\"format\":1}}",
            }) + "]}";

            int port = Random.Shared.Next(20000, 40000);
            listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/api/data/");
            listener.Start();
            _ = Task.Run(async () =>
            {
                while (listener.IsListening)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await listener.GetContextAsync();
                    }
                    catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
                    {
                        return;
                    }

                    byte[] body = Array.Empty<byte>();
                    if (context.Request.Url!.AbsolutePath.EndsWith("/manifest", StringComparison.Ordinal))
                    {
                        context.Response.AddHeader("ETag", "\"t\"");
                        body = Encoding.UTF8.GetBytes(manifest);
                    }
                    else if (served.TryGetValue(context.Request.QueryString["path"] ?? "", out byte[]? file))
                    {
                        body = file;
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                    }

                    context.Response.OutputStream.Write(body, 0, body.Length);
                    context.Response.Close();
                }
            });

            DataUpdateResult result = DataUpdateClient.RefreshAsync($"http://127.0.0.1:{port}/api/data").GetAwaiter().GetResult();
            string maps = AssetStore.Path("aion2", "maps", "maps.json");
            bool takenChanged = maps.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && File.ReadAllText(maps) == Encoding.UTF8.GetString(changed);
            bool takenNew = AssetStore.Path("aion2", "maps", "brand_new.json").StartsWith(dir, StringComparison.OrdinalIgnoreCase);
            bool sameStays = AssetStore.Path("aion2", "pets", "regions.json").StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase) && File.ReadAllText(AssetStore.Path("aion2", "pets", "regions.json")) == regionsShipped;
            bool damagedLeft = !AssetStore.Path("aion2", "pets", "damaged.json").StartsWith(dir, StringComparison.OrdinalIgnoreCase) && !File.Exists(Path.Combine(dir, "assets", "aion2", "pets", "damaged.json"));
            bool futureLeft = !File.Exists(Path.Combine(dir, "assets", "aion2", "pets", "future.json"));
            bool notData = !File.Exists(Path.Combine(dir, "assets", "feedback", "public.pem")) && !DataUpdateClient.IsAllowed("aion2/../app/aiondps.ico") && !DataUpdateClient.IsAllowed("feedback/public.pem") && !DataUpdateClient.IsAllowed("app/aiondps.ico");
            Console.WriteLine($"  -> reached {result.Reached}, taken {result.Taken}, failed {result.Failed}: changed table taken {takenChanged}, new file taken {takenNew}, equal file stays shipped {sameStays}");
            Console.WriteLine($"  -> a file with another hash is not taken {damagedLeft}, a layout of the future is left alone {futureLeft}, nothing outside the data is taken {notData}");

            // the index survives a restart, and a file whose size no longer fits is not used
            AssetStore.Reset();
            bool remembered = AssetStore.Path("aion2", "maps", "maps.json").StartsWith(dir, StringComparison.OrdinalIgnoreCase);
            File.WriteAllText(AssetStore.OverlayPath("aion2/maps/maps.json"), "cut");
            AssetStore.Reset();
            bool damagedFallsBack = AssetStore.Path("aion2", "maps", "maps.json").StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"  -> index remembered after a restart {remembered}, a cut file falls back to the shipped one {damagedFallsBack}");

            bool ok = result.Reached && result.Taken == 2 && result.Failed == 1 && takenChanged && takenNew && sameStays && damagedLeft && futureLeft && notData && remembered && damagedFallsBack;
            Console.WriteLine($"  -> data delivery: {ok}");
            return ok;
        }
        finally
        {
            listener?.Close();
            Environment.SetEnvironmentVariable("AIONDPS_DATA_DIR", before);
            AssetStore.Reset();
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // a temporary folder
            }
        }
    }
}
