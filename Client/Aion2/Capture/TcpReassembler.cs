using System.Buffers.Binary;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Aion2.Capture;

/// <summary>
/// Puts captured TCP segments back into a byte stream per direction and cuts that stream into
/// game frames using the length prefix described by <see cref="FrameLayout"/>. Out-of-order
/// segments wait for the missing piece; retransmissions are dropped; a hole that never fills
/// (packet loss on the capture side, not on the wire) is given up on after a bound, the stream
/// realigned at the next segment and the event counted in <see cref="Gaps"/> - a frame boundary
/// is then found again by the length prefix, so at most the frames spanning the hole are lost.
/// </summary>
public sealed class TcpReassembler
{
    private const int MaxPendingBytes = 256 * 1024;
    private const int MaxPendingSegments = 64;

    private readonly Dictionary<string, StreamState> _streams = new();

    public int Gaps { get; private set; }
    public int Retransmissions { get; private set; }
    public int Frames { get; private set; }
    public int Desyncs { get; private set; }

    public IReadOnlyList<ReadOnlyMemory<byte>> Push(TcpSegment segment, FrameLayout layout, IReadOnlySet<int>? syncOpcodes = null)
    {
        if (segment.Payload.Length == 0)
        {
            return Array.Empty<ReadOnlyMemory<byte>>();
        }

        if (!_streams.TryGetValue(segment.StreamKey, out StreamState? stream))
        {
            stream = new StreamState { NextSequence = segment.Sequence };
            _streams[segment.StreamKey] = stream;
        }

        Append(stream, segment.Sequence, segment.Payload);
        return CutFrames(stream, layout, syncOpcodes);
    }

    private void Append(StreamState stream, uint sequence, ReadOnlyMemory<byte> payload)
    {
        long delta = (int)(sequence - stream.NextSequence);
        if (delta == 0)
        {
            stream.Buffer.AddRange(payload.Span);
            stream.NextSequence = unchecked(sequence + (uint)payload.Length);
            DrainPending(stream);
            return;
        }

        if (delta < 0)
        {
            // Already have (part of) this: keep only the bytes beyond what was consumed.
            long overlap = -delta;
            if (overlap >= payload.Length)
            {
                Retransmissions++;
                return;
            }

            Retransmissions++;
            stream.Buffer.AddRange(payload.Span[(int)overlap..]);
            stream.NextSequence = unchecked(sequence + (uint)payload.Length);
            DrainPending(stream);
            return;
        }

        stream.Pending[sequence] = payload;
        stream.PendingBytes += payload.Length;
        if (stream.Pending.Count > MaxPendingSegments || stream.PendingBytes > MaxPendingBytes)
        {
            // The hole is not going to fill. Start over from the oldest waiting segment; whatever
            // half-frame sits in the buffer is unusable now.
            Gaps++;
            stream.Buffer.Clear();
            stream.Synced = false;
            stream.NextSequence = stream.Pending.Keys.First();
            DrainPending(stream);
        }
    }

    private static void DrainPending(StreamState stream)
    {
        while (stream.Pending.Count > 0)
        {
            uint first = stream.Pending.Keys.First();
            long delta = (int)(first - stream.NextSequence);
            if (delta > 0)
            {
                return;
            }

            ReadOnlyMemory<byte> payload = stream.Pending[first];
            stream.Pending.Remove(first);
            stream.PendingBytes -= payload.Length;
            long overlap = -delta;
            if (overlap < payload.Length)
            {
                stream.Buffer.AddRange(payload.Span[(int)overlap..]);
                stream.NextSequence = unchecked(first + (uint)payload.Length);
            }
        }
    }

    private List<ReadOnlyMemory<byte>> CutFrames(StreamState stream, FrameLayout layout, IReadOnlySet<int>? syncOpcodes)
    {
        var frames = new List<ReadOnlyMemory<byte>>();
        if (layout.IsVarint)
        {
            CutVarintFrames(stream, layout, frames, syncOpcodes);
            return frames;
        }

        while (stream.Buffer.Count >= layout.HeaderSize)
        {
            int declared = ReadLength(stream.Buffer, layout);
            int total = layout.LengthIncludesHeader ? declared : declared + layout.HeaderSize;
            if (total < layout.HeaderSize || total > layout.MaxFrameLength)
            {
                // Not looking at a frame start - drop until the next plausible length prefix.
                Desyncs++;
                stream.Buffer.RemoveAt(0);
                continue;
            }

            if (stream.Buffer.Count < total)
            {
                break;
            }

            frames.Add(stream.Buffer.GetRange(0, total).ToArray());
            stream.Buffer.RemoveRange(0, total);
            Frames++;
        }

        return frames;
    }

