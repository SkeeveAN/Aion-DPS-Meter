using System.Diagnostics;
using System.IO;
using AionDPS.Aion2;
using AionDPS.Aion2.Capture;
using AionDPS.Aion2.Protocol;

namespace AionDPS;

/// <summary>
/// <c>aion2-stress</c>: makes up game-server traffic (damage frames in the real wire format, cut into TCP segments)
/// for many fights, plays it through the same reassembly, decoder and window logic the live meter uses - never
/// shown, nothing sent, nothing written to the fight history - and reports how long each second of it took.
/// A tick that takes longer than a second is a meter that falls behind the game.
/// </summary>
internal static class StressTest
{
    private const int ServerPort = 13328;
    private const string ServerEndpoint = "193.202.112.97:13328";
    private const string ClientEndpoint = "10.0.0.5:51000";
    private const int Mss = 1400;

    /// <summary>A tick slower than this ends the run: the machine is the user's, and a meter that stalls is the finding.</summary>
    private const double AbortAtMs = 8000;

    public static void Run(string[] args)
    {
        string scenario = args.Length > 1 ? args[1] : "duel";
        if (scenario == "arena")
        {
            RunArena(args);
            return;
        }

        int[] stages = args.Length > 2 ? args[2].Split(',').Select(int.Parse).ToArray() : scenario == "raid" ? new[] { 5, 15, 30 } : new[] { 100, 200, 400, 1000 };

        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        string report = Path.Combine(Path.GetTempPath(), "aion2-stress.txt");
        using var file = new StreamWriter(report, false) { AutoFlush = true };
        void Say(string line)
        {
            Console.WriteLine(line);
            file.WriteLine(line);
        }

        var app = new System.Windows.Application();
        var settings = Ui.MeterSettings.Load();
        Ui.ThemeManager.Apply(app, settings.Theme, settings.FontSize);
        Ui.MainWindow.Headless = true;

        Say($"aion2-stress: scenario {scenario}, stages {string.Join(", ", stages)} ({(scenario == "raid" ? "minutes of a 24-player raid on one boss" : "duels between random players of a pool")})");
        Say("stage | hits | segments | MB | decode ms | decode hit/s | tick avg ms | tick p95 ms | tick max ms | events | rows | heap MB | core %");

        foreach (int stage in stages)
        {
            bool ok = RunStage(scenario, stage, Say);
            if (!ok)
            {
                Say("aion2-stress: stopped - a tick took longer than " + (AbortAtMs / 1000) + " s, the larger stages would only freeze the machine.");
                break;
            }
        }

        Say($"aion2-stress: report also in {report}");
        Environment.Exit(0);
    }

    /// <summary>arena [fights,...] [minutes]: every fight runs the whole time, all of them at once - the worst case.
    /// The stream is made a second at a time, so the test itself does not eat the memory it is measuring.</summary>
    private static void RunArena(string[] args)
    {
        int[] stages = args.Length > 2 ? args[2].Split(',').Select(int.Parse).ToArray() : new[] { 100, 200, 500, 1000 };
        int minutes = args.Length > 3 ? int.Parse(args[3]) : 15;
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        string report = Path.Combine(Path.GetTempPath(), "aion2-stress.txt");
        using var file = new StreamWriter(report, false) { AutoFlush = true };
        void Say(string line)
        {
            Console.WriteLine(line);
            file.WriteLine(line);
        }

        var app = new System.Windows.Application();
        var settings = Ui.MeterSettings.Load();
        Ui.ThemeManager.Apply(app, settings.Theme, settings.FontSize);
        Ui.MainWindow.Headless = true;
        Say($"aion2-stress: arena, {minutes} minutes, all fights at once (two players each, one hit about every 0.75 s per player), stages {string.Join(", ", stages)}");
        Say("fights | hits | segments | MB | decode ms | decode hit/s | tick avg ms | tick p95 ms | tick max ms | events | rows | heap MB | core %");
        foreach (int fights in stages)
        {
            long hits = 0, segments = 0, bytes = 0;
            bool ok = Measure(fights, ArenaChunks(fights, minutes, () => (hits, segments, bytes), (h, sg, b) => (hits, segments, bytes) = (h, sg, b)), 0, 0, 0, Say, () => (hits, segments, bytes));
            if (!ok)
            {
                Say("aion2-stress: stopped - the meter fell behind or used too much memory, the larger stages would only make it worse.");
                break;
            }
        }

        Say($"aion2-stress: report also in {report}");
        Environment.Exit(0);
    }

