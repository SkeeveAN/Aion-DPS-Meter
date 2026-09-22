namespace AionDPS.Aion2.Capture;

/// <summary>One TCP segment's payload as captured, with just enough transport context to
/// reassemble the stream it belongs to. <see cref="FromServer"/> is decided by the game-server
/// port list in the protocol description, not by which side spoke first.</summary>
public sealed record TcpSegment(
    DateTime Timestamp,
    string Source,
    string Destination,
    uint Sequence,
    ReadOnlyMemory<byte> Payload,
    bool FromServer)
{
    /// <summary>Stream identity: one direction of one connection.</summary>
    public string StreamKey => $"{Source}>{Destination}";
}
