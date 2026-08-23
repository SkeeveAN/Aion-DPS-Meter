using System.Text;
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
    private static readonly HashSet<string> AttemptedFlows = new();
    private static readonly Dictionary<ushort, int> OpcodeCounts = new();

    private static void Main(string[] args)
    {
        var devices = CaptureDeviceList.Instance;

        if (devices.Count == 0)
        {
            Console.WriteLine("No capture devices found. Is Npcap installed (WinPcap API-compatible mode)?");
            return;
        }

        if (args.Length == 0)
        {
            Console.WriteLine("Usage: AionSniffer <deviceIndex> [serverIpHint]");
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

        if (ConfirmedSessions.TryGetValue(flowKey, out var session))
        {
            session.FeedSegment(tcp.SequenceNumber, payload);
            return;
        }

        if (!AttemptedFlows.Add(flowKey))
        {
            return; // already tried this flow's first segment and it wasn't Aion's handshake
        }

        if (!LooksLikeAionHandshake(payload))
        {
            return;
        }

        var newSession = new AionSession(flowKey);
        newSession.Diagnostic += msg => Console.WriteLine($"[diag] {msg}");
        newSession.PacketDecoded += (opcode, body) => HandleDecoded(flowKey, opcode, body);
        Console.WriteLine($"[diag] {flowKey}: looks like the Aion game server stream, attaching decoder.");
        newSession.FeedSegment(tcp.SequenceNumber, payload);
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

    private static void HandleDecoded(string flowKey, ushort opcode, byte[] body)
    {
        OpcodeCounts[opcode] = OpcodeCounts.GetValueOrDefault(opcode) + 1;

        string hexPreview = Convert.ToHexString(body.Take(32).ToArray());
        string asciiPreview = AsciiPreview(body);
        Console.WriteLine($"[0x{opcode:X4} len={body.Length}] {hexPreview}  '{asciiPreview}'");

        if (opcode == Opcodes.SM_ATTACK_STATUS)
        {
            var desc = CombatPacketParser.TryDescribeAttackStatus(body);
            if (desc is not null)
            {
                Console.WriteLine($"    -> {desc}");
            }
        }
        else if (opcode == Opcodes.SM_ATTACK)
        {
            var desc = CombatPacketParser.TryDescribeAttack(body);
            if (desc is not null)
            {
                Console.WriteLine($"    -> {desc}");
            }
        }
    }

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
