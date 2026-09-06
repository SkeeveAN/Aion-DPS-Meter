using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AionSniffer.ChatLog;
using AionSniffer.Combat;
using AionSniffer.Protocol;
using PacketDotNet;
using SharpPcap;

namespace AionSniffer;

/// <summary>
/// Calibration tool: sniffs live traffic, tries to detect the Aion game-server TCP stream via
/// its SM_KEY handshake, decrypts everything after that, and prints every decoded packet
/// (opcode + hex preview + ascii preview) plus a best-effort decode for the two combat opcodes
/// we're hoping are still SM_ATTACK_STATUS (0x05) / SM_ATTACK (0x36).
///
/// Run this WHILE (RE)CONNECTING to the game server (start capture first, then log in / change
/// channel / reconnect) -- the handshake is only sent once, at the start of the TCP connection.
/// If nothing gets decoded, the constants in Crypto/AionCrypt.cs and Protocol/Opcodes.cs are
/// wrong for this server build and need adjusting from what this tool prints (see README.md).
/// </summary>
internal static class Program
{
    private static readonly Dictionary<string, AionSession> ConfirmedSessions = new();

    /// <summary>
    /// Per-flow count of handshake-check attempts. Deliberately NOT a one-shot HashSet: an
    /// earlier version gave every flow exactly one try at LooksLikeAionHandshake and then
    /// blacklisted it forever via HashSet.Add's "already present" return, permanently discarding
    /// a flow if its very first captured payload happened to be a partial/misaligned segment --
    /// found during the first real calibration run against a live server (see README), where it
    /// meant a failed heuristic on packet 1 could never be revisited even if packet 2 or 3 of the
    /// same flow would have matched.
    /// </summary>
    private static readonly Dictionary<string, int> HandshakeAttempts = new();
    private const int MaxHandshakeAttempts = 5;

    private static readonly Dictionary<ushort, int> OpcodeCounts = new();
    private static readonly LiveAggregator Aggregator = new();

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [STAThread] // required for WPF (Ui/MainWindow) -- Clipboard, drag-move etc. need the STA apartment.
    private static void Main(string[] args)
    {
        // The csproj builds this as WinExe now (no automatic console), specifically so "gui" mode
        // doesn't pop up an empty terminal window next to the meter - found by the user. The CLI
        // modes below (selftest/chatlog/capture) still need visible Console.WriteLine output when
        // launched from an existing shell, so attach to whichever console started this process, if
        // any. The return value matters beyond that side effect: false means there is no console
        // at all (double-click, desktop shortcut, MSI Start-menu shortcut -- Packaging/Product.wxs
        // passes no arguments either), and a process with nowhere to print must not "start" by
        // writing usage text into the void. That is exactly what it used to do in that case: no
        // window, no message, nothing happened at all -- reported by the user. See the GUI branch.
        bool hasConsole = AttachConsole(AttachParentProcess);

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

        // "gui" explicitly, or no arguments at all with no console to talk to -- see hasConsole
        // above. Launched from a shell without arguments you still get the usage/device list
        // further down, which is what someone typing the command there is asking for.
        if ((args.Length > 0 && args[0] == "gui") || (args.Length == 0 && !hasConsole))
        {
            // No App.xaml on purpose: an ApplicationDefinition item would generate its own Main
            // and collide with this one. Building System.Windows.Application by hand keeps the
            // console entry points (selftest, capture) and the GUI in the same exe without
            // fighting over program entry.
            var app = new System.Windows.Application();
            try
            {
                app.Run(new Ui.MainWindow());
            }
            catch (Exception ex)
            {
                // Without a console there is nowhere for an unhandled startup exception to show
                // up, so the failure looks exactly like the argument bug above ("nothing happens")
                // -- put it on screen instead of letting the process die silently.
                System.Windows.MessageBox.Show(ex.ToString(), "AionSniffer konnte nicht starten",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }

            return;
        }

        var devices = CaptureDeviceList.Instance;

        if (devices.Count == 0)
        {
            Console.WriteLine("No capture devices found. Is Npcap installed (WinPcap API-compatible mode)?");
            return;
        }

        if (args.Length == 0)
        {
            Console.WriteLine("Usage: AionSniffer <deviceIndex> [serverIpHint]");
            Console.WriteLine("       AionSniffer selftest   (verifies the DPS/iDPS math against synthetic + real reference numbers, no capture needed)");
            Console.WriteLine("       AionSniffer gui        (opens the WPF meter window -- see Ui/, not yet wired to a live capture, has a \"Load Demo Data\" button)");
            Console.WriteLine("       AionSniffer chatlog <path-to-Chat.log>   (parses a Chat.log file, prints the same live-DPS summary as the network path)");
            Console.WriteLine();
            Console.WriteLine("Available devices:");
            for (int i = 0; i < devices.Count; i++)
            {
                Console.WriteLine($"  [{i}] {devices[i].Name} - {devices[i].Description}");
            }

            return;
        }

        int deviceIndex = int.Parse(args[0]);
        string? serverIpHint = args.Length > 1 ? args[1] : null;

        using var device = devices[deviceIndex];
        device.Open(DeviceModes.Promiscuous, 1000);
        device.Filter = serverIpHint is null ? "tcp" : $"tcp and host {serverIpHint}";
        device.OnPacketArrival += OnPacketArrival;

        Console.WriteLine($"Capturing on {device.Name} ({device.Description})...");
        Console.WriteLine("Log into / reconnect to the game server now. Press Enter to stop.");

        device.StartCapture();
        Console.ReadLine();
        device.StopCapture();

        Console.WriteLine();
        Console.WriteLine("Opcode counts seen this run:");
        foreach (var kv in OpcodeCounts.OrderByDescending(kv => kv.Value))
        {
            Console.WriteLine($"  0x{kv.Key:X4} : {kv.Value}");
        }
    }

    /// <summary>
    /// One-shot Chat.log parse: no live tailing yet (see README's "leere Themen" list --
    /// following the file as new lines are appended is the natural next step once this static
    /// parse is confirmed against a real fight, not implemented here to avoid guessing at
    /// polling/FileSystemWatcher behavior before there's a real log to test it against).
    /// </summary>
    private static void RunChatLogMode(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"chatlog: file not found: {path}");
            return;
        }

