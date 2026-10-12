using System.IO;

namespace AionDPS.Aion2.Capture;

/// <summary>
/// The "debug recording" setting: while it is on, every segment the capture lets through (the game-server
/// stream, both directions) is also written to a file on the player's own PC, in the format of
/// <c>aion2-record</c>, so it can be sent to the developer by hand when something goes wrong (a wrong
/// character, legion or board in an upload). Nothing is ever sent by the program itself.
/// The file name carries the character: <c>aion2-2026-10-11_14-02-31-Name.jsonl</c>. A file started before the
/// game has named the character is renamed once it does (it then holds the whole login); when another
/// character logs in, a new file starts and is seeded with the last <see cref="SeedSeconds"/> seconds, so
/// the login of the new character is in it from its first packet. A file also ends at
/// <see cref="MaxFileBytes"/>. Files older than two days are deleted (and the oldest ones beyond
/// <see cref="KeepBytes"/> in all), so it can stay on without filling the disk.
/// </summary>
public static class DebugRecording
{
    private const long MaxFileBytes = 150L * 1024 * 1024;
    private static readonly TimeSpan KeepFor = TimeSpan.FromDays(2);
    private const long KeepBytes = 1_500_000_000;
    private const int SeedSeconds = 90;

    private static readonly object Gate = new();
    private static volatile bool _enabled;
    private static SegmentRecording.Writer? _writer;
    private static string? _path;
    private static long _bytes;
    private static string? _name; // the character the game named last
    private static long _lastFlushTick;

    // Serialising and writing run on their own thread: before, every segment was written on the capture's
    // worker, so a raid's traffic made the worker fall behind and the queue ate memory. When the writer cannot
    // keep up the debug file loses segments; the meter does not.
    private const int MaxPending = 20_000;
    private static readonly System.Collections.Concurrent.BlockingCollection<TcpSegment> Pending = new(MaxPending);
    private static Thread? _thread;

    public static string FolderPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aion DPS Meter", "Debug");

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            if (!value)
            {
                lock (Gate)
                {
                    Close();
                }
            }
        }
    }

    /// <summary>The file being written now, null while off or before the first segment.</summary>
    public static string? CurrentPath
    {
        get { lock (Gate) { return _path; } }
    }

    /// <summary>The game named the character that is logged in (a live record, not one restored from disk).</summary>
    public static void NoteCharacter(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        lock (Gate)
        {
            string? previous = _name;
            _name = name;
            if (!_enabled || _writer is null || previous == name)
            {
                return;
            }

            try
            {
                if (previous is null)
                {
                    Rename();
                }
                else
                {
                    // Another character: the new file starts with the last moments, which hold its login.
                    Close();
                    Open(Feedback.CaptureRingBuffer.Since(DateTime.Now.AddSeconds(-SeedSeconds)));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Stop();
            }
        }
    }

    public static void Add(TcpSegment segment)
    {
        if (!_enabled)
        {
            return;
        }

        if (_thread is null)
        {
            lock (Gate)
            {
                _thread ??= StartThread();
            }
        }

        Pending.TryAdd(segment);
    }

    private static Thread StartThread()
    {
        var thread = new Thread(() =>
        {
            foreach (TcpSegment segment in Pending.GetConsumingEnumerable())
            {
                if (_enabled)
                {
                    Record(segment);
                }
            }
        })
        {
            IsBackground = true,
            Name = "Debug recording writer",
            Priority = ThreadPriority.BelowNormal,
        };
        thread.Start();
        return thread;
    }

    private static void Record(TcpSegment segment)
    {
        lock (Gate)
        {
            try
            {
                if (_writer is null || _bytes > MaxFileBytes)
                {
                    Close();
                    Open(null);
                }

                Write(segment);

                // One write per segment was too slow for a burst; flush about once a second instead.
                if (Environment.TickCount64 - _lastFlushTick > 1000)
                {
                    _lastFlushTick = Environment.TickCount64;
                    _writer!.Flush();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Disk full or folder not writable: stop recording rather than disturb the meter.
                Stop();
            }
        }
    }

    private static void Write(TcpSegment segment)
    {
        _writer!.Write(segment);
        _bytes += segment.Payload.Length * 4L / 3 + 120;
    }

    private static void Stop()
    {
        _enabled = false;
        Close();
    }

    private static string PathFor(string? name) => Path.Combine(FolderPath, $"aion2-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}-{SafeName(name)}.jsonl");

    private static string SafeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "unknown";
        }

        string clean = new(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c) && !char.IsControl(c)).ToArray());
        return clean.Length == 0 ? "unknown" : clean.Length > 30 ? clean[..30] : clean;
    }

    private static void Open(IReadOnlyList<TcpSegment>? seed)
    {
        Directory.CreateDirectory(FolderPath);
        Prune();

        _path = PathFor(_name);
        _writer = new SegmentRecording.Writer(_path, autoFlush: false);
        _bytes = 0;
        foreach (TcpSegment segment in seed ?? Array.Empty<TcpSegment>())
        {
            Write(segment);
        }
    }

    private static void Prune()
    {
        long total = 0;
        foreach (FileInfo file in new DirectoryInfo(FolderPath).GetFiles("aion2-*.jsonl").OrderByDescending(f => f.LastWriteTimeUtc))
        {
            total += file.Length;
            if (DateTime.UtcNow - file.LastWriteTimeUtc <= KeepFor && total <= KeepBytes)
            {
                continue;
            }

            try
            {
                file.Delete();
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>The file was started before the character was known: the name is put into the file name.</summary>
    private static void Rename()
    {
        string old = _path!;
        long bytes = _bytes;
        _writer!.Dispose();
        _writer = null;
        string stamp = Path.GetFileName(old).Split("-unknown")[0]; // keeps the time the file was started
        string target = Path.Combine(FolderPath, $"{stamp}-{SafeName(_name)}.jsonl");
        if (File.Exists(target))
        {
            target = PathFor(_name);
        }

        File.Move(old, target);
        _path = target;
        _writer = new SegmentRecording.Writer(target, append: true, autoFlush: false);
        _bytes = bytes;
    }

    private static void Close()
    {
        _writer?.Dispose();
        _writer = null;
        _path = null;
        _bytes = 0;
    }
}
