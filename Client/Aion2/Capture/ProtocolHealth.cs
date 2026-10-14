using System.IO;

namespace AionDPS.Aion2.Capture;

/// <summary>
/// The protocol health monitor, part of the debug output: while "Debug recording" is on, the frames the decoder sees are counted
/// per half minute and written to <c>protocol-health.log</c> in the debug folder (never uploaded). A game patch shifts opcodes;
/// without this the meter then shows zeros and nobody knows which feature broke. The log names it:
/// <list type="bullet">
/// <item>a census line per window: how often each watched frame family came, and how many frames in all;</item>
/// <item><c>PROTOCOL BREAK SUSPECTED</c> when a family that came regularly stays silent for <see cref="SilentWindows"/> windows
/// while the other families keep flowing (the opcode has most likely moved: record a fresh fight);</item>
/// <item><c>NEW OPCODE</c> the first time an opcode appears that is not in the known list (what a patch added).</item>
/// </list>
/// Windows follow the frames' own timestamps, so a replay of a recording behaves like the live game.
/// </summary>
public static class ProtocolHealth
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(30);
    public const int SilentWindows = 4;
    private const int FlowingFrames = 50;
    private const int EstablishedWindows = 3;
    private const int EstablishedFrames = 5;
    private const int MaxNewOpcodes = 200;

    /// <summary>The families that are watched and counted: opcode and what it carries.</summary>
    public static readonly IReadOnlyDictionary<int, string> Watched = new Dictionary<int, string>
    {
        [0x0438] = "damage",
        [0x0538] = "dot",
        [0x008d] = "hp",
        [0x0238] = "cast",
        [0x0638] = "skill-end",
        [0x0938] = "windup",
        [0x2a38] = "effect-new",
        [0x2b38] = "effect-refresh",
        [0x2c38] = "effect-end",
    };

    /// <summary>Not every fight has them (no damage over time, no windup): counted, but never reported as broken.</summary>
    private static readonly HashSet<int> Optional = new() { 0x0538, 0x0938, 0x0638, 0x2a38, 0x2c38 };

    private static readonly object Gate = new();
    private static readonly Dictionary<int, int> Counts = new();
    private static readonly Dictionary<int, int> ActiveWindows = new();
    private static readonly Dictionary<int, int> Silent = new();
    private static readonly HashSet<int> Reported = new();
    private static readonly HashSet<int> NewSeen = new();
    private static DateTime _windowStart;
    private static int _frames;

    /// <summary>A test writes to its own file instead of the real log.</summary>
    public static string? PathOverride { get; set; }

    public static string LogPath => PathOverride ?? Path.Combine(DebugRecording.FolderPath, "protocol-health.log");

    /// <summary>Every report this process wrote (what a test looks at).</summary>
    public static List<string> Reports { get; } = new();

    /// <summary>One frame seen by the decoder (not the bundle around it). Cheap: a lock and two dictionary updates.</summary>
    public static void Note(int opcode, int length, DateTime at, IReadOnlySet<int>? known)
    {
        lock (Gate)
        {
            if (_windowStart == default)
            {
                _windowStart = at;
            }
            else if (at - _windowStart >= Window)
            {
                CloseWindow(at);
            }

            _frames++;
            if (Watched.ContainsKey(opcode))
            {
                Counts[opcode] = Counts.GetValueOrDefault(opcode) + 1;
            }

            if (known is not null && known.Count > 0 && !known.Contains(opcode) && NewSeen.Count < MaxNewOpcodes && NewSeen.Add(opcode))
            {
                Report($"NEW OPCODE 0x{opcode:x4} (length {length}) at {at:HH:mm:ss}: not in the known list - a patch added it, or it is a login/rare frame");
            }
        }
    }

    /// <summary>Forget everything (a test; a new recording).</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            Counts.Clear();
            ActiveWindows.Clear();
            Silent.Clear();
            Reported.Clear();
            NewSeen.Clear();
            Reports.Clear();
            _windowStart = default;
            _frames = 0;
        }
    }

    private static void CloseWindow(DateTime at)
    {
        int frames = _frames;
        if (frames > 0)
        {
            string census = string.Join(", ", Watched.OrderBy(w => w.Key).Select(w => $"{w.Value}={Counts.GetValueOrDefault(w.Key)}"));
            Report($"PROTOCOL CENSUS [{Window.TotalSeconds:0}s]: frames={frames}, {census}", writeToList: false);
        }

        // Which families came in this window; a family counts as established after a few windows with some frames.
        foreach (int opcode in Watched.Keys)
        {
            int count = Counts.GetValueOrDefault(opcode);
            if (count >= EstablishedFrames)
            {
                ActiveWindows[opcode] = ActiveWindows.GetValueOrDefault(opcode) + 1;
            }
        }

        // Flowing = another established family brings plenty of frames: then silence of one family is not just "nothing happens".
        foreach (int opcode in Watched.Keys.Where(o => !Optional.Contains(o)))
        {
            bool established = ActiveWindows.GetValueOrDefault(opcode) >= EstablishedWindows;
            if (!established)
            {
                continue;
            }

            int count = Counts.GetValueOrDefault(opcode);
            bool othersFlow = Watched.Keys.Any(o => o != opcode && !Optional.Contains(o)
                && ActiveWindows.GetValueOrDefault(o) >= EstablishedWindows && Counts.GetValueOrDefault(o) >= FlowingFrames);
            if (count == 0 && othersFlow)
            {
                int silent = Silent[opcode] = Silent.GetValueOrDefault(opcode) + 1;
                if (silent >= SilentWindows && Reported.Add(opcode))
                {
                    string others = string.Join(", ", Watched.Where(w => w.Key != opcode && Counts.GetValueOrDefault(w.Key) > 0).Select(w => $"{w.Value}={Counts[w.Key]}"));
                    Report($"PROTOCOL BREAK SUSPECTED: 0x{opcode:x4} ({Watched[opcode]}) silent for {silent} windows while the others flow ({others}) - "
                        + "the opcode has most likely moved with a patch: record a fresh fight and compare the opcodes");
                }
            }
            else if (count > 0)
            {
                if (Reported.Remove(opcode))
                {
                    Report($"PROTOCOL RECOVERED: 0x{opcode:x4} ({Watched[opcode]}) is back ({count} frames)");
                }

                Silent[opcode] = 0;
            }
        }

        Counts.Clear();
        _frames = 0;
        _windowStart = at;
    }

    private static void Report(string message, bool writeToList = true)
    {
        if (writeToList)
        {
            Reports.Add(message);
            if (Reports.Count > 500)
            {
                Reports.RemoveAt(0);
            }
        }

        try
        {
            string path = LogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > 400_000)
            {
                File.WriteAllLines(path, File.ReadLines(path).TakeLast(2000));
            }

            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
