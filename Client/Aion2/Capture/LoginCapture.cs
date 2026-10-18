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

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value)
            {
                lock (Gate)
                {
                    _segments = null;
                    _name = null;
                    _bytes = 0;
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

    /// <summary>True while the capture of <paramref name="name"/> still collects.</summary>
    public static bool Collecting(string name)
    {
        lock (Gate)
        {
            return _segments is not null && !_sent && _name == name && DateTime.UtcNow - _beganAt <= TimeSpan.FromSeconds(ExtraSeconds);
        }
    }

    /// <summary>The finished capture of <paramref name="name"/> as gzip of JSON lines (the format of the debug recording), or null.</summary>
    public static byte[]? Finished(string name)
    {
        TcpSegment[] copy;
        lock (Gate)
        {
            if (_segments is null || _sent || _name != name || DateTime.UtcNow - _beganAt <= TimeSpan.FromSeconds(ExtraSeconds))
            {
                return null;
            }

            copy = _segments.ToArray();
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

        return memory.ToArray();
    }

    /// <summary>The capture was delivered (or must not be sent any more): it is dropped from memory.</summary>
    public static void Done(string name)
    {
        lock (Gate)
        {
            if (_name == name)
            {
                _sent = true;
                _segments = null;
                _bytes = 0;
            }
        }
    }
}
