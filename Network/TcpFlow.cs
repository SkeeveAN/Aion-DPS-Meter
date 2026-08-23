namespace AionSniffer.Network;

/// <summary>
/// Minimal TCP byte-stream reassembler for one direction of one connection.
/// Buffers out-of-order segments by sequence number and releases bytes to
/// <see cref="Buffer"/> as soon as they become contiguous. Good enough for a
/// passive sniffer on the same host as one of the endpoints, where segments
/// mostly arrive in order; it does not handle retransmission edge cases beyond
/// "drop anything that looks like it's already been seen".
/// </summary>
public sealed class TcpFlow
{
    private uint? _nextSeq;
    private readonly SortedDictionary<uint, byte[]> _outOfOrder = new();

    public List<byte> Buffer { get; } = new();

    public void Feed(uint seq, byte[] payload)
    {
        if (payload.Length == 0)
        {
            return;
        }

        _nextSeq ??= seq;

        if (seq == _nextSeq.Value)
        {
            AppendAndAdvance(payload);
            DrainOutOfOrder();
        }
        else if (IsAfter(seq, _nextSeq.Value))
        {
            _outOfOrder[seq] = payload;
        }
        // else: seq is at or before the already-consumed point (retransmit/overlap) -> ignore.
    }

    private void AppendAndAdvance(byte[] payload)
    {
        Buffer.AddRange(payload);
        _nextSeq = unchecked(_nextSeq!.Value + (uint)payload.Length);
    }

    private void DrainOutOfOrder()
    {
        while (_outOfOrder.TryGetValue(_nextSeq!.Value, out var next))
        {
            _outOfOrder.Remove(_nextSeq.Value);
            AppendAndAdvance(next);
        }
    }

    /// <summary>True if sequence number <paramref name="a"/> is strictly after <paramref name="b"/>, with 32-bit wraparound.</summary>
    private static bool IsAfter(uint a, uint b) => unchecked((int)(a - b)) > 0;
}