    /// <summary>Varint framing: LEB128 length L, record = L + prefixBytes + bias bytes in total; the
    /// emitted frame is the record without its prefix. An implausible length means we are not at a
    /// record start (a capture that began mid-stream) - drop one byte and look again.</summary>
    private void CutVarintFrames(StreamState stream, FrameLayout layout, List<ReadOnlyMemory<byte>> frames, IReadOnlySet<int>? syncOpcodes)
    {
        while (stream.Buffer.Count > 0)
        {
            // A capture that starts in the middle of a connection begins in the middle of a frame.
            // One plausible-looking length is not proof of a frame start, and trusting it can make
            // the decoder wait for kilobytes of real traffic that then vanish into a phantom frame.
            // So while unsynced, the buffer is searched for the first offset where several COMPLETE
            // frames in a row line up; everything before it is dropped, and candidates that do not
            // fit are simply skipped rather than waited for.
            if (!stream.Synced)
            {
                int offset = FindSyncOffset(stream.Buffer, layout, syncOpcodes);
                if (offset < 0)
                {
                    // Nothing lines up yet. Keep only the tail (a real frame start can still be in
                    // it) so a stream that never syncs cannot grow without bound.
                    if (stream.Buffer.Count > SyncGiveUpBytes)
                    {
                        Desyncs++;
                        stream.Buffer.RemoveRange(0, stream.Buffer.Count - SyncSearchWindow);
                    }

                    return;
                }

                if (offset > 0)
                {
                    Desyncs++;
                    stream.Buffer.RemoveRange(0, offset);
                }

                stream.Synced = true;
            }

            long length = 0;
            int prefix = 0;
            bool complete = false;
            while (prefix < 5 && prefix < stream.Buffer.Count)
            {
                byte b = stream.Buffer[prefix];
                length |= (long)(b & 0x7f) << (7 * prefix);
                prefix++;
                if ((b & 0x80) == 0)
                {
                    complete = true;
                    break;
                }
            }

            if (!complete)
            {
                if (prefix >= 5)
                {
                    Desyncs++;
                    stream.Synced = false;
                    stream.Buffer.RemoveAt(0);
                    continue;
                }

                return; // the length itself is still arriving
            }

            long total = length + prefix + layout.LengthBias;
            int bodyMin = Math.Max(layout.OpcodeOffset + layout.OpcodeSize, 1);
            if (total - prefix < bodyMin || total > layout.MaxFrameLength)
            {
                Desyncs++;
                stream.Synced = false;
                stream.Buffer.RemoveAt(0);
                continue;
            }

            if (stream.Buffer.Count < total)
            {
                return;
            }

            frames.Add(stream.Buffer.GetRange(prefix, (int)total - prefix).ToArray());
            stream.Buffer.RemoveRange(0, (int)total);
            Frames++;
        }
    }

    private const int SyncChainLength = 3;
    private const int SyncSearchWindow = 4096;
    private const int SyncGiveUpBytes = 8192;

    /// <summary>First offset (within the search window) at which <see cref="SyncChainLength"/>
    /// complete, plausible varint frames follow each other inside the buffer; -1 if none.</summary>
    private static int FindSyncOffset(List<byte> buffer, FrameLayout layout, IReadOnlySet<int>? syncOpcodes)
    {
        int last = Math.Min(buffer.Count - 1, SyncSearchWindow);
        for (int start = 0; start <= last; start++)
        {
            if (ChainFits(buffer, start, layout, syncOpcodes))
            {
                return start;
            }
        }

        return -1;
    }

    private static bool ChainFits(List<byte> buffer, int start, FrameLayout layout, IReadOnlySet<int>? syncOpcodes)
    {
        int pos = start;
        for (int n = 0; n < SyncChainLength; n++)
        {
            long length = 0;
            int prefix = 0;
            bool complete = false;
            while (prefix < 5 && pos + prefix < buffer.Count)
            {
                byte b = buffer[pos + prefix];
                length |= (long)(b & 0x7f) << (7 * prefix);
                prefix++;
                if ((b & 0x80) == 0)
                {
                    complete = true;
                    break;
                }
            }

            if (!complete)
            {
                return false;
            }

            long total = length + prefix + layout.LengthBias;
            if (total - prefix < Math.Max(layout.OpcodeOffset + layout.OpcodeSize, 1) || total > layout.MaxFrameLength || pos + total > buffer.Count)
            {
                return false;
            }

            // The frame's opcode must be one seen in real traffic (when such a list exists).
            if (syncOpcodes is { Count: > 0 })
            {
                int opcodeAt = pos + prefix + layout.OpcodeOffset;
                int opcode = layout.OpcodeBigEndian
                    ? buffer[opcodeAt] << 8 | buffer[opcodeAt + 1]
                    : buffer[opcodeAt + 1] << 8 | buffer[opcodeAt];
                if (!syncOpcodes.Contains(opcode))
                {
                    return false;
                }
            }

            pos += (int)total;

            // Frames that tile the buffer exactly - the usual case when the server sends one
            // message - are as convincing as a chain of three, even for a single frame.
            if (pos == buffer.Count)
            {
                return true;
            }
        }

        return true;
    }

    private static int ReadLength(List<byte> buffer, FrameLayout layout)
    {
        Span<byte> bytes = stackalloc byte[4];
        for (int i = 0; i < layout.LengthSize; i++)
        {
            bytes[i] = buffer[layout.LengthOffset + i];
        }

        return layout.LengthSize switch
        {
            1 => bytes[0],
            2 => layout.LittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes),
            _ => (int)(layout.LittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes)),
        };
    }

    private sealed class StreamState
    {
        public uint NextSequence;
        public bool Synced;
        public readonly List<byte> Buffer = new();
        public readonly SortedDictionary<uint, ReadOnlyMemory<byte>> Pending = new();
        public int PendingBytes;
    }
}
