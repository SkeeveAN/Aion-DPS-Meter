using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Streaming;

/// <summary>
/// A small web server for the stream overlay: a browser source in OBS or Streamlabs opens its link and shows the meter live. For a two-PC setup (the game PC runs the
/// meter, the streaming PC runs OBS) it listens on the local network; it can be limited to this PC. Only addresses of the own network (private ranges) are answered, the
/// link carries a random key, only GET and HEAD are served, the Host header must be an address or this PC's own name (no DNS rebinding), and the page may load nothing
/// but its own files. The state travels as server-sent events: one stream per browser source, the newest state always wins.
/// </summary>
public sealed class StreamOverlayServer : IDisposable
{
    public const int DefaultPort = 28920;
    private const int MaxClients = 8;
    private const int PortTries = 10;

    private TcpListener? _listener;
    private bool _network;
    private string _address = "127.0.0.1";
    private CancellationTokenSource? _stop;
    private readonly List<Client> _clients = new();
    private readonly object _gate = new();
    private string _key = "";
    private string _last = "{}";

    private sealed class Client
    {
        public Channel<string> Queue { get; } = Channel.CreateBounded<string>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    }

    public bool Running => _listener is not null;

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
    public string? Url => Running ? $"http://{_address}:{Port}/{_key}/" : null;

    /// <summary>Whether other PCs of the network can reach it (else only this PC).</summary>
    public bool Network => Running && _network;

    /// <summary>This PC's address on the local network: the one the network card that reaches the router uses; the loopback address when there is none.</summary>
    public static string LocalAddress()
    {
        try
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect(new IPEndPoint(IPAddress.Parse("192.0.2.1"), 9)); // nothing is sent; it only picks the route
            if (probe.LocalEndPoint is IPEndPoint { Address: { } found } && !IPAddress.IsLoopback(found) && !found.Equals(IPAddress.Any))
            {
                return found.ToString();
            }
        }
        catch (SocketException)
        {
        }

