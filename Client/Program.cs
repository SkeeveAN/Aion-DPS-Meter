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

    /// <summary>
    /// The meter window needs admin rights for the packet capture, and the manifest no longer
    /// demands them (see app.manifest): a plain start re-launches itself through the "runas" verb -
    /// the UAC prompt - and this instance ends. Only for a plain start: the command-line modes
    /// (selftest, recording, replays) run as they were started, and Velopack's hook runs never get
    /// here. Declining the prompt simply ends the meter, as the old manifest did.
    /// </summary>
    private static bool RelaunchElevatedIfNeeded(string[] args)
    {
        if (args.Length > 0)
        {
            return false;
        }

        using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
        {
            if (new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            {
                return false;
            }
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // UAC prompt declined: nothing to start.
        }

        return true;
    }

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

        if (RelaunchElevatedIfNeeded(args))
        {
            return;
        }

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

            // "--points": the recorder with debug points (see RunAion2RecordMode)
            string[] positional = args.Skip(1).Where(a => a != "--points").ToArray();
            RunAion2RecordMode(positional[0], positional.Length > 1 ? positional[1] : null, args.Contains("--points"));
            return;
        }

        if (args.Length > 0 && args[0] == "aion2-opcode-census")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: AionDPS aion2-opcode-census <recording.jsonl>... [--port=13328]");
                Console.WriteLine("  Replays recordings and lists the opcodes of the frames that the project neither documents nor decodes (what the recorder's debug points would beep for). No window.");
                return;
            }

            RunAion2OpcodeCensus(args.Skip(1).Where(a => !a.StartsWith("--port=", StringComparison.Ordinal)).ToArray(),
                args.Where(a => a.StartsWith("--port=", StringComparison.Ordinal)).Select(a => int.Parse(a[7..])).DefaultIfEmpty(13328).First());
            return;
        }

        if (args.Length > 0 && args[0] == "aion2-view-check")
        {
            // aion2-view-check <recording.jsonl>...: the list's views from a real recording, the meter's way against a full rescan
            ViewCheck.Run(args);
            return;
        }

        if (args.Length > 0 && args[0] == "aion2-session-sim")
        {
            // aion2-session-sim <recording.jsonl>: stay, change the map, fight, change back - what the list and the session menu show at each step
            SessionSim.Run(args);
            return;
        }

        if (args.Length > 0 && args[0] == "aion2-profile-upload")
        {
            // aion2-profile-upload <recording> <data-folder copy> <names> [--send]: puts the profile of looked-at players online after the fact
            ProfileUploadTool.Run(args);
            return;
        }

        if (args.Length > 0 && args[0] == "aion2-stress")
        {
            // aion2-stress [duel|raid] [stages, comma separated]: made-up game traffic through the real decoder and window logic
            StressTest.Run(args);
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

        if (args.Length > 0 && args[0] == "render-bossoverlay")
        {
            RunRenderBossOverlayMode(args.Length > 1 ? args[1] : "bossoverlay.png", args.Length > 2 ? int.Parse(args[2]) : 0);
            return;
        }

        if (args.Length > 1 && args[0] == "render-themeeditor")
        {
            RunRenderThemeEditorMode(args[1]);
            return;
        }

        if (args.Length > 1 && args[0] == "render-assist")
        {
            RunRenderAssistMode(args[1], args.Length > 2 ? args[2] : "");
            return;
        }

        if (args.Length > 1 && args[0] == "render-checklist")
        {
            RunRenderChecklistMode(args[1], args.Length > 2 ? args[2] : "");
            return;
        }

        if (args.Length > 1 && args[0] == "render-loot")
        {
            RunRenderLootMode(args[1], args.Length > 2 ? args[2] : "");
            return;
        }

        if (args.Length > 1 && args[0] == "render-statcalc")
        {
            RunRenderStatCalcMode(args[1], args.Length > 2 && args[2] == "expert", args.Length > 3 ? args[3] : "");
            return;
        }

        if (args.Length > 1 && args[0] == "render-attributes")
        {
            RunRenderAttributesMode(args[1], args.Length > 2 && args[2] == "stats", args.Length > 3 ? args[3] : "");
            return;
        }

        if (args.Length > 0 && args[0] == "render-network")
        {
            RunRenderNetworkMode(args.Length > 1 ? args[1] : "network.png", args.Length > 2 ? int.Parse(args[2]) : 2);
            return;
        }

        if (args.Length > 0 && args[0] == "render-combatoverlay")
        {
            RunRenderCombatOverlayMode(args.Length > 1 ? args[1] : "combatoverlay.png", args.Length > 2 ? args[2] : "live", args.Length > 3 ? int.Parse(args[3]) : 2);
            return;
        }

        if (args.Length > 0 && args[0] == "render-replay")
        {
            RunRenderReplayMode(args.Length > 1 ? args[1] : "replay.png", args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 14.0, args.Length > 3 ? int.Parse(args[3]) : 1);
            return;
        }

        if (args.Length > 0 && args[0] == "render-privacy")
        {
            Ui.PrivacyRender.Run(args.Length > 1 ? args[1] : "privacy.png", args.Length > 2 ? args[2] : "dialog");
            return;
        }

        if (args.Length > 0 && args[0] == "render-messagebox")
        {
            RunRenderMessageBoxMode(args.Length > 1 ? args[1] : "messagebox.png");
            return;
        }

        if (args.Length > 0 && args[0] == "render-settings")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: AionDPS render-settings <out.png> [Interface|Capture|Hotkeys|Timetable|Updates]");
                Console.WriteLine("  Draws the Settings window's content into a picture; no window is ever shown.");
                return;
            }

            RunRenderSettingsMode(args[1], args.Length > 2 ? args[2] : "Interface");
            return;
        }

        if (args.Length > 0 && args[0] == "render-overlay")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: AionDPS render-overlay <out.png> [compact|chips|window]");
                Console.WriteLine("  Draws the compact overlay panel, the chips overlay or the normal window with sample rows into a picture; no window is shown.");
                return;
            }

            RunRenderOverlayMode(args[1], args.Length > 2 ? args[2] : "compact");
            return;
        }

        if (args.Length > 0 && args[0] == "render-character")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: AionDPS render-character <out.png> [character|equipment|arcana|skills|board|species]");
                Console.WriteLine("  Draws the saved character's profile window into a picture; no window is ever shown.");
                return;
            }

            RunRenderCharacterMode(args[1], args.Length > 2 ? args[2] : "equipment");
            return;
        }

        if (args.Length > 0 && args[0] == "render-sessions")
        {
            RunRenderSessionsMode(args.Length > 1 ? args[1] : "sessions.png", args.Length > 2 ? int.Parse(args[2]) : 1, args.Length > 3 ? args[3] : null);
            return;
        }

        if (args.Length > 0 && args[0] == "render-details")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: AionDPS render-details <out.png> [skills|rotation|buffs]");
                Console.WriteLine("  Draws a player's details window with sample hits into a picture; no window is shown.");
                return;
            }

            RunRenderDetailsMode(args[1], args.Length > 2 ? args[2] : "skills");
            return;
        }

        if (args.Length > 0 && args[0] == "aion2-upload-dryrun")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: AionDPS aion2-upload-dryrun <recording.jsonl> [server-name] [server-port] [--send Boss@HH:mm ...]");
                Console.WriteLine("  Plays a recording through the meter's own window logic (no window is shown), then builds the");
                Console.WriteLine("  upload for every boss it recognised and prints it. Nothing is sent - unless --send names bosses");
                Console.WriteLine("  by name and UTC start minute (Thamon@15:14): exactly those fights are uploaded, to catch up on");
                Console.WriteLine("  runs that never went online.");
                return;
            }

            int sendAt = Array.IndexOf(args, "--send");
            string[] positional = sendAt >= 0 ? args[..sendAt] : args;
            RunAion2UploadDryRun(args[1], positional.Length > 2 ? positional[2] : "Europe - Kaisinel", positional.Length > 3 ? int.Parse(positional[3]) : 13328,
                sendAt >= 0 ? args[(sendAt + 1)..] : Array.Empty<string>());
            return;
        }

        if (args.Length > 1 && args[0] == "aion2-preset-check")
        {
            RunAion2PresetCheck(args[1], args.Length > 2 ? int.Parse(args[2]) : 13328);
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

        if (args.Length > 0 && args[0] == "aion2-skill-averages")
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: AionDPS aion2-skill-averages <out.json> <recording.jsonl> [more recordings...] [--port N]");
                Console.WriteLine("  Replays the recordings and writes the average hit of every skill (players' hits on monsters) to the table");
                Console.WriteLine("  the world boss estimate reads: assets/aion2/protocol/skill_hit_average.json. No game, no network, no window.");
                return;
            }

            RunAion2SkillAverages(args);
            return;
        }

        if (args.Length > 0 && args[0] == "aion2-buff-selftest")
        {
            // only the buff-upload check, no window (the full selftest builds overlays)
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            bool buffOk = Combat.SelfCheckBuffUpload.Run();
            Console.WriteLine(buffOk ? "[selftest] BUFF CHECKS PASSED" : "[selftest] BUFF CHECKS FAILED");
            Environment.Exit(buffOk ? 0 : 1);
            return;
        }

        if (args.Length > 2 && args[0] == "aion2-buff-dump")
        {
            Combat.BuffDump.Run(args[1], args[2], 13328, args.Length > 3 ? args[3] : null);
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

        if (args.Length > 0 && args[0] == "aion2-frame-dump")
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: AionDPS aion2-frame-dump <recording.jsonl> <opcode hex, e.g. 058a> [max frames] [--port=13328]");
                Console.WriteLine("  Prints the frames of one opcode of a recording as hex (opcode as the decoder reads it). No window.");
                return;
            }

            int max = args.Skip(3).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).Select(int.Parse).DefaultIfEmpty(5).First();
            int port = args.Where(a => a.StartsWith("--port=", StringComparison.Ordinal)).Select(a => int.Parse(a[7..])).DefaultIfEmpty(13328).First();
            RunAion2FrameDump(args[1], Convert.ToInt32(args[2], 16), max, port);
            return;
        }

        if (args.Length > 0 && args[0] == "aion2-position-check")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: AionDPS aion2-position-check <recording.jsonl>... [--port=13328]");
                Console.WriteLine("  Replays recordings and, at every exact own position, compares the previous exact place and the middle of the entities around with it. No window.");
                return;
            }

            RunAion2PositionCheckMode(args.Skip(1).Where(a => !a.StartsWith("--port=", StringComparison.Ordinal)).ToArray(),
                args.Where(a => a.StartsWith("--port=", StringComparison.Ordinal)).Select(a => int.Parse(a[7..])).DefaultIfEmpty(13328).First());
            return;
        }

        if (args.Length > 0 && args[0] == "aion2-stock-check")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: AionDPS aion2-stock-check <recording.jsonl> [server-port]");
                Console.WriteLine("  Replays a recording through the decoder and prints what the crafting tool would count: inventory, character storage, account storage. No window.");
                return;
            }

            RunAion2StockCheckMode(args[1], args.Length > 2 ? int.Parse(args[2]) : 13328);
            return;
        }

        if (args.Length > 0 && args[0] == "render-craft")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: AionDPS render-craft <out.png> [recording.jsonl] [language]");
                Console.WriteLine("  Draws the crafting tool (page, overlay, configuration) with a made-up plan and the stock of a recording into one picture. No window is shown; set AIONDPS_DATA_DIR to a throw-away folder.");
                return;
            }

            RunRenderCraftMode(args[1], args.Length > 2 ? args[2] : "", args.Length > 3 ? args[3] : "");
            return;
        }

        if (args.Length > 0 && args[0] == "settings-dirty-check")
        {
            RunSettingsDirtyCheckMode();
            return;
        }

        if (args.Length > 0 && args[0] == "render-petmap")
        {
            RunRenderPetMapMode(args.Length > 1 ? args[1] : "petmap.png", args.Length > 2 ? args[2] : "verteron", args.Length > 4 ? double.Parse(args[3]) : -215789, args.Length > 4 ? double.Parse(args[4]) : 262953, args.Length > 5 && args[5] == "guards");
            return;
        }

        if (args.Length > 0 && args[0] == "render-petfarm")
        {
            RunRenderPetFarmMode(args.Length > 1 ? args[1] : "petfarm.png");
            return;
        }

        if (args.Length > 0 && args[0] == "render-petlist-shop")
        {
            RunRenderPetListShopMode(args.Length > 1 ? args[1] : "petlist-shop.png");
            return;
        }

        if (args.Length > 0 && args[0] == "petfarm-read")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: AionDPS petfarm-read <screenshot.png> [language]");
                Console.WriteLine("  Finds the red target plate in a picture, reads its name and says which pet it leads to. No window, nothing saved.");
                return;
            }

            RunPetFarmReadMode(args[1], args.Length > 2 ? args[2] : "de");
            return;
        }

        if (args.Length == 0 || args[0] == "gui")
        {
            // No App.xaml on purpose: an ApplicationDefinition item would generate its own Main
            // and collide with this one. Building System.Windows.Application by hand keeps the
            // console entry points and the GUI in the same exe without fighting over program entry.
            var app = new System.Windows.Application();
            CrashLog.Hook(app); // crash.log in the data folder: what an unhandled error was, for the next report
            // A screen reader, an overlay or a capture tool that reads the window through UI Automation
            // can ask a list item for its name just after the row behind it was replaced; WPF's own
            // ItemAutomationPeer.GetNameCore then throws a NullReferenceException from inside layout
            // (seen with v0.10.5: the meter closed with "konnte nicht starten"). It only concerns what
            // the accessibility tree reports, so it is skipped instead of ending the meter.
            app.DispatcherUnhandledException += (_, e) =>
            {
                if (e.Exception is NullReferenceException && e.Exception.StackTrace?.Contains("AutomationPeer", StringComparison.Ordinal) == true)
                {
                    e.Handled = true;
                }
            };
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
                Ui.AudioOutput.DeviceId = startupSettings.AudioDeviceId;
                Ui.ThemeManager.Apply(app, startupSettings);
                app.Run(new Ui.MainWindow());
            }
            catch (Exception ex)
            {
                // Without a console there is nowhere for an unhandled startup exception to show
                // up, so the failure looks like nothing happening at all -- put it on screen
                // instead of letting the process die silently.
                CrashLog.Write("the meter could not start", ex);
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
    private static void RunAion2RecordMode(string outPath, string? portList, bool points = false)
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

        Aion2.Capture.SegmentRecording.Writer writerOrNull;
        try
        {
            writerOrNull = new Aion2.Capture.SegmentRecording.Writer(outPath);
        }
        catch (IOException ex)
        {
            // Usually a second recorder window (or one started in the same minute) already holds the file.
            Console.WriteLine($"aion2-record: cannot open {outPath}: {ex.Message}");
            Console.WriteLine("aion2-record: another recording is probably still running - end it first (type  stop  in its window) or wait a minute.");
            Environment.Exit(1);
            return;
        }

        using var writer = writerOrNull;
        // With debug points the frames are decoded while they are recorded: one of an opcode nobody has described gives a tone and a line in <recording>.points.jsonl
        Aion2.Capture.DebugPointLog? pointLog = points ? new Aion2.Capture.DebugPointLog(outPath) : null;
        Aion2.Aion2PacketCombatSource? watchSource = points ? new Aion2.Aion2PacketCombatSource(Aion2.Protocol.Aion2Protocol.Load()) : null;
        int gamePort = ports.Count > 0 ? ports[0] : 13328;
        DateTime lastBeep = DateTime.MinValue;
        DateTime lastPoint = DateTime.MinValue;
        DateTime recordingStart = DateTime.UtcNow;
        // the login sends a burst of frames nobody described: they are logged but give no tone for the first minute
        TimeSpan loginBurst = TimeSpan.FromSeconds(60);
        if (points)
        {
            var watch = new Aion2.Capture.UnknownFrameWatch(Aion2.Capture.UnknownFrameWatch.LoadKnown());
            Aion2.Protocol.Aion2FrameDecoder.FrameTap = (opcode, frame, at) =>
            {
                if (watch.Note(opcode, frame, at) is { } point)
                {
                    pointLog!.Point(point);
                    lastPoint = point.At;
                    bool loginPhase = DateTime.UtcNow - recordingStart < loginBurst;
                    Console.WriteLine($"aion2-record: DEBUG POINT {point.At:HH:mm:ss}  unknown frame 0x{point.Opcode:x4} ({point.Length} bytes, seen {point.Seen}x){(loginPhase ? " (first minute: no tone)" : " - tell me what you just did")}");
                    if (!loginPhase && DateTime.UtcNow - lastBeep > TimeSpan.FromSeconds(2))
                    {
                        lastBeep = DateTime.UtcNow;
                        ThreadPool.QueueUserWorkItem(_ => { try { Console.Beep(1100, 160); Console.Beep(1500, 220); } catch (Exception) { /* no sound device: the line above still says it */ } });
                    }
                }
            };
        }

        using var capture = new Aion2.Capture.NpcapCaptureService(
            ports,
            segment =>
            {
                writer.Write(segment);
                if (watchSource is not null)
                {
                    bool fromServer = segment.Source.EndsWith(":" + gamePort, StringComparison.Ordinal);
                    watchSource.Ingest(segment with { FromServer = fromServer });
                }
            },
            (state, message) => Console.WriteLine($"aion2-record: [{state}] {message}"),
            adapterId) { Network = network };

        capture.Start();
        Console.WriteLine($"aion2-record: writing to {outPath} (filter \"{capture.Filter}\").");
        if (points)
        {
            Console.WriteLine($"aion2-record: DEBUG POINTS on - a frame nobody has described gives two tones and a line in {pointLog!.Path}. Then tell me (in chat) what you did; the time is on the line.");
            Console.WriteLine("aion2-record: you may also type a short note here and press Enter, it is saved with its time next to the point (alt-tab away from the game for it).");
        }

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

                if (pointLog is not null && line.Trim().Length > 0)
                {
                    pointLog.Note(DateTime.Now, line.Trim());
                    Console.WriteLine($"aion2-record: note saved ({(DateTime.Now - lastPoint).TotalSeconds:0} s after the last debug point).");
                    continue;
                }

                Console.WriteLine("aion2-record: still recording - type  stop  to end it.");
            }
        }) { IsBackground = true };
        reader.Start();
        while (!stopRequested.Wait(watchSource is null ? TimeSpan.FromSeconds(60) : TimeSpan.FromMilliseconds(250)))
        {
            // the frames are decoded as they come: the poll drives the decoder (the damage events are not used here)
            watchSource?.Poll(false);
            if (DateTime.UtcNow.Second < 1 && (DateTime.UtcNow - lastBeep).TotalSeconds > 5)
            {
                Console.WriteLine($"aion2-record: recording ... {writer.Count} segment(s), {pointLog?.Count ?? 0} debug point(s) so far ({DateTime.Now:HH:mm:ss}).");
                lastBeep = DateTime.UtcNow;
            }
        }

        Aion2.Protocol.Aion2FrameDecoder.FrameTap = null;
        pointLog?.Dispose();
        watchSource?.Dispose();

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
        using var source = new Aion2.Aion2PacketCombatSource(protocol, settings.CaptureAdapterId);
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

    /// <summary>Renders a player's details window with generated hits to a PNG, never showing a window.</summary>
    private static void RunRenderCharacterMode(string path, string tab)
    {
        var app = new System.Windows.Application();
        var settings = Ui.MeterSettings.Load();
        if (Environment.GetEnvironmentVariable("AIONDPS_LANG") is { Length: > 0 } picked)
        {
            Ui.LocalizationManager.Instance.Language = picked;
        }
        else if (settings.Language.Length > 0)
        {
            Ui.LocalizationManager.Instance.Language = settings.Language; // skill and item names in the chosen language
        }

        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        if (Aion2.Aion2CharacterStore.Load(Aion2.Aion2CharacterStore.DefaultPath) is not { } saved)
        {
            Console.WriteLine("render-character: no saved character");
            Environment.Exit(1);
            return;
        }

        var directory = new Aion2.Aion2EntityDirectory();
        directory.RestoreFrom(saved);
        var window = new Ui.CharacterWindow(directory);
        window.ShowTab(tab);
        // AIONDPS_OTHER = "name|class code|level|item level|server id|legion" shows another character of the account instead; AIONDPS_OTHER_JSON is the file with
        // the server's answer for it (GET /api/players/:id), none = the character the server does not know.
        if (Environment.GetEnvironmentVariable("AIONDPS_OTHER") is { Length: > 0 } other)
        {
            string[] f = other.Split('|');
            var listed = new Aion2.AccountCharacter(f[0], int.Parse(f[1]), 1, int.Parse(f[2]), int.Parse(f[3]), f.Length > 5 && f[5].Length > 0 ? f[5] : null, 0, 0, int.Parse(f[4]), 0);
            Aion2.Aion2Tickets.NoteListedCharacters(null, new[] { listed });
            Aion2.Aion2Tickets.NoteListedCharacters(null, new[] { new Aion2.AccountCharacter(saved.Name, saved.ClassCode, 1, saved.Level, 0, null, 0, 0, saved.ServerId, 0) });
            Upload.RemoteCharacter? remote = null;
            if (Environment.GetEnvironmentVariable("AIONDPS_OTHER_JSON") is { Length: > 0 } json && File.Exists(json))
            {
                using System.Text.Json.JsonDocument answer = System.Text.Json.JsonDocument.Parse(File.ReadAllText(json));
                remote = Upload.RemoteCharacterClient.Parse(answer.RootElement);
            }

            window.ShowOtherForPicture(f[0], remote);
        }

        const double width = 1180, height = 1000;
        // a Window cannot be the child of a border: its content (the page inside the window's chrome) is drawn instead
        System.Windows.UIElement? inner = window.Content as System.Windows.UIElement;
        window.Content = null;
        var content = new System.Windows.Controls.Border { Child = inner };
        content.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0E, 0x18, 0x20));
        content.Measure(new System.Windows.Size(width, height));
        content.Arrange(new System.Windows.Rect(0, 0, width, height));
        content.UpdateLayout();
        content.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)width, (int)height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var backdrop = new System.Windows.Controls.Border { Width = width, Height = height, Background = (System.Windows.Media.Brush)app.FindResource("Brush.Window") };
        backdrop.Measure(new System.Windows.Size(width, height));
        backdrop.Arrange(new System.Windows.Rect(0, 0, width, height));
        bitmap.Render(backdrop);
        bitmap.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        Console.WriteLine("render-character: wrote " + path);
        Environment.Exit(0);
    }

    /// <summary>Draws the session manager with a day of invented stays into a picture; no window is shown.</summary>
    private static void RunRenderSessionsMode(string path, int args_selected = 1, string? analyzerTab = null)
    {
        var app = new System.Windows.Application();
        var settings = Ui.MeterSettings.Load();
        if (settings.Language.Length > 0)
        {
            Ui.LocalizationManager.Instance.Language = settings.Language;
        }

        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));

        var recorder = new History.SessionRecorder(zone => Aion2.Protocol.Aion2Zones.IsInstance(zone) ? History.SessionKind.Instance : History.SessionKind.Map);
        var names = new Dictionary<int, (string Name, string Class)> { [1] = ("Skeeve", "Sorcerer"), [2] = ("Aahz", "Gladiator"), [3] = ("Chumley", "Cleric"), [4] = ("Nunzio", "Assassin") };
        recorder.Identify = id => names.TryGetValue(id, out var n) ? (n.Name, n.Class) : (id == 100 ? "Ruthilis of Pain" : null, null);
        var random = new Random(3);
        DateTime day = DateTime.Today.AddHours(7);
        (int Zone, int Minutes, int Hits)[] stays = { (1010, 120, 300), (600001, 25, 900), (20, 150, 500), (1010, 8, 0), (600092, 31, 1100), (1010, 40, 120) };
        DateTime at = day;
        int stayNo = 0;
        foreach ((int zone, int minutes, int hits) in stays)
        {
            recorder.Enter(new Combat.Sources.ZoneChange(at, zone));
            var batch = new List<Combat.DamageEvent>();
            for (int i = 0; i < hits; i++)
            {
                batch.Add(new Combat.DamageEvent(at.AddSeconds(minutes * 60.0 * i / Math.Max(1, hits)), random.Next(1, 5), 100, random.Next(800, 9000) / (random.Next(1, 5)), false, "Hit"));
            }

            // The instances have a boss: it hits the group every two seconds and goes into a rage 55 % of the way in.
            if (zone is 600001 or 600092)
            {
                for (int i = 0; i < minutes * 30; i++)
                {
                    bool raging = i > minutes * 30 * 0.55;
                    batch.Add(new Combat.DamageEvent(at.AddSeconds(i * 2), 100, random.Next(1, 5), (raging ? 3600 : 1200) + random.Next(-150, 150), false, "Smash"));
                }
            }

            batch = batch.OrderBy(e => e.Timestamp).ToList();
            recorder.Feed(Combat.Sources.CombatBatch.DamageOnly(batch));
            if (zone is 600001 or 600092)
            {
                // What the server said the boss lost: the second instance's totals are twice what it really lost.
                long total = batch.Where(e => e.TargetObjectId == 100).Sum(e => e.Amount);
                bool honest = zone == 600001;
                long hp = total + 4000, max = hp;
                var samples = new List<Aion2.HpSample> { new(at.AddSeconds(-1), hp) };
                foreach (Combat.DamageEvent e in batch.Where(e => e.TargetObjectId == 100))
                {
                    hp = Math.Max(0, hp - (honest ? e.Amount : e.Amount / 2));
                    samples.Add(new Aion2.HpSample(e.Timestamp, hp));
                }

                recorder.Sessions[^1].Hp[100] = new History.TargetHp(max, samples, Array.Empty<DateTime>());
            }

            stayNo++;
            at = at.AddMinutes(minutes);
        }

        recorder.Sessions[1].IsSaved = true;
        var window = new Ui.SessionManagerWindow(recorder);
        window.AnalyzerFactory = (session, row, mode, boss) => analyzerTab is not null ? CreateSampleDetails(analyzerTab, row.Name, row.ClassName) : new Ui.PlayerDetailsView(row.Name, row.ClassName, "",
            session.Data.Events.Where(ev => ev.SourceObjectId == row.Id).ToList(), id => session.Names.GetValueOrDefault(id),
            fightEvents: session.Data.Events.Where(ev => !ev.IsHeal).ToList());
        const double width = 1760, height = 820;
        var content = (System.Windows.UIElement)window.Content;
        content.Measure(new System.Windows.Size(width, height));
        content.Arrange(new System.Windows.Rect(0, 0, width, height));
        content.UpdateLayout();
        if (window.FindName("SessionList") is System.Windows.Controls.ListBox list && list.Items.Count > 1)
        {
            list.SelectedIndex = args_selected;
        }

        if (analyzerTab is not null && window.FindName("PlayersGrid") is System.Windows.Controls.DataGrid players && players.Items.Count > 1)
        {
            players.SelectedIndex = 1; // the Sorcerer: the sample skills are a Sorcerer's
        }

        content.UpdateLayout();
        content.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)width, (int)height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var back = new System.Windows.Shapes.Rectangle { Width = width, Height = height, Fill = (System.Windows.Media.Brush)app.Resources["Brush.Window"] };
        back.Measure(new System.Windows.Size(width, height));
        back.Arrange(new System.Windows.Rect(0, 0, width, height));
        bitmap.Render(back);
        bitmap.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        Console.WriteLine("render-sessions: wrote " + path);
        Environment.Exit(0);
    }

    /// <summary>A player's details with made-up fights (five real Sorcerer skills with their icons), for the pictures of the details window and of the session manager.</summary>
    private static Ui.PlayerDetailsView CreateSampleDetails(string tab, string name, string className)
    {
        var random = new Random(7);
        var events = new List<Combat.DamageEvent>();
        DateTime start = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Local);
        // five real Sorcerer skills (with icons) so the rotation tab shows what a player really sees
        var table = Aion2.Protocol.Aion2SkillNames.Load();
        var sorcerer = table.Where(kv => Aion2.Protocol.Aion2SkillNames.ClassOf(kv.Key) == "Sorcerer" && !Aion2.Protocol.Aion2Artwork.IsPassive(kv.Key) && !Aion2.Protocol.Aion2SkillNames.IsNonDamageEffect(kv.Key)
                && Aion2.Protocol.Aion2SkillIcons.PathFor(kv.Key) is not null && kv.Value is not ("Dodge" or "Basic Attack"))
            .GroupBy(kv => kv.Value).Select(g => g.First()).Take(5).ToList();
        (string Skill, int Id, int Weight)[] skills = sorcerer.Count >= 5
            ? sorcerer.Select((kv, i) => (kv.Value, kv.Key, new[] { 5, 2, 9, 9, 20 }[i])).ToArray()
            : new[] { ("Blaze", 11000000, 5), ("Hellfire", 11100000, 2), ("Cold Wave", 12000000, 9), ("Ice Chain", 13000000, 9), ("Bittercold Wind", 15280000, 20) };
        for (int t = 0; t < 75; t++)
        {
            double burst = t is > 28 and < 36 ? 3.5 : 1.0;
            for (int k = 0; k < (int)(2 * burst); k++)
            {
                var skill = skills[random.Next(skills.Length)];
                int target = random.Next(5) == 0 ? 2 : 1;
                {
                    double roll = random.NextDouble();
                    var kinds = (roll < 0.18 ? Combat.HitKinds.Back : Combat.HitKinds.Front) | (random.NextDouble() < 0.12 ? Combat.HitKinds.Perfect : 0) | (random.NextDouble() < 0.04 ? Combat.HitKinds.Double : 0);
                    events.Add(new Combat.DamageEvent(start.AddSeconds(t + random.NextDouble()), 5, target, random.Next(300, 3000) * (skill.Weight > 8 ? 1 : 3), false, skill.Skill, random.Next(5) == 0, SkillId: skill.Id,
                        Kinds: kinds, HitCount: random.NextDouble() < 0.1 ? 2 : 1));
                }
            }
        }

        var all = events.Concat(events.Select(e => e with { SourceObjectId = 6, Amount = e.Amount / 2 })).ToList();
        // made-up extras for the rotation and effect tabs: a use before every hit, support skills now and then, three effects and two staggers
        var support = Aion2.Protocol.Aion2SkillNames.Load().Where(kv => kv.Value.Contains("Armor") || kv.Value.Contains("Shield") || kv.Value.Contains("Boost") || kv.Value.Contains("Stance")).Take(3).Select(kv => kv.Key).ToList();
        var uses = events.Select(e => new Combat.SkillUse(e.Timestamp.AddSeconds(-0.12), 5, e.SkillId)).ToList();
        for (int t = 0; t < 75; t += 18)
        {
            for (int i = 0; i < support.Count; i++)
            {
                uses.Add(new Combat.SkillUse(start.AddSeconds(t + i * 0.2), 5, support[i]));
            }
        }

        var effects = new List<Combat.BuffEvent>();
        for (int i = 0; i < support.Count; i++)
        {
            effects.Add(new Combat.BuffEvent(start.AddSeconds(i * 0.2), Combat.BuffKind.On, 5, 10 + i, support[i], i == 0 ? 6 : 5, i == 2 ? 12000 : -1));
        }

        var extras = new Combat.FightExtras(uses.OrderBy(u => u.At).ToList(), effects,
            new[] { new Combat.StaggerEvent(start.AddSeconds(21), 1, 0, 0, true), new Combat.StaggerEvent(start.AddSeconds(52), 1, 0, 0, true) },
            new Dictionary<int, string> { [5] = name, [6] = "Goku" });
        var window = new Ui.PlayerDetailsView(name, className, "", events, id => id == 1 ? "Training Scarecrow" : "Elite Guard", fightEvents: all,
            extras: extras, playerId: 5, fightStart: start, fightEnd: start.AddSeconds(75));
        window.TabRotationButton.IsChecked = tab == "rotation";
        window.TabBuffsButton.IsChecked = tab == "buffs";
        return window;
    }

    private static void RunRenderDetailsMode(string path, string tab)
    {
        Environment.SetEnvironmentVariable("AIONDPS_DATA_DIR", Path.Combine(Path.GetTempPath(), "aiondps-render-details"));
        var app = new System.Windows.Application();
        var settings = Ui.MeterSettings.Load();
        if (Environment.GetEnvironmentVariable("AIONDPS_LANG") is { Length: > 0 } picked)
        {
            Ui.LocalizationManager.Instance.Language = picked;
        }
        else if (settings.Language.Length > 0)
        {
            Ui.LocalizationManager.Instance.Language = settings.Language; // skill and item names in the chosen language
        }

        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var window = CreateSampleDetails(tab, "Ichigo", "Sorcerer");
        const double width = 860, height = 680;
        var content = new System.Windows.Controls.Border { Child = window, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0E, 0x18, 0x20)) };
        content.Measure(new System.Windows.Size(width, height));
        content.Arrange(new System.Windows.Rect(0, 0, width, height));
        content.UpdateLayout();
        content.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)width, (int)height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        Console.WriteLine("render-details: wrote " + path);
        Environment.Exit(0);
    }

    /// <summary>Renders the compact overlay panel with a few sample rows to a PNG, never showing a window.</summary>
    /// <summary>The open list of the target dropdown drawn under its closed box (a real popup would be a window of its own, and none is shown here).</summary>
    private static void DrawOpenTargetList(Ui.MainWindow window, System.Windows.FrameworkElement panel, System.Windows.Media.Imaging.RenderTargetBitmap bitmap)
    {
        var combo = window.MobBossFilter;
        foreach (string name in new[] { "Talisra of the Void", "Siliator of Deceit", "Ruthilis of Pain", "Transcendent Bakarma, Keeper of the Lost Gate" })
        {
            combo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = name });
        }

        combo.ApplyTemplate();
        System.Windows.Controls.Primitives.Popup? popup = null;
        var queue = new Queue<System.Windows.DependencyObject>();
        queue.Enqueue(combo);
        while (queue.Count > 0 && popup is null)
        {
            var node = queue.Dequeue();
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i);
                if (child is System.Windows.Controls.Primitives.Popup found)
                {
                    popup = found;
                    break;
                }

                queue.Enqueue(child);
            }
        }

        if (popup?.Child is not System.Windows.FrameworkElement list)
        {
            Console.WriteLine("render-overlay: the dropdown has no popup to draw");
            return;
        }

        for (int pass = 0; pass < 4; pass++)
        {
            list.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            list.Arrange(new System.Windows.Rect(list.DesiredSize));
            list.UpdateLayout();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        var at = combo.TranslatePoint(new System.Windows.Point(0, combo.ActualHeight), panel);
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new System.Windows.Media.VisualBrush(list), null, new System.Windows.Rect(at.X, at.Y, list.ActualWidth, list.ActualHeight));
        }

        bitmap.Render(visual);
        Console.WriteLine($"render-overlay: the open list is {list.ActualWidth:0} px wide, the closed box {combo.ActualWidth:0} px");
    }

    private static void RunRenderOverlayMode(string path, string kind)
    {
        // "chips-all" etc.: the same look in the All in One mode.
        bool allCompact = kind.EndsWith("-allc", StringComparison.Ordinal);
        bool all = kind.EndsWith("-all", StringComparison.Ordinal) || allCompact;
        kind = allCompact ? kind[..^5] : all ? kind[..^4] : kind;
        var app = new System.Windows.Application();
        var settings = Ui.MeterSettings.Load();
        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var window = new Ui.MainWindow();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
        var type = typeof(Ui.MainWindow);
        var rows = (System.Collections.ObjectModel.ObservableCollection<Ui.PlayerRow>)type.GetField("_rows", flags)!.GetValue(window)!;
        (string Name, string Cls, long Dmg, double Dps)[] sample =
        {
            ("Jinwoo", "Gladiator", 916_700, 8300), ("Goku", "Ranger", 790_800, 7100), ("Ichigo", "Sorcerer", 757_700, 6800),
            ("Naruto", "Cleric", 434_200, 3900), ("Tsubasa", "Spiritmaster", 232_800, 2100),
        };
        long total = sample.Sum(x => x.Dmg);
        int rank = 0;
        foreach (var x in sample)
        {
            var row = new Ui.PlayerRow(++rank) { Name = x.Name, ClassName = x.Cls, Damage = x.Dmg, Dps = x.Dps, SharePercent = 100.0 * x.Dmg / total, FillPercent = 100.0 * x.Dmg / sample[0].Dmg,
                Rank = rank, Faction = rank is 1 or 5 ? "Elyos" : rank is 3 ? "" : "Asmodian" }; // a sample of both factions and one unknown
            if (all)
            {
                row.AllMode = !allCompact;
                row.AllCompact = allCompact;
                row.Healing = x.Cls == "Cleric" ? 384_500 : rank * 3_100;
                row.DamageTaken = 100_000 / rank;
                row.Absorbed = rank switch { 1 => 41_800, 3 => 12_400, 4 => 18_900, _ => 0 }; // a group shield on three of them
            }

            rows.Add(row);
        }

        window.OverlayTargetText.Text = "Transcendent Bakarma";
        window.OverlayTimeText.Text = "1:51";
        window.OverlayModeText.Text = allCompact ? "ALL+" : all ? "ALL" : "DMG";
        if (all && !allCompact)
        {
            window.OverlayRateHeader.Text = "Damage";
            window.OverlayShareHeader.Text = "Heal";
            window.OverlayTotalHeader.Text = "Taken";
        }

        System.Windows.FrameworkElement panel;
        if (kind == "chips")
        {
            panel = window.OverlayContent;
            panel.Visibility = System.Windows.Visibility.Visible;
            panel.Width = 390;
        }
        else if (kind is "window" or "window-open")
        {
            // The normal window as it is without Hide UI: its whole content at a typical size.
            window.OverlayTargetText.Text = "Transcendent Bakarma";
            panel = (System.Windows.FrameworkElement)window.Content;
            panel.Measure(new System.Windows.Size(520, 420));
            panel.Arrange(new System.Windows.Rect(0, 0, 520, 420));
            panel.UpdateLayout();
            for (int pass = 0; pass < 3; pass++)
            {
                // the grid builds its rows and columns on the dispatcher; let it, then lay out again
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                panel.Measure(new System.Windows.Size(520, 420));
                panel.Arrange(new System.Windows.Rect(0, 0, 520, 420));
                panel.UpdateLayout();
            }
        }
        else
        {
            window.CompactOverlayPanel.Visibility = System.Windows.Visibility.Visible;
            panel = window.CompactOverlayPanel;
        }

        if (kind is not ("window" or "window-open"))
        {
            panel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            panel.Arrange(new System.Windows.Rect(panel.DesiredSize));
        }

        panel.UpdateLayout();
        int w = kind is "window" or "window-open" ? 520 : (int)Math.Ceiling(panel.DesiredSize.Width), h = kind is "window" or "window-open" ? 420 : (int)Math.Ceiling(panel.DesiredSize.Height); // the window is drawn at the size it was arranged at
        var backdrop = new System.Windows.Controls.Border { Width = w, Height = h, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x30, 0x40, 0x30)) };
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        backdrop.Measure(new System.Windows.Size(w, h));
        backdrop.Arrange(new System.Windows.Rect(0, 0, w, h));
        bitmap.Render(backdrop);
        bitmap.Render(panel);
        if (kind == "window-open")
        {
            DrawOpenTargetList(window, panel, bitmap);
        }

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        Console.WriteLine("render-overlay: wrote " + path);
        Environment.Exit(0);
    }

    /// <summary>Renders the Settings window's content (a tab of it) to a PNG without showing a window:
    /// the way to look at a layout change without opening anything on the player's screen.</summary>
    /// <summary>Draws the theme editor into a picture; no window is ever shown.</summary>
    private static void RunRenderThemeEditorMode(string path)
    {
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var settings = Ui.MeterSettings.Load();
        if (settings.Language.Length > 0)
        {
            Ui.LocalizationManager.Instance.Language = settings.Language;
        }

        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        var window = new Ui.ThemeEditorWindow(settings);
        var content = (System.Windows.FrameworkElement)window.Content;
        int w = (int)window.Width, h = (int)window.Height;
        for (int pass = 0; pass < 2; pass++)
        {
            content.Measure(new System.Windows.Size(w, h));
            content.Arrange(new System.Windows.Rect(0, 0, w, h));
            content.UpdateLayout();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        var backdrop = new System.Windows.Controls.Border { Width = w, Height = h, Background = (System.Windows.Media.Brush)app.Resources["Brush.Window"] ?? System.Windows.Media.Brushes.Black };
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        backdrop.Measure(new System.Windows.Size(w, h));
        backdrop.Arrange(new System.Windows.Rect(0, 0, w, h));
        bitmap.Render(backdrop);
        bitmap.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        // the colour picker as it opens from a field (the popup itself is a window of its own that is never shown here)
        var picker = new Ui.ColorPickerPanel();
        picker.SetColor(System.Windows.Media.Color.FromRgb(0xFF, 0xBA, 0x42));
        var pickerCard = new System.Windows.Controls.Border { Padding = new System.Windows.Thickness(8), Child = picker, BorderThickness = new System.Windows.Thickness(1) };
        pickerCard.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Brush.Panel");
        pickerCard.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "Brush.Border");
        pickerCard.Measure(new System.Windows.Size(400, 400));
        pickerCard.Arrange(new System.Windows.Rect(0, 0, pickerCard.DesiredSize.Width, pickerCard.DesiredSize.Height));
        pickerCard.UpdateLayout();
        var pickerBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)pickerCard.DesiredSize.Width, (int)pickerCard.DesiredSize.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        pickerBitmap.Render(pickerCard);
        var pickerEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        pickerEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(pickerBitmap));
        using (var pickerFile = File.Create(Path.ChangeExtension(path, ".picker.png")))
        {
            pickerEncoder.Save(pickerFile);
        }

        Console.WriteLine("render-themeeditor: wrote " + path);
        Environment.Exit(0);
    }

    /// <summary>Draws the checklist window (Tools menu) with two characters made of a real login list into a picture; no window is ever shown.</summary>
    private static void RunRenderChecklistMode(string path, string language)
    {
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var settings = Ui.MeterSettings.Load();
        string chosen = language.Length > 0 ? language : settings.Language;
        if (chosen.Length > 0)
        {
            Ui.LocalizationManager.Instance.Language = chosen;
        }

        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        if (Environment.GetEnvironmentVariable("AIONDPS_REAL_TICKETS") == "1")
        {
            Aion2.Aion2Tickets.Reset();
            Aion2.Aion2Tickets.EnablePersistence(); // the file in AIONDPS_DATA_DIR: the characters as the meter saved them
        }
        else
        {
            Aion2.Aion2Tickets.Reset();
            Aion2.Aion2Tickets.TryParseList(Convert.FromHexString(Combat.SelfCheckTickets.SampleLogin), out var entries);
            var t0 = DateTime.Now;
            Aion2.Aion2Tickets.ReceiveList(entries, t0);
            Aion2.Aion2Tickets.NoteCharacter("Chumley", 1304, t0.AddSeconds(4));
            // a second character that has used a lot: the Odyle energy is down and the bonus is in use, some counters are used up
            Aion2.Aion2Tickets.ReceiveList(entries, t0.AddSeconds(10));
            Aion2.Aion2Tickets.ReceiveUpdate(new Aion2.TicketReading(Aion2.Aion2Tickets.OdyleId, new Aion2.TicketValue(75, 520, 0)), t0.AddSeconds(11));
            Aion2.Aion2Tickets.ReceiveUpdate(new Aion2.TicketReading(60000003, new Aion2.TicketValue(0, 0, 0)), t0.AddSeconds(11));
            Aion2.Aion2Tickets.ReceiveUpdate(new Aion2.TicketReading(102, new Aion2.TicketValue(0, 0, 0)), t0.AddSeconds(11));
            Aion2.Aion2Tickets.ReceiveUpdate(new Aion2.TicketReading(Aion2.Aion2Tickets.MissionsId, new Aion2.TicketValue(5, 5, 0)), t0.AddSeconds(11));
            Aion2.Aion2Tickets.ReceiveUpdate(new Aion2.TicketReading(Aion2.Aion2Tickets.PremiumId, new Aion2.TicketValue(0, 0, DateTimeOffset.UtcNow.AddHours(16 * 24 + 20).ToUnixTimeMilliseconds())), t0.AddSeconds(11));
            Aion2.Aion2Tickets.NoteCharacter("Aahz", 1304, t0.AddSeconds(14), level: 28); // a level 28 character: the daily dungeon (30) and the level 45 contents are still locked
        }

        int scale = int.TryParse(Environment.GetEnvironmentVariable("AIONDPS_RENDER_SCALE"), out int sc) && sc is >= 1 and <= 4 ? sc : 1; // 2 = a sharp picture for the website
        var window = new Ui.ChecklistWindow();
        var content = (System.Windows.FrameworkElement)window.Content;
        int w = (int)window.Width;
        content.Measure(new System.Windows.Size(w, double.PositiveInfinity));
        int h = (int)Math.Ceiling(content.DesiredSize.Height);
        content.Measure(new System.Windows.Size(w, h));
        content.Arrange(new System.Windows.Rect(0, 0, w, h));
        content.UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        content.Measure(new System.Windows.Size(w, h));
        content.Arrange(new System.Windows.Rect(0, 0, w, h));
        content.UpdateLayout();
        var backdrop = new System.Windows.Controls.Border { Width = w, Height = h, Background = (System.Windows.Media.Brush)app.Resources["Brush.Window"] ?? System.Windows.Media.Brushes.Black };
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w * scale, h * scale, 96 * scale, 96 * scale, System.Windows.Media.PixelFormats.Pbgra32);
        backdrop.Measure(new System.Windows.Size(w, h));
        backdrop.Arrange(new System.Windows.Rect(0, 0, w, h));
        bitmap.Render(backdrop);
        bitmap.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        Console.WriteLine("render-checklist: wrote " + path);
        Environment.Exit(0);
    }

    /// <summary>Draws the assist window (Tools menu) with made-up cooldowns and effects into a picture; no window is ever shown.</summary>
    private static void RunRenderAssistMode(string path, string language)
    {
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var settings = Ui.MeterSettings.Load();
        string chosen = language.Length > 0 ? language : settings.Language;
        if (chosen.Length > 0)
        {
            Ui.LocalizationManager.Instance.Language = chosen;
            Aion2.Protocol.Aion2SkillNames.Language = chosen;
        }

        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        var table = Aion2.Protocol.Aion2SkillNames.Load();
        var skills = table.Where(kv => kv.Key >= 10_000_000 && !Aion2.Protocol.Aion2Artwork.IsPassive(kv.Key) && !Aion2.Protocol.Aion2SkillNames.IsNonDamageEffect(kv.Key)
                && Aion2.Protocol.Aion2SkillIcons.PathFor(kv.Key) is not null && kv.Value is not ("Dodge" or "Basic Attack"))
            .GroupBy(kv => kv.Value).Select(g => g.First().Key).Take(8).ToList();
        var effects = table.Where(kv => kv.Value.Contains("Armor") || kv.Value.Contains("Shield") || kv.Value.Contains("Boost") || kv.Value.Contains("Stance"))
            .Where(kv => Aion2.Protocol.Aion2SkillIcons.PathFor(kv.Key) is not null).GroupBy(kv => kv.Value).Select(g => g.First().Key).Take(3).ToList();
        int[] remaining = { 94_000, 61_000, 28_000, 9_000, 0, 0, 0, 0 };
        var now = DateTime.Now;
        Aion2.Aion2Cooldowns.Receive(skills.Select((id, i) => new Aion2.CooldownEntry(id, remaining[i % remaining.Length])).ToList(), now);
        var dir = new Aion2.Aion2EntityDirectory { LocalPlayerId = 8956 };
        int[] lasts = { 18_000, 42_000, -1 };
        for (int i = 0; i < effects.Count; i++)
        {
            dir.Combat.NoteBuff(new Aion2.BuffNotice(now.AddSeconds(-5), 8956, 100 + i, effects[i] * 10, effects[i], lasts[i], 8956));
        }

        int scale = int.TryParse(Environment.GetEnvironmentVariable("AIONDPS_RENDER_SCALE"), out int sc) && sc is >= 1 and <= 4 ? sc : 1; // 2 = a sharp picture for the website
        var window = new Ui.AssistWindow(() => dir);
        var content = (System.Windows.FrameworkElement)window.Content;
        int w = (int)window.Width;
        content.Measure(new System.Windows.Size(w, double.PositiveInfinity));
        int h = (int)Math.Ceiling(content.DesiredSize.Height);
        content.Measure(new System.Windows.Size(w, h));
        content.Arrange(new System.Windows.Rect(0, 0, w, h));
        content.UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        content.Measure(new System.Windows.Size(w, h));
        content.Arrange(new System.Windows.Rect(0, 0, w, h));
        content.UpdateLayout();
        var backdrop = new System.Windows.Controls.Border { Width = w, Height = h, Background = (System.Windows.Media.Brush)app.Resources["Brush.Window"] ?? System.Windows.Media.Brushes.Black };
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w * scale, h * scale, 96 * scale, 96 * scale, System.Windows.Media.PixelFormats.Pbgra32);
        backdrop.Measure(new System.Windows.Size(w, h));
        backdrop.Arrange(new System.Windows.Rect(0, 0, w, h));
        bitmap.Render(backdrop);
        bitmap.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        Console.WriteLine("render-assist: wrote " + path);
        Environment.Exit(0);
    }

    /// <summary>Draws the world boss overlay with the bosses of the saved lists into a picture; no window is ever shown.</summary>
    private static void RunRenderBossOverlayMode(string path, int mapIndex)
    {
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var settings = Ui.MeterSettings.Load();
        if (settings.Language.Length > 0)
        {
            Ui.LocalizationManager.Instance.Language = settings.Language;
        }

        if (Environment.GetEnvironmentVariable("AIONDPS_SPEECH_ON") is { Length: > 0 })
        {
            settings.SpeechOn = true; // layout check only (nothing is saved here)
        }

        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        Aion2.Aion2FieldBosses.EnablePersistence();
        var now = DateTime.Now;
        string language = Ui.LocalizationManager.Instance.Language;
        var rows = new List<(string, string, bool)>();
        var shown = Aion2.Aion2FieldBosses.Maps[mapIndex];
        foreach (var boss in shown.Bosses.Take(8))
        {
            var status = Aion2.Aion2FieldBosses.StatusOf(boss, now);
            rows.Add((Aion2.Aion2FieldBosses.DisplayName(shown, boss, language), Ui.WorldBossText.Of(status, now), status.Phase == Aion2.BossPhase.Now));
        }

        var window = new Ui.WorldBossOverlayWindow();
        window.Render(rows.OrderBy(r => r.Item3 ? 0 : 1).ToList());
        var content = (System.Windows.UIElement)window.Content;
        content.Measure(new System.Windows.Size(600, 600));
        int w = (int)Math.Ceiling(content.DesiredSize.Width), h = (int)Math.Ceiling(content.DesiredSize.Height);
        content.Arrange(new System.Windows.Rect(0, 0, w, h));
        content.UpdateLayout();
        var backdrop = new System.Windows.Controls.Border { Width = w, Height = h, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2a, 0x36, 0x2c)), Child = null };
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        backdrop.Measure(new System.Windows.Size(w, h));
        backdrop.Arrange(new System.Windows.Rect(0, 0, w, h));
        bitmap.Render(backdrop);
        bitmap.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        Console.WriteLine("render-bossoverlay: wrote " + path);
        Environment.Exit(0);
    }

    /// <summary>Draws the network overlay with sample measurements into a picture (scale 2 = twice the pixels); no window is ever shown.</summary>
    /// <summary>Draws the loot tool (its page and its overlay) with made-up pickups into one picture; no window is ever shown, the loot log is not written.</summary>
    private static void RunRenderLootMode(string path, string language)
    {
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var settings = Ui.MeterSettings.Load();
        if (language.Length > 0)
        {
            Ui.LocalizationManager.Instance.Language = language;
        }
        else if (settings.Language.Length > 0)
        {
            Ui.LocalizationManager.Instance.Language = settings.Language;
        }

        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        DateTime t = DateTime.Now.AddMinutes(-12);
        Aion2.Aion2Loot.Clear();
        void Pick(uint id, int item, uint count, int seconds)
        {
            Aion2.Aion2Loot.NoteGroundItem(Combat.SelfCheckLoot.Ground(id, item, count));
            Aion2.Aion2Loot.NoteInventory(Combat.SelfCheckLoot.Inventory(0, item), t.AddSeconds(seconds));
            Aion2.Aion2Loot.NoteGroundGone(Combat.SelfCheckLoot.Gone(id, 0), t.AddSeconds(seconds).AddMilliseconds(60));
        }

        Aion2.Aion2Loot.NoteZone(1010);
        Pick(1, 534660001, 1, 0);
        Pick(2, 534660001, 2, 30);
        Pick(3, 110120003, 1, 90);
        Pick(4, 513560001, 3, 200);
        Aion2.Aion2Loot.NoteZone(20);
        Pick(5, 513551001, 1, 400);
        Pick(6, 534660002, 4, 500);
        var overlay = Ui.LootText.OverlayLines(Aion2.Aion2Loot.Current, 1010, settings, sample: false);
        var window = new Ui.LootOverlayWindow();
        window.Render(overlay.Caption, overlay.Lines);
        var overlayContent = window.DetachBody()!;
        var page = new Ui.ModulePage(Ui.LocalizationManager.Instance["Main.MenuTools.Loot"], new Ui.LootPage(), s => true, (s, v) => { }, s => "Ctrl+Alt+L", () => { }, () => { }, s => { });
        var root = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Vertical, Width = 860 };
        var host = new System.Windows.Controls.Border { Width = 860, Height = 460, Child = page, Background = (System.Windows.Media.Brush)app.Resources["Brush.Window"] ?? System.Windows.Media.Brushes.Black };
        root.Children.Add(host);
        var strip = new System.Windows.Controls.Border { Padding = new System.Windows.Thickness(20), Background = new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Color.FromRgb(0x3a, 0x4a, 0x52), System.Windows.Media.Color.FromRgb(0x1c, 0x26, 0x2e), 60) };
        var holder = new System.Windows.Controls.Border { Child = overlayContent, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        strip.Child = holder;
        root.Children.Add(strip);
        root.Measure(new System.Windows.Size(860, 2000));
        root.Arrange(new System.Windows.Rect(0, 0, 860, root.DesiredSize.Height));
        root.UpdateLayout();
        int w = 860, h = (int)Math.Ceiling(root.DesiredSize.Height);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        Aion2.Aion2Loot.Clear();
        Console.WriteLine("render-loot: wrote " + path);
        Environment.Exit(0);
    }

    /// <summary>Draws the attribute tool (ring or stat tab) with sample values into a picture; no window is ever shown.</summary>
    private static void RunRenderAttributesMode(string path, bool stats, string language)
    {
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var settings = Ui.MeterSettings.Load();
        if (language.Length > 0)
        {
            Ui.LocalizationManager.Instance.Language = language;
        }
        else if (settings.Language.Length > 0)
        {
            Ui.LocalizationManager.Instance.Language = settings.Language;
        }

        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        var directory = new Aion2.Aion2EntityDirectory();
        directory.SetLocalAttributes(new Dictionary<int, int>
        {
            [1] = 34, [2] = 12, [4] = 38, [5] = 27, [6] = 9, [7] = 41, [8] = 54, [9] = 39, [10] = 46, [11] = 62, [13] = 24, [14] = 46, [15] = 25, [16] = 27, [17] = 28,
        });
        var page = new Ui.AttributesPage(() => directory);
        if (stats)
        {
            page.ShowStatsTab();
        }

        var host = new System.Windows.Controls.Border { Width = 1060, Height = 760, Child = page, Background = (System.Windows.Media.Brush)app.Resources["Brush.Window"] ?? System.Windows.Media.Brushes.Black };
        host.Measure(new System.Windows.Size(1060, 760));
        host.Arrange(new System.Windows.Rect(0, 0, 1060, 760));
        host.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1060, 760, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(host);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        page.Stop();
        Console.WriteLine("render-attributes: wrote " + path);
        Environment.Exit(0);
    }

    private static void RunRenderStatCalcMode(string path, bool expert, string language)
    {
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var settings = Ui.MeterSettings.Load();
        if (language.Length > 0)
        {
            Ui.LocalizationManager.Instance.Language = language;
        }
        else if (settings.Language.Length > 0)
        {
            Ui.LocalizationManager.Instance.Language = settings.Language;
        }

        settings.StatCalcExpert = expert;
        settings.StatCalcSide = "back";
        settings.StatCalcSideRate = 62;
        settings.Save();
        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        var directory = new Aion2.Aion2EntityDirectory();
        directory.SetLocalStats(new Dictionary<int, int>
        {
            [19] = 277, [317] = 1050, [31] = 542, [33] = 431, [425] = 337, [32] = 1000, [28] = 500, [379] = 1500, [44] = 600, [442] = 1750, [443] = 620, [589] = 560, [102] = 93, [56] = 259, [50] = 55,
        });
        var page = new Ui.StatCalcPage(() => directory, () => Array.Empty<Combat.DamageEvent>());
        page.ApplyAssumptions(34, Combat.AttackSide.Back, 62);
        page.ApplyChange(Combat.StatKey.GearAttack, 120);
        page.ApplyChange(Combat.StatKey.CritAmp, -2);
        page.ApplyChange(Combat.StatKey.AllAmp, 2.5);
        int height = expert ? 1000 : 760;
        var host = new System.Windows.Controls.Border { Width = 1180, Height = height, Child = page, Background = (System.Windows.Media.Brush)app.Resources["Brush.Window"] ?? System.Windows.Media.Brushes.Black };
        // the button style the tools window takes over from the main window (see ThemedChrome); the picture has no main window
        host.Resources[typeof(System.Windows.Controls.Button)] = System.Windows.Markup.XamlReader.Parse(
            "<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"Button\">" +
            "<Setter Property=\"Background\" Value=\"{DynamicResource Brush.Control}\"/><Setter Property=\"Foreground\" Value=\"{DynamicResource Brush.Text}\"/>" +
            "<Setter Property=\"BorderBrush\" Value=\"{DynamicResource Brush.Border}\"/><Setter Property=\"BorderThickness\" Value=\"1\"/>" +
            "<Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"Button\"><Border x:Name=\"Bd\" Background=\"{TemplateBinding Background}\" BorderBrush=\"{TemplateBinding BorderBrush}\" BorderThickness=\"{TemplateBinding BorderThickness}\" Padding=\"{TemplateBinding Padding}\"><ContentPresenter HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/></Border>" +
            "<ControlTemplate.Triggers><Trigger Property=\"IsEnabled\" Value=\"False\"><Setter TargetName=\"Bd\" Property=\"Opacity\" Value=\"0.4\"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
        host.Measure(new System.Windows.Size(1180, height));
        host.Arrange(new System.Windows.Rect(0, 0, 1180, height));
        host.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1180, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(host);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        page.Stop();
        Console.WriteLine("render-statcalc: wrote " + path);
        Environment.Exit(0);
    }

    private static void RunRenderNetworkMode(string path, int scale)
    {
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var settings = Ui.MeterSettings.Load();
        if (settings.Language.Length > 0)
        {
            Ui.LocalizationManager.Instance.Language = settings.Language;
        }

        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        var sample = new Aion2.Capture.NetworkSnapshot(true, "87.232.75.220:13328", TimeSpan.FromSeconds(642), 1, 0, 7, 16, 6, 49, 4.2, 8.6, 0.2, 26, 31, 0, 0, TimeSpan.FromSeconds(0.4), 0, 0,
            0, 0, null, null, null, new Aion2.Capture.NetworkProbeSnapshot(true, "192.168.2.1", 1, 0, 60, 12, 0, 60));
        var window = new Ui.NetworkOverlayWindow();
        window.Render(Ui.NetworkOverlayRows.Of(sample, Array.Empty<string>()));
        var content = (System.Windows.UIElement)window.Content;
        content.Measure(new System.Windows.Size(600, 600));
        int w = (int)Math.Ceiling(content.DesiredSize.Width), h = (int)Math.Ceiling(content.DesiredSize.Height);
        content.Arrange(new System.Windows.Rect(0, 0, w, h));
        content.UpdateLayout();
        var backdrop = new System.Windows.Controls.Border
        {
            Width = w, Height = h,
            Background = new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Color.FromRgb(0x3a, 0x4a, 0x52), System.Windows.Media.Color.FromRgb(0x1c, 0x26, 0x2e), 60),
        };
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w * scale, h * scale, 96 * scale, 96 * scale, System.Windows.Media.PixelFormats.Pbgra32);
        backdrop.Measure(new System.Windows.Size(w, h));
        backdrop.Arrange(new System.Windows.Rect(0, 0, w, h));
        bitmap.Render(backdrop);
        bitmap.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        Console.WriteLine("render-network: wrote " + path);
        Environment.Exit(0);
    }

    /// <summary>Draws the combat overlay into a picture with made-up fight data ("live") or as it looks while it is being arranged ("edit"); no window is ever shown,
    /// and the settings come from a throw-away folder so nothing of the player's own is read or written.</summary>
    private static void RunRenderCombatOverlayMode(string path, string mode, int scale)
    {
        Environment.SetEnvironmentVariable("AIONDPS_DATA_DIR", Path.Combine(Path.GetTempPath(), "aiondps-render-combat"));
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var settings = new Ui.MeterSettings { CombatShowHealer = true, ShowBossHp = true };
        if (Environment.GetEnvironmentVariable("AIONDPS_LANG") is { Length: > 0 } language)
        {
            Ui.LocalizationManager.Instance.Language = language;
        }

        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        bool edit = mode == "edit";
        var windows = new[] { new Ui.CombatOverlayWindow(Ui.CombatOverlayWindow.Part.Cast), new Ui.CombatOverlayWindow(Ui.CombatOverlayWindow.Part.Target), new Ui.CombatOverlayWindow(Ui.CombatOverlayWindow.Part.Hp) };
        foreach (var window in windows)
        {
            window.ApplyLocked(!edit);
            window.SetEditing(edit);
        }

        string bossName = Aion2.Protocol.Aion2Npcs.NameOf(2300708, Ui.LocalizationManager.Instance.Language) ?? "Ruthilis of Pain";
        string? bossPicture = Aion2.Protocol.Aion2Artwork.BossThumbPath(2300708);
        var frame = edit
            ? new Ui.CombatFrame(Array.Empty<Ui.CastRowData>(), null)
            : new Ui.CombatFrame(
                new[]
                {
                    new Ui.CastRowData(Aion2.Protocol.Aion2SkillNames.Display("Attack"), bossName, Ui.LocalizationManager.Instance["Combat.You"], true, false, false, 900, 1500, 650, null, Generic: true),
                    new Ui.CastRowData(Aion2.Protocol.Aion2SkillNames.DisplayOf(1801180), bossName, Ui.LocalizationManager.Instance["Combat.AllAround"], false, true, true, 400, 1900, null, null),
                },
                new Ui.TargetOfTargetData("Plague", bossName, Aion2.Protocol.Aion2Artwork.ClassEmblemPath("Cleric"), false, 12480, 20563,
                    new[]
                    {
                        new Ui.EffectData("Poison", null, "12", true),
                        new Ui.EffectData("Slow", null, "5", true),
                        new Ui.EffectData("Bleed", null, "9", true),
                    }),
                new Ui.BossHpData(1224000, 3400000, 1, "✓", Since: DateTime.Now - TimeSpan.FromSeconds(Environment.GetEnvironmentVariable("AIONDPS_FIGHT_SECONDS") is { Length: > 0 } sec ? int.Parse(sec) : 151)));

        // the two windows one above the other, each as big as it wants to be
        var contents = new List<(System.Windows.UIElement Content, int Width, int Height)>();
        foreach (var window in windows)
        {
            window.Render(frame, settings);
            var part = (System.Windows.UIElement)window.Content;
            part.Measure(new System.Windows.Size(700, 500));
            int pw = (int)Math.Ceiling(part.DesiredSize.Width), ph = (int)Math.Ceiling(part.DesiredSize.Height);
            part.Arrange(new System.Windows.Rect(0, 0, pw, ph));
            part.UpdateLayout();
            contents.Add((part, pw, ph));
        }

        int w = contents.Max(c => c.Width), h = contents.Sum(c => c.Height) + 8 * (contents.Count - 1);
        var backdrop = new System.Windows.Controls.Border
        {
            Width = w, Height = h,
            Background = new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Color.FromRgb(0x3a, 0x4a, 0x52), System.Windows.Media.Color.FromRgb(0x1c, 0x26, 0x2e), 60),
        };
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w * scale, h * scale, 96 * scale, 96 * scale, System.Windows.Media.PixelFormats.Pbgra32);
        backdrop.Measure(new System.Windows.Size(w, h));
        backdrop.Arrange(new System.Windows.Rect(0, 0, w, h));
        if (Environment.GetEnvironmentVariable("AIONDPS_TRANSPARENT") != "1") // 1 = only the parts, on a see-through ground (for the website)
        {
            bitmap.Render(backdrop);
        }

        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double y = 0;
            foreach (var (part, pw, ph) in contents)
            {
                dc.DrawRectangle(new System.Windows.Media.VisualBrush(part), null, new System.Windows.Rect(0, y, pw, ph));
                y += ph + 8;
            }
        }

        bitmap.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        Console.WriteLine("render-combatoverlay: wrote " + path + $" ({w}x{h}), boss picture: {(bossPicture is null ? "none" : "yes")}");
        Environment.Exit(0);
    }

    /// <summary>Draws the replay window at one moment of a made-up boss fight (eight players around a boss that winds up areas); no window is ever shown.</summary>
    private static void RunRenderReplayMode(string path, double seconds, int scale)
    {
        Environment.SetEnvironmentVariable("AIONDPS_DATA_DIR", Path.Combine(Path.GetTempPath(), "aiondps-render-replay"));
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var settings = Ui.MeterSettings.Load();
        if (Environment.GetEnvironmentVariable("AIONDPS_LANG") is { Length: > 0 } language)
        {
            Ui.LocalizationManager.Instance.Language = language;
        }

        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        DateTime start = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Local);
        const int boss = 100;
        var random = new Random(11);
        string[] classes = { "Gladiator", "Templar", "Cleric", "Chanter", "Sorcerer", "Ranger", "Assassin", "Spiritmaster" };
        string[] names = { "Jinwoo", "Goku", "Ichigo", "Naruto", "Tsubasa", "Lyra", "Wendy", "Rin" };
        var tracks = new List<Combat.PositionTrack>();
        for (int i = 0; i < classes.Length; i++)
        {
            double phase = i * Math.PI / 4;
            var t = new List<int>();
            var x = new List<int>();
            var y = new List<int>();
            for (int ms = 0; ms <= 60000; ms += 700)
            {
                double radius = (i < 2 ? 450 : 900 + 150 * (i % 3)) + 120 * Math.Sin(ms / 5000.0 + i);
                double angle = phase + ms / 14000.0 * (i % 2 == 0 ? 1 : -1);
                t.Add(ms);
                x.Add((int)(radius * Math.Cos(angle) + random.Next(-40, 40)));
                y.Add((int)(radius * Math.Sin(angle) + random.Next(-40, 40)));
            }

            tracks.Add(new Combat.PositionTrack(i + 1, start, t.ToArray(), x.ToArray(), y.ToArray()));
        }

        tracks.Add(new Combat.PositionTrack(boss, start, new[] { 0, 60000 }, new[] { 0, 0 }, new[] { 0, 0 }));
        var windups = new List<Combat.WindupRecord>();
        var hits = new List<Combat.DamageEvent>();
        int[] skills = { 1236440, 1217430, 1223500 };
        for (int s = 3; s < 58; s += 4)
        {
            int skill = skills[(s / 4) % skills.Length];
            double angle = s * 0.9;
            windups.Add(new Combat.WindupRecord(start.AddSeconds(s), boss, skill, (s / 4) % 8 + 1, 1500, angle * 57.3 % 360, (int)(700 * Math.Cos(angle)), (int)(700 * Math.Sin(angle)), true));
            hits.Add(new Combat.DamageEvent(start.AddSeconds(s + 2.1), boss, (s / 4) % 8 + 1, 3000 + s * 10, false, "Attack", SkillId: skill));
        }

        var extras = new Combat.FightExtras(Array.Empty<Combat.SkillUse>(), Array.Empty<Combat.BuffEvent>(), new[] { new Combat.StaggerEvent(start.AddSeconds(30), boss, 0, 0, true) },
            new Dictionary<int, string>(), tracks, windups, new[] { new Combat.DeathRecord(start.AddSeconds(41), 5, start.AddSeconds(52)) },
            new[] { new Combat.HpSeries(boss, start, Enumerable.Range(0, 61).Select(s => s * 1000).ToArray(), Enumerable.Range(0, 61).Select(s => (short)(1000 - s * 15)).ToArray()) });
        var data = Combat.ReplayBuilder.Build(Aion2.Protocol.Aion2Npcs.NameOf(2300708, Ui.LocalizationManager.Instance.Language) ?? "Boss", extras, hits, start, start.AddSeconds(60),
            id => id == boss ? (Aion2.Protocol.Aion2Npcs.NameOf(2300708, Ui.LocalizationManager.Instance.Language) ?? "Boss", "", false) : (names[id - 1], classes[id - 1], true),
            skill => skill == 1236440 ? 520 : null, _ => Aion2.Protocol.Aion2SkillNames.Display("Attack"));
        var window = new Ui.FightReplayWindow(data);
        window.ShowAt(seconds);
        const double width = 960, height = 700;
        var content = (System.Windows.UIElement)window.Content;
        content.Measure(new System.Windows.Size(width, height));
        content.Arrange(new System.Windows.Rect(0, 0, width, height));
        content.UpdateLayout();
        content.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)width * scale, (int)height * scale, 96 * scale, 96 * scale, System.Windows.Media.PixelFormats.Pbgra32);
        var backdrop = new System.Windows.Controls.Border { Width = width, Height = height, Background = (System.Windows.Media.Brush)app.Resources["Brush.Window"] };
        backdrop.Measure(new System.Windows.Size(width, height));
        backdrop.Arrange(new System.Windows.Rect(0, 0, width, height));
        bitmap.Render(backdrop);
        bitmap.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        Console.WriteLine("render-replay: wrote " + path);
        Environment.Exit(0);
    }

    /// <summary>Draws the "window will turn transparent" message box into a picture; no window is ever shown.</summary>
    private static void RunRenderMessageBoxMode(string path)
    {
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var settings = Ui.MeterSettings.Load();
        if (settings.Language.Length > 0)
        {
            Ui.LocalizationManager.Instance.Language = settings.Language;
        }

        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        var loc = Ui.LocalizationManager.Instance;
        var window = Ui.ThemedMessageBox.Build(null, string.Format(loc["Main.HideUiHint"], "Ctrl+Alt+H"), loc["Main.HideUiHint.Title"], System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information, loc["Main.HideUiHint.DontShow"], null, out _);
        // in the running meter the box takes its button/combo styles from the main window; here there is none, so they come from the settings window (same look)
        var styled = new Ui.SettingsWindow(settings);
        foreach (var type in new[] { typeof(System.Windows.Controls.Button) })
        {
            if (styled.Resources[type] is System.Windows.Style style)
            {
                window.Resources[type] = style;
            }
        }

        var content = (System.Windows.UIElement)window.Content;
        content.Measure(new System.Windows.Size(560, double.PositiveInfinity));
        int w = Math.Max(340, (int)Math.Ceiling(content.DesiredSize.Width)), h = (int)Math.Ceiling(content.DesiredSize.Height);
        content.Measure(new System.Windows.Size(w, h));
        content.Arrange(new System.Windows.Rect(0, 0, w, h));
        content.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        Console.WriteLine("render-messagebox: wrote " + path);
        Environment.Exit(0);
    }

    private static void RunRenderSettingsMode(string path, string tab)
    {
        var app = new System.Windows.Application();
        // The shared styles (ThemedSlider ...) are merged by the GUI start; the Settings window needs them here too.
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var settings = Ui.MeterSettings.Load();
        if (Environment.GetEnvironmentVariable("AIONDPS_LANG") is { Length: > 0 } forcedLanguage)
        {
            Ui.LocalizationManager.Instance.Language = forcedLanguage; // pictures for the docs in a chosen language, whatever the user's settings say
        }
        else if (settings.Language.Length > 0)
        {
            Ui.LocalizationManager.Instance.Language = settings.Language; // so a layout can be checked in every language
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("AIONDPS_PET_RARE_MAX"), out int rareMax))
        {
            settings.PetRareMax = rareMax; // layout check only (nothing is saved here)
        }

        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        Aion2.Aion2FieldBosses.EnablePersistence(); // read-only here: the page shows the last lists the game sent
        var window = new Ui.SettingsWindow(settings);
        if (window.FindName("Nav" + (tab == "Interface" ? "Ui" : tab == "Capture" ? "Install" : tab)) is System.Windows.Controls.RadioButton nav)
        {
            nav.IsChecked = true;
        }

        if (Environment.GetEnvironmentVariable("AIONDPS_SPEECH_ON") is { Length: > 0 } && window.FindName("SpeechOnBox") is System.Windows.Controls.CheckBox speechOn)
        {
            speechOn.IsChecked = true; // layout check: the voice output block open
        }

        if (tab is "Loot" or "Combat")
        {
            window.ShowModule(tab.ToLowerInvariant()); // the tool's own settings, without the sidebar
        }

        if (Environment.GetEnvironmentVariable("AIONDPS_LONGEST_PETS") is { Length: > 0 } longest && int.TryParse(longest, out int longestCount))
        {
            window.ShowLongestPetNames(longestCount); // layout check: the pets with the longest names in this language
        }

        if (Environment.GetEnvironmentVariable("AIONDPS_PET_REGION") is { Length: > 0 })
        {
            window.ShowPetRegionTab(); // layout check: the Region tab of the pet page
        }

        string? rare = Environment.GetEnvironmentVariable("AIONDPS_PET_RARE");
        if (!string.IsNullOrEmpty(rare))
        {
            window.ShowPetRareTab(rare == "1" ? null : rare); // layout check: the Rare tab (the value is the key of the line to select, e.g. "verteron.2100079")
        }

        double width = 620, height = string.IsNullOrEmpty(rare) && tab != "Hotkeys" ? 740 : 1000;
        if (int.TryParse(Environment.GetEnvironmentVariable("AIONDPS_RENDER_HEIGHT"), out int customHeight))
        {
            height = Math.Clamp(customHeight, 200, 1400);
        }

        var content = (System.Windows.UIElement)window.Content;
        if (tab is "Pets" or "Gather" && window.TakePanel(tab == "Pets" ? "PanelPets" : "PanelGather") is { } taken)
        {
            // the page as the tools window shows it (layout check, nothing is saved)
            var body = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(14) };
            body.Resources.MergedDictionaries.Add(window.Resources);
            body.Children.Add(taken);
            var host = new System.Windows.Controls.Border { Child = body };
            host.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Brush.Window");
            content = host;
            width = 860;
        }

        content.Measure(new System.Windows.Size(width, height));
        content.Arrange(new System.Windows.Rect(0, 0, width, height));
        content.UpdateLayout();
        int scale = int.TryParse(Environment.GetEnvironmentVariable("AIONDPS_RENDER_SCALE"), out int sc) ? Math.Clamp(sc, 1, 4) : 1;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)width * scale, (int)height * scale, 96 * scale, 96 * scale, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        Console.WriteLine("render-settings: wrote " + path);
        Environment.Exit(0);
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
        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        var window = new Ui.MainWindow();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var type = typeof(Ui.MainWindow);
        Console.WriteLine("aion2-ui-live: window created, starting the live source...");
        type.GetMethod("StartCapture", flags)!.Invoke(window, new object?[] { settings });
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

    /// <summary>Plays a recording through the packet source (no window) and prints the skill presets each time the game switches one.</summary>
    private static void RunAion2PresetCheck(string path, int serverPort)
    {
        var source = new Aion2.Aion2PacketCombatSource(Aion2.Protocol.Aion2Protocol.Load());
        var directory = (Aion2.Aion2EntityDirectory)source.Entities;
        var names = Aion2.Protocol.Aion2SkillNames.Load();
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        int last = -1;
        string lastSummary = "";
        foreach (var segment in Aion2.Capture.SegmentRecording.Read(path)
                     .Where(s => s.Source.EndsWith(":" + serverPort, StringComparison.Ordinal) || s.Destination.EndsWith(":" + serverPort, StringComparison.Ordinal))
                     .Select(s => s with { FromServer = s.Source.EndsWith(":" + serverPort, StringComparison.Ordinal) }))
        {
            source.Ingest(segment);
            string summary = Summarise();
            if (directory.ActivePreset != last || summary != lastSummary)
            {
                Console.WriteLine($"{segment.Timestamp:HH:mm:ss.fff} active preset {directory.ActivePreset}: {summary}");
                last = directory.ActivePreset;
                lastSummary = summary;
            }
        }

        Console.WriteLine("final:");
        foreach (var preset in directory.LocalPresets)
        {
            Console.WriteLine($"  preset {preset.Index}{(preset.Active ? " (active)" : "")} seen {preset.SeenAt:HH:mm:ss}: {preset.Skills.Count} skills");
            Console.WriteLine("    stigmas: " + string.Join(", ", preset.Skills.Where(k => k.Stigma && k.SkillId % 10000 == 0).Select(k => $"{names.GetValueOrDefault(k.SkillId)} Lv{k.Level}")));
            foreach (var k in preset.Skills.Where(k => k.SkillId % 10000 == 0 && !k.Stigma && k.Stones is not null && k.Equipped))
            {
                Console.WriteLine($"    {names.GetValueOrDefault(k.SkillId),-20} Lv{k.Level,2}/{k.BaseLevel,-2} stones [{string.Join(",", k.Stones!)}]");
            }
        }

        string Summarise() => string.Join(", ", directory.LocalSkills.Where(k => k.Stigma && k.SkillId % 10000 == 0).Select(k => $"{names.GetValueOrDefault(k.SkillId)} {k.Level}")) + $" | {directory.LocalSkills.Count} skills, presets known {directory.LocalPresets.Count}";
    }

    private static void RunAion2UploadDryRun(string path, string serverName, int serverPort, string[] sendOnly)
    {
        var app = new System.Windows.Application();
        var settings = Ui.MeterSettings.Load();
        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        var window = new Ui.MainWindow();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var type = typeof(Ui.MainWindow);
        var source = new Aion2.Aion2PacketCombatSource(Aion2.Protocol.Aion2Protocol.Load());
        type.GetField("_source", flags)!.SetValue(window, source);
        // A recording that starts after the login has no character record: the stored character of the last session (maybe another one) would be "self".
        // AION_OWN_NAME names the character the recording belongs to.
        if (Environment.GetEnvironmentVariable("AION_OWN_NAME") is { Length: > 0 } ownCharacter)
        {
            if (source.Entities is Aion2.Aion2EntityDirectory ownDirectory)
            {
                ownDirectory.SetConfiguredLocalName(ownCharacter);
                // the saved character of the last session (another one) must not be "self" either
                var characterField = typeof(Aion2.Aion2EntityDirectory).GetField("_character", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (characterField?.GetValue(ownDirectory) is Aion2.Aion2CharacterInfo { Restored: true })
                {
                    characterField.SetValue(ownDirectory, null);
                }
            }
        }

        var tick = type.GetMethod("OnPollTimerTick", flags)!;

        var segments = Aion2.Capture.SegmentRecording.Read(path)
            .Where(s => s.Source.EndsWith(":" + serverPort, StringComparison.Ordinal) || s.Destination.EndsWith(":" + serverPort, StringComparison.Ordinal))
            .Select(s => s with { FromServer = s.Source.EndsWith(":" + serverPort, StringComparison.Ordinal) })
            .ToList();
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        var entries = (System.Collections.IList)type.GetField("_mobBossEntries", flags)!.GetValue(window)!;
        var build = type.GetMethod("BuildEncounterUpload", flags, null, new[] { typeof(int), typeof(string), typeof(string) }, null)!;
        // The same slug the meter files real uploads under (MainWindow.ServerSlug): "Europe - Kaisinel" is
        // aion2:europe-kaisinel. A plain space-to-dash replace gave "europe---kaisinel" and a second server row.
        string fingerprint = "aion2:" + System.Text.RegularExpressions.Regex.Replace(serverName.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

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
            Console.WriteLine($"  {request.BossNpcName} (entity {targetId}) NPC id {request.BossNpcId}, level {request.BossLevel}, max HP {request.BossMaxHp}, game {request.Game}, server {request.ServerName} [{request.ServerFingerprint}], {request.StartedAt:HH:mm:ss}-{request.EndedAt:HH:mm:ss} UTC, {request.Participants.Count} participant(s), {request.Participants.Count(p => p.IsSelf)} self");
            foreach (var d in request.BossDebuffs ?? Array.Empty<Upload.BuffUsageUpload>())
            {
                Console.WriteLine($"      boss effect {d.Skill,-26} id {d.SkillId,9} uptime {d.UptimePermille / 10.0,5:0.0}% x{d.Casts,-4} givers {(d.Givers is { Count: > 0 } dg ? string.Join("/", dg) : "-")}");
            }

            foreach (var p in request.Participants.OrderByDescending(x => x.TotalDamage))
            {
                Console.WriteLine($"      {p.Name,-12} {p.ClassName,-13} self={p.IsSelf,-5} damage {p.TotalDamage,9:N0} taken {p.DamageTaken,8:N0} heal {p.TotalHealing,7:N0} guild {p.Guild ?? "-"} profile {(p.Profile is null ? "-" : p.Profile.Source)} buffs {p.Buffs?.Count ?? 0} (group {p.Buffs?.Count(b => b.Givers is { Count: > 0 }) ?? 0})");
                if (Environment.GetEnvironmentVariable("AION_DRYRUN_BUFFS") == "1")
                {
                    foreach (var b in (p.Buffs ?? Array.Empty<Upload.BuffUsageUpload>()).Take(40))
                    {
                        Console.WriteLine($"          {b.Skill,-28} id {b.SkillId,9} uptime {b.UptimePermille / 10.0,5:0.0}% x{b.Casts,-4} givers {(b.Givers is { Count: > 0 } g ? string.Join("/", g) : "-")}");
                    }
                }
            }
        }

        var buildProfiles = type.GetMethod("BuildProfilesUpload", flags);
        if (buildProfiles?.Invoke(window, new object?[] { fingerprint, serverName, false, null }) is Upload.ProfilesUploadRequest profilesRequest)
        {
            if (Environment.GetEnvironmentVariable("AION_DRYRUN_JSON") is { Length: > 0 } jsonPath)
            {
                File.WriteAllText(jsonPath, System.Text.Json.JsonSerializer.Serialize(profilesRequest, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
            }

            Console.WriteLine($"aion2-upload-dryrun: the profile upload (no boss) would carry {profilesRequest.Participants.Count} player(s):");
            foreach (var pp in profilesRequest.Participants)
            {
                Console.WriteLine($"  {pp.Name,-14} {pp.ClassName,-13} {pp.Profile.Source,-5} level {pp.Profile.Level?.ToString() ?? "-"} gear {pp.Profile.Gear.Count} (enchant sum {pp.Profile.Gear.Sum(g => g.Enchant)}) legion {pp.Guild ?? "-"}");
            }
        }
        else
        {
            Console.WriteLine("aion2-upload-dryrun: the profile upload (no boss) would be EMPTY (no own character / class in this recording)");
        }

        if (source.Entities is Aion2.Aion2EntityDirectory directory)
        {
            var inspected = directory.InspectedPlayers();
            string roundTrip = Path.Combine(Path.GetTempPath(), "aion2-inspected-roundtrip.json");
            Aion2.Aion2CharacterStore.SaveInspected(roundTrip, inspected);
            var reloaded = Aion2.Aion2CharacterStore.LoadInspected(roundTrip);
            Console.WriteLine($"aion2-upload-dryrun: saved {inspected.Count} window(s), reloaded {reloaded.Count}, gear of the first {reloaded.FirstOrDefault()?.Gear.Count} item(s), enchant sum {reloaded.FirstOrDefault()?.Gear.Sum(g => g.Enchant)}");
            File.Delete(roundTrip);
            Console.WriteLine($"aion2-upload-dryrun: {inspected.Count} character window(s) of other players read; {directory.LocalSkills.Count(s => s.Stigma)} own stigma(s) marked");
            foreach (var player in inspected)
            {
                Console.WriteLine($"  {player.Name,-14} {Aion2.Protocol.Aion2SkillNames.ClassFromCode(player.ClassCode),-13} level {player.Level} gear score {player.GearScore} legion {player.Guild ?? "-"} titles [{string.Join(",", (player.Titles ?? Array.Empty<Aion2.Aion2TitleSlot>()).Select(t => t.TitleId))}] pets [{string.Join(" ", (player.Pets ?? Array.Empty<Aion2.Aion2Pet>()).Select(k => $"{k.SpeciesId}/{k.Level}:{string.Join("", k.Kinds)}"))}] boards [{string.Join(",", (player.BoardCounts ?? Array.Empty<Aion2.Aion2BoardCount>()).Select(b => $"{b.BoardId}={b.Count}"))}]: {string.Join(", ", player.Gear.Select(g => $"{g.SlotIndex}:{g.ItemId}+{g.Enchant}"))}");
            }
        }

        if (sendOnly.Length == 0)
        {
            Console.WriteLine($"aion2-upload-dryrun: {uploads} upload(s) would be sent. Nothing was sent.");
            Environment.Exit(0);
        }

        int sent = 0;
        foreach (string wanted in sendOnly)
        {
            string[] parts = wanted.Split('@');
            var match = best.Values.FirstOrDefault(r => r.BossNpcName == parts[0] && parts.Length == 2 && r.StartedAt.ToString("HH:mm") == parts[1]);
            if (match is null)
            {
                Console.WriteLine($"aion2-upload-dryrun: --send {wanted}: no such fight in this recording, skipped");
                continue;
            }

            // The recording has no login of its own character: the character of the last session shows as "self". AION_OWN_NAME names the real one.
            if (Environment.GetEnvironmentVariable("AION_OWN_NAME") is { Length: > 0 } realSelf)
            {
                if (!match.Participants.Any(p => p.Name == realSelf))
                {
                    Console.WriteLine($"aion2-upload-dryrun: --send {wanted}: {realSelf} is not in this fight, skipped");
                    continue;
                }

                match = match with { Participants = match.Participants.Where(p => p.Name == realSelf || !(p.IsSelf && p.TotalDamage == 0)).Select(p => p with { IsSelf = p.Name == realSelf }).ToList() };
            }

            var result = Upload.UploadClient.SendAsync(match).GetAwaiter().GetResult();
            Console.WriteLine($"aion2-upload-dryrun: --send {wanted}: {(result.Success ? "uploaded" : "FAILED " + result.Error)}");
            sent += result.Success ? 1 : 0;
        }

        Console.WriteLine($"aion2-upload-dryrun: {sent} of {sendOnly.Length} requested upload(s) sent.");
        Environment.Exit(0);
    }

    private static void RunAion2UiTestMode(string path, int serverPort)
    {
        {
            var app = new System.Windows.Application();
            var settings = Ui.MeterSettings.Load();
            Ui.ThemeManager.Apply(app, settings);
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

    private static void RunRenderPetMapMode(string path, string mapKey, double x, double y, bool guards = false)
    {
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var settings = Ui.MeterSettings.Load();
        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        var map = Aion2.Protocol.Aion2Maps.All.Concat(Aion2.Protocol.Aion2Maps.Instances).First(m => m.Key == mapKey);
        var chosen = settings.PetMapPets.Count > 0 ? settings.PetMapPets.ToHashSet() : new HashSet<int> { 1170, 1162, 1171, 1219 };
        var points = Aion2.Protocol.Aion2Maps.PointsOf(map, chosen);
        var pets = points.Select(p => p.PetId).Distinct().OrderBy(i => i).ToList();
        var colors = pets.Select((id, i) => (id, i)).ToDictionary(t => t.id, t => Ui.PetMapPalette.Of(t.i));
        var window = new Ui.PetMapWindow();
        if (guards && window.Content is System.Windows.UIElement root)
        {
            // never shown: lay the frame out once so that the map knows how much of itself is visible
            root.Measure(new System.Windows.Size(Ui.PetMapWindow.ViewSize, Ui.PetMapWindow.ViewSize));
            root.Arrange(new System.Windows.Rect(0, 0, Ui.PetMapWindow.ViewSize, Ui.PetMapWindow.ViewSize));
            root.UpdateLayout();
        }

        // sample "live" monsters: every 7th spawn point within 120 m stands there right now
        var live = points.Where(p => Math.Abs(p.X - x) < 12000 && Math.Abs(p.Y - y) < 12000).Where((p, i) => i % 7 == 0).ToList();
        window.Render(map, x, y, settings.PetMapRadius, settings.PetMapOpacity, points.ToList(), live, colors,
            Aion2.Protocol.Aion2Gather.PointsOf(map, Aion2.Protocol.Aion2Gather.Items().Where(i => i.Count > 0).Select(i => i.Key).ToHashSet()),
            guards: guards ? new List<(float X, float Y)> { ((float)x + 1500, (float)y + 700), ((float)x - 1200, (float)y - 400), ((float)x - 30000, (float)y + 5000), ((float)x + 9000, (float)y - 21000) } : null);
        var list = new Ui.PetListWindow();
        list.Render(pets.Select(id => (colors[id], Aion2.Protocol.Aion2Pets.PetName(id, "en") ?? "?", "Level 2 · 22/75", "104 m", false)).ToList());
        var visual = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2a, 0x36, 0x2c)) };
        foreach (var overlay in new System.Windows.Window[] { window, list })
        {
            var content = (System.Windows.UIElement)overlay.Content;
            overlay.Content = null;
            visual.Children.Add(content);
        }

        visual.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        visual.Arrange(new System.Windows.Rect(visual.DesiredSize));
        visual.UpdateLayout();
        int w = (int)Math.Ceiling(visual.DesiredSize.Width), h = (int)Math.Ceiling(visual.DesiredSize.Height);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        Console.WriteLine($"render-petmap: wrote {path} ({points.Count} points of {pets.Count} pets on {map.Title}); tiles shown {window.TilesShown}: {window.TileDebug}");
        Environment.Exit(0);
    }

    /// <summary>The pet list with the pets of an open Black Cloud shop (sample data of the recording 2026-10-11_12-34), drawn into a picture; no window is shown.</summary>
    /// <summary>Draws the pet farming overlay (pet level of the targeted monster) with a made-up pet into a picture; no window is ever shown.</summary>
    private static void RunRenderPetFarmMode(string path)
    {
        Environment.SetEnvironmentVariable("AIONDPS_DATA_DIR", Path.Combine(Path.GetTempPath(), "aiondps-render-petfarm"));
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var settings = new Ui.MeterSettings();
        if (Environment.GetEnvironmentVariable("AIONDPS_LANG") is { Length: > 0 } language)
        {
            Ui.LocalizationManager.Instance.Language = language;
        }

        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        var loc = Ui.LocalizationManager.Instance;
        string[] pets = { "Sylph", "Kibelisk" };
        string[] levels = { string.Format(loc["PetFarm.Level"], 7) + " (12/25)", loc["PetFarm.NotOwned"] };
        var lines = new System.Windows.Controls.StackPanel();
        for (int i = 0; i < pets.Length; i++)
        {
            lines.Children.Add(Ui.PetFarmWindow.BuildRow(pets[i], levels[i]));
        }

        var panel = new System.Windows.Controls.Border
        {
            CornerRadius = new System.Windows.CornerRadius(6), Padding = new System.Windows.Thickness(8, 5, 8, 5), Child = lines,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xB0, 0, 0, 0)),
            LayoutTransform = new System.Windows.Media.ScaleTransform(2, 2),
        };
        var visual = new System.Windows.Controls.Border { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2a, 0x36, 0x2c)), Padding = new System.Windows.Thickness(12), Child = panel };
        visual.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        visual.Arrange(new System.Windows.Rect(visual.DesiredSize));
        visual.UpdateLayout();
        int w = (int)Math.Ceiling(visual.DesiredSize.Width), h = (int)Math.Ceiling(visual.DesiredSize.Height);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        Console.WriteLine($"render-petfarm: wrote {path}");
        Environment.Exit(0);
    }

    private static void RunRenderPetListShopMode(string path)
    {
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var settings = Ui.MeterSettings.Load();
        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        // (pet id, tab, price) of the special and pet tabs; (pet id, level, progress) as the login frame reported them
        var offers = new (int, int, long)[] { (1041, 0, 70000), (1161, 0, 80000), (1128, 0, 90000), (1108, 0, 80000), (1118, 0, 70000), (1190, 1, 80000), (1123, 1, 90000), (1096, 1, 70000), (1174, 1, 90000), (1154, 1, 90000), (1160, 1, 70000), (1060, 1, 50000), (1115, 1, 90000), (1102, 1, 70000), (1141, 1, 90000) }
            .Select(o => new Aion2.Protocol.Aion2ShopPet(o.Item1, o.Item2, o.Item3)).ToList();
        var states = new (int, int, int)[] { (1128, 1, 6), (1108, 1, 18), (1118, 2, 17), (1190, 1, 0), (1123, 1, 20), (1096, 1, 5), (1174, 1, 16), (1160, 1, 0), (1060, 2, 27), (1141, 2, 12) }
            .Select(s => new Aion2.Protocol.Aion2PetState(s.Item1, s.Item2, s.Item3)).ToList();
        var rows = Ui.PetMapController.ShopRowsOf(offers, states).ToList();
        rows.Add((System.Windows.Media.Color.FromRgb(0xE6, 0x78, 0x50), "Sylphe", "Level 1 · 4/25", "38 m", false));
        var list = new Ui.PetListWindow();
        list.Render(rows);
        var content = (System.Windows.UIElement)list.Content;
        list.Content = null;
        var visual = new System.Windows.Controls.Border { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2a, 0x36, 0x2c)), Padding = new System.Windows.Thickness(12), Child = content };
        visual.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        visual.Arrange(new System.Windows.Rect(visual.DesiredSize));
        visual.UpdateLayout();
        int w = (int)Math.Ceiling(visual.DesiredSize.Width), h = (int)Math.Ceiling(visual.DesiredSize.Height);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        Console.WriteLine($"render-petlist-shop: wrote {path}");
        Environment.Exit(0);
    }

    private static void RunPetFarmReadMode(string path, string language)
    {
        using var frame = new System.Drawing.Bitmap(path);
        var bars = Ui.PetFarmReader.FindBars(frame);
        Console.WriteLine($"petfarm-read: {frame.Width}x{frame.Height}, {bars.Count} red bar(s): " + string.Join(", ", bars.Take(5).Select(b => $"{b.X},{b.Y} {b.Width}x{b.Height}")));
        var reader = new Ui.PetFarmReader();
        string? name = reader.ReadTargetNameAsync(frame, language).GetAwaiter().GetResult();
        Console.WriteLine($"petfarm-read: recognised '{name}'");
        if (name is not null)
        {
            var match = Ui.PetFarmReader.Match(name);
            Console.WriteLine(match is null ? "petfarm-read: no pet monster" : $"petfarm-read: monster '{match.Value.Name}' -> pet(s) " +
                string.Join(", ", match.Value.Pets.Select(p => $"{Aion2.Protocol.Aion2Pets.PetName(p, language)} ({p})")));
        }
    }

    /// <summary>Average hit per skill out of recordings, for <see cref="Aion2.Protocol.Aion2HitAverages"/>. Only exact hits of players on
    /// monsters count (no ticks, no heals, no estimates); the biggest 1 % of each skill are left out as outliers (a placeholder or a stacked burst).</summary>
    private static void RunAion2SkillAverages(string[] args)
    {
        string output = args[1];
        int port = 13328;
        var files = new List<string>();
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--port" && i + 1 < args.Length)
            {
                port = int.Parse(args[++i]);
            }
            else
            {
                files.Add(args[i]);
            }
        }

        var protocol = Aion2.Protocol.Aion2Protocol.Load();
        var bySkill = new Dictionary<int, List<long>>();
        foreach (string path in files)
        {
            using var source = new Aion2.Aion2PacketCombatSource(protocol);
            var directory = (Aion2.Aion2EntityDirectory)source.Entities;
            var hits = new List<(int Source, int Target, int Skill, long Amount)>();
            try
            {
                foreach (Aion2.Capture.TcpSegment segment in Aion2.Capture.SegmentRecording.Read(path))
                {
                    bool fromServer = segment.Source.EndsWith(":" + port, StringComparison.Ordinal);
                    if (!fromServer && !segment.Destination.EndsWith(":" + port, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    source.Ingest(segment with { FromServer = fromServer });
                    foreach (Combat.DamageEvent e in source.Poll(false).Damage)
                    {
                        if (!e.IsHeal && !e.IsTick && !e.Estimated && e.SkillId > 0)
                        {
                            hits.Add((e.SourceObjectId, e.TargetObjectId, e.SkillId, e.Amount));
                        }
                    }
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // a recording still being written ends in half a line: what was read before it counts
            }

            // Judged at the end of the recording, when the directory knows who is a monster and who a player.
            int kept = 0;
            foreach (var hit in hits)
            {
                if (directory.NpcIdOf(hit.Target) is null || directory.NpcIdOf(hit.Source) is not null || directory.IsKnownPlayer(hit.Target))
                {
                    continue;
                }

                if (!bySkill.TryGetValue(hit.Skill, out List<long>? list))
                {
                    bySkill[hit.Skill] = list = new List<long>();
                }

                list.Add(hit.Amount);
                kept++;
            }

            Console.WriteLine($"aion2-skill-averages: {Path.GetFileName(path)}: {hits.Count} hit(s), {kept} on monsters");
        }

        static (long Average, int Samples) Summarize(List<long> amounts)
        {
            amounts.Sort();
            int keep = amounts.Count >= 100 ? amounts.Count - amounts.Count / 100 : amounts.Count;
            return ((long)Math.Round(amounts.Take(keep).Average()), amounts.Count);
        }

        var skills = bySkill.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => Summarize(kv.Value));
        var families = bySkill.GroupBy(kv => kv.Key / 10).OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => Summarize(g.SelectMany(kv => kv.Value).ToList()));
        using (var stream = File.Create(output))
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("generated", DateTime.UtcNow.ToString("yyyy-MM-dd"));
            writer.WriteString("note", "Average hit of a skill (players' exact hits on monsters, top 1 percent left out) from recordings; read by Aion2HitAverages for the world boss estimate. Made by: AionDPS aion2-skill-averages.");
            writer.WriteNumber("recordings", files.Count);
            foreach ((string name, Dictionary<int, (long Average, int Samples)> table) in new[] { ("skills", skills), ("families", families) })
            {
                writer.WriteStartObject(name);
                foreach (var (id, row) in table.Where(kv => kv.Value.Samples >= 3))
                {
                    writer.WriteStartObject(id.ToString());
                    writer.WriteNumber("avg", row.Average);
                    writer.WriteNumber("n", row.Samples);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        Console.WriteLine($"aion2-skill-averages: {skills.Count} skill(s), {skills.Count(kv => kv.Value.Samples >= Aion2.Protocol.Aion2HitAverages.MinSamples)} with {Aion2.Protocol.Aion2HitAverages.MinSamples}+ hits, written to {output}");
    }

    /// <summary>Draws the crafting tool (the page with its overlay, and the configuration window) into one picture: a made-up plan, the stock of a recording; no window is ever shown.</summary>
    private static void RunRenderCraftMode(string path, string recording, string language)
    {
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var settings = Ui.MeterSettings.Load();
        Ui.LocalizationManager.Instance.Language = language.Length > 0 ? language : settings.Language.Length > 0 ? settings.Language : "de";
        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        // in the running meter the buttons, combo boxes, check boxes and scroll bars take their styles from the main window: it is made (never shown) to take them from there
        var hostWindow = new Ui.MainWindow();
        app.MainWindow = hostWindow;
        foreach (var type in new[] { typeof(System.Windows.Controls.Button), typeof(System.Windows.Controls.ComboBox), typeof(System.Windows.Controls.ComboBoxItem), typeof(System.Windows.Controls.CheckBox), typeof(System.Windows.Controls.Primitives.ScrollBar), typeof(System.Windows.Controls.ToolTip), typeof(System.Windows.Controls.MenuItem) })
        {
            if (hostWindow.Resources[type] is System.Windows.Style style)
            {
                app.Resources[type] = style;
            }
        }

        Aion2.Aion2Stock.Reset();
        if (recording.Length > 0)
        {
            using var source = new Aion2.Aion2PacketCombatSource(Aion2.Protocol.Aion2Protocol.Load());
            foreach (Aion2.Capture.TcpSegment segment in Aion2.Capture.SegmentRecording.Read(recording))
            {
                bool fromServer = segment.Source.EndsWith(":13328", StringComparison.Ordinal);
                if (!fromServer && !segment.Destination.EndsWith(":13328", StringComparison.Ordinal))
                {
                    continue;
                }

                source.Ingest(segment with { FromServer = fromServer });
                source.Poll(false);
            }
        }

        Aion2.CraftPlan.ReplaceFor("", new[]
        {
            new Aion2.CraftEntry { Recipe = 314016004, Count = 20 }, new Aion2.CraftEntry { Recipe = 314036002, Count = 10 },
            new Aion2.CraftEntry { Recipe = 111025001, Count = 1, AccountWide = false }, new Aion2.CraftEntry { Recipe = 213034001, Count = 2 },
            new Aion2.CraftEntry { Recipe = 514085002, Count = 10, AccountWide = false },
        });

        var craftPage = new Ui.CraftPage(() => null);
        var module = new Ui.ModulePage("", craftPage, s => true, (s, v) => { }, s => "Ctrl+Alt+R", () => { }, () => { }, s => { }, null, craftPage.ModeBar);
        // the page inside the tools window, with its title bar and its list of tools
        var groups = Ui.ToolsWindow.Groups();
        var toolsWindow = new Ui.ToolsWindow(groups, id => id == "craft" ? new Ui.ToolPage { Content = module } : null, _ => { });
        toolsWindow.Select("craft");
        var page = (System.Windows.UIElement)toolsWindow.Content;
        toolsWindow.Content = null;
        var results = Ui.CraftText.Results(null, settings);
        var overlayLines = Ui.CraftOverlayController.OverlayLines(results, settings, sample: false);
        var overlay = new Ui.CraftOverlayWindow();
        overlay.Render(overlayLines.Caption, overlayLines.Lines, overlayLines.More);
        var overlayContent = overlay.DetachBody()!;
        var window = new Ui.CraftConfigWindow(() => null);
        // the whole frame of the window (title bar with icon, title, minimize and close), lifted out of the never shown window
        var config = (System.Windows.UIElement)window.Content;
        window.Content = null;

        System.Windows.Media.Brush back = (System.Windows.Media.Brush)app.Resources["Brush.Window"] ?? System.Windows.Media.Brushes.Black;
        // one part only (AIONDPS_PART = window | config | overlay), for the pictures of the website: AIONDPS_RENDER_SCALE=2 makes them sharp, the overlay is drawn on a see-through ground
        string part = Environment.GetEnvironmentVariable("AIONDPS_PART") ?? "all";
        if (part is "window" or "config" or "overlay")
        {
            int pictureScale = int.TryParse(Environment.GetEnvironmentVariable("AIONDPS_RENDER_SCALE"), out int ps) && ps is >= 1 and <= 4 ? ps : 1;
            System.Windows.FrameworkElement single = part switch
            {
                "window" => new System.Windows.Controls.Border { Width = 980, Height = 640, Background = back, Child = page },
                "config" => new System.Windows.Controls.Border { Width = 720, Height = 1030, Background = back, Child = config },
                _ => new System.Windows.Controls.Border { Child = overlayContent, HorizontalAlignment = System.Windows.HorizontalAlignment.Left },
            };
            single.Measure(new System.Windows.Size(1200, 2400));
            int sw = (int)Math.Ceiling(single.DesiredSize.Width), sh = (int)Math.Ceiling(single.DesiredSize.Height);
            single.Measure(new System.Windows.Size(sw, sh));
            single.Arrange(new System.Windows.Rect(0, 0, sw, sh));
            single.UpdateLayout();
            var shot = new System.Windows.Media.Imaging.RenderTargetBitmap(sw * pictureScale, sh * pictureScale, 96 * pictureScale, 96 * pictureScale, System.Windows.Media.PixelFormats.Pbgra32);
            shot.Render(single);
            var shotEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            shotEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(shot));
            using (var shotFile = File.Create(path))
            {
                shotEncoder.Save(shotFile);
            }

            Console.WriteLine($"render-craft: wrote {path} ({part}, {sw}x{sh} at x{pictureScale})");
            Environment.Exit(0);
        }

        var left = new System.Windows.Controls.StackPanel { Width = 980 };
        left.Children.Add(new System.Windows.Controls.Border { Width = 980, Height = 860, Background = back, Child = page });
        var strip = new System.Windows.Controls.Border { Padding = new System.Windows.Thickness(20), Background = new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Color.FromRgb(0x3a, 0x4a, 0x52), System.Windows.Media.Color.FromRgb(0x1c, 0x26, 0x2e), 60) };
        strip.Child = new System.Windows.Controls.Border { Child = overlayContent, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        left.Children.Add(strip);
        var right = new System.Windows.Controls.Border { Width = 720, Height = 1030, Background = back, Child = config, VerticalAlignment = System.Windows.VerticalAlignment.Top };
        var root = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Background = System.Windows.Media.Brushes.Black };
        root.Children.Add(left);
        root.Children.Add(new System.Windows.Controls.Border { Width = 20 });
        root.Children.Add(right);
        root.Measure(new System.Windows.Size(1740, 2400));
        root.Arrange(new System.Windows.Rect(0, 0, 1740, root.DesiredSize.Height));
        root.UpdateLayout();
        int w = 1740, h = (int)Math.Ceiling(root.DesiredSize.Height);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        // the question every configuration window asks when it is closed with changes
        var ask = Ui.ThemedMessageBox.Build(null, Ui.LocalizationManager.Instance["Config.Unsaved"], Ui.LocalizationManager.Instance["Craft.ConfigTitle"], System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Question, null, null, out _, (Ui.LocalizationManager.Instance["Config.Save"], Ui.LocalizationManager.Instance["Config.Discard"], Ui.LocalizationManager.Instance["Config.Cancel"]));
        ask.MinWidth = 0;
        var dialog = (System.Windows.UIElement)ask.Content;
        dialog.Measure(new System.Windows.Size(560, double.PositiveInfinity));
        int dw = (int)Math.Ceiling(dialog.DesiredSize.Width), dh = (int)Math.Ceiling(dialog.DesiredSize.Height);
        dialog.Measure(new System.Windows.Size(dw, dh));
        dialog.Arrange(new System.Windows.Rect(0, 0, dw, dh));
        dialog.UpdateLayout();
        var dialogBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(dw, dh, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        dialogBitmap.Render(dialog);
        var dialogEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        dialogEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(dialogBitmap));
        using (var file = File.Create(Path.ChangeExtension(path, null) + "-dialog.png"))
        {
            dialogEncoder.Save(file);
        }

        Console.WriteLine("render-craft: wrote " + path);
        Environment.Exit(0);
    }

    /// <summary>Checks that the settings window knows when something was changed (it asks before it closes then): the state read from the controls is the same twice and differs after a change. No window is shown.</summary>
    private static void RunSettingsDirtyCheckMode()
    {
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var settings = Ui.MeterSettings.Load();
        Ui.ThemeManager.Apply(app, settings);
        Ui.MainWindow.Headless = true;
        var window = new Ui.SettingsWindow(settings);
        var snapshot = typeof(Ui.SettingsWindow).GetMethod("Snapshot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        string first = (string)snapshot.Invoke(window, null)!;
        string second = (string)snapshot.Invoke(window, null)!;
        var box = (System.Windows.Controls.CheckBox)window.FindName("AlwaysOnTopBox");
        box.IsChecked = !(box.IsChecked ?? false);
        string changed = (string)snapshot.Invoke(window, null)!;
        box.IsChecked = !(box.IsChecked ?? false);
        string back = (string)snapshot.Invoke(window, null)!;
        Console.WriteLine($"settings-dirty-check: stable {first == second}, changed after a switch {changed != first}, back to the start {back == first}");
        Environment.Exit(first == second && changed != first && back == first ? 0 : 1);
    }

    /// <summary>Replays a recording and prints the three stocks the crafting tool counts (see <see cref="Aion2.Aion2Stock"/>); no window, nothing saved.</summary>
    private static void RunAion2StockCheckMode(string path, int serverPort)
    {
        var protocol = Aion2.Protocol.Aion2Protocol.Load();
        using var source = new Aion2.Aion2PacketCombatSource(protocol);
        Aion2.Aion2Stock.Reset();
        Aion2.Aion2Recipes.Reset();
        foreach (Aion2.Capture.TcpSegment segment in Aion2.Capture.SegmentRecording.Read(path))
        {
            bool fromServer = segment.Source.EndsWith(":" + serverPort, StringComparison.Ordinal);
            if (!fromServer && !segment.Destination.EndsWith(":" + serverPort, StringComparison.Ordinal))
            {
                continue;
            }

            source.Ingest(segment with { FromServer = fromServer });
            source.Poll(false);
        }

        Console.WriteLine($"aion2-stock-check: login frame {Aion2.Aion2Stock.SnapshotAt:HH:mm:ss}, last full listing {Aion2.Aion2Stock.ListedAt:HH:mm:ss}, learned recipes: {(Aion2.Aion2Recipes.HasData ? Aion2.Aion2CraftData.All.Count(r => Aion2.Aion2Recipes.IsKnown(r) && !r.Hidden) + " of " + Aion2.Aion2CraftData.All.Count(r => !r.Hidden) : "no list")}");
        if (source.Entities is Aion2.Aion2EntityDirectory ownedDirectory)
        {
            Console.WriteLine("aion2-stock-check: owned " + string.Join(", ", ownedDirectory.OwnedCounts().Select(o => $"{o.Key} {o.Value}")));
        }

        foreach ((string label, Aion2.StockSources sources) in new[] { ("Inventory", Aion2.StockSources.Inventory), ("Character storage", Aion2.StockSources.CharacterStorage), ("Account storage", Aion2.StockSources.AccountStorage) })
        {
            Dictionary<int, long> totals = Aion2.Aion2Stock.Totals(sources);
            Console.WriteLine($"-- {label}: {totals.Count} kinds, {totals.Values.Sum():N0} pieces");
            foreach ((int item, long count) in totals.OrderBy(t => t.Key))
            {
                Console.WriteLine($"   {Aion2.Protocol.Aion2ItemNames.NameOf(item, "de") ?? "#" + item} x{count:N0}");
            }
        }
    }

    private static void RunAion2OpcodeCensus(string[] paths, int serverPort)
    {
        var watch = new Aion2.Capture.UnknownFrameWatch(Aion2.Capture.UnknownFrameWatch.LoadKnown());
        int points = 0, frames = 0;
        var examples = new Dictionary<int, string>();
        foreach (string path in paths)
        {
            using var source = new Aion2.Aion2PacketCombatSource(Aion2.Protocol.Aion2Protocol.Load());
            Aion2.Protocol.Aion2FrameDecoder.FrameTap = (opcode, frame, at) =>
            {
                frames++;
                if (watch.Note(opcode, frame, at) is { } point)
                {
                    points++;
                    examples.TryAdd(opcode, Convert.ToHexString(frame[..Math.Min(frame.Length, 24)]));
                }
            };
            foreach (Aion2.Capture.TcpSegment segment in Aion2.Capture.SegmentRecording.Read(path))
            {
                bool fromServer = segment.Source.EndsWith(":" + serverPort, StringComparison.Ordinal);
                if (!fromServer && !segment.Destination.EndsWith(":" + serverPort, StringComparison.Ordinal))
                {
                    continue;
                }

                source.Ingest(segment with { FromServer = fromServer });
                source.Poll(false);
            }
        }

        Aion2.Protocol.Aion2FrameDecoder.FrameTap = null;
        Console.WriteLine($"aion2-opcode-census: {frames:N0} frames, {watch.UnknownOpcodes} unknown opcodes, {points} debug points (each would give a tone)");
        foreach (var u in watch.Unknown.OrderByDescending(u => u.Value.Count).Take(40))
        {
            Console.WriteLine($"  0x{u.Key:x4}  {u.Value.Count,7}x  {examples.GetValueOrDefault(u.Key)}");
        }
    }

    /// <summary>Replays recordings and measures the own-position estimate against the exact reports (every effect frame of the player carries his place): by the age of the previous report, and for several trust times.</summary>
    private static void RunAion2FrameDump(string path, int opcode, int max, int serverPort)
    {
        var protocol = Aion2.Protocol.Aion2Protocol.Load();
        using var source = new Aion2.Aion2PacketCombatSource(protocol);
        int shown = 0, total = 0;
        Aion2.Protocol.Aion2FrameDecoder.FrameTap = (op, frame, at) =>
        {
            if (op != opcode)
            {
                return;
            }

            total++;
            if (op == Aion2.Aion2LegionList.Opcode && shown < max && Aion2.Aion2LegionList.TryParse(frame, out List<Aion2.LegionMember> legion))
            {
                Console.WriteLine($"  legion list: {legion.Count} members ({legion.Count(m => m.ServerId == legion[0].ServerId)} on server {legion[0].ServerId}): " + string.Join(", ", legion.Select(m => $"{m.Name} L{m.Level} {Aion2.Protocol.Aion2SkillNames.ClassFromCode(m.ClassCode)} {m.ItemLevel}/{m.CombatPower}")));
            }

            if (shown++ < max)
            {
                Console.WriteLine($"{at:HH:mm:ss.fff} len {frame.Length}: {Convert.ToHexString(frame)}");
            }
        };
        foreach (Aion2.Capture.TcpSegment segment in Aion2.Capture.SegmentRecording.Read(path))
        {
            bool fromServer = segment.Source.EndsWith(":" + serverPort, StringComparison.Ordinal);
            if (!fromServer && !segment.Destination.EndsWith(":" + serverPort, StringComparison.Ordinal))
            {
                continue;
            }

            source.Ingest(segment with { FromServer = fromServer });
            source.Poll(false);
        }

        Aion2.Protocol.Aion2FrameDecoder.FrameTap = null;
        Console.WriteLine($"aion2-frame-dump: {total} frames of opcode 0x{opcode:x4}");
    }

    private static void RunAion2PositionCheckMode(string[] paths, int serverPort)
    {
        var variants = new List<(string Name, double MaxAge, Aion2.Aion2EntityDirectory.AroundMode How, int Min)>();
        foreach (double age in new[] { 5.0, 10.0, 20.0, 60.0, 120.0 })
        {
            foreach (var how in new[] { Aion2.Aion2EntityDirectory.AroundMode.Mean, Aion2.Aion2EntityDirectory.AroundMode.Median, Aion2.Aion2EntityDirectory.AroundMode.Trimmed })
            {
                variants.Add(($"{how} <= {age:0}s", age, how, 3));
            }
        }

        var probes = new List<(double Age, double Prev, double[] Env, int Entities)>();
        foreach (string path in paths)
        {
            var protocol = Aion2.Protocol.Aion2Protocol.Load();
            using var source = new Aion2.Aion2PacketCombatSource(protocol);
            var directory = (Aion2.Aion2EntityDirectory)source.Entities;
            DateTime clock = DateTime.MinValue;
            directory.Clock = () => clock;
            directory.PositionProbe += p =>
            {
                double[] env = variants.Select(v => directory.AroundAt(p.Now, v.MaxAge, v.How, v.Min) is { } e ? Math.Sqrt(((double)e.X - p.X) * (e.X - p.X) + ((double)e.Y - p.Y) * (e.Y - p.Y)) / 100.0 : double.NaN).ToArray();
                probes.Add((p.PreviousAgeSeconds, p.PreviousError, env, p.Entities));
            };
            foreach (Aion2.Capture.TcpSegment segment in Aion2.Capture.SegmentRecording.Read(path))
            {
                bool fromServer = segment.Source.EndsWith(":" + serverPort, StringComparison.Ordinal);
                if (!fromServer && !segment.Destination.EndsWith(":" + serverPort, StringComparison.Ordinal))
                {
                    continue;
                }

                clock = segment.Timestamp;
                source.Ingest(segment with { FromServer = fromServer });
                source.Poll(false);
            }
        }

        static double Median(IEnumerable<double> v) { var l = v.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList(); return l.Count == 0 ? double.NaN : l[l.Count / 2]; }
        static double P90(IEnumerable<double> v) { var l = v.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList(); return l.Count == 0 ? double.NaN : l[(int)(l.Count * 0.9)]; }
        Console.WriteLine($"aion2-position-check: {probes.Count} exact reports with a previous one");
        var buckets = new[] { (0.0, 1.0), (1.0, 2.5), (2.5, 5.0), (5.0, 15.0), (15.0, 1e9) };
        Console.WriteLine("error in metres, median / p90, by the age of the previous exact report (count in brackets); '-' = no environment of 3+ entities");
        Console.Write($"{"",-22}");
        foreach ((double lo, double hi) in buckets)
        {
            Console.Write($"{lo:0.#}-{(hi > 1e8 ? "inf" : hi.ToString("0.#"))} ({probes.Count(p => p.Age >= lo && p.Age < hi)})".PadRight(20));
        }

        Console.WriteLine();
        Console.Write($"{"previous report",-22}");
        foreach ((double lo, double hi) in buckets)
        {
            var sel = probes.Where(p => p.Age >= lo && p.Age < hi).ToList();
            Console.Write($"{Median(sel.Select(p => p.Prev)):0.0} / {P90(sel.Select(p => p.Prev)):0.0}".PadRight(20));
        }

        Console.WriteLine();
        for (int k = 0; k < variants.Count; k++)
        {
            Console.Write($"{variants[k].Name,-22}");
            foreach ((double lo, double hi) in buckets)
            {
                var sel = probes.Where(p => p.Age >= lo && p.Age < hi).Select(p => p.Env[k]).ToList();
                Console.Write($"{Median(sel):0.0} / {P90(sel):0.0} [{sel.Count(v => !double.IsNaN(v))}]".PadRight(20));
            }

            Console.WriteLine();
        }
    }

    private static void RunAion2ReplayMode(string path, int serverPort, string? ownName)
    {
        var protocol = Aion2.Protocol.Aion2Protocol.Load();
        using var source = new Aion2.Aion2PacketCombatSource(protocol);
        (source.Entities as Aion2.Aion2EntityDirectory)?.SetConfiguredLocalName(ownName);
        if (source.Entities is Aion2.Aion2EntityDirectory idWatch)
        {
            idWatch.LocalIdChanged += (a, b) => Console.WriteLine($"aion2-replay: own combat id changed {a} -> {b}");
            int moved = 0;
            idWatch.IdentityMoved += (a, b) => { if (++moved <= 6) { Console.WriteLine($"aion2-replay: player {idWatch.NameFor(b)} announced under a new id {a} -> {b}"); } };
        }

        var events = new List<Combat.DamageEvent>();
        var stays = new History.SessionRecorder(zone => Aion2.Protocol.Aion2Zones.IsInstance(zone) ? History.SessionKind.Instance : History.SessionKind.Map);
        if (source.Entities is Aion2.Aion2EntityDirectory replayDirectory)
        {
            stays.Identify = id => (replayDirectory.NameFor(id) ?? replayDirectory.MonsterNameFor(id), replayDirectory.ClassOf(id));
            stays.HpSource = (id, from, to) =>
            {
                var samples = replayDirectory.HitPoints.SamplesAround(id, from, to);
                return samples.Count < 2 ? null : new History.TargetHp(replayDirectory.HitPoints.HighestSeen(id) ?? 0, samples, replayDirectory.HitPoints.ResetsOf(id));
            };
        }
        int segments = 0;
        string? petLog = null;
        {
            // A replay never writes to the real pet-farm.log: its lines go to a throw-away file.
            petLog = Path.Combine(Path.GetTempPath(), "aion2-replay-pet-farm.log");
            File.Delete(petLog);
            Aion2.PetFarmLog.PathOverride = petLog;
        }

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
            Combat.Sources.CombatBatch polled = source.Poll(false);
            events.AddRange(polled.Damage);
            stays.Feed(polled);
            if (source.Entities is Aion2.Aion2EntityDirectory ownersIn)
            {
                // summons whose owner turned up later: their earlier hits go to the owner, as the meter does
                foreach ((int summon, int owner) in ownersIn.DrainResolvedOwners())
                {
                    stays.Reattribute(summon, owner);
                    for (int i = 0; i < events.Count; i++)
                    {
                        if (events[i].SourceObjectId == summon)
                        {
                            events[i] = events[i] with { SourceObjectId = owner };
                        }
                    }
                }
            }
            foreach (Combat.Sources.ZoneChange zone in polled.ZoneChanges)
            {
                Console.WriteLine($"aion2-replay: zone {zone.ZoneId} entered at {zone.Timestamp:yyyy-MM-dd HH:mm:ss}");
            }
        }

        if (events.Count > 0)
        {
            stays.CloseCurrent(events[^1].Timestamp);
        }

        foreach (History.SessionInfo stay in stays.Sessions)
        {
            Console.WriteLine($"aion2-replay: session {stay.Number} zone {stay.ZoneId} {stay.Kind} {stay.Start:HH:mm:ss} - {(stay.End is DateTime stayEnd ? stayEnd.ToString("HH:mm:ss") : "open")}: {stay.Data.Events.Count} event(s)");
            // The plausibility check and the rage guess for every boss fight of the stay (see History/SessionAnalysis.cs).
            History.SessionAnalysis analysis = History.SessionAnalyzer.Analyze(stay);
            foreach (History.BossAttempt attempt in analysis.Attempts)
            {
                string rage = attempt.Enrage is { } r ? $"rage after {r.At:mm\\:ss} x{r.Factor}" : "no rage seen";
                string check = attempt.Check is { } c ? $"{c.Ratio:P0} of {c.Lost:N0} HP lost, {(c.Killed ? "killed" : "alive")}" : "no HP data";
                Console.WriteLine($"aion2-replay:   boss {attempt.Name} {attempt.Start:HH:mm:ss} for {attempt.Duration:mm\\:ss}: {attempt.Counted:N0} damage, {check}, {rage}");
                if (Environment.GetEnvironmentVariable("AION2_ENRAGE") == "1")
                {
                    // Boss damage per 30 s, to look at by eye where it jumps.
                    var bins = stay.Data.Events.Where(e => !e.IsHeal && e.SourceObjectId == attempt.TargetId && e.Timestamp >= attempt.Start && e.Timestamp <= attempt.End)
                        .GroupBy(e => (int)((e.Timestamp - attempt.Start).TotalSeconds / 30)).OrderBy(g => g.Key).Select(g => $"{g.Key * 30 / 60}:{g.Key * 30 % 60:00}={g.Sum(e => e.Amount):N0}");
                    Console.WriteLine("aion2-replay:     boss damage per 30 s: " + string.Join("  ", bins));
                }
            }
        }

        if (source.Entities is Aion2.Aion2EntityDirectory rowsDir && events.Count > 0)
        {
            DateTime rowsFrom = events[^1].Timestamp.AddMinutes(-10);
            foreach (var g in events.Where(e => !e.IsHeal && e.Timestamp >= rowsFrom && rowsDir.ClassOf(e.SourceObjectId) is not null).GroupBy(e => e.SourceObjectId).OrderByDescending(g => g.Sum(e => (long)e.Amount)))
            {
                Console.WriteLine($"aion2-replay: rows id {g.Key} name {rowsDir.NameFor(g.Key)} class {rowsDir.ClassOf(g.Key)} hits {g.Count()} dmg {g.Sum(e => (long)e.Amount):N0} first {g.Min(e => e.Timestamp):HH:mm:ss} last {g.Max(e => e.Timestamp):HH:mm:ss} skills {string.Join(",", g.GroupBy(e => e.SkillId).OrderByDescending(x => x.Count()).Take(4).Select(x => x.Key))}");
            }
        }

        foreach (Aion2.LootSession loot in Aion2.Aion2Loot.Sessions.AsEnumerable().Reverse())
        {
            Console.WriteLine($"aion2-replay: loot session {loot.Start:HH:mm:ss} - {loot.End:HH:mm:ss}");
            foreach (Aion2.LootMap map in loot.Maps)
            {
                Console.WriteLine($"aion2-replay:   zone {map.Zone}: " + string.Join(", ", map.Items.Select(i => $"{Aion2.Protocol.Aion2ItemNames.NameOf(i.ItemId, "en") ?? "#" + i.ItemId} x{i.Count}")));
            }
        }

        if (source.Entities is Aion2.Aion2EntityDirectory idEnd)
        {
            Console.WriteLine($"aion2-replay: {idEnd.IdentityMovesSeen} player(s) came back under a new combat id");
        }

        if (Environment.GetEnvironmentVariable("AION2_STATS") == "1" && source.Entities is Aion2.Aion2EntityDirectory statsDirectory && statsDirectory.LocalStats is { } stats)
        {
            Console.WriteLine($"aion2-replay: stat frame of the own character, {stats.Count} ids: " + string.Join("  ", stats.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
        }

        if (Environment.GetEnvironmentVariable("AION2_STATREC") is { Length: > 0 } statRecPath && source.Entities is Aion2.Aion2EntityDirectory recDirectory && recDirectory.LocalStats is { } recStats && events.Count > 0)
        {
            // a stat recording over the whole replay, to check the document's size and its shape against the server on real data (nothing is sent)
            DateTime recStart = events.Min(e => e.Timestamp), recEnd = events.Max(e => e.Timestamp);
            int recLocal = recDirectory.LocalPlayerId;
            var recMine = events.Where(e => e.SourceObjectId == recLocal).ToList();
            Combat.FightExtras recExtras = recDirectory.Combat.Slice(recStart, recEnd, _ => null);
            Aion2.Aion2CharacterInfo? recCharacter = recDirectory.LocalCharacter;
            int? recServer = recCharacter is { ServerId: > 0 } ? recCharacter.ServerId : null;
            var recSetup = new Aion2.StatRecordingSetup("expert", "back", 60, 30, 0, 0, 0, 0, 0, 150, 100);
            string recJson = Aion2.StatRecording.Build(new string('a', 32), recStart, recEnd, recSetup,
                new Aion2.StatRecordingCharacter(recCharacter is { ClassCode: > 0 } ? (recCharacter.ClassCode - 1) / 4 : null, recCharacter?.Level, recServer, Aion2.StatRecording.FactionOf(recServer)),
                recStats, recStats, Array.Empty<Aion2.StatChange>(), recMine, recExtras, recLocal, id => recDirectory.NpcIdOf(id), id => recDirectory.MonsterNameFor(id));
            File.WriteAllText(statRecPath, recJson);
            Console.WriteLine($"aion2-replay: stat recording of {recMine.Count} own events written to {statRecPath} ({recJson.Length / 1024} KB)");
        }

        Console.WriteLine($"aion2-replay: tickets belong now to {Aion2.Aion2Tickets.CurrentName ?? "nobody"}");
        foreach (Aion2.TicketCharacter ticketed in Aion2.Aion2Tickets.Snapshot())
        {
            ticketed.Tickets.TryGetValue(Aion2.Aion2Tickets.OdyleId, out Aion2.TicketValue odyle);
            ticketed.Tickets.TryGetValue(Aion2.Aion2Tickets.MissionsId, out Aion2.TicketValue missions);
            ticketed.Tickets.TryGetValue(Aion2.Aion2Tickets.PremiumId, out Aion2.TicketValue premium);
            Console.WriteLine($"aion2-replay: tickets of {ticketed.Name} (server {ticketed.ServerId}): Odyle {odyle.Count} + {odyle.Bonus}, missions {missions.Count} handed in, premium until {(premium.TimeMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(premium.TimeMs).ToLocalTime().ToString("g") : "none")}");
        }

        if (events.Count > 0)
        {
            // what the hit data says about how hits landed, and whether the flags do what they are called (a double hit should do more than the same skill without it)
            Console.WriteLine("aion2-replay: hit kinds (hits with the flag; skills compared; median damage ratio with / without):");
            foreach (var effect in Combat.HitKindStats.Effects(events))
            {
                Console.WriteLine($"  {effect.Name,-8} {effect.Hits,7} hits  {effect.SkillsCompared,3} skills  x{effect.MedianRatio:0.00}");
            }
        }

        if (source.Entities is Aion2.Aion2EntityDirectory combatDirectory)
        {
            if (events.Count > 1 && source.Entities.LocalPlayerId >= 0)
            {
                // the rotation view's numbers for the local player over the whole recording
                DateTime from = events.Min(e => e.Timestamp), to = events.Max(e => e.Timestamp);
                var extras = combatDirectory.Combat.Slice(from, to, id => source.Entities.NameFor(id));
                var replayExtras = combatDirectory.Combat.Slice(from, to, id => source.Entities.NameFor(id), _ => true);
                Console.WriteLine($"aion2-replay: replay data: {replayExtras.Tracks?.Count ?? 0} tracks ({replayExtras.Tracks?.Sum(t => t.T.Length) ?? 0} points), {replayExtras.Windups?.Count ?? 0} windups, {replayExtras.Deaths?.Count ?? 0} deaths");
                foreach (var track in (replayExtras.Tracks ?? Array.Empty<Combat.PositionTrack>()).OrderByDescending(t => t.T.Length).Take(4))
                {
                    Console.WriteLine($"  entity {track.Entity,6} {source.Entities.NameFor(track.Entity),-14} {track.T.Length,5} points over {track.T[^1] / 1000.0,7:0} s, x {track.X.Min()}..{track.X.Max()} y {track.Y.Min()}..{track.Y.Max()}");
                }

                var own = events.Where(e => e.SourceObjectId == source.Entities.LocalPlayerId).ToList();
                var report = Combat.RotationAnalysis.Build(extras, source.Entities.LocalPlayerId, from, to, own, id => source.Entities.NameFor(id));
                Console.WriteLine($"aion2-replay: rotation of the local player: {report.Uses.Count} skill uses ({report.Skills.Count} skills), {report.Buffs.Count} effects, {report.Staggers.Count} staggers, {extras.Staggers.Count} stagger readings, {extras.Buffs.Count} effect events");
                Console.WriteLine("  opener: " + string.Join(" > ", report.Opener.Select(u => $"{Aion2.Protocol.Aion2SkillNames.Display(u.Name)}@{u.At:0.0}")));
                foreach (var skill in report.Skills.Take(6))
                {
                    Console.WriteLine($"  {skill.Name,-26} {skill.Uses,4} uses {skill.PerMinute,5:0.0}/min gap {skill.AvgGapSeconds,5:0.0}s share {skill.SharePercent,5:0.0}%");
                }

                foreach (var buff in report.Buffs.Take(6))
                {
                    Console.WriteLine($"  buff {buff.Name,-24} {buff.UptimePercent,5:0.0}% (you {buff.FromYouPercent,5:0.0}%) x{buff.Applications} {buff.Givers}");
                }
            }

            var lessons = combatDirectory.Combat.Lessons();
            Console.WriteLine($"aion2-replay: {lessons.Count} skill(s) learnt from casts and hits, {lessons.Count(l => l.Lesson.IsArea)} of them areas");
            foreach (var (skillId, lesson) in lessons.Where(l => l.Lesson.Seen > 0).Take(14))
            {
                Console.WriteLine($"  {skillId,9} {Aion2.Protocol.Aion2SkillNames.NameOf(skillId),-28} cast {lesson.CastMs,5} ms  seen {lesson.Seen,3}  wide {lesson.Wide,3}  single {lesson.Single,3}  lands {(lesson.LandMs is int land ? land + " ms" : "?"),-7} max on you {lesson.MaxOnYou}");
            }
        }

        Console.WriteLine($"aion2-replay: {segments} segment(s) on port {serverPort}, {events.Count} damage/heal event(s), calibrated={protocol.IsCalibrated}");
        if (events.Any(e => e.Estimated))
        {
            Console.WriteLine($"aion2-replay: {events.Count(e => e.Estimated)} of them estimated from hit notices ({events.Where(e => e.Estimated).Sum(e => e.Amount):N0} damage, {events.Where(e => e.Estimated).Select(e => e.SourceObjectId).Distinct().Count()} player(s)): world boss, the server sent no amounts");
        }
        foreach (var bossMap in Aion2.Aion2FieldBosses.Maps.Where(m => m.Id != 0))
        {
            var now = DateTime.Now;
            var lines = bossMap.Bosses.Select(b => (Boss: b, Status: Aion2.Aion2FieldBosses.StatusOf(b, now))).Where(x => x.Status.Phase != Aion2.BossPhase.Unknown).ToList();
            Console.WriteLine($"aion2-replay: field bosses of {bossMap.NameIn("en")} (list {bossMap.Id}): {lines.Count} of {bossMap.Bosses.Count} with a state");
            foreach (var (boss, status) in lines)
            {
                Console.WriteLine($"  {boss.NameIn("en"),-34} {status.Phase,-8} {(status.At is { } at ? at.ToString("yyyy-MM-dd HH:mm:ss") : "")}");
            }
        }

        // AION2_DUMP=<file>: every event as CSV (time, source id and name, target id, amount, heal, tick,
        // crit, skill) for working out how a total could be reproduced from the recording.
        if (Environment.GetEnvironmentVariable("AION2_DUMP") is { Length: > 0 } dumpPath)
        {
            using var dump = new StreamWriter(dumpPath);
            dump.WriteLine("time;source;sourceName;target;amount;heal;tick;crit;skill;viaSummon");
            foreach (var e in events)
            {
                dump.WriteLine(string.Join(';', e.Timestamp.ToString("O"), e.SourceObjectId, source.Entities.NameFor(e.SourceObjectId), e.TargetObjectId,
                    e.Amount, e.IsHeal ? 1 : 0, e.IsTick ? 1 : 0, e.IsCritical ? 1 : 0, e.Skill, e.ViaSummon ? 1 : 0));
            }
        }

        if (petLog is not null && source.Entities is Aion2.Aion2EntityDirectory petDirectory)
        {
            var pets = petDirectory.LocalPetStates;
            Console.WriteLine($"aion2-replay: {pets.Count} pet(s) in the pet list, {pets.Count(p => p.Level >= 3)} at the top level");
            foreach (var pet in pets.Where(p => p.Level >= 2).OrderByDescending(p => p.Level).ThenByDescending(p => p.Progress).Take(12))
            {
                Console.WriteLine($"  {Aion2.Protocol.Aion2Pets.PetName(pet.PetId, "de") ?? "?",-30} level {pet.Level}  {pet.Progress}/{Aion2.Protocol.Aion2Pets.ProgressNeeded(pet.Level)}");
            }

            var factions = petDirectory.Snapshot().Names.Keys.GroupBy(id => petDirectory.FactionOf(id) ?? "unknown").ToDictionary(g => g.Key, g => g.Count());
            var unknownNames = petDirectory.Snapshot().Names.Where(kv => petDirectory.FactionOf(kv.Key) is null).ToList();
            Console.WriteLine("aion2-replay: unknown faction, sample: " + string.Join(" | ", unknownNames.Take(40).Select(kv => $"{kv.Value}{(petDirectory.IsKnownPlayer(kv.Key) ? "(P)" : "")}{(petDirectory.NpcIdOf(kv.Key) is not null ? "(npc)" : "")}")));
            var bitByServerFaction = petDirectory.Snapshot().Names.Keys
                .Where(id => petDirectory.ServerIdOf(id) is int sv && sv / 1000 is 1 or 2)
                .GroupBy(id => (Server: petDirectory.ServerIdOf(id)!.Value / 1000, Bit: petDirectory.SeenProfileOf(id)?.ClassBits ?? -1))
                .OrderBy(g => g.Key.Server).ThenBy(g => g.Key.Bit).Select(g => $"server {g.Key.Server}xxx class bits {g.Key.Bit}: {g.Count()}");
            Console.WriteLine("aion2-replay: class bits of players with a known server: " + string.Join(" | ", bitByServerFaction));
            var actors = events.Select(e => e.SourceObjectId).Distinct().Where(id => petDirectory.NpcIdOf(id) is null && petDirectory.NameFor(id) is not null).ToList();
            Console.WriteLine($"aion2-replay: players that dealt damage or healed: {actors.Count}, faction known for {actors.Count(id => petDirectory.FactionOf(id) is not null)} ({actors.Count(id => petDirectory.FactionOf(id) == "Asmodian")} Asmodian)");
            Console.WriteLine($"aion2-replay: of {unknownNames.Count} unknown, {unknownNames.Count(kv => petDirectory.IsKnownPlayer(kv.Key))} are known players, {unknownNames.Count(kv => petDirectory.NpcIdOf(kv.Key) is not null)} are npcs");
            Console.WriteLine("aion2-replay: factions of the named players: " + string.Join(", ", factions.Select(kv => $"{kv.Key} {kv.Value}")));
            var detected = Aion2.Protocol.Aion2Maps.Detect(petDirectory.RecentMobPositions());
            Console.WriteLine($"aion2-replay: map detected from the last {petDirectory.RecentMobPositions().Count} monsters: {detected?.Title ?? "none"}; own position {(petDirectory.LocalPosition is { } lp ? $"({lp.X:0}, {lp.Y:0})" : "unknown")}");
            foreach (var mob in petDirectory.RecentMobPositions().TakeLast(6))
            {
                Console.WriteLine($"aion2-replay:   recent monster {Aion2.Protocol.Aion2Npcs.NameOf(mob.NpcId, "en") ?? "?"} [{mob.NpcId}] at ({mob.X:0}, {mob.Y:0}), pets {string.Join("/", Aion2.Protocol.Aion2Pets.PetsOfNpc(mob.NpcId))}");
            }

            if (petDirectory.LocalTarget is { } target)
            {
                var petsOfTarget = target.NpcId is int tn ? Aion2.Protocol.Aion2Pets.PetsOfNpc(tn) : Array.Empty<int>();
                Console.WriteLine($"aion2-replay: last marked monster: entity {target.EntityId}, npc {target.NpcId} ({(target.NpcId is int n2 ? Aion2.Protocol.Aion2Npcs.NameOf(n2, "en") : "?")}) -> pet(s) " +
                    string.Join(", ", petsOfTarget.Select(p => Aion2.Protocol.Aion2Pets.PetName(p, "de"))));
            }

            if (File.Exists(petLog))
            {
                Console.WriteLine("aion2-replay: pet farm log:");
                foreach (string line in File.ReadLines(petLog).Take(40))
                {
                    Console.WriteLine("  " + line);
                }
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

        if (source.Entities is Aion2.Aion2EntityDirectory partyDirectory && Environment.GetEnvironmentVariable("AION2_PARTY") is { Length: > 0 })
        {
            // Who the meter thinks is in the party, and for which players the server reports hit points.
            Console.WriteLine("party roster: " + string.Join(", ", partyDirectory.PartyNames));
            foreach ((int id, int readings) in partyDirectory.HitPoints.ReadingCounts().OrderByDescending(kv => kv.Value))
            {
                if (partyDirectory.IsKnownPlayer(id))
                {
                    string pname = partyDirectory.NameFor(id) ?? id.ToString();
                    Console.WriteLine($"  hp readings for player {pname,-18} id {id,6}: {readings,5}  inRoster={partyDirectory.PartyNames.Contains(pname)} local={partyDirectory.IsLocalPlayer(id)}");
                }
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
                    var ownHeals = events.Where(e => e.IsHeal && e.SourceObjectId == entityId && e.TargetObjectId == entityId && e.Timestamp >= attemptStart);
                    var check = Combat.HpCheck.Evaluate(readings, attemptHits.Concat(ownHeals).ToList(), full);
                    Console.WriteLine($"aion2-replay:   hit points: max {full:N0}, {resets.Count} reset(s), last attempt {attemptHits.Sum(e => e.Amount):N0} damage counted"
                        + (check is null ? "" : $"; HP lost {check.Lost:N0}, counted {check.Counted:N0} ({check.Ratio:P1}), killed {check.Killed}, verdict {check.Verdict}"
                            + (check.Healed > 0 ? $", healed itself {check.Healed:N0}" : "")));
                }
            }
        }

        foreach (var (serverId, names) in source.ServerIdsSeen.OrderBy(k => k.Key))
        {
            Console.WriteLine($"aion2-replay: server id {serverId} = {Aion2.Protocol.Aion2Servers.NameOf(serverId)}: {names.Count} sample name(s) {string.Join(", ", names.Take(6))}");
        }

        Console.WriteLine($"aion2-replay: local player id {(local >= 0 ? local.ToString() : "unknown")} = {(local >= 0 ? source.Entities.NameFor(local) : "-")}");
        long heal = events.Where(e => e.IsHeal).Sum(e => e.Amount);
        Console.WriteLine($"aion2-replay: self-heals {heal:N0} ({events.Count(e => e.IsHeal)} event(s))");
    }
}
