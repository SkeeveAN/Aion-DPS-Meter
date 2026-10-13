namespace AionDPS.Aion2.Capture;

/// <summary>One TCP segment's payload as captured, with just enough transport context to
/// reassemble the stream it belongs to. <see cref="FromServer"/> is decided by the game-server
/// port list in the protocol description, not by which side spoke first. <see cref="Window"/> is the receive window the sender
/// advertises (raw, -1 = unknown; 0 = it cannot take more data) and <see cref="Ttl"/> the IP time to live (0 = unknown); both only feed the network log.</summary>
public sealed record TcpSegment(
    DateTime Timestamp,
    string Source,
    string Destination,
    uint Sequence,
    ReadOnlyMemory<byte> Payload,
    bool FromServer,
    uint Ack = 0,
    int Window = -1,
    int Ttl = 0)
{
    /// <summary>Stream identity: one direction of one connection.</summary>
    public string StreamKey => $"{Source}>{Destination}";
}
