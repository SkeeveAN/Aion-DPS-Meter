using System.Diagnostics;
using System.IO;
using AionDPS.Aion2;
using AionDPS.Aion2.Capture;
using AionDPS.Aion2.Protocol;

namespace AionDPS;

/// <summary>
/// <c>aion2-session-sim &lt;recording.jsonl&gt; [--tick SECONDS]</c>: plays a recording through the main window (never shown, throw-away
/// data folder, nothing sent) the way a player lives it - stay on a map, change the map, fight, change back - and prints what the
/// list shows at each step: the live view of the current session and of all sessions, PvE and PvP, and at the end every finished
/// session opened from the session menu.
/// </summary>
internal static class SessionSim
{
    private const int ServerPort = 13328;
    private const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

    public static void Run(string[] args)
    {
        var file = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && !int.TryParse(a, out _));
        if (file is null)
        {
            Console.WriteLine("Usage: AionDPS aion2-session-sim <recording.jsonl> [--tick SECONDS]");
            return;
        }

        int at = Array.IndexOf(args, "--tick");
        int tickSeconds = at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out int t) ? t : 2;

        string dataDir = Path.Combine(Path.GetTempPath(), "aiondps-session-sim");
        Directory.CreateDirectory(dataDir);
        Environment.SetEnvironmentVariable("AIONDPS_DATA_DIR", dataDir);
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        string report = Path.Combine(Path.GetTempPath(), "aion2-session-sim.txt");
        using var output = new StreamWriter(report, false) { AutoFlush = true };
        void Say(string line)
        {
            Console.WriteLine(line);
            output.WriteLine(line);
        }

        var app = new System.Windows.Application();
        var settings = Ui.MeterSettings.Load();
        Ui.ThemeManager.Apply(app, settings.Theme, settings.FontSize);
        Ui.MainWindow.Headless = true;

        var type = typeof(Ui.MainWindow);
        var window = new Ui.MainWindow();
        (type.GetField("_liveSource", Flags)!.GetValue(window) as IDisposable)?.Dispose();
        var source = new Aion2PacketCombatSource(Aion2Protocol.Load());
        type.GetField("_source", Flags)!.SetValue(window, source);
        type.GetField("_liveSource", Flags)!.SetValue(window, source);
        type.GetField("_autoReset", Flags)!.SetValue(window, false);
        var tick = type.GetMethod("OnPollTimerTick", Flags)!;
        var refresh = type.GetMethod("RefreshRows", Flags)!;
        var pvpField = type.GetField("_pvpOnly", Flags)!;
        var sessions = (History.SessionRecorder)type.GetField("_sessions", Flags)!.GetValue(window)!;
        var rows = (System.Collections.Generic.IEnumerable<Ui.PlayerRow>)type.GetField("_rows", Flags)!.GetValue(window)!;
        var showLive = type.GetMethod("ShowLiveViewFromMenu", Flags)!;
        var showPast = type.GetMethod("ShowPastSession", Flags)!;
        var viewType = type.GetNestedType("SessionView", System.Reflection.BindingFlags.NonPublic)!;

        string Rows() => string.Join("; ", rows.Select(r => $"{r.Name} dmg {r.Damage:N0} dps {r.Dps:N0} taken {r.DamageTaken:N0}")) is { Length: > 0 } s ? s : "(empty)";
        void Show(string label)
        {
            foreach (bool pvp in new[] { false, true })
            {
                pvpField.SetValue(window, pvp);
                refresh.Invoke(window, null);
                Say($"    {label,-34} {(pvp ? "PvP" : "PvE")}: {Rows()}");
            }

            pvpField.SetValue(window, false);
        }

        void LiveViews(string when)
        {
            var current = sessions.Current;
            Say($"[{when}] session now: {(current is null ? "none" : Ui.SessionLabels.NameOf(current) + " #" + current.Number)}, finished: {sessions.Sessions.Count(s => !s.IsOpen)}");
            foreach (string view in new[] { "Current", "All" })
            {
                showLive.Invoke(window, new[] { Enum.Parse(viewType, view) });
                Show($"live, view {view}");
            }

            showLive.Invoke(window, new[] { Enum.Parse(viewType, "Current") });
        }

        DateTime bucketStart = DateTime.MinValue;
        int ticks = 0, lastNumber = -1;
        DateTime lastStamp = DateTime.MinValue;
        foreach (TcpSegment segment in SegmentRecording.Read(file))
        {
            bool fromServer = segment.Source.EndsWith(":" + ServerPort, StringComparison.Ordinal);
            if (!fromServer && !segment.Destination.EndsWith(":" + ServerPort, StringComparison.Ordinal))
            {
                continue;
            }

            source.Ingest(segment with { FromServer = fromServer });
            lastStamp = segment.Timestamp;
            if (bucketStart == DateTime.MinValue)
            {
                bucketStart = segment.Timestamp;
                continue;
            }

            if ((segment.Timestamp - bucketStart).TotalSeconds < tickSeconds)
            {
                continue;
            }

            bucketStart = segment.Timestamp;
            int before = sessions.Current?.Number ?? -1;
            tick.Invoke(window, new object?[] { null, EventArgs.Empty });
            ticks++;
            int number = sessions.Current?.Number ?? -1;
            if (number != lastNumber)
            {
                if (lastNumber != -1)
                {
                    Say($"--- map change at {segment.Timestamp:HH:mm:ss} (tick {ticks}): session #{lastNumber} -> #{number}");
                }

                LiveViews($"{segment.Timestamp:HH:mm:ss} first tick of session #{number}");
                lastNumber = number;
            }
        }

        tick.Invoke(window, new object?[] { null, EventArgs.Empty });
        LiveViews($"{lastStamp:HH:mm:ss} end of the recording");

        Say("--- every finished session opened from the session menu:");
        foreach (var session in sessions.Sessions.Where(s => !s.IsOpen).ToList())
        {
            Say($"  #{session.Number} {Ui.SessionLabels.NameOf(session)} {Ui.SessionLabels.TimeSpanOf(session)}: {session.Data.Events.Count} event(s)");
            try
            {
                showPast.Invoke(window, new object[] { session });
                Show("past session");
            }
            catch (Exception ex)
            {
                Say($"    CRASH opening it: {(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}");
                Say("    " + string.Join(" | ", ((ex.InnerException ?? ex).StackTrace ?? "").Split('\n').Take(4).Select(l => l.Trim())));
            }

            showLive.Invoke(window, new[] { Enum.Parse(viewType, "Current") });
        }

        source.Dispose();
        Say($"aion2-session-sim: report also in {report}");
        Environment.Exit(0);
    }
}
