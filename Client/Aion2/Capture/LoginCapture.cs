using System.IO;
using System.IO.Compression;
using System.Text;

namespace AionDPS.Aion2.Capture;

/// <summary>
/// The login traffic of the character that just logged in: the game server's segments of the last <see cref="SeedSeconds"/> seconds (the login
/// starts before the game names the character) and of the <see cref="ExtraSeconds"/> that follow, held in memory. It only ever runs while the
/// setting "share my login with the legion" is on - a switch only members of Akatsuki on Kaisinel get to see (see MainWindow.IsLoginShareEligible) -
/// and goes nowhere unless the character is still in that legion when it is sent. Only the server's direction is kept, never what the PC sent.
/// </summary>
public static class LoginCapture
{
    private const int SeedSeconds = 90;
    private const int ExtraSeconds = 25;
    private const long MaxBytes = 10L * 1024 * 1024;

    private static readonly object Gate = new();
    private static volatile bool _enabled;
    private static string? _name;
    private static DateTime _beganAt;
    private static List<TcpSegment>? _segments;
    private static long _bytes;
    private static bool _sent;
    private static readonly List<(string Name, byte[] Gzip)> _ready = new();

    /// <summary>No login is captured or sent for now (the dumps are not needed at present). The switch in the settings is left as it is; set to true to bring the analysis back.</summary>
    public const bool Available = false;

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            value &= Available;
            _enabled = value;
            if (!value)
            {
                lock (Gate)
                {
                    _segments = null;
                    _name = null;
                    _bytes = 0;
                    _ready.Clear();
                }
            }
        }
    }

    /// <summary>The login's character list arrived (it comes at a login only, not at a map change): a capture starts, seeded with the last moments.</summary>
    public static void LoginStarted()
    {
        if (!_enabled)
        {
            return;
        }

        Flush(force: true); // a capture that is still running when the next login begins is finished early, not lost
        lock (Gate)
        {
            _name = null;
            _beganAt = DateTime.UtcNow;
            _sent = false;
            _segments = Feedback.CaptureRingBuffer.Since(DateTime.Now.AddSeconds(-SeedSeconds)).Where(s => s.FromServer).ToList();
            _bytes = _segments.Sum(s => (long)s.Payload.Length);
        }
    }

    /// <summary>The game named the character that logged in: it is the name of the capture that is running and has none yet.</summary>
    public static void NoteCharacter(string name)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        lock (Gate)
        {
            if (_segments is not null && _name is null)
            {
                _name = name;
            }
        }
    }

    public static void Add(TcpSegment segment)
    {
        if (!_enabled || !segment.FromServer)
        {
            return;
        }

        lock (Gate)
        {
            if (_segments is null || _sent || DateTime.UtcNow - _beganAt > TimeSpan.FromSeconds(ExtraSeconds) || _bytes + segment.Payload.Length > MaxBytes)
            {
                return;
            }

            _segments.Add(segment);
            _bytes += segment.Payload.Length;
        }
    }

    /// <summary>A capture is running or waits to be sent (cheap check for the sender's timer).</summary>
    public static bool Pending
    {
        get { lock (Gate) { return _enabled && (_segments is not null || _ready.Count > 0); } }
    }

    /// <summary>Moves the running capture to the queue of finished ones once its time is up (or at once with <paramref name="force"/>: the next login begins).</summary>
    public static void Flush(bool force = false)
    {
        TcpSegment[] copy;
        string name;
        lock (Gate)
        {
            if (_segments is null || _sent || _name is null || (!force && DateTime.UtcNow - _beganAt <= TimeSpan.FromSeconds(ExtraSeconds)))
            {
                return;
            }

            copy = _segments.ToArray();
            name = _name;
            _segments = null;
            _bytes = 0;
        }

        using var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)) { NewLine = "\n" })
        {
            foreach (TcpSegment segment in copy)
            {
                writer.WriteLine(SegmentRecording.Serialize(segment));
            }
        }

        lock (Gate)
        {
            _ready.Add((name, memory.ToArray()));
        }
    }

    /// <summary>The oldest finished capture that is not delivered yet, or null.</summary>
    public static (string Name, byte[] Gzip)? Next()
    {
        lock (Gate)
        {
            return _ready.Count > 0 ? _ready[0] : null;
        }
    }

    /// <summary>The capture was delivered (or must not be sent any more): it is dropped.</summary>
    public static void Done(string name)
    {
        lock (Gate)
        {
            int index = _ready.FindIndex(r => r.Name == name);
            if (index >= 0)
            {
                _ready.RemoveAt(index);
            }
        }
    }
}
