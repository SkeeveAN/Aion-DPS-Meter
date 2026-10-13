using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Streaming;

/// <summary>
/// A small web server on this PC only, for the stream overlay: a browser source in OBS or Streamlabs opens its link and shows the meter live. It listens on 127.0.0.1
/// (not reachable from other machines), the link carries a random key, only GET and HEAD are served, the Host header must be the loopback address (no DNS rebinding),
/// and the page may load nothing but its own files. The state travels as server-sent events: one stream per browser source, the newest state always wins.
/// </summary>
public sealed class StreamOverlayServer : IDisposable
{
    public const int DefaultPort = 28920;
    private const int MaxClients = 8;
    private const int PortTries = 10;

    private HttpListener? _listener;
    private CancellationTokenSource? _stop;
    private readonly List<Client> _clients = new();
    private readonly object _gate = new();
    private string _key = "";
    private string _last = "{}";

    private sealed class Client
    {
        public Channel<string> Queue { get; } = Channel.CreateBounded<string>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    }

    public bool Running => _listener is { IsListening: true };

    public int Port { get; private set; }

    public int ClientCount
    {
        get
        {
            lock (_gate)
            {
                return _clients.Count;
            }
        }
    }

    /// <summary>The link to put into the browser source, or null while the server is not running.</summary>
    public string? Url => Running ? $"http://127.0.0.1:{Port}/{_key}/" : null;

    /// <summary>A fresh key: 16 random bytes as 32 hex characters.</summary>
    public static string NewKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>Starts on <paramref name="port"/> (or the next free one, up to ten); returns whether it listens.</summary>
    public bool Start(int port, string key)
    {
        Stop();
        _key = key;
        for (int i = 0; i < PortTries; i++)
        {
            int candidate = port + i;
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{candidate}/");
            try
            {
                listener.Start();
            }
            catch (Exception ex) when (ex is HttpListenerException or InvalidOperationException)
            {
                listener.Close();
                continue;
            }

            _listener = listener;
            Port = candidate;
            _stop = new CancellationTokenSource();
            _ = Task.Run(() => AcceptLoop(listener, _stop.Token));
            return true;
        }

        return false;
    }

    public void Stop()
    {
        _stop?.Cancel();
        try
        {
            _listener?.Close();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException)
        {
        }

        _listener = null;
        lock (_gate)
        {
            foreach (Client client in _clients)
            {
                client.Queue.Writer.TryComplete();
            }

            _clients.Clear();
        }
    }

    public void Dispose() => Stop();

    /// <summary>The newest state (a JSON text) goes to every browser source; a later one replaces one that was not read yet.</summary>
    public void Publish(string json)
    {
        lock (_gate)
        {
            _last = json;
            foreach (Client client in _clients)
            {
                client.Queue.Writer.TryWrite(json);
            }
        }
    }

    private async Task AcceptLoop(HttpListener listener, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            _ = Task.Run(() => Handle(context, stop), stop);
        }
    }

    private static bool KeysMatch(string given, string expected)
    {
        byte[] a = Encoding.ASCII.GetBytes(given), b = Encoding.ASCII.GetBytes(expected);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static void Reply(HttpListenerResponse response, int status, string contentType = "text/plain", byte[]? body = null, bool head = false)
    {
        response.StatusCode = status;
        response.ContentType = contentType + (contentType.StartsWith("text/", StringComparison.Ordinal) ? "; charset=utf-8" : "");
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'";
        if (body is not null)
        {
            response.ContentLength64 = body.Length;
            if (!head)
            {
                response.OutputStream.Write(body, 0, body.Length);
            }
        }

        response.Close();
    }

    private async Task Handle(HttpListenerContext context, CancellationToken stop)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;
        try
        {
            string host = request.Headers["Host"] ?? "";
            if (host != $"127.0.0.1:{Port}" && host != $"localhost:{Port}")
            {
                Reply(response, 403);
                return;
            }

            bool head = request.HttpMethod == "HEAD";
            if (request.HttpMethod != "GET" && !head)
            {
                Reply(response, 405);
                return;
            }

            string[] parts = (request.Url?.AbsolutePath ?? "/").Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !KeysMatch(parts[0], _key))
            {
                Reply(response, 404);
                return;
            }

            string? tail = request.Url!.AbsolutePath.EndsWith('/') || parts.Length > 1 ? (parts.Length > 1 ? string.Join('/', parts.Skip(1)) : "") : null;
            if (tail is null)
            {
                response.Redirect($"/{_key}/");
                response.Close();
                return;
            }

            switch (tail)
            {
                case "":
                    Reply(response, 200, "text/html", Encoding.UTF8.GetBytes(StreamOverlayPage.Html), head);
                    return;
                case "overlay.js":
                    Reply(response, 200, "text/javascript", Encoding.UTF8.GetBytes(StreamOverlayPage.Script), head);
                    return;
                case "overlay.css":
                    Reply(response, 200, "text/css", Encoding.UTF8.GetBytes(StreamOverlayPage.Style), head);
                    return;
                case "events" when !head:
                    await ServeEvents(response, stop);
                    return;
            }

            if (parts.Length == 3 && parts[1] == "c" && parts[2].EndsWith(".png", StringComparison.Ordinal)
                && Aion2Artwork.ClassEmblemPath(parts[2][..^4]) is string icon && File.Exists(icon))
            {
                Reply(response, 200, "image/png", File.ReadAllBytes(icon), head);
                return;
            }

            Reply(response, 404);
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            // the browser source went away; nothing to tell
        }
    }

    private async Task ServeEvents(HttpListenerResponse response, CancellationToken stop)
    {
        var client = new Client();
        string first;
        lock (_gate)
        {
            if (_clients.Count >= MaxClients)
            {
                Reply(response, 503);
                return;
            }

            _clients.Add(client);
            first = _last;
        }

        try
        {
            response.StatusCode = 200;
            response.ContentType = "text/event-stream; charset=utf-8";
            response.SendChunked = true;
            response.Headers["Cache-Control"] = "no-store";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["Referrer-Policy"] = "no-referrer";
            Stream output = response.OutputStream;
            await WriteAsync(output, "retry: 2000\n\n", stop);
            await WriteAsync(output, "data: " + first + "\n\n", stop);
            while (!stop.IsCancellationRequested)
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(stop);
                wait.CancelAfter(TimeSpan.FromSeconds(15));
                string message;
                try
                {
                    message = await client.Queue.Reader.ReadAsync(wait.Token);
                }
                catch (OperationCanceledException) when (!stop.IsCancellationRequested)
                {
                    await WriteAsync(output, ": ping\n\n", stop); // keeps the connection alive
                    continue;
                }
                catch (ChannelClosedException)
                {
                    return;
                }

                await WriteAsync(output, "data: " + message + "\n\n", stop);
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException or OperationCanceledException)
        {
            // the browser source closed
        }
        finally
        {
            lock (_gate)
            {
                _clients.Remove(client);
            }

            try
            {
                response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
            }
        }
    }

    private static async Task WriteAsync(Stream output, string text, CancellationToken stop)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        await output.WriteAsync(bytes, stop);
        await output.FlushAsync(stop);
    }
}
