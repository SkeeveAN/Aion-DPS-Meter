using System.IO;
using System.Runtime.InteropServices;
using AionDPS.Combat;
using AionDPS.Data;
using Velopack;

namespace AionDPS;

/// <summary>
/// Entry point: the meter window (no arguments), the self-check suite, and the Aion 2 tools
/// (recorder, replay, live and upload dry runs).
///
/// Everything the meter knows comes from Aion 2's network traffic, read passively through the Npcap
/// driver (see Aion2/). There is no access to the game process.
/// </summary>
internal static class Program
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [STAThread] // required for WPF (Ui/MainWindow) -- Clipboard, drag-move etc. need the STA apartment.
    private static void Main(string[] args)
    {
        // The csproj builds this as WinExe (no automatic console), specifically so the GUI doesn't
        // pop up an empty terminal window next to the meter - found by the user. The CLI modes
        // (selftest/chatlog) still need visible Console.WriteLine output when launched from an
        // existing shell, so attach to whichever console started this process, if any; a no-op
        // when there is none.
        //
        // No arguments opens the GUI, always -- per the user: the installed exe should show the
        // meter with no parameters, and the CLI is what needs one. The rule therefore does not
        // depend on how the process was started: the installer's shortcuts (Velopack creates them
        // with no arguments), a double-click, and "Aion DPS" typed in a shell all open the
        // window.
        AttachConsole(AttachParentProcess);

        // Must run before anything else, and before any window exists: this is what handles
        // Velopack's own install/update/uninstall hook arguments, which the updater passes to a
        // freshly-swapped build. Getting a meter window on screen in those runs instead of doing
        // the hook's job is exactly how a self-updating app breaks its own update.
        //
        // No manual shortcut handling here on purpose: Velopack.Windows.Shortcuts is explicitly
        // marked obsolete ("Desktop and StartMenuRoot shortcuts are now created and removed
        // automatically when your app is installed / uninstalled"), and upstream issue #67
        // ("Shortcuts not updated when --packTitle or application icon changes", fixed by PR
        // #165) covers exactly the symptom the user reported (Start Menu still showing the old
        // app icon after an in-place update) -- already handled by Velopack itself on the 1.2.0
        // this project packs with (see AionDpsMeter's release.yml). If it recurs, suspect Windows'
        // own shell icon cache (a stale bitmap cached against the unchanged .lnk file) or a
        // locally installed build old enough to predate that upstream fix, not a gap here.
        VelopackApp.Build().Run();

        if (args.Length > 0 && args[0] == "selftest")
        {
            bool ok = SelfCheck.Run();
            Console.WriteLine(ok ? "\n[selftest] ALL CHECKS PASSED" : "\n[selftest] SOME CHECKS FAILED");
            Environment.Exit(ok ? 0 : 1);
            return;
        }

        if (args.Length > 0 && args[0] == "aion2-record")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: AionDPS aion2-record <out.jsonl> [port[,port...]]");
                Console.WriteLine("  Records the raw TCP payloads exchanged with the Aion 2 game server (Npcap,");
                Console.WriteLine("  passive) into a JSON-lines file for protocol calibration - see");
                Console.WriteLine("  assets/aion2/protocol/opcodes.json. Without ports, every TCP stream is");
                Console.WriteLine("  recorded; type stop + Enter to end.");
                return;
            }

            RunAion2RecordMode(args[1], args.Length > 2 ? args[2] : null);
            return;
        }

        if (args.Length > 0 && args[0] == "aion2-ui-test")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: AionDPS aion2-ui-test <recording.jsonl> [server-port]");
                Console.WriteLine("  Runs the REAL meter window logic (never shown) on a recording: plays part of it, presses");
                Console.WriteLine("  Clear (the red X), plays on, and prints how many rows the list has at each stage.");
                return;
            }

            RunAion2UiTestMode(args[1], args.Length > 2 ? int.Parse(args[2]) : 13328);
            return;
        }

        if (args.Length > 0 && args[0] == "aion2-upload-dryrun")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: AionDPS aion2-upload-dryrun <recording.jsonl> [server-name] [server-port]");
                Console.WriteLine("  Plays a recording through the meter's own window logic (no window is shown), then builds the");
                Console.WriteLine("  upload for every boss it recognised and prints it. Nothing is sent.");
                return;
            }

            RunAion2UploadDryRun(args[1], args.Length > 2 ? args[2] : "Europe - Kaisinel", args.Length > 3 ? int.Parse(args[3]) : 13328);
            return;
        }

        if (args.Length > 0 && args[0] == "aion2-ui-live")
        {
            RunAion2UiLiveMode(args.Length > 1 ? int.Parse(args[1]) : 70);
            return;
        }

        if (args.Length > 0 && args[0] == "aion2-live")
        {
            RunAion2LiveMode(args.Length > 1 ? int.Parse(args[1]) : 20);
            return;
        }

        if (args.Length > 0 && args[0] == "aion2-replay")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: AionDPS aion2-replay <recording.jsonl> [server-port] [own-character-name]");
                Console.WriteLine("  Replays an aion2-record file through the same reassembly and decoder the live meter");
                Console.WriteLine("  uses and prints damage/heal per actor and per skill. No game, no network, no window.");
                return;
            }

            RunAion2ReplayMode(args[1], args.Length > 2 ? int.Parse(args[2]) : 13328, args.Length > 3 ? args[3] : null);
            return;
        }

        if (args.Length == 0 || args[0] == "gui")
        {
            // No App.xaml on purpose: an ApplicationDefinition item would generate its own Main
            // and collide with this one. Building System.Windows.Application by hand keeps the
            // console entry points and the GUI in the same exe without fighting over program entry.
            var app = new System.Windows.Application();
            try
            {
                // Before the first window exists: every window's brushes resolve through the
                // theme dictionary this merges in (see Ui/ThemeManager). Shared.xaml is
                // theme-independent (DynamicResource brush refs only) and merged once here so any
                // window can reference its styles (e.g. ShareBar) via StaticResource without
                // redefining them.
                app.Resources.MergedDictionaries.Add(
                    (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
                Ui.MeterSettings startupSettings = Ui.MeterSettings.Load();
                Ui.ThemeManager.Apply(app, startupSettings.Theme, startupSettings.FontSize);
                app.Run(new Ui.MainWindow());
            }
            catch (Exception ex)
            {
                // Without a console there is nowhere for an unhandled startup exception to show
                // up, so the failure looks like nothing happening at all -- put it on screen
                // instead of letting the process die silently.
                System.Windows.MessageBox.Show(ex.ToString(), "Aion DPS konnte nicht starten",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }

            return;
        }

        // Anything else is a typo far more often than it is an attempt at something real, so say
        // what the program accepts rather than failing silently or opening the window anyway.
        Console.WriteLine("Usage: AionDPS                       (no arguments: opens the meter window)");
        Console.WriteLine("       AionDPS selftest                     (runs the self-checks)");
        Console.WriteLine("       AionDPS aion2-record <out.jsonl> [ports]  (records Aion 2 game traffic for protocol calibration)");
    }

    /// <summary>
    /// Calibration recorder for the Aion 2 packet source (see Aion2/Protocol/Aion2Protocol.cs):
    /// captures the game-server TCP stream passively and writes it as JSON lines. The first real
    /// fight recorded this way is what turns opcodes.json from a template into a working layout,
    /// and then becomes a replayable SelfCheck fixture. Ports come from the argument, else from
    /// the shipped protocol file, else everything TCP is recorded.
    /// </summary>
    private static void RunAion2RecordMode(string outPath, string? portList)
    {
        var npcap = Aion2.Capture.NpcapAvailability.Detect();
        if (!npcap.IsInstalled)
        {
            Console.WriteLine($"aion2-record: Npcap is not installed - get it from {Aion2.Capture.NpcapAvailability.DownloadUrl}");
            Environment.Exit(1);
            return;
        }

        // "net:193.202.112.0/24" records every TCP port of that network instead of the game's ports.
        string? network = portList is not null && portList.StartsWith("net:", StringComparison.Ordinal) ? portList[4..] : null;
        List<int> ports = network is not null
            ? new List<int>()
            : portList is null
                ? Aion2.Protocol.Aion2Protocol.Load().ServerPorts.ToList()
                : portList.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();

        // Calibration defaults to every adapter (the game might run through a VPN adapter);
        // an explicit choice in Settings narrows it.
        string adapterId = Ui.MeterSettings.Load().CaptureAdapterId is { Length: > 0 } chosen ? chosen : Aion2.Capture.CaptureAdapters.AllAdapters;
        foreach (var adapter in Aion2.Capture.CaptureAdapters.List())
        {
            Console.WriteLine($"aion2-record: adapter {adapter.Label}{(adapter.Id == adapterId ? " <- selected" : "")}");
        }

        using var writer = new Aion2.Capture.SegmentRecording.Writer(outPath);
        using var capture = new Aion2.Capture.NpcapCaptureService(
            ports,
            writer.Write,
            (state, message) => Console.WriteLine($"aion2-record: [{state}] {message}"),
            adapterId) { Network = network };

        capture.Start();
        Console.WriteLine($"aion2-record: writing to {outPath} (filter \"{capture.Filter}\").");
        Console.WriteLine("aion2-record: to END the recording type  stop  and press Enter (closing this window also ends it).");
        Console.WriteLine("aion2-record: a plain Enter - for example one meant for the game's chat - does NOT stop it.");

        // A bare ReadLine() returned on any Enter and on end-of-input, which ended recordings by
        // accident (one stopped 13 minutes before the run it was meant for). Now only the word "stop"
        // ends it, and a closed input stream just keeps recording. A line every minute shows it lives.
        using var stopRequested = new ManualResetEventSlim();
        var reader = new Thread(() =>
        {
            while (Console.ReadLine() is { } line)
            {
                if (line.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase))
                {
                    stopRequested.Set();
                    return;
                }

                Console.WriteLine("aion2-record: still recording - type  stop  to end it.");
            }
        }) { IsBackground = true };
        reader.Start();
        while (!stopRequested.Wait(TimeSpan.FromSeconds(60)))
        {
            Console.WriteLine($"aion2-record: recording ... {writer.Count} segment(s) so far ({DateTime.Now:HH:mm:ss}).");
        }

        Console.WriteLine($"aion2-record: {writer.Count} segment(s) from {capture.Packets} packet(s) written; server endpoint {capture.ServerEndpoint ?? "not seen"}.");
    }

    /// <summary>
    /// Runs the meter's real live path - the same Aion2PacketCombatSource with the same Settings
    /// (adapter, own character) - headless for a few seconds and prints what it sees: status text,
    /// frames, events and the top players. The fastest way to tell "capture sees nothing" from
    /// "decoder sees nothing" without a window.
    /// </summary>
    private static void RunAion2LiveMode(int seconds)
    {
        var settings = Ui.MeterSettings.Load();
        var protocol = Aion2.Protocol.Aion2Protocol.Load();
        using var source = new Aion2.Aion2PacketCombatSource(protocol, settings.CaptureAdapterId, settings.Aion2CharacterName);
        source.StatusChanged += status => Console.WriteLine($"aion2-live: [{status.State}] {status.Message}");
        Console.WriteLine($"aion2-live: adapter setting \"{settings.CaptureAdapterId ?? "(automatic)"}\", calibrated={protocol.IsCalibrated}, ports {string.Join(",", protocol.ServerPorts)}");
        source.Start();
        var events = new List<Combat.DamageEvent>();
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end)
        {
            Thread.Sleep(1000);
            events.AddRange(source.Poll(false).Damage);
        }

        Console.WriteLine($"aion2-live: {events.Count} damage/heal event(s) in {seconds}s");
        foreach (var actor in events.Where(e => !e.IsHeal).GroupBy(e => e.SourceObjectId).OrderByDescending(g => g.Sum(e => e.Amount)).Take(6))
        {
            Console.WriteLine($"  {source.Entities.NameFor(actor.Key) ?? actor.Key.ToString(),-22} damage {actor.Sum(e => e.Amount),9:N0}  hits {actor.Count(),4}");
        }
    }

    /// <summary>The real window logic with the real live capture, never shown: ticks once a second
    /// like the window's timer, presses Clear a third of the way through, and prints the row list
    /// every few seconds - to tell a capture/decoder problem from a window problem on a live game.</summary>
    private static void RunAion2UiLiveMode(int seconds)
    {
        // Write through at once and end the process by hand: the capture threads keep it alive
        // otherwise, and a killed process loses whatever stdout had buffered.
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        var app = new System.Windows.Application();
        var settings = Ui.MeterSettings.Load();
        Ui.ThemeManager.Apply(app, settings.Theme, settings.FontSize);
        Ui.MainWindow.Headless = true;
        var window = new Ui.MainWindow();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var type = typeof(Ui.MainWindow);
        Console.WriteLine("aion2-ui-live: window created, starting the live source...");
        type.GetMethod("StartChatLogTailing", flags)!.Invoke(window, new object?[] { settings });
        Console.WriteLine("aion2-ui-live: source started");
        var tick = type.GetMethod("OnPollTimerTick", flags)!;
        var clear = type.GetMethod("ClearDamageData", flags)!;
        var rows = (System.Collections.IList)type.GetField("_rows", flags)!.GetValue(window)!;
        var aggregator = type.GetField("_aggregator", flags)!.GetValue(window)!;
        var events = (System.Collections.ICollection)aggregator.GetType().GetProperty("Events")!.GetValue(aggregator)!;

        int clearAt = seconds / 3;
        for (int t = 1; t <= seconds; t++)
        {
            Thread.Sleep(1000);
            if (t <= 3)
            {
                Console.WriteLine($"[{t,3}s] ticking...");
            }

            tick.Invoke(window, new object?[] { null, EventArgs.Empty });
            if (t == clearAt)
            {
                clear.Invoke(window, null);
                Console.WriteLine($"[{t,3}s] >>> Clear pressed");
            }

            if (t % 5 == 0 || t == clearAt + 1)
            {
                string top = rows.Count > 0 ? string.Join(", ", rows.Cast<Ui.PlayerRow>().Take(3).Select(r => $"{r.Name} {r.Damage:N0}")) : "-";
                Console.WriteLine($"[{t,3}s] aggregator events {events.Count,5} | rows {rows.Count,3} | {top}");
            }
        }

        Environment.Exit(0);
    }

    private static void RunAion2UploadDryRun(string path, string serverName, int serverPort)
    {
        var app = new System.Windows.Application();
        var settings = Ui.MeterSettings.Load();
        Ui.ThemeManager.Apply(app, settings.Theme, settings.FontSize);
        Ui.MainWindow.Headless = true;
        var window = new Ui.MainWindow();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var type = typeof(Ui.MainWindow);
        var source = new Aion2.Aion2PacketCombatSource(Aion2.Protocol.Aion2Protocol.Load());
        type.GetField("_source", flags)!.SetValue(window, source);
        var tick = type.GetMethod("OnPollTimerTick", flags)!;

        var segments = Aion2.Capture.SegmentRecording.Read(path)
            .Where(s => s.Source.EndsWith(":" + serverPort, StringComparison.Ordinal) || s.Destination.EndsWith(":" + serverPort, StringComparison.Ordinal))
            .Select(s => s with { FromServer = s.Source.EndsWith(":" + serverPort, StringComparison.Ordinal) })
            .ToList();
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        var entries = (System.Collections.IList)type.GetField("_mobBossEntries", flags)!.GetValue(window)!;
        var build = type.GetMethod("BuildEncounterUpload", flags, null, new[] { typeof(int), typeof(string), typeof(string) }, null)!;
        string fingerprint = "aion2:" + serverName.ToLowerInvariant().Replace(' ', '-');

        // The meter clears its rows at a later map change, and an upload is built from the rows - so
        // like a real user, build while playing (after every step) and keep the fullest result per boss.
        var best = new Dictionary<int, Upload.EncounterUploadRequest>();
        int chunk = Math.Max(1, segments.Count / 400);
        for (int i = 0; i < segments.Count; i += chunk)
        {
            foreach (var segment in segments.Skip(i).Take(chunk))
            {
                source.Ingest(segment);
            }

            tick.Invoke(window, new object?[] { null, EventArgs.Empty });
            foreach (object entry in entries.Cast<object>().ToList())
            {
                var (targetId, _) = ((int, string))entry;
                if (build.Invoke(window, new object?[] { targetId, fingerprint, serverName }) is Upload.EncounterUploadRequest request
                    && (!best.TryGetValue(targetId, out var old) || request.Participants.Sum(p => p.TotalDamage) >= old.Participants.Sum(p => p.TotalDamage)))
                {
                    best[targetId] = request;
                }
            }
        }

        Console.WriteLine($"aion2-upload-dryrun: {segments.Count} segments played; uploads that would be sent:");
        int uploads = 0;
        foreach (var (targetId, request) in best)
        {
            uploads++;
            Console.WriteLine($"  {request.BossNpcName} (entity {targetId}) NPC id {request.BossNpcId}, game {request.Game}, server {request.ServerName} [{request.ServerFingerprint}], {request.StartedAt:HH:mm:ss}-{request.EndedAt:HH:mm:ss} UTC, {request.Participants.Count} participant(s), {request.Participants.Count(p => p.IsSelf)} self");
            foreach (var p in request.Participants.OrderByDescending(x => x.TotalDamage))
            {
                Console.WriteLine($"      {p.Name,-12} {p.ClassName,-13} self={p.IsSelf,-5} damage {p.TotalDamage,9:N0} taken {p.DamageTaken,8:N0} heal {p.TotalHealing,7:N0} guild {p.Guild ?? "-"} profile {(p.Profile is null ? "-" : p.Profile.Source)}");
            }
        }

        Console.WriteLine($"aion2-upload-dryrun: {uploads} upload(s) would be sent. Nothing was sent.");
        Environment.Exit(0);
    }

    private static void RunAion2UiTestMode(string path, int serverPort)
    {
        {
            var app = new System.Windows.Application();
            var settings = Ui.MeterSettings.Load();
            Ui.ThemeManager.Apply(app, settings.Theme, settings.FontSize);
            Ui.MainWindow.Headless = true;
        var window = new Ui.MainWindow();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var type = typeof(Ui.MainWindow);

            // Not Start()ed: no capture, the recording is fed in by hand, one chunk per timer tick.
            var source = new Aion2.Aion2PacketCombatSource(Aion2.Protocol.Aion2Protocol.Load());
            type.GetField("_source", flags)!.SetValue(window, source);
            var tick = type.GetMethod("OnPollTimerTick", flags)!;
            var clear = type.GetMethod("ClearDamageData", flags)!;
            var rows = (System.Collections.IList)type.GetField("_rows", flags)!.GetValue(window)!;

            var segments = Aion2.Capture.SegmentRecording.Read(path)
                .Where(s => s.Source.EndsWith(":" + serverPort, StringComparison.Ordinal) || s.Destination.EndsWith(":" + serverPort, StringComparison.Ordinal))
                .Select(s => s with { FromServer = s.Source.EndsWith(":" + serverPort, StringComparison.Ordinal) })
                .ToList();
            int chunk = Math.Max(1, segments.Count / 60);
            int next = 0;

            string Describe() => $"{rows.Count} row(s)" + (rows.Count > 0 ? ": " + string.Join(", ", rows.Cast<Ui.PlayerRow>().Take(4).Select(r => $"{r.Name} {r.Damage:N0}")) : "");
            void Play(int ticks)
            {
                for (int i = 0; i < ticks && next < segments.Count; i++)
                {
                    foreach (var segment in segments.Skip(next).Take(chunk))
                    {
                        source.Ingest(segment);
                    }

                    next += chunk;
                    tick.Invoke(window, new object?[] { null, EventArgs.Empty });
                }
            }

            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.WriteLine($"aion2-ui-test: {segments.Count} segments fed from a second thread (like the capture), the window ticks once per 500 ms");

            // The feeder is the "capture thread": it hands segments to the source in small bursts.
            bool feederDone = false;
            var feeder = new Thread(() =>
            {
                try
                {
                    while (next < segments.Count)
                    {
                        foreach (var segment in segments.Skip(next).Take(chunk))
                        {
                            source.Ingest(segment);
                        }

                        next += chunk;
                        Thread.Sleep(250);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  !! feeder thread died: {ex.GetType().Name}: {ex.Message}");
                }

                feederDone = true;
            });
            feeder.Start();

            int ticks = 0;
            bool cleared = false;
            while (!feederDone && ticks < 400)
            {
                Thread.Sleep(500);
                tick.Invoke(window, new object?[] { null, EventArgs.Empty });
                ticks++;
                if (!cleared && next > segments.Count / 3)
                {
                    clear.Invoke(window, null);
                    cleared = true;
                    Console.WriteLine($"  [tick {ticks}] >>> Clear pressed: {Describe()}");
                }

                if (ticks % 10 == 0)
                {
                    Console.WriteLine($"  [tick {ticks}] fed {next}/{segments.Count} | {Describe()}");
                }
            }

            Console.WriteLine($"  end: {Describe()} (feeder finished: {feederDone})");
            Environment.Exit(0);
        }
    }

    private static void RunAion2ReplayMode(string path, int serverPort, string? ownName)
    {
        var protocol = Aion2.Protocol.Aion2Protocol.Load();
        using var source = new Aion2.Aion2PacketCombatSource(protocol);
        (source.Entities as Aion2.Aion2EntityDirectory)?.SetConfiguredLocalName(ownName);
        var events = new List<Combat.DamageEvent>();
        int segments = 0;
        foreach (Aion2.Capture.TcpSegment segment in Aion2.Capture.SegmentRecording.Read(path))
        {
            // The recording holds every TCP stream of the machine; only the game server's counts,
            // and the direction is decided by the port (the recorder's own flag is a guess when it
            // ran without a port filter).
            bool fromServer = segment.Source.EndsWith(":" + serverPort, StringComparison.Ordinal);
            if (!fromServer && !segment.Destination.EndsWith(":" + serverPort, StringComparison.Ordinal))
            {
                continue;
            }

            segments++;
            source.Ingest(segment with { FromServer = fromServer });
            events.AddRange(source.Poll(false).Damage);
        }

        Console.WriteLine($"aion2-replay: {segments} segment(s) on port {serverPort}, {events.Count} damage/heal event(s), calibrated={protocol.IsCalibrated}");
        // AION2_DUMP=<file>: every event as CSV (time, source id and name, target id, amount, heal, tick,
        // crit, skill) for working out how a total could be reproduced from the recording.
        if (Environment.GetEnvironmentVariable("AION2_DUMP") is { Length: > 0 } dumpPath)
        {
            using var dump = new StreamWriter(dumpPath);
            dump.WriteLine("time;source;sourceName;target;amount;heal;tick;crit;skill");
            foreach (var e in events)
            {
                dump.WriteLine(string.Join(';', e.Timestamp.ToString("O"), e.SourceObjectId, source.Entities.NameFor(e.SourceObjectId), e.TargetObjectId,
                    e.Amount, e.IsHeal ? 1 : 0, e.IsTick ? 1 : 0, e.IsCritical ? 1 : 0, e.Skill));
            }
        }

        if (events.Count == 0)
        {
            return;
        }

        TimeSpan span = events.Max(e => e.Timestamp) - events.Min(e => e.Timestamp);
        Console.WriteLine($"aion2-replay: span {span:hh\\:mm\\:ss}");
        foreach (var actor in events.Where(e => !e.IsHeal).GroupBy(e => e.SourceObjectId).OrderByDescending(g => g.Sum(e => e.Amount)).Take(12))
        {
            long total = actor.Sum(e => e.Amount);
            int crits = actor.Count(e => e.IsCritical);
            Console.WriteLine($"  {source.Entities.NameFor(actor.Key) ?? actor.Key.ToString(),-22} damage {total,9:N0}  hits {actor.Count(),5}  crit {100.0 * crits / actor.Count(),4:F0}%");
            foreach (var skill in actor.GroupBy(e => e.Skill).OrderByDescending(g => g.Sum(e => e.Amount)).Take(3))
            {
                Console.WriteLine($"      {skill.Key,-26} {skill.Sum(e => e.Amount),9:N0} in {skill.Count(),4} hit(s), avg {skill.Average(e => e.Amount):F0}");
            }
        }

        if (source.Entities is Aion2.Aion2EntityDirectory directory)
        {
            Console.WriteLine("aion2-replay: " + directory.Describe());
        }

        if ((source.Entities as Aion2.Aion2EntityDirectory)?.LocalCharacter is { } character)
        {
            Console.WriteLine($"aion2-replay: character {character.Name}, class code {character.ClassCode}, level {character.Level}, {character.Equipment.Count} equipped item(s), server id {character.ServerId} = {Aion2.Protocol.Aion2Servers.NameOf(character.ServerId)}");
            var directory2 = (Aion2.Aion2EntityDirectory)source.Entities;
            foreach (var item in directory2.LocalEquipment)
            {
                var info = Aion2.Protocol.Aion2ItemCatalog.Find(item.ItemId);
                Console.WriteLine($"    slot {item.SlotIndex,2}: {info?.Name ?? item.ItemId.ToString()}{(item.Enchant > 0 ? " +" + item.Enchant : "")} (item level {info?.ItemLevel}, grade {info?.Grade}, tier {info?.Tier})");
            }

            var skillNames = Aion2.Protocol.Aion2SkillNames.Load();
            foreach (var board in directory2.LocalDaevanion)
            {
                var sum = Aion2.Protocol.Aion2DaevanionCatalog.Summarize(board.BoardId, board.NodeIds);
                Console.WriteLine($"    daevanion {sum.Name}: {sum.ActiveNodes} nodes ({sum.KnownNodes} known) | skills {string.Join(", ", sum.SkillBonuses.Select(kv => skillNames.GetValueOrDefault(kv.Key, kv.Key.ToString()) + " +" + kv.Value))} | {string.Join(", ", sum.Stats.Select(kv => kv.Key + "+" + kv.Value))}");
            }

            Console.WriteLine($"    skills: {directory2.LocalSkills.Count}");
            foreach (var skill in directory2.LocalSkills.Where(k => k.SkillId % 10000 == 0))
            {
                Console.WriteLine($"      {skillNames.GetValueOrDefault(skill.SkillId, skill.SkillId.ToString()),-28} level {skill.Level}{(skill.Level > skill.BaseLevel ? $" ({skill.BaseLevel}+{skill.Level - skill.BaseLevel})" : "")}");
            }
        }

        int local = source.Entities.LocalPlayerId;
        if (source.Entities is Aion2.Aion2EntityDirectory bossDirectory)
        {
            if (Environment.GetEnvironmentVariable("AION2_DUMP") is { Length: > 0 } hpDumpPath)
            {
                using var hpDump = new StreamWriter(hpDumpPath + ".hp.csv");
                hpDump.WriteLine("entity;time;hp");
                foreach (var (bossEntity, _) in bossDirectory.KnownBosses())
                {
                    foreach (var sample in bossDirectory.HitPoints.SamplesAround(bossEntity, DateTime.MinValue, DateTime.MaxValue))
                    {
                        hpDump.WriteLine($"{bossEntity};{sample.At:O};{sample.Hp}");
                    }
                }
            }

            foreach (var (entityId, npcId) in bossDirectory.KnownBosses())
            {
                var info = Aion2.Protocol.Aion2BossCatalog.Find(npcId);
                int hitCount = events.Count(e => !e.IsHeal && e.TargetObjectId == entityId);
                long taken = events.Where(e => !e.IsHeal && e.TargetObjectId == entityId).Sum(e => e.Amount);
                Console.WriteLine($"aion2-replay: boss entity {entityId} = NPC {npcId} {info?.Name} ({info?.Instance}): {hitCount} hits, {taken:N0} damage taken");
                var resets = bossDirectory.HitPoints.ResetsOf(entityId);
                long? highest = bossDirectory.HitPoints.HighestSeen(entityId);
                if (highest is long full && full > 0)
                {
                    DateTime attemptStart = resets.Count > 0 ? resets[^1] : DateTime.MinValue;
                    var attemptHits = events.Where(e => !e.IsHeal && e.TargetObjectId == entityId && e.Timestamp >= attemptStart).ToList();
                    var readings = bossDirectory.HitPoints.SamplesAround(entityId, attemptStart, DateTime.MaxValue).Select(r => (r.At, r.Hp)).ToList();
                    var check = Combat.HpCheck.Evaluate(readings, attemptHits, full);
                    Console.WriteLine($"aion2-replay:   hit points: max {full:N0}, {resets.Count} reset(s), last attempt {attemptHits.Sum(e => e.Amount):N0} damage counted"
                        + (check is null ? "" : $"; HP lost {check.Lost:N0}, counted {check.Counted:N0} ({check.Ratio:P1}), killed {check.Killed}, verdict {check.Verdict}"));
                }
            }
        }

        Console.WriteLine($"aion2-replay: local player id {(local >= 0 ? local.ToString() : "unknown")} = {(local >= 0 ? source.Entities.NameFor(local) : "-")}");
        long heal = events.Where(e => e.IsHeal).Sum(e => e.Amount);
        Console.WriteLine($"aion2-replay: self-heals {heal:N0} ({events.Count(e => e.IsHeal)} event(s))");
    }
}
