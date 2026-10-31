using System.IO;
using System.Text.Json;
using AionDPS.Update;

namespace AionDPS.Aion2.Capture;

/// <summary>A frame of an opcode nobody has described yet, seen by the recorder with debug points.</summary>
public sealed record DebugPoint(DateTime At, int Opcode, int Length, string Hex, int Seen);

/// <summary>
/// The recorder's debug points: a server frame whose opcode is not in <c>assets/aion2/protocol/known_opcodes.json</c> (every opcode the protocol documents, the decoder or the
/// layout file mention, see Tools/aion2-dat/build_known_opcodes.py) is a point - the first time, and again (at most once more, see <see cref="MaxPointsPerOpcode"/>) when the opcode comes back after <see cref="Quiet"/> of silence (the
/// player did the thing again), but never for every frame of an unknown stream. Pure, so a test can run it.
/// </summary>
public sealed class UnknownFrameWatch
{
    /// <summary>An unknown opcode that was silent this long and comes back is a new point.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(120);

    /// <summary>One opcode gives at most this many points: a stream that keeps coming (thousands of frames in a few hours) is a heartbeat of the game, not something the player did.</summary>
    public const int MaxPointsPerOpcode = 2;

    private readonly Dictionary<int, int> _points = new();

    private const int MaxHexBytes = 96;

    private readonly HashSet<int> _known;
    private readonly Dictionary<int, (DateTime Last, int Count)> _seen = new();

    public UnknownFrameWatch(IEnumerable<int> known) => _known = new HashSet<int>(known);

    /// <summary>The opcodes the project knows, from the shipped file (empty when it is missing: then everything is "unknown").</summary>
    public static IReadOnlyCollection<int> LoadKnown()
    {
        try
        {
            string path = AssetStore.Path("aion2", "protocol", "known_opcodes.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<int[]>(File.ReadAllText(path)) ?? Array.Empty<int>() : Array.Empty<int>();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return Array.Empty<int>();
        }
    }

    /// <summary>How many different unknown opcodes were seen.</summary>
    public int UnknownOpcodes => _seen.Count;

    /// <summary>One frame: a point when it is of an unknown opcode that is new or back after the silence, else null.</summary>
    public DebugPoint? Note(int opcode, ReadOnlySpan<byte> frame, DateTime at)
    {
        if (_known.Contains(opcode))
        {
            return null;
        }

        bool point = !_seen.TryGetValue(opcode, out var before) || at - before.Last >= Quiet;
        _seen[opcode] = (at, (before.Count) + 1);
        if (point && _points.GetValueOrDefault(opcode) >= MaxPointsPerOpcode)
        {
            point = false;
        }

        if (point)
        {
            _points[opcode] = _points.GetValueOrDefault(opcode) + 1;
        }

        return point ? new DebugPoint(at, opcode, frame.Length, Convert.ToHexString(frame[..Math.Min(frame.Length, MaxHexBytes)]), before.Count + 1) : null;
    }

    /// <summary>How often an unknown opcode was seen so far (0 = never).</summary>
    public int CountOf(int opcode) => _seen.TryGetValue(opcode, out var s) ? s.Count : 0;

    public IEnumerable<KeyValuePair<int, (DateTime Last, int Count)>> Unknown => _seen;
}

/// <summary>The file next to the recording (<c>&lt;recording&gt;.points.jsonl</c>): one line per debug point and one per note the player typed, each with its time.</summary>
public sealed class DebugPointLog : IDisposable
{
    private readonly StreamWriter _out;
    private readonly object _gate = new();

    public DebugPointLog(string recordingPath)
    {
        Path = System.IO.Path.ChangeExtension(recordingPath, null) + ".points.jsonl";
        _out = new StreamWriter(Path, false, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
    }

    public string Path { get; }

    public int Count { get; private set; }

    public void Point(DebugPoint p)
    {
        Write(new { t = p.At, kind = "point", opcode = "0x" + p.Opcode.ToString("x4"), len = p.Length, seen = p.Seen, hex = p.Hex });
        Count++;
    }

    public void Note(DateTime at, string text) => Write(new { t = at, kind = "note", text });

    private void Write(object line)
    {
        lock (_gate)
        {
            _out.WriteLine(JsonSerializer.Serialize(line));
        }
    }

    public void Dispose() => _out.Dispose();
}
