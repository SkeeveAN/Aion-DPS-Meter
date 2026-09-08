using System.IO;
using System.Runtime.InteropServices;
using AionSniffer.ChatLog;
using AionSniffer.Combat;
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
