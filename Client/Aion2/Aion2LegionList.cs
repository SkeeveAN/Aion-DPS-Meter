using System.Buffers.Binary;
using System.Text;

namespace AionDPS.Aion2;

/// <summary>One member of the own legion as the legion list names him: the game's own server, class, level and numbers.</summary>
public sealed record LegionMember(int CharacterId, int ServerId, int ClassCode, string Name, int Level, int Rank, long LastActiveMs, int Zone, int ItemLevel, int CombatPower);

/// <summary>
/// The legion list frame 0x058a, sent at login: <c>opcode | 14 bytes head | member ...</c>, a member being
/// <c>character id u32 | 00 00 | server u16 | class code u32 | name (length in characters, UTF-8) | level u8 | 00 00 00 | rank u8 | [comment: length in bytes + text, and a byte]
/// | time u64 (ms) | zone u32 | item level u32 | combat power u32 | 0 u32 | flag u8 | E u32 | 0 u32</c> (docs/protocol/login-und-fenster-frames.md). The comment is
/// optional and has no marker, so the time (an epoch value of 2020 to 2030) is looked for after the rank. The server is the game's own record of the character:
/// it does not depend on any combat id or party frame.
/// </summary>
public static class Aion2LegionList
{
    public const int Opcode = 0x058a;
    private const int HeadLength = 14;
    private const long MinTimeMs = 1_600_000_000_000, MaxTimeMs = 1_900_000_000_000;
    private const int MaxComment = 300;

    public static bool TryParse(ReadOnlySpan<byte> frame, out List<LegionMember> members)
    {
        members = new List<LegionMember>();
        int p = HeadLength;
        while (p + 13 <= frame.Length)
        {
            int characterId = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]);
            int server = BinaryPrimitives.ReadUInt16LittleEndian(frame[(p + 6)..]);
            int classCode = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(p + 8)..]);
            int q = p + 12;
            if (server is < 1000 or > 9999 || !TryReadName(frame, ref q, out string name) || q + 5 > frame.Length)
            {
                break;
            }

            int level = frame[q], rank = frame[q + 4];
            q += 5;
            int time = q;
            while (time + 8 <= frame.Length && time < q + MaxComment && !IsTime(BinaryPrimitives.ReadUInt64LittleEndian(frame[time..])))
            {
                time++;
            }

            if (time + 33 > frame.Length || !IsTime(BinaryPrimitives.ReadUInt64LittleEndian(frame[time..])))
            {
                break;
            }

            long active = (long)BinaryPrimitives.ReadUInt64LittleEndian(frame[time..]);
            int zone = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(time + 8)..]);
            int itemLevel = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(time + 12)..]);
            int combatPower = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(time + 16)..]);
            members.Add(new LegionMember(characterId, server, classCode, name, level, rank, active, zone, itemLevel, combatPower));
            p = time + 8 + 16 + 1 + 8;
        }

        return members.Count > 0;
    }

    private static bool IsTime(ulong ms) => ms is > MinTimeMs and < MaxTimeMs;

    /// <summary>A name: a length in characters, then that many UTF-8 characters.</summary>
    private static bool TryReadName(ReadOnlySpan<byte> frame, ref int p, out string name)
    {
        name = "";
        if (p >= frame.Length)
        {
            return false;
        }

        int chars = frame[p++];
        int start = p;
        for (int i = 0; i < chars; i++)
        {
            if (p >= frame.Length)
            {
                return false;
            }

            byte lead = frame[p];
            p += lead < 0x80 ? 1 : lead < 0xE0 ? 2 : lead < 0xF0 ? 3 : 4;
        }

        if (p > frame.Length || chars < 2)
        {
            return false;
        }

        name = Encoding.UTF8.GetString(frame[start..p]);
        return true;
    }
}