        return "127.0.0.1";
    }

    /// <summary>A fresh key: 16 random bytes as 32 hex characters.</summary>
    public static string NewKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>Starts on <paramref name="port"/> (or the next free one, up to ten); returns whether it listens. With <paramref name="network"/> other PCs of the network can reach it.</summary>
    public bool Start(int port, string key, bool network = true)
    {
        Stop();
        _key = key;
        _network = network;
        for (int i = 0; i < PortTries; i++)
        {
            int candidate = port + i;
            var listener = new TcpListener(network ? IPAddress.Any : IPAddress.Loopback, candidate);
            try
            {
                listener.Start();
            }
            catch (SocketException)
            {
                listener.Stop();
                continue;
            }

            _listener = listener;
            Port = candidate;
            _address = network ? LocalAddress() : "127.0.0.1";
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
            _listener?.Stop();
        }
        catch (SocketException)
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

    private async Task AcceptLoop(TcpListener listener, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient connection;
            try
            {
                connection = await listener.AcceptTcpClientAsync(stop);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
            {
                return;
            }

            _ = Task.Run(() => Handle(connection, stop), stop);
        }
    }

    private static bool KeysMatch(string given, string expected)
    {
        byte[] a = Encoding.ASCII.GetBytes(given), b = Encoding.ASCII.GetBytes(expected);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>Only this PC and the own network may ask: loopback, private IPv4 ranges, link-local and unique-local IPv6. Anything from the internet is dropped unanswered.</summary>
    internal static bool IsLocalPeer(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            byte[] six = address.GetAddressBytes();
            return address.IsIPv6LinkLocal || (six[0] & 0xFE) == 0xFC;
        }

        byte[] four = address.GetAddressBytes();
        return four[0] == 10 || (four[0] == 172 && four[1] is >= 16 and <= 31) || (four[0] == 192 && four[1] == 168) || (four[0] == 169 && four[1] == 254);
    }

    private static bool HostAllowed(string host)
    {
        int colon = host.LastIndexOf(':');
        string name = colon > 0 && !host.EndsWith(']') && host.IndexOf(':') == colon ? host[..colon] : host;
        name = name.Trim('[', ']');
        return IPAddress.TryParse(name, out _)
            || name.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || name.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly string SecurityHeaders =
        "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\n";

    private static async Task Reply(Stream output, int status, string contentType = "text/plain", byte[]? body = null, bool head = false, string? location = null)
    {
        string reason = status switch { 200 => "OK", 302 => "Found", 403 => "Forbidden", 404 => "Not Found", 405 => "Method Not Allowed", 503 => "Service Unavailable", _ => "Error" };
        var text = new StringBuilder();
        text.Append($"HTTP/1.1 {status} {reason}\r\n");
        text.Append($"Content-Type: {contentType}{(contentType.StartsWith("text/", StringComparison.Ordinal) ? "; charset=utf-8" : "")}\r\n");
        text.Append(SecurityHeaders);
        text.Append("Content-Security-Policy: default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'\r\n");
        if (location is not null)
        {
            text.Append($"Location: {location}\r\n");
        }

        text.Append($"Content-Length: {body?.Length ?? 0}\r\nConnection: close\r\n\r\n");
        byte[] headBytes = Encoding.ASCII.GetBytes(text.ToString());
        await output.WriteAsync(headBytes);
        if (body is not null && !head)
        {
            await output.WriteAsync(body);
        }

        await output.FlushAsync();
    }

    /// <summary>Reads the request head (up to 8 KB, ten seconds); null when it is not a request.</summary>
    private static async Task<(string Method, string Path, string Host)?> ReadRequest(Stream input, CancellationToken stop)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
        limit.CancelAfter(TimeSpan.FromSeconds(10));
        var buffer = new byte[8192];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await input.ReadAsync(buffer.AsMemory(length), limit.Token);
            if (read == 0)
            {
                return null;
            }

            length += read;
            if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                break;
            }
        }

        string[] lines = Encoding.ASCII.GetString(buffer, 0, length).Split("\r\n");
        string[] first = lines[0].Split(' ');
        if (first.Length < 3)
        {
            return null;
        }

        string host = "";
        foreach (string line in lines.Skip(1))
        {
            if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
            {
                host = line[5..].Trim();
            }
        }

        string path = first[1];
        int query = path.IndexOfAny(new[] { '?', '#' });
        return (first[0], query >= 0 ? path[..query] : path, host);
    }

    private async Task Handle(TcpClient connection, CancellationToken stop)
    {
        using (connection)
        {
            try
            {
                if (!IsLocalPeer((connection.Client.RemoteEndPoint as IPEndPoint)?.Address))
                {
                    return; // not from this PC or the own network: no answer at all
                }

                connection.NoDelay = true;
                Stream output = connection.GetStream();
                if (await ReadRequest(output, stop) is not { } request)
                {
                    return;
                }

                if (!HostAllowed(request.Host))
                {
                    await Reply(output, 403);
                    return;
                }

                bool head = request.Method == "HEAD";
                if (request.Method != "GET" && !head)
                {
                    await Reply(output, 405);
                    return;
                }

                string[] parts = request.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0 || !KeysMatch(parts[0], _key))
                {
                    await Reply(output, 404);
                    return;
                }

                string? tail = request.Path.EndsWith('/') || parts.Length > 1 ? (parts.Length > 1 ? string.Join('/', parts.Skip(1)) : "") : null;
                if (tail is null)
                {
                    await Reply(output, 302, location: $"/{_key}/");
                    return;
                }

                switch (tail)
                {
                    case "":
                        await Reply(output, 200, "text/html", Encoding.UTF8.GetBytes(StreamOverlayPage.Html), head);
                        return;
                    case "overlay.js":
                        await Reply(output, 200, "text/javascript", Encoding.UTF8.GetBytes(StreamOverlayPage.Script), head);
                        return;
                    case "overlay.css":
                        await Reply(output, 200, "text/css", Encoding.UTF8.GetBytes(StreamOverlayPage.Style), head);
                        return;
                    case "events" when !head:
                        await ServeEvents(output, stop);
                        return;
                }

                if (parts.Length == 3 && parts[1] == "c" && parts[2].EndsWith(".png", StringComparison.Ordinal)
                    && Aion2Artwork.ClassEmblemPath(parts[2][..^4]) is string icon && File.Exists(icon))
                {
                    await Reply(output, 200, "image/png", File.ReadAllBytes(icon), head);
                    return;
                }

                await Reply(output, 404);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
            {
                // the browser source went away; nothing to tell
            }
        }
    }

    private async Task ServeEvents(Stream output, CancellationToken stop)
    {
        var client = new Client();
        string first;
        lock (_gate)
        {
            if (_clients.Count >= MaxClients)
            {
                first = "";
            }
            else
            {
                _clients.Add(client);
                first = _last;
            }
        }

        if (first.Length == 0)
        {
            await Reply(output, 503);
            return;
        }

        try
        {
            string head = "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\n" + SecurityHeaders + "Connection: close\r\n\r\n";
            await output.WriteAsync(Encoding.ASCII.GetBytes(head), stop);
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
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // the browser source closed
        }
        finally
        {
            lock (_gate)
            {
                _clients.Remove(client);
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
