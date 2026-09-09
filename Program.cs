using System.IO;
using System.Runtime.InteropServices;
using AionSniffer.ChatLog;
using AionSniffer.Combat;
using AionSniffer.Data;
using Velopack;

namespace AionSniffer;

/// <summary>
/// Entry point for all three ways this program is used: the meter window (no arguments), a
/// one-shot parse of a Chat.log file, and the self-check suite.
///
/// Everything the meter knows comes from Aion's own Chat.log. There is no packet capture and no
/// access to the game process -- an earlier version of this file drove a live SharpPcap capture
/// and decrypted the game-server stream, which was removed once the chat-log path had been
/// validated against real raid logs and the network path had run into a packed client whose
/// crypto could not be reached without touching a running process. Removing it also let the GUI
/// drop its self-elevation: reading a text file needs no administrator rights, and a meter that
/// runs unprivileged next to the client is the less intrusive neighbour.
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
        // with no arguments), a double-click, and "AionSniffer" typed in a shell all open the
        // window.
        AttachConsole(AttachParentProcess);

        // Must run before anything else, and before any window exists: this is what handles
        // Velopack's own install/update/uninstall hook arguments, which the updater passes to a
        // freshly-swapped build. Getting a meter window on screen in those runs instead of doing
        // the hook's job is exactly how a self-updating app breaks its own update.
        VelopackApp.Build().Run();

        if (args.Length > 0 && args[0] == "selftest")
        {
            bool ok = SelfCheck.Run();
            Console.WriteLine(ok ? "\n[selftest] ALL CHECKS PASSED" : "\n[selftest] SOME CHECKS FAILED");
            Environment.Exit(ok ? 0 : 1);
            return;
        }

        if (args.Length > 0 && args[0] == "chatlog")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: AionSniffer chatlog <path-to-Chat.log>");
                return;
            }

            RunChatLogMode(args[1]);
            return;
        }

        // Temporary diagnostic for a live bug report (real teammates flipping to "enemy" mid-
        // session) - runs FactionResolver.Resolve() the exact same way MainWindow.ResolveSides()
        // does, but against a one-shot full-file parse, and dumps the resulting side per name plus
        // the raw fought/sameTarget edges for the names given. Not wired into the normal usage list
        // on purpose: this is an investigation tool, not a feature.
        if (args.Length > 0 && args[0] == "factioncheck")
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: AionSniffer factioncheck <path-to-Chat.log> <name1,name2,...>");
                return;
            }

            RunFactionCheckMode(args[1], args[2].Split(',', StringSplitOptions.TrimEntries));
            return;
        }

        if (args.Length > 0 && args[0] == "upload")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: AionSniffer upload <boss-name> [gap-seconds] [path-to-Chat.log]");
                Console.WriteLine("  Reads the Chat.log already configured in Settings (or the one given as the");
                Console.WriteLine("  third argument - e.g. a differently-named log from another client language),");
                Console.WriteLine("  splits every kill of <boss-name> into separate runs by a time gap (default");
                Console.WriteLine("  120s), and uploads each one - same pipeline as the GUI's \"Reload from");
                Console.WriteLine("  Chat.log\" + \"Upload last run\", just driven from a shell instead of");
                Console.WriteLine("  clicking through both.");
                return;
            }

            double gapSeconds = args.Length > 2 && double.TryParse(args[2], out double g) ? g : 120;
            string? logPathOverride = args.Length > 3 ? args[3] : null;
            RunHeadlessUploadMode(args[1], gapSeconds, logPathOverride);
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
                app.Run(new Ui.MainWindow());
            }
            catch (Exception ex)
            {
                // Without a console there is nowhere for an unhandled startup exception to show
                // up, so the failure looks like nothing happening at all -- put it on screen
                // instead of letting the process die silently.
                System.Windows.MessageBox.Show(ex.ToString(), "AionSniffer konnte nicht starten",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }

            return;
        }

        // Anything else is a typo far more often than it is an attempt at something real, so say
        // what the program accepts rather than failing silently or opening the window anyway.
        Console.WriteLine("Usage: AionSniffer                       (no arguments: opens the meter window)");
        Console.WriteLine("       AionSniffer chatlog <path-to-Chat.log>   (parses a Chat.log file and prints a summary)");
        Console.WriteLine("       AionSniffer selftest                     (runs the parser and DPS self-checks)");
        Console.WriteLine("       AionSniffer upload <boss-name> [gap-seconds]   (re-parses Chat.log and uploads every run of that boss)");
    }

    /// <summary>
    /// Constructs the exact same MainWindow the GUI uses, but never calls .Show() on it - so its
    /// class/faction/roster logic and Upload/UploadClient calls run identically to a live session,
    /// without a window actually appearing. A running Dispatcher message loop is still required:
    /// the headless method awaits UploadClient.SendAsync, and that continuation is posted back onto
    /// MainWindow's own DispatcherSynchronizationContext (set up as soon as the window/Application
    /// exist) rather than resuming inline - without app.Run() pumping that queue, execution would
    /// never get past the first await. Dispatcher.BeginInvoke queues the async work onto that same
    /// loop instead of running it synchronously here, so app.Run() is guaranteed to already be
    /// pumping by the time the work starts; app.Shutdown() in the finally block is what ends Run()
    /// once the work (success or failure) is done.
    /// </summary>
    private static void RunHeadlessUploadMode(string bossName, double gapSeconds, string? logPathOverride)
    {
        var app = new System.Windows.Application();
        var window = new Ui.MainWindow();
        int exitCode = 0;

        window.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                string report = await window.RunHeadlessClusteredUploadAsync(bossName, gapSeconds, logPathOverride);
                Console.WriteLine(report);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"upload: failed - {ex}");
                exitCode = 1;
            }
            finally
            {
                app.Shutdown();
            }
        }));

        app.Run();
        Environment.Exit(exitCode);
    }

    private static void RunFactionCheckMode(string path, string[] watchNames)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"factioncheck: file not found: {path}");
            return;
        }

        var parser = new ChatLogParser();
        List<DamageEvent> events = parser.ParseFile(path);
        string? NameOf(int id) => parser.Names.NameFor(id);
        bool IsPlayer(int id)
        {
            string name = NameOf(id) ?? "";
            return !name.Contains(' ') && !NpcDatabase.IsKnownNpc(name);
        }

        int youId = parser.Names.GetOrAssignId("You");

        // No settings/anchors available outside MainWindow - empty anchors is the worst case
        // (matches a fresh session with no registered characters and no loot seen yet), which is
        // exactly the scenario worth checking since it puts the most weight on the fought/sameTarget
        // logic alone.
        var sides = FactionResolver.Resolve(events, NameOf, IsPlayer, youId, new HashSet<string>());

        Console.WriteLine($"factioncheck: {events.Count} events, youId={youId}");
        foreach (string watch in watchNames)
        {
            int id = parser.Names.GetOrAssignId(watch);
            Side side = sides.GetValueOrDefault(id, Side.Unknown);
            Console.WriteLine($"  {watch,-15} id={id} isPlayer={IsPlayer(id)} side={side}");
        }

        // Raw edge dump for the watched names - who they fought (player-vs-player) and which
        // non-player targets they share attackers with, straight from the same events FactionResolver
        // itself used, so a wrong verdict above can be traced to the exact edge that caused it.
        var watchIds = watchNames.Select(n => parser.Names.GetOrAssignId(n)).ToHashSet();
        var fought = new Dictionary<int, HashSet<int>>();
        var sameTargetByTarget = new Dictionary<int, HashSet<int>>();
        foreach (DamageEvent e in events)
        {
            bool sourceIsPlayer = IsPlayer(e.SourceObjectId);
            bool targetIsPlayer = IsPlayer(e.TargetObjectId);
            if (!e.IsHeal && sourceIsPlayer && targetIsPlayer && e.SourceObjectId != e.TargetObjectId)
            {
                if (!fought.TryGetValue(e.SourceObjectId, out var set)) fought[e.SourceObjectId] = set = new HashSet<int>();
                set.Add(e.TargetObjectId);
            }
            if (!e.IsHeal && sourceIsPlayer && !targetIsPlayer)
            {
                if (!sameTargetByTarget.TryGetValue(e.TargetObjectId, out var attackers)) sameTargetByTarget[e.TargetObjectId] = attackers = new HashSet<int>();
                attackers.Add(e.SourceObjectId);
            }
        }

        Console.WriteLine("  -- fought edges involving watched names --");
        foreach (int id in watchIds)
        {
            if (fought.TryGetValue(id, out var opponents))
            {
                Console.WriteLine($"  {NameOf(id)} fought: {string.Join(", ", opponents.Select(o => NameOf(o) ?? o.ToString()))}");
            }
        }

        Console.WriteLine("  -- shared non-player targets involving watched names --");
        foreach ((int targetId, HashSet<int> attackers) in sameTargetByTarget)
        {
            if (attackers.Overlaps(watchIds))
            {
                Console.WriteLine($"  target={NameOf(targetId)}: attackers={string.Join(", ", attackers.Select(a => NameOf(a) ?? a.ToString()))}");
            }
        }
    }

    private static void RunChatLogMode(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"chatlog: file not found: {path}");
            return;
        }

        var parser = new ChatLogParser();

        // Personal stats are accumulated here rather than ignored (the GUI shows them in its
        // footer, this mode used to drop them) because they are what a chat-log run can be
        // checked against from outside: AP per kill in particular is reported by the client
        // itself, so seeing the same total here proves the pattern actually fired.
        var personalTotals = new Dictionary<PersonalStatKind, long>();
        parser.PersonalStatChanged += (kind, delta) =>
        {
            personalTotals.TryGetValue(kind, out long running);
            personalTotals[kind] = running + delta;
        };

        var events = parser.ParseFile(path);
        int healCount = events.Count(e => e.IsHeal);
        // Found by terminal_windows against the real file: this used to say "N damage events" for
        // the raw total, which includes heals -- misleading right above a damage-only summary line
        // that (correctly) shows a smaller number, reading like a discrepancy/miscount rather than
        // two different, both-correct figures.
        Console.WriteLine($"chatlog: parsed {events.Count} events ({events.Count - healCount} damage, {healCount} heal) from {path}");

        var aggregator = new LiveAggregator();
        aggregator.IngestEvents(events);
        Console.WriteLine(aggregator.Summarize(id => parser.Names.NameFor(id)));

        if (personalTotals.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("personal totals: " + string.Join(" | ",
                personalTotals.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value:N0}")));
        }

        // Damage grouped by TARGET, which the source-side leaderboard above cannot show. Exists to
        // check the meter against a known quantity: a boss's HP is published (origincdx.com lists
        // max_hp per npc), so "damage dealt to that boss" has an expected value, and a parser that
        // silently drops a line shape shows up here as a total that falls short of it.
        var byTarget = events
            .Where(e => !e.IsHeal)
            .GroupBy(e => e.TargetObjectId)
            .Select(g => (Name: parser.Names.NameFor(g.Key) ?? $"0x{g.Key:X8}", Total: g.Sum(e => e.Amount), Hits: g.Count()))
            .OrderByDescending(x => x.Total)
            .Take(15)
            .ToList();

        if (byTarget.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("damage taken, by target (top 15):");
            foreach (var (name, total, hits) in byTarget)
            {
                Console.WriteLine($"  {name,-42} {total,14:N0}  ({hits:N0} hits)");
            }
        }
    }
}