    private static IEnumerable<List<TcpSegment>> ArenaChunks(int fights, int minutes, Func<(long, long, long)> _, Action<long, long, long> report)
    {
        var rng = new Random(fights);
        int players = fights * 2;
        var next = new double[players];
        for (int i = 0; i < players; i++)
        {
            next[i] = rng.NextDouble();
        }

        DateTime start = DateTime.Now;
        uint sequence = 1000;
        long hits = 0, segmentCount = 0, bytes = 0;
        for (int second = 0; second < minutes * 60; second++)
        {
            var data = new List<byte>();
            for (int p = 0; p < players; p++)
            {
                while (next[p] < second + 1)
                {
                    int victim = p < fights ? p + fights : p - fights; // the pair
                    int actor = PlayerId(p), target = PlayerId(victim);
                    int skill = (11 + p % 8) * 1_000_000 + 10_340 + rng.Next(6) * 10;
                    data.AddRange(Damage(target, actor, skill, 800 + rng.Next(40_000), rng.Next(5) == 0));
                    data.AddRange(Notice(target, actor, skill));
                    hits++;
                    next[p] += 0.5 + rng.NextDouble() * 0.5;
                }
            }

            var segments = new List<TcpSegment>();
            DateTime stamp = start.AddSeconds(second);
            for (int offset = 0; offset < data.Count; offset += Mss)
            {
                byte[] payload = data.GetRange(offset, Math.Min(Mss, data.Count - offset)).ToArray();
                segments.Add(new TcpSegment(stamp, ServerEndpoint, ClientEndpoint, sequence, payload, true));
                sequence = unchecked(sequence + (uint)payload.Length);
                bytes += payload.Length;
            }

            segmentCount += segments.Count;
            report(hits, segmentCount, bytes);
            yield return segments;
        }
    }

    private static bool RunStage(string scenario, int stage, Action<string> say)
    {
        var rng = new Random(stage);
        DateTime start = DateTime.Now;
        List<(double At, byte[] Frame)> frames = scenario == "raid" ? MakeRaid(rng, stage) : MakeDuels(rng, stage);
        frames.Sort((a, b) => a.At.CompareTo(b.At));
        long hits = frames.Count(f => f.Frame.Length > 0 && f.Frame[^1] != 0xEE);

        // One TCP stream, a second of frames per chunk, each chunk cut into segments of one MSS.
        var seconds = new SortedDictionary<int, List<byte>>();
        foreach (var (at, frame) in frames)
        {
            int second = (int)at;
            if (!seconds.TryGetValue(second, out var list))
            {
                seconds[second] = list = new List<byte>();
            }

            list.AddRange(frame);
        }

        var chunks = new List<List<TcpSegment>>();
        uint sequence = 1000;
        long segmentCount = 0, bytes = 0;
        foreach (var (second, data) in seconds)
        {
            var segments = new List<TcpSegment>();
            DateTime stamp = start.AddSeconds(second);
            for (int offset = 0; offset < data.Count; offset += Mss)
            {
                byte[] payload = data.GetRange(offset, Math.Min(Mss, data.Count - offset)).ToArray();
                segments.Add(new TcpSegment(stamp, ServerEndpoint, ClientEndpoint, sequence, payload, true));
                sequence = unchecked(sequence + (uint)payload.Length);
                bytes += payload.Length;
            }

            segmentCount += segments.Count;
            chunks.Add(segments);
        }

        return Measure(stage, chunks, hits, segmentCount, bytes, say);
    }

    /// <summary>Plays the chunks (one second of stream each) through the window logic, one tick per chunk.</summary>
    private static bool Measure(int stage, IEnumerable<List<TcpSegment>> chunks, long hits, long segmentCount, long bytes, Action<string> say, Func<(long Hits, long Segments, long Bytes)>? counters = null)
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var type = typeof(Ui.MainWindow);
        var window = new Ui.MainWindow();
        var source = new Aion2PacketCombatSource(Aion2Protocol.Load());
        type.GetField("_source", flags)!.SetValue(window, source);
        type.GetField("_autoReset", flags)!.SetValue(window, false); // keep everything: the point is a long, growing session
        var tick = type.GetMethod("OnPollTimerTick", flags)!;
        var rows = (System.Collections.IList)type.GetField("_rows", flags)!.GetValue(window)!;
        var aggregator = type.GetField("_aggregator", flags)!.GetValue(window)!;
        var events = (System.Collections.IList)aggregator.GetType().GetProperty("Events")!.GetValue(aggregator)!;

