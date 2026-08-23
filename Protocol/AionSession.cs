using AionSniffer.Crypto;
using AionSniffer.Network;

namespace AionSniffer.Protocol;

/// <summary>
/// Tracks one directed TCP flow believed to be a game server sending Aion protocol frames to a
/// client. Reassembles the byte stream, extracts length-prefixed frames, decrypts them once the
/// SM_KEY handshake has been observed, and reports decoded (opcode, body) pairs.
/// </summary>
public sealed class AionSession
{
    private const int MinFrameLen = 7; // 2 (len) + 2 (opcode) + 1 (static code) + 2 (checksum)

    private readonly TcpFlow _flow = new();
    private AionCrypt.ServerStream? _crypt;

    public string Label { get; }

    public event Action<ushort, byte[]>? PacketDecoded;
    public event Action<string>? Diagnostic;

    public AionSession(string label)
    {
        Label = label;
    }

    public void FeedSegment(uint seq, byte[] payload)
    {
        _flow.Feed(seq, payload);
        DrainFrames();
    }

    private void DrainFrames()
    {
        while (true)
        {
            if (_flow.Buffer.Count < 2)
            {
                return;
            }

            ushort totalLen = (ushort)(_flow.Buffer[0] | (_flow.Buffer[1] << 8));

            if (totalLen < MinFrameLen)
            {
                // Not a plausible frame length -- resync by dropping a byte rather than stalling forever.
                Diagnostic?.Invoke($"{Label}: implausible frame length {totalLen}, resyncing.");
                _flow.Buffer.RemoveAt(0);
                continue;
            }

            if (_flow.Buffer.Count < totalLen)
            {
                return; // wait for more data
            }

            byte[] frame = _flow.Buffer.GetRange(0, totalLen).ToArray();
            _flow.Buffer.RemoveRange(0, totalLen);
            ProcessFrame(frame);
        }
    }

    private void ProcessFrame(byte[] frame)
    {
        int payloadOffset = 2;
        int payloadLen = frame.Length - 2;

        if (_crypt == null)
        {
            HandleHandshakeFrame(frame);
            return;
        }

        _crypt.Decrypt(frame, payloadOffset, payloadLen);

        if (payloadLen < 5)
        {
            Diagnostic?.Invoke($"{Label}: frame too short after decrypt ({payloadLen} bytes).");
            return;
        }

        ushort obfOpcode = (ushort)(frame[2] | (frame[3] << 8));
        byte code = frame[4];
        ushort checksum = (ushort)(frame[5] | (frame[6] << 8));

        if (code != AionCrypt.StaticServerPacketCode || checksum != (ushort)~obfOpcode)
        {
            Diagnostic?.Invoke($"{Label}: decrypted frame failed header validation -- key desynced or wrong constants for this server build. Dropping session.");
            _crypt = null;
            return;
        }

        ushort opcode = AionCrypt.DecodeOpcode(obfOpcode);
        byte[] body = frame[7..];
        PacketDecoded?.Invoke(opcode, body);
    }

    private void HandleHandshakeFrame(byte[] frame)
    {
        // Expect the plaintext SM_KEY packet: header (5 bytes) + a single 4-byte int body = 11 bytes total.
        if (frame.Length < 11 || frame[4] != AionCrypt.StaticServerPacketCode)
        {
            Diagnostic?.Invoke($"{Label}: first frame ({frame.Length} bytes) doesn't look like SM_KEY -- discarding session.");
            return;
        }

        ushort obf = (ushort)(frame[2] | (frame[3] << 8));
        ushort checksum = (ushort)(frame[5] | (frame[6] << 8));

        if (checksum != (ushort)~obf)
        {
            Diagnostic?.Invoke($"{Label}: SM_KEY checksum mismatch -- wrong protocol constants for this server build.");
            return;
        }

        int falseKey = frame[7] | (frame[8] << 8) | (frame[9] << 16) | (frame[10] << 24);
        int baseKey = AionCrypt.DecodeBaseKey(falseKey);
        _crypt = new AionCrypt.ServerStream(baseKey);
        Diagnostic?.Invoke($"{Label}: handshake captured, base key derived (0x{baseKey:X8}). Decryption armed.");
    }
}
