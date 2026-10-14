using System.Buffers.Binary;

namespace AionDPS.Aion2;

/// <summary>One entry of the cooldown list: the skill (or a shared cooldown group) and what is left of it, in the game's unit (see <see cref="Aion2Cooldowns"/>).</summary>
public readonly record struct CooldownEntry(int Id, int Remaining);

/// <summary>
/// The local player's cooldowns, kept for later features (a cooldown window of its own, notices): the frame <c>0x4738</c> is the whole list,
/// <c>n (u8) | n x (id u32 | remaining varint)</c>, 0 = ready; the game sends it about every 0.6 s while something runs, and a short one after a skill was used.
/// Verified on the 95 frames of the debug recording of 2026-10-13 (every frame reads to its last byte). The unit is not settled: the number falls
/// about 2.3 to 2.5 times as fast as the clock (see docs/protocol/cooldown-frames.md). Nothing shows it yet; only the last list is kept.
/// </summary>
public static class Aion2Cooldowns
{
    private static readonly object Gate = new();
    private static IReadOnlyList<CooldownEntry> _last = Array.Empty<CooldownEntry>();
    private static DateTime _at;

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
        }

        Changed?.Invoke();
    }

    public static void Reset()
    {
        lock (Gate)
        {
            _last = Array.Empty<CooldownEntry>();
            _at = default;
        }
    }
}
