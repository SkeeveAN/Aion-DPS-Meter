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
                Console.WriteLine("Usage: AionDPS render-character <out.png> [equipment|arcana|skills|board|species]");
                Console.WriteLine("  Draws the saved character's profile window into a picture; no window is ever shown.");
                return;
            }

            RunRenderCharacterMode(args[1], args.Length > 2 ? args[2] : "equipment");
            return;
        }

        if (args.Length > 0 && args[0] == "render-details")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: AionDPS render-details <out.png>");
                Console.WriteLine("  Draws a player's details window with sample hits into a picture; no window is shown.");
                return;
            }

            RunRenderDetailsMode(args[1]);
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

        if (args.Length > 0 && args[0] == "render-petmap")
        {
            RunRenderPetMapMode(args.Length > 1 ? args[1] : "petmap.png", args.Length > 2 ? args[2] : "verteron", args.Length > 4 ? double.Parse(args[3]) : -215789, args.Length > 4 ? double.Parse(args[4]) : 262953);
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
        Ui.ThemeManager.Apply(app, settings.Theme, settings.FontSize);
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
        const double width = 1180, height = 1000;
        var content = (System.Windows.UIElement)window.Content;
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

    private static void RunRenderDetailsMode(string path)
    {
        var app = new System.Windows.Application();
        var settings = Ui.MeterSettings.Load();
        Ui.ThemeManager.Apply(app, settings.Theme, settings.FontSize);
        Ui.MainWindow.Headless = true;
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var random = new Random(7);
        var events = new List<Combat.DamageEvent>();
        DateTime start = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Local);
        (string Skill, int Id, int Weight)[] skills = { ("Blaze", 11000000, 5), ("Hellfire", 11100000, 2), ("Cold Wave", 12000000, 9), ("Ice Chain", 13000000, 9), ("Bittercold Wind", 15280000, 20) };
        for (int t = 0; t < 75; t++)
        {
            double burst = t is > 28 and < 36 ? 3.5 : 1.0;
            for (int k = 0; k < (int)(2 * burst); k++)
            {
                var skill = skills[random.Next(skills.Length)];
                int target = random.Next(5) == 0 ? 2 : 1;
                events.Add(new Combat.DamageEvent(start.AddSeconds(t + random.NextDouble()), 5, target, random.Next(300, 3000) * (skill.Weight > 8 ? 1 : 3), false, skill.Skill, random.Next(5) == 0, SkillId: skill.Id));
            }
        }

        var all = events.Concat(events.Select(e => e with { SourceObjectId = 6, Amount = e.Amount / 2 })).ToList();
        var window = new Ui.PlayerDetailsWindow("Wakayashi", "Sorcerer", "", events, id => id == 1 ? "Training Scarecrow" : "Elite Guard", fightEvents: all);
        const double width = 860, height = 680;
        var content = (System.Windows.UIElement)window.Content;
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
    private static void RunRenderOverlayMode(string path, string kind)
    {
        // "chips-all" etc.: the same look in the All in One mode.
        bool allCompact = kind.EndsWith("-allc", StringComparison.Ordinal);
        bool all = kind.EndsWith("-all", StringComparison.Ordinal) || allCompact;
        kind = allCompact ? kind[..^5] : all ? kind[..^4] : kind;
        var app = new System.Windows.Application();
        var settings = Ui.MeterSettings.Load();
        Ui.ThemeManager.Apply(app, settings.Theme, settings.FontSize);
        Ui.MainWindow.Headless = true;
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var window = new Ui.MainWindow();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
        var type = typeof(Ui.MainWindow);
        var rows = (System.Collections.ObjectModel.ObservableCollection<Ui.PlayerRow>)type.GetField("_rows", flags)!.GetValue(window)!;
        (string Name, string Cls, long Dmg, double Dps)[] sample =
        {
            ("Keraut", "Templar", 916_700, 8300), ("Boulenbouche", "Elementalist", 790_800, 7100), ("Lumy", "Sorcerer", 757_700, 6800),
            ("Aurulio", "Sorcerer", 434_200, 3900), ("Butterfinger", "Cleric", 232_800, 2100),
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
        else if (kind == "window")
        {
            // The normal window as it is without Hide UI: its whole content at a typical size.
            window.OverlayTargetText.Text = "Transcendent Bakarma";
            panel = (System.Windows.FrameworkElement)window.Content;
            panel.Measure(new System.Windows.Size(520, 420));
            panel.Arrange(new System.Windows.Rect(0, 0, 520, 420));
            panel.UpdateLayout();
        }
        else
        {
            window.CompactOverlayPanel.Visibility = System.Windows.Visibility.Visible;
            panel = window.CompactOverlayPanel;
        }

        if (kind != "window")
        {
            panel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            panel.Arrange(new System.Windows.Rect(panel.DesiredSize));
        }

        panel.UpdateLayout();
        int w = (int)Math.Ceiling(panel.DesiredSize.Width), h = (int)Math.Ceiling(panel.DesiredSize.Height);
        var backdrop = new System.Windows.Controls.Border { Width = w, Height = h, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x30, 0x40, 0x30)) };
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        backdrop.Measure(new System.Windows.Size(w, h));
        backdrop.Arrange(new System.Windows.Rect(0, 0, w, h));
        bitmap.Render(backdrop);
        bitmap.Render(panel);
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
    private static void RunRenderSettingsMode(string path, string tab)
    {
        var app = new System.Windows.Application();
        // The shared styles (ThemedSlider ...) are merged by the GUI start; the Settings window needs them here too.
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var settings = Ui.MeterSettings.Load();
        if (settings.Language.Length > 0)
        {
            Ui.LocalizationManager.Instance.Language = settings.Language; // so a layout can be checked in every language
        }

        Ui.ThemeManager.Apply(app, settings.Theme, settings.FontSize);
        Ui.MainWindow.Headless = true;
        var window = new Ui.SettingsWindow(settings);
        if (window.FindName("Nav" + (tab == "Interface" ? "Ui" : tab == "Capture" ? "Install" : tab)) is System.Windows.Controls.RadioButton nav)
        {
            nav.IsChecked = true;
        }

        if (Environment.GetEnvironmentVariable("AIONDPS_LONGEST_PETS") is { Length: > 0 } longest && int.TryParse(longest, out int longestCount))
        {
            window.ShowLongestPetNames(longestCount); // layout check: the pets with the longest names in this language
        }

        const double width = 620, height = 740;
        var content = (System.Windows.UIElement)window.Content;
        content.Measure(new System.Windows.Size(width, height));
        content.Arrange(new System.Windows.Rect(0, 0, width, height));
        content.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)width, (int)height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
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
        Ui.ThemeManager.Apply(app, settings.Theme, settings.FontSize);
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

    private static void RunAion2UploadDryRun(string path, string serverName, int serverPort, string[] sendOnly)
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
            Console.WriteLine($"  {request.BossNpcName} (entity {targetId}) NPC id {request.BossNpcId}, max HP {request.BossMaxHp}, game {request.Game}, server {request.ServerName} [{request.ServerFingerprint}], {request.StartedAt:HH:mm:ss}-{request.EndedAt:HH:mm:ss} UTC, {request.Participants.Count} participant(s), {request.Participants.Count(p => p.IsSelf)} self");
            foreach (var p in request.Participants.OrderByDescending(x => x.TotalDamage))
            {
                Console.WriteLine($"      {p.Name,-12} {p.ClassName,-13} self={p.IsSelf,-5} damage {p.TotalDamage,9:N0} taken {p.DamageTaken,8:N0} heal {p.TotalHealing,7:N0} guild {p.Guild ?? "-"} profile {(p.Profile is null ? "-" : p.Profile.Source)}");
            }
        }

        var buildProfiles = type.GetMethod("BuildProfilesUpload", flags);
        if (buildProfiles?.Invoke(window, new object?[] { fingerprint, serverName, false }) is Upload.ProfilesUploadRequest profilesRequest)
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

    private static void RunRenderPetMapMode(string path, string mapKey, double x, double y)
    {
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(
            (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        var settings = Ui.MeterSettings.Load();
        Ui.ThemeManager.Apply(app, settings.Theme, settings.FontSize);
        Ui.MainWindow.Headless = true;
        var map = Aion2.Protocol.Aion2Maps.All.First(m => m.Key == mapKey);
        var chosen = settings.PetMapPets.Count > 0 ? settings.PetMapPets.ToHashSet() : new HashSet<int> { 1170, 1162, 1171, 1219 };
        var points = Aion2.Protocol.Aion2Maps.PointsOf(map, chosen);
        var pets = points.Select(p => p.PetId).Distinct().OrderBy(i => i).ToList();
        var colors = pets.Select((id, i) => (id, i)).ToDictionary(t => t.id, t => Ui.PetMapPalette.Of(t.i));
        var window = new Ui.PetMapWindow();
        // sample "live" monsters: every 7th spawn point within 120 m stands there right now
        var live = points.Where(p => Math.Abs(p.X - x) < 12000 && Math.Abs(p.Y - y) < 12000).Where((p, i) => i % 7 == 0).ToList();
        window.Render(map, x, y, settings.PetMapRadius, settings.PetMapOpacity, points.ToList(), live, colors,
            Aion2.Protocol.Aion2Gather.PointsOf(map, Aion2.Protocol.Aion2Gather.Items().Where(i => i.Count > 0).Select(i => i.Key).ToHashSet()));
        var list = new Ui.PetListWindow();
        list.Render(pets.Select(id => (colors[id], Aion2.Protocol.Aion2Pets.PetName(id, "de") ?? "?", "Stufe 2 · 22/75", "104 m", false)).ToList());
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
            events.AddRange(source.Poll(false).Damage);
        }

        if (source.Entities is Aion2.Aion2EntityDirectory idEnd)
        {
            Console.WriteLine($"aion2-replay: {idEnd.IdentityMovesSeen} player(s) came back under a new combat id");
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
                .GroupBy(id => (Server: petDirectory.ServerIdOf(id)!.Value / 1000, Bit: petDirectory.SeenProfileOf(id)?.Faction ?? -1))
                .OrderBy(g => g.Key.Server).ThenBy(g => g.Key.Bit).Select(g => $"server {g.Key.Server}xxx bit {g.Key.Bit}: {g.Count()}");
            Console.WriteLine("aion2-replay: faction bit of players with a known server: " + string.Join(" | ", bitByServerFaction));
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
