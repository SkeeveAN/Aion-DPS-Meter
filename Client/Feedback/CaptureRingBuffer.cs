using System.IO;
using AionDPS.Aion2.Capture;

namespace AionDPS.Feedback;

/// <summary>
/// The most recent game-server segments, kept in memory so "attach recording" in the feedback
/// window has something to send without the user having started a recording beforehand. Only what
/// the capture filter already lets through (the game-server ports) ever lands here; nothing is
/// written to disk until the user ticks the box and sends, and then only in encrypted form.
/// </summary>
public static class CaptureRingBuffer
{
    /// <summary>Payload bytes kept (before JSON/base64/gzip) - a few minutes of a busy fight.</summary>
    private const long MaxBytes = 16L * 1024 * 1024;

    private static readonly object Gate = new();
    private static readonly Queue<(TcpSegment Segment, int Size)> Segments = new();
    private static long _bytes;

    public static void Add(TcpSegment segment)
    {
        int size = segment.Payload.Length;
        lock (Gate)
        {
            Segments.Enqueue((segment, size));
            _bytes += size;
            while (_bytes > MaxBytes && Segments.Count > 0)
            {
                _bytes -= Segments.Dequeue().Size;
            }
        }
    }

    public static int Count
    {
        get { lock (Gate) { return Segments.Count; } }
    }

    public static long Bytes
    {
        get { lock (Gate) { return _bytes; } }
    }

    /// <summary>The buffer as JSON lines, the same format as <c>aion2-record</c> writes.</summary>
    public static byte[] SnapshotJsonLines()
    {
        TcpSegment[] copy;
        lock (Gate)
        {
            copy = Segments.Select(s => s.Segment).ToArray();
        }

        using var memory = new MemoryStream();
        using (var writer = new StreamWriter(memory, new System.Text.UTF8Encoding(false), leaveOpen: true) { NewLine = "\n" })
        {
            foreach (TcpSegment segment in copy)
            {
                writer.WriteLine(SegmentRecording.Serialize(segment));
            }
        }

        return memory.ToArray();
    }
}
