using System.Buffers.Binary;

namespace AionDPS.Aion2;

/// <summary>
/// The chain skills the own character can use right now. Frame 0x1838 (<c>opcode | skill u32 | 01 | follow-up u32</c>) opens the window of the follow-up after a cast, frame 0x1938
/// (<c>opcode | 01 | skill u32</c>) closes the window of a skill: it was used, or the time ran out (603 and 618 frames of the recordings, see docs/protocol/skill-frames.md).
/// The window is 3 s for 1,124 of the client's 1,158 chain skills.
/// </summary>
public static class Aion2Chains
{
    public const int OpenOpcode = 0x1838;
    public const int CloseOpcode = 0x1938;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(3);

    private static readonly object Gate = new();
    private static readonly Dictionary<int, (int From, DateTime At)> Open = new();

    /// <summary>The game's stamp is the local clock (<see cref="DateTime.Now"/>); a replay sets its own.</summary>
    public static Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    public static void Reset()
    {
        lock (Gate)
        {
            Open.Clear();
        }
    }

    /// <summary>A follow-up skill became available (frame with its opcode).</summary>
    public static bool NoteOpen(ReadOnlySpan<byte> frame, DateTime at)
    {
        if (frame.Length != 11 || frame[6] != 1)
        {
            return false;
        }

        int from = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[2..]);
        int to = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[7..]);
        lock (Gate)
        {
            Open[to] = (from, at);
        }

        return true;
    }

    /// <summary>The window of a skill closed (frame with its opcode).</summary>
    public static bool NoteClose(ReadOnlySpan<byte> frame)
    {
        if (frame.Length != 7 || frame[2] != 1)
        {
            return false;
        }

        int skill = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[3..]);
        lock (Gate)
        {
            return Open.Remove(skill);
        }
    }

    /// <summary>The follow-ups available now with the seconds left of their window, the least left first. A window the game never closed (a frame lost) ends by itself after 3.5 s.</summary>
    public static IReadOnlyList<(int Skill, double SecondsLeft)> Available(DateTime now)
    {
        lock (Gate)
        {
            foreach (int expired in Open.Where(o => now - o.Value.At > Window + TimeSpan.FromMilliseconds(500)).Select(o => o.Key).ToList())
            {
                Open.Remove(expired);
            }

            return Open.Select(o => (o.Key, Math.Max(0, (Window - (now - o.Value.At)).TotalSeconds))).OrderBy(o => o.Item2).ToList();
        }
    }
}
