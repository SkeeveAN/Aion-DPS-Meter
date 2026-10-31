using System.IO;

namespace AionDPS.Aion2.Capture;

/// <summary>
/// The protocol health monitor: the frames the decoder sees are counted per half minute, always (a lock and two dictionary
/// updates per frame). A game patch shifts opcodes; without this the meter then shows zeros and nobody knows which feature
/// broke. A family that stays silent while a fight goes on is shown to the player (<see cref="Flagged"/>, a note in the main
/// window) and, while "Debug recording" is on, written to <c>protocol-health.log</c> in the debug folder (never uploaded). The log names it:
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
    private static readonly HashSet<int> FlaggedSet = new();
    private static DateTime _windowStart;
    private static int _frames;
    private static bool _knownLoaded;

    /// <summary>The families whose silence the player is told about: damage and casts, which every fight has, and only while the other
    /// of the two keeps coming (so it is not just a quiet town). Hit points and effects are left out on purpose: the game sends hit
    /// points only for a target you have, and in 1,850 half-minute windows of real play they were silent in 12 of them without any break.</summary>
    private static readonly HashSet<int> CombatFamilies = new() { 0x0438, 0x0238 };

    /// <summary>A test writes to its own file instead of the real log.</summary>
    public static string? PathOverride { get; set; }

    public static string LogPath => PathOverride ?? Path.Combine(DebugRecording.FolderPath, "protocol-health.log");

    /// <summary>The families that came regularly in earlier sessions, kept so a patch that breaks one right at the start is noticed
    /// too (without it a family only counts as established after three windows of this session). Not kept in a test.</summary>
    private static string? KnownPath => PathOverride is null ? Path.Combine(Ui.MeterSettings.DataDirectory, "protocol-families.json") : null;

    /// <summary>What a family is called in <see cref="Watched"/> for each family the player is told about, in a stable order; empty while all is well.</summary>
    public static IReadOnlyList<string> Flagged
    {
        get
        {
            lock (Gate)
            {
                return FlaggedSet.OrderBy(o => o).Select(o => Watched[o]).ToList();
            }
        }
    }

    /// <summary>Every report this process wrote (what a test looks at).</summary>
    public static List<string> Reports { get; } = new();

    /// <summary>One frame seen by the decoder (not the bundle around it). Cheap: a lock and two dictionary updates.</summary>
    public static void Note(int opcode, int length, DateTime at, IReadOnlySet<int>? known)
    {
        lock (Gate)
        {
            LoadKnown();
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
            FlaggedSet.Clear();
            NewSeen.Clear();
            _knownLoaded = false;
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
                int windows = ActiveWindows[opcode] = ActiveWindows.GetValueOrDefault(opcode) + 1;
                if (windows == EstablishedWindows)
                {
                    SaveKnown();
                }
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
                if (silent >= SilentWindows && CombatFamilies.Contains(opcode)
                    && Watched.Keys.Any(o => o != opcode && CombatFamilies.Contains(o) && Counts.GetValueOrDefault(o) >= FlowingFrames))
                {
                    FlaggedSet.Add(opcode);
                }

                if (silent >= SilentWindows && Reported.Add(opcode))
                {
                    string others = string.Join(", ", Watched.Where(w => w.Key != opcode && Counts.GetValueOrDefault(w.Key) > 0).Select(w => $"{w.Value}={Counts[w.Key]}"));
                    Report($"PROTOCOL BREAK SUSPECTED: 0x{opcode:x4} ({Watched[opcode]}) silent for {silent} windows while the others flow ({others}) - "
                        + "the opcode has most likely moved with a patch: record a fresh fight and compare the opcodes");
                }
            }
            else if (count > 0)
            {
                FlaggedSet.Remove(opcode);
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

        if (PathOverride is null && !DebugRecording.Enabled)
        {
            return; // the note for the player is Flagged; the log file belongs to the debug output
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

    /// <summary>Takes over what earlier sessions saw, once per process: those families count as established from the first window.</summary>
    private static void LoadKnown()
    {
        if (_knownLoaded)
        {
            return;
        }

        _knownLoaded = true;
        if (KnownPath is not { } path)
        {
            return;
        }

        try
        {
            if (File.Exists(path) && System.Text.Json.JsonSerializer.Deserialize<int[]>(File.ReadAllText(path)) is { } opcodes)
            {
                foreach (int opcode in opcodes.Where(Watched.ContainsKey))
                {
                    ActiveWindows[opcode] = Math.Max(ActiveWindows.GetValueOrDefault(opcode), EstablishedWindows);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
        }
    }

    private static void SaveKnown()
    {
        if (KnownPath is not { } path)
        {
            return;
        }

        try
        {
            var known = Watched.Keys.Where(o => ActiveWindows.GetValueOrDefault(o) >= EstablishedWindows).OrderBy(o => o).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(known));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
