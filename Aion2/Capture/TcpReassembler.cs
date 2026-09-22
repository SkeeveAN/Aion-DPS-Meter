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

    public IReadOnlyList<ReadOnlyMemory<byte>> Push(TcpSegment segment, FrameLayout layout)
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
        return CutFrames(stream, layout);
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

    private List<ReadOnlyMemory<byte>> CutFrames(StreamState stream, FrameLayout layout)
    {
        var frames = new List<ReadOnlyMemory<byte>>();
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
        public readonly List<byte> Buffer = new();
        public readonly SortedDictionary<uint, ReadOnlyMemory<byte>> Pending = new();
        public int PendingBytes;
    }
}
