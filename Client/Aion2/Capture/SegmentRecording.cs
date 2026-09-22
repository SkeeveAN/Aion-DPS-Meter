using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Capture;

/// <summary>
/// Writes captured segments as JSON lines and reads them back - the calibration fixture format.
/// One recorded fight from a real Aion 2 session is what turns opcodes.json from a template into
/// a working protocol, and the same file then becomes a regression test that replays without the
/// game. Payloads are base64; nothing but the game-server stream is ever in here.
/// </summary>
public static class SegmentRecording
{
    private sealed record Line(DateTime T, string From, string To, uint Seq, bool Server, string Data);

    public sealed class Writer : IDisposable
    {
        private readonly StreamWriter _out;
        private readonly object _gate = new();

        public Writer(string path)
        {
            _out = new StreamWriter(path, append: false) { AutoFlush = true };
        }

        public int Count { get; private set; }

        public void Write(TcpSegment segment)
        {
            string json = JsonSerializer.Serialize(new Line(segment.Timestamp, segment.Source, segment.Destination, segment.Sequence, segment.FromServer, Convert.ToBase64String(segment.Payload.Span)));
            lock (_gate)
            {
                _out.WriteLine(json);
                Count++;
            }
        }

        public void Dispose() => _out.Dispose();
    }

    public static IEnumerable<TcpSegment> Read(string path)
    {
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0)
            {
                continue;
            }

            Line? parsed = JsonSerializer.Deserialize<Line>(line);
            if (parsed is null)
            {
                continue;
            }

            yield return new TcpSegment(parsed.T, parsed.From, parsed.To, parsed.Seq, Convert.FromBase64String(parsed.Data), parsed.Server);
        }
    }
}
