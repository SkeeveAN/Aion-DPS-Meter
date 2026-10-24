using System.Buffers.Binary;

namespace AionDPS.Aion2;

/// <summary>What an instance timer counts: the phase byte of the frame.</summary>
public enum InstancePhase
{
    /// <summary>The 30 s wait after entering.</summary>
    Entry = 1,
    /// <summary>The time limit of the instance (3600 s, 1800 s in the Deus Research Base and Shattered Arkanis). The frame comes again with the same times.</summary>
    Limit = 2,
    /// <summary>The 600 s left to leave after the last boss.</summary>
    Leave = 3,
    /// <summary>The last 10 s before the instance closes.</summary>
    Closing = 4,
}

/// <summary>One reading of the instance timer: the zone, what it counts, and the server's UTC start and end of it.</summary>
public readonly record struct InstanceTimer(int MapId, InstancePhase Phase, DateTime StartUtc, DateTime EndUtc, DateTime SeenAt)
{
    public TimeSpan Length => EndUtc - StartUtc;

    /// <summary>What is left at <paramref name="serverUtc"/> (the server's clock, see <see cref="ServerClock"/>), never below zero.</summary>
    public TimeSpan Remaining(DateTime serverUtc) => EndUtc > serverUtc ? EndUtc - serverUtc : TimeSpan.Zero;
}

/// <summary>
/// The instance timer, kept for later features (a time-limit countdown, a "time left to leave" notice): the frame <c>0x0061</c> is
/// <c>opcode | map u32 | phase u8 | start u64 | end u64</c>, 23 bytes, the times in UTC milliseconds of the server's clock. Read on the 18 debug
/// recordings of Aahz (2026-10-21 to 23): in every 600xxx instance the phases come in the order 1 (30 s), 2 (the limit), 3 (600 s after the last
/// boss; the frame <c>0x0161</c> comes right before it), 4 (10 s), and phase 2 comes again with the same times after a map reload. Open maps send the
/// frame too, with other numbers (the end before the start, or lengths of 110 s, 600 s, 780 s); those are not taken. Nothing shows it yet; only the
/// last reading is kept.
/// </summary>
public static class Aion2InstanceTimer
{
    private static readonly object Gate = new();
    private static InstanceTimer? _last;

    /// <summary>The longest length taken; anything longer is not an instance timer.</summary>
    public static readonly TimeSpan LongestLength = TimeSpan.FromHours(3);

    /// <summary>Raised (on the capturing thread) when a reading was taken.</summary>
    public static event Action? Changed;

    /// <summary>The last reading, or null before one came.</summary>
    public static InstanceTimer? Last
    {
        get
        {
            lock (Gate)
            {
                return _last;
            }
        }
    }

    /// <summary>The frame (opcode included) must be exactly 23 bytes, a known phase, and an end after the start by at most <see cref="LongestLength"/>.</summary>
    public static bool TryParse(ReadOnlySpan<byte> frame, DateTime seenAt, out InstanceTimer timer)
    {
        timer = default;
        if (frame.Length != 23 || frame[6] is < 1 or > 4)
        {
            return false;
        }

        long start = BinaryPrimitives.ReadInt64LittleEndian(frame[7..15]);
        long end = BinaryPrimitives.ReadInt64LittleEndian(frame[15..23]);
        if (start <= 0 || end <= start || end - start > (long)LongestLength.TotalMilliseconds)
        {
            return false;
        }

        timer = new InstanceTimer(
            unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[2..6])),
            (InstancePhase)frame[6],
            DateTimeOffset.FromUnixTimeMilliseconds(start).UtcDateTime,
            DateTimeOffset.FromUnixTimeMilliseconds(end).UtcDateTime,
            seenAt);
        return true;
    }

    public static void Receive(InstanceTimer timer)
    {
        lock (Gate)
        {
            _last = timer;
        }

        Changed?.Invoke();
    }

    public static void Reset()
    {
        lock (Gate)
        {
            _last = null;
        }
    }
}