        var parser = new ChatLogParser();
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
    }

    private static void OnPacketArrival(object sender, PacketCapture e)
    {
        var raw = e.GetPacket();
        var packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
        var ip = packet.Extract<IPPacket>();
        var tcp = packet.Extract<TcpPacket>();

        if (ip is null || tcp is null)
        {
            return;
        }

        byte[] payload = tcp.PayloadData;
        if (payload.Length == 0)
        {
            return;
        }

        string flowKey = $"{ip.SourceAddress}:{tcp.SourcePort}->{ip.DestinationAddress}:{tcp.DestinationPort}";
        DateTime capturedAt = raw.Timeval.Date;

        if (ConfirmedSessions.TryGetValue(flowKey, out var session))
        {
            session.FeedSegment(capturedAt, tcp.SequenceNumber, payload);
            return;
        }

        int attempts = HandshakeAttempts.GetValueOrDefault(flowKey);
        if (attempts >= MaxHandshakeAttempts)
        {
            return; // gave this flow enough early packets to prove itself, moving on
        }

        HandshakeAttempts[flowKey] = attempts + 1;

        if (!LooksLikeAionHandshake(payload))
        {
            return;
        }

        var newSession = new AionSession(flowKey);
        newSession.Diagnostic += msg => Console.WriteLine($"[diag] {msg}");
        newSession.PacketDecoded += (timestamp, opcode, body) => HandleDecoded(flowKey, timestamp, opcode, body);
        Console.WriteLine($"[diag] {flowKey}: looks like the Aion game server stream, attaching decoder.");
        newSession.FeedSegment(capturedAt, tcp.SequenceNumber, payload);
        ConfirmedSessions[flowKey] = newSession;
    }

    /// <summary>
    /// Cheap pre-check on a flow's first observed payload, before paying for full session
    /// tracking: does it look exactly like the plaintext 11-byte SM_KEY packet?
    /// </summary>
    private static bool LooksLikeAionHandshake(byte[] payload)
    {
        if (payload.Length < 11)
        {
            return false;
        }

        ushort totalLen = (ushort)(payload[0] | (payload[1] << 8));
        if (totalLen != 11)
        {
            return false;
        }

        if (payload[4] != Crypto.AionCrypt.StaticServerPacketCode)
        {
            return false;
        }

        ushort obf = (ushort)(payload[2] | (payload[3] << 8));
        ushort checksum = (ushort)(payload[5] | (payload[6] << 8));
        return checksum == (ushort)~obf;
    }

    private static void HandleDecoded(string flowKey, DateTime timestamp, ushort opcode, byte[] body)
    {
        OpcodeCounts[opcode] = OpcodeCounts.GetValueOrDefault(opcode) + 1;

        string hexPreview = Convert.ToHexString(body.Take(32).ToArray());
        string asciiPreview = AsciiPreview(body);
        Console.WriteLine($"[0x{opcode:X4} len={body.Length}] {hexPreview}  '{asciiPreview}'");

        if (Describers.TryGetValue(opcode, out var describe))
        {
            var desc = describe(body);
            if (desc is not null)
            {
                Console.WriteLine($"    -> {desc}");
            }
        }

        if (opcode == Opcodes.SM_ATTACK)
        {
            var attack = CombatPacketParser.TryParseAttack(body);
            if (attack is not null)
            {
                Aggregator.IngestAttack(timestamp, attack);
                Console.WriteLine($"    -> live DPS (ALL view, unverified opcode -- see README): {Aggregator.Summarize()}");
            }
        }
    }

    private static readonly Dictionary<ushort, Func<byte[], string?>> Describers = new()
    {
        [Opcodes.SM_ATTACK_STATUS] = CombatPacketParser.TryDescribeAttackStatus,
        [Opcodes.SM_ATTACK] = CombatPacketParser.TryDescribeAttack,
        [Opcodes.SM_SYSTEM_MESSAGE] = CombatPacketParser.TryDescribeSystemMessage,
        [Opcodes.SM_NPC_INFO] = CombatPacketParser.TryDescribeNpcInfo,
        [Opcodes.SM_DELETE] = CombatPacketParser.TryDescribeDelete,
        [Opcodes.SM_GROUP_MEMBER_INFO] = CombatPacketParser.TryDescribeGroupMemberInfo,
    };

    private static string AsciiPreview(byte[] body)
    {
        var sb = new StringBuilder();
        foreach (byte b in body.Take(48))
        {
            sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        }

        return sb.ToString();
    }
}
