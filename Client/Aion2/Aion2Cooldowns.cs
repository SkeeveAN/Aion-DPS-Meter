using System.Buffers.Binary;

namespace AionDPS.Aion2;

/// <summary>One entry of the cooldown list: the skill (or a shared cooldown group) and what is left of it, in the game's unit (see <see cref="Aion2Cooldowns"/>).</summary>
public readonly record struct CooldownEntry(int Id, int Remaining);

/// <summary>
/// The local player's cooldowns, kept for later features (a cooldown window of its own, notices): the frame <c>0x4738</c> is the whole list,
/// <c>n (u8) | n x (id u32 | remaining varint)</c>, 0 = ready; the game sends it about every 0.6 s while something runs, and a short one after a skill was used.
/// Verified on the 95 frames of the debug recording of 2026-10-13 (every frame reads to its last byte). The unit is not settled: the number falls
/// about 2.3 to 2.5 times as fast as the clock, and not the same for every skill (see docs/protocol/cooldown-frames.md). The assist shows the time left from the
/// speed measured per skill (<see cref="Left"/>): every stone, stat or Daevanion node that changes a cooldown is in it without being known.
/// </summary>
public static class Aion2Cooldowns
{
    private static readonly object Gate = new();
    private static IReadOnlyList<CooldownEntry> _last = Array.Empty<CooldownEntry>();
    private static DateTime _at;

    /// <summary>The numbers seen per skill since it last was ready (or last started again), oldest first.</summary>
    private static readonly Dictionary<int, List<(DateTime At, int Value)>> Runs = new();

    /// <summary>The speed assumed until a skill's own was measured (units per second): the counter falls about 2.4 times as fast as the clock.</summary>
    public const double FallbackRate = 2400;

    private const int KeptSamples = 8;

    /// <summary>A number that rises by more than this over the one before is a new use of the skill (the counter starts again).</summary>
    private const int RestartRise = 200;

    /// <summary>What one frame's timestamp may be off the straight line (capture and batching jitter, about 0.6 s of counter); more than this is a jump (a stone that took time off).</summary>
    private const double JumpResidual = 1500;

    /// <summary>Raised (on the capturing thread) when a list arrived.</summary>
    public static event Action? Changed;

    /// <summary>The last list and when it came.</summary>
    public static (IReadOnlyList<CooldownEntry> Entries, DateTime At) Last
    {
        get
        {
            lock (Gate)
            {
                return (_last, _at);
            }
        }
    }

    /// <summary>The frame (opcode included) must read to its last byte.</summary>
    public static bool TryParse(ReadOnlySpan<byte> frame, out List<CooldownEntry> entries)
    {
        entries = new List<CooldownEntry>();
        if (frame.Length < 3)
        {
            return false;
        }

        int count = frame[2];
        int p = 3;
        for (int i = 0; i < count; i++)
        {
            if (p + 4 > frame.Length)
            {
                return false;
            }

            int id = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]);
            p += 4;
            long value = 0;
            int shift = 0;
            bool done = false;
            while (p < frame.Length && shift < 35)
            {
                byte b = frame[p++];
                value |= (long)(b & 0x7f) << shift;
                if ((b & 0x80) == 0)
                {
                    done = true;
                    break;
                }

                shift += 7;
            }

            if (!done || value > int.MaxValue)
            {
                return false;
            }

            entries.Add(new CooldownEntry(id, (int)value));
        }

        return p == frame.Length;
    }

    public static void Receive(List<CooldownEntry> entries, DateTime at)
    {
        lock (Gate)
        {
            // the short frames name one skill: what they do not mention stays as it was
            if (entries.Count < 5 && _last.Count > entries.Count && _at != default && at - _at < TimeSpan.FromSeconds(10))
            {
                _last = _last.Where(e => entries.All(n => n.Id != e.Id)).Concat(entries).ToList();
            }
            else
            {
                _last = entries;
            }

            _at = at;
            foreach (CooldownEntry e in entries)
            {
                Note(e, at);
            }
        }

        Changed?.Invoke();
    }

    private static void Note(CooldownEntry e, DateTime at)
    {
        if (e.Remaining <= 0)
        {
            Runs.Remove(e.Id);
            return;
        }

        if (!Runs.TryGetValue(e.Id, out List<(DateTime At, int Value)>? run))
        {
            Runs[e.Id] = run = new List<(DateTime, int)>();
        }

        if (run.Count > 0)
        {
            (DateTime lastAt, int lastValue) = run[^1];
            if (at <= lastAt)
            {
                return;
            }

            if (e.Remaining > lastValue + RestartRise)
            {
                run.Clear(); // used again
            }
        }

        run.Add((at, e.Remaining));
        if (run.Count > KeptSamples)
        {
            run.RemoveAt(0);
        }
    }

    /// <summary>
    /// The speed of a skill's counter in units per second, measured from its own numbers since it started: a straight line through the last few of them, and where a
    /// stone took time off in between (a jump), only the numbers after it. Null while there are not two numbers at least half a second apart.
    /// </summary>
    public static double? MeasuredRate(int id)
    {
        lock (Gate)
        {
            return Runs.TryGetValue(id, out var run) ? Fit(run) : null;
        }
    }

    private static double? Fit(IReadOnlyList<(DateTime At, int Value)> run)
    {
        for (int from = 0; from < run.Count - 1; from++)
        {
            int n = run.Count - from;
            double t0 = run[from].At.Ticks;
            double meanT = 0, meanV = 0;
            for (int i = from; i < run.Count; i++)
            {
                meanT += (run[i].At.Ticks - t0) / TimeSpan.TicksPerSecond;
                meanV += run[i].Value;
            }

            meanT /= n;
            meanV /= n;
            double sxx = 0, sxy = 0;
            for (int i = from; i < run.Count; i++)
            {
                double dt = (run[i].At.Ticks - t0) / TimeSpan.TicksPerSecond - meanT;
                sxx += dt * dt;
                sxy += dt * (run[i].Value - meanV);
            }

            double span = (run[^1].At - run[from].At).TotalSeconds;
            if (sxx <= 0 || span < 0.5)
            {
                return null;
            }

            double slope = sxy / sxx; // negative: the number falls
            double worst = 0;
            for (int i = from; i < run.Count; i++)
            {
                double dt = (run[i].At.Ticks - t0) / TimeSpan.TicksPerSecond - meanT;
                worst = Math.Max(worst, Math.Abs(run[i].Value - (meanV + slope * dt)));
            }

            if (slope < 0 && (worst <= JumpResidual || n == 2))
            {
                return -slope;
            }
        }

        return null;
    }

    /// <summary>
    /// What is left of a running cooldown in seconds at <paramref name="now"/>, and whether the speed was measured from the skill's own numbers (otherwise the
    /// assumed <see cref="FallbackRate"/>: an estimate). <paramref name="remaining"/> and <paramref name="at"/> are the entry of the list and when it came;
    /// the skill's own last number is used when there is one.
    /// </summary>
    public static (double Seconds, double Rate, bool Measured) Left(int id, int remaining, DateTime at, DateTime now)
    {
        lock (Gate)
        {
            double? rate = Runs.TryGetValue(id, out var run) ? Fit(run) : null;
            if (Runs.TryGetValue(id, out var own) && own.Count > 0)
            {
                (at, remaining) = own[^1];
            }

            double used = rate ?? FallbackRate;
            return (remaining / used - Math.Max(0, (now - at).TotalSeconds), used, rate is not null);
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            _last = Array.Empty<CooldownEntry>();
            _at = default;
            Runs.Clear();
        }
    }
}