        var decode = new Stopwatch();
        var ticks = new List<double>();
        bool survived = true;
        int streamSeconds = 0;
        double tickMs = 0;
        foreach (var chunk in chunks)
        {
            streamSeconds++;
            decode.Start();
            foreach (TcpSegment segment in chunk)
            {
                source.Ingest(segment);
            }

            decode.Stop();

            var watch = Stopwatch.StartNew();
            tick.Invoke(window, new object?[] { null, EventArgs.Empty });
            watch.Stop();
            ticks.Add(watch.Elapsed.TotalMilliseconds);
            tickMs += watch.Elapsed.TotalMilliseconds;
            if (watch.Elapsed.TotalMilliseconds > AbortAtMs || GC.GetTotalMemory(false) > 3L * 1024 * 1024 * 1024)
            {
                survived = false;
                break;
            }
        }

        Verify(window, type, flags, rows, say);
        if (counters is not null)
        {
            (hits, segmentCount, bytes) = counters();
        }

        // Share of one core the meter needs to keep up: work per second of game time. The decode half runs on the
        // capture worker, the tick on the UI thread; together they are what the game's machine has to spare.
        double load = 100.0 * (decode.Elapsed.TotalMilliseconds + tickMs) / Math.Max(1, streamSeconds * 1000.0);
        ticks.Sort();
        double avg = ticks.Count > 0 ? ticks.Average() : 0;
        double p95 = ticks.Count > 0 ? ticks[(int)(ticks.Count * 0.95)] : 0;
        double max = ticks.Count > 0 ? ticks[^1] : 0;
        say($"{stage,5} | {hits,7} | {segmentCount,8} | {bytes / 1048576.0,5:F1} | {decode.ElapsedMilliseconds,9} | {hits / Math.Max(0.001, decode.Elapsed.TotalSeconds),12:F0} | {avg,11:F1} | {p95,11:F1} | {max,11:F1} | {events.Count,6} | {rows.Count,4} | {GC.GetTotalMemory(true) / 1048576.0,6:F0} | {load,5:F1}"
            + (survived ? "" : "  <- ABORTED"));
        return survived;
    }

    /// <summary>The list the index produced must be the list a full rescan produces, in PvE and in PvP view.</summary>
    private static void Verify(Ui.MainWindow window, Type type, System.Reflection.BindingFlags flags, System.Collections.IList rows, Action<string> say)
    {
        var refresh = type.GetMethod("RefreshRows", flags)!;
        var pvp = type.GetField("_pvpOnly", flags)!;
        var forceFull = type.GetProperty("ForceFullRefresh", flags)!;
        List<(int, long, double?, long)> Snapshot() => rows.Cast<Ui.PlayerRow>().Select(r => (r.ObjectId, r.Damage, r.Dps, r.DamageTaken)).OrderBy(r => r.Item1).ToList();
        foreach (bool pvpView in new[] { false, true })
        {
            pvp.SetValue(window, pvpView);
            forceFull.SetValue(window, false);
            var timer = Stopwatch.StartNew();
            refresh.Invoke(window, null);
            double fastMs = timer.Elapsed.TotalMilliseconds;
            var fast = Snapshot();
            var shown = System.Windows.Data.CollectionViewSource.GetDefaultView(rows).Cast<Ui.PlayerRow>().Select(r => r.Damage).ToList();
            int unsorted = shown.Zip(shown.Skip(1)).Count(pair => pair.First < pair.Second);
            forceFull.SetValue(window, true);
            timer.Restart();
            refresh.Invoke(window, null);
            double fullMs = timer.Elapsed.TotalMilliseconds;
            var full = Snapshot();
            forceFull.SetValue(window, false);
            int different = fast.Count != full.Count ? Math.Max(fast.Count, full.Count)
                : fast.Zip(full).Count(pair => pair.First.Item1 != pair.Second.Item1 || pair.First.Item2 != pair.Second.Item2
                    || pair.First.Item4 != pair.Second.Item4
                    || (pair.First.Item3 is null) != (pair.Second.Item3 is null)
                    || (pair.First.Item3 is double a && pair.Second.Item3 is double b && Math.Abs(a - b) > 1e-6 * Math.Max(1, Math.Abs(b))));
            say($"  check {(pvpView ? "PvP" : "PvE")}: {fast.Count} rows, {different} differ between index and full rescan, {unsorted} out of order in the sorted list; one refresh {fastMs:F0} ms with index, {fullMs:F0} ms without");
        }

        pvp.SetValue(window, false);
        refresh.Invoke(window, null);
    }

    // ---- scenarios ----

    private static List<(double, byte[])> MakeDuels(Random rng, int fights)
    {
        int pool = Math.Min(400, fights * 2);
        var frames = new List<(double, byte[])>();
        double clock = 0;
        for (int i = 0; i < fights; i++)
        {
            clock += 0.3 + rng.NextDouble() * 0.4; // fights overlap: ~80 of them are running at any time
            int a = rng.Next(pool), b = rng.Next(pool);
            if (a == b)
            {
                b = (b + 1) % pool;
            }

            double length = 25 + rng.NextDouble() * 35;
            foreach ((int attacker, int victim) in new[] { (a, b), (b, a) })
            {
                for (double t = rng.NextDouble() * 0.7; t < length; t += 0.5 + rng.NextDouble() * 0.5)
                {
                    AddHit(frames, clock + t, attacker, victim, rng);
                }
            }
        }

        return frames;
    }

    private static List<(double, byte[])> MakeRaid(Random rng, int minutes)
    {
        const int boss = 9_000_001;
        var frames = new List<(double, byte[])>();
        for (int player = 0; player < 24; player++)
        {
            for (double t = rng.NextDouble(); t < minutes * 60; t += 0.25 + rng.NextDouble() * 0.2)
            {
                AddHit(frames, t, player, boss, rng, bossTarget: true);
            }
        }

        return frames;
    }

    /// <summary>One hit: the damage frame plus the companion notice the game sends next to it.</summary>
    private static void AddHit(List<(double, byte[])> frames, double at, int attacker, int victim, Random rng, bool bossTarget = false)
    {
        int actor = PlayerId(attacker);
        int target = bossTarget ? victim : PlayerId(victim);
        int prefix = 11 + attacker % 8; // the class is told by the skill id's first digits
        int skill = prefix * 1_000_000 + 10_340 + rng.Next(6) * 10;
        long amount = 800 + rng.Next(40_000);
        frames.Add((at, Damage(target, actor, skill, amount, critical: rng.Next(5) == 0)));
        frames.Add((at, Notice(target, actor, skill)));
    }

    private static int PlayerId(int index) => 1_000_000 + index;

    // ---- wire format (see Aion2FrameDecoder.DecodeVarintDamage) ----

    private static byte[] Damage(int target, int actor, int skill, long amount, bool critical)
    {
        var body = new List<byte> { 0x04, 0x38 }; // opcode 1080, big endian
        AddVarint(body, target);
        body.AddRange(new byte[] { 0x04, 0x00 }); // flags: bit 0x04 = carries a damage block
        AddVarint(body, actor);
        body.AddRange(BitConverter.GetBytes(skill));
        body.Add(1); // sequence
        body.Add((byte)(critical ? 3 : 2)); // hit type
        body.AddRange(new byte[] { 0, 0, 0 }); // the variable block
        body.AddRange(new byte[] { 1, 0, 0, 0 }); // hit count marker
        body.AddRange(new byte[] { 0x81, 0x01 }); // hit kind (two-byte varint)
        AddVarint(body, amount);
        body.AddRange(new byte[] { 0, 0 });
        return Framed(body);
    }

    private static byte[] Notice(int target, int caster, int skill)
    {
        var body = new List<byte> { 0x04, 0x38 };
        AddVarint(body, target);
        body.AddRange(new byte[] { 0x00, 0x00 }); // no damage block
        AddVarint(body, caster);
        body.AddRange(BitConverter.GetBytes(skill));
        body.AddRange(new byte[] { 0, 0, 0, 0 });
        byte[] frame = Framed(body);
        frame[^1] = 0xEE; // not a hit, only marked so the generator can count the real ones
        return frame;
    }

    /// <summary>Length varint (body + 4: the layout's bias is -4), then the body.</summary>
    private static byte[] Framed(List<byte> body)
    {
        var frame = new List<byte>();
        AddVarint(frame, body.Count + 4);
        frame.AddRange(body);
        return frame.ToArray();
    }

    private static void AddVarint(List<byte> into, long value)
    {
        ulong v = (ulong)value;
        while (v >= 0x80)
        {
            into.Add((byte)(v | 0x80));
            v >>= 7;
        }

        into.Add((byte)v);
    }
}
