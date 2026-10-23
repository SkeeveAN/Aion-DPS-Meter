using System.Buffers.Binary;
using System.Text;

namespace AionDPS.Aion2;

/// <summary>One entry of the game's rankings: the rank, the character and the numbers shown next to it.</summary>
public sealed record RankEntry(int Rank, int CharacterId, int ServerId, string Name, string Legion, int ClassId, long Points, string StatsJson, int Change, int Grade);

/// <summary>The own rank in one content: <see cref="Type"/> 1 Abyss, 3 Nightmare, 4 Transcendence, 5 Arena 1v1, 6 Arena 5v5, 21 Ascension rite; <see cref="Scope"/> 0 = overall, 2 = within the class.</summary>
public sealed record OwnRank(int Type, int Scope, RankEntry Entry, DateTime At);

/// <summary>
/// The ranking frames. An entry (also the entries of the lists in frame <c>0x558d</c>) is
/// <c>rank u32 | character id u32 | 00 00 | server u16 | name | legion | 01 00 2d | 7 x 00 + class byte | points u64 | stats JSON | change i32 | 0 u32 | grade u32 | grade u32</c>, the
/// strings with a length byte in front; <c>0x578d</c> (the own rank, sent when the ranking window opens) is <c>00 00 | type u8 | scope u8 | entry | 12 bytes</c>
/// (docs/protocol/login-und-fenster-frames.md).
/// </summary>
public static class Aion2Rankings
{
    public const int OwnRankOpcode = 0x578d;

    public static bool TryParseOwnRank(ReadOnlySpan<byte> frame, DateTime at, out OwnRank rank)
    {
        rank = null!;
        if (frame.Length < 8 || frame[2] != 0 || frame[3] != 0)
        {
            return false;
        }

        int p = 6;
        if (!TryReadEntry(frame, ref p, out RankEntry? entry))
        {
            return false;
        }

        rank = new OwnRank(frame[4], frame[5], entry!, at);
        return true;
    }

    public static bool TryReadEntry(ReadOnlySpan<byte> frame, ref int p, out RankEntry? entry)
    {
        entry = null;
        if (p + 12 > frame.Length)
        {
            return false;
        }

        int rank = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]);
        int characterId = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(p + 4)..]);
        int server = BinaryPrimitives.ReadUInt16LittleEndian(frame[(p + 10)..]);
        int q = p + 12;
        if (rank < 1 || server is < 1000 or > 9999 || !TryReadString(frame, ref q, out string name) || !TryReadString(frame, ref q, out string legion) || q + 3 + 8 + 8 + 1 > frame.Length)
        {
            return false;
        }

        q += 3; // 01 00 2d
        int classId = frame[q + 7];
        q += 8;
        long points = (long)BinaryPrimitives.ReadUInt64LittleEndian(frame[q..]);
        q += 8;
        if (!TryReadString(frame, ref q, out string stats) || q + 16 > frame.Length)
        {
            return false;
        }

        int change = BinaryPrimitives.ReadInt32LittleEndian(frame[q..]);
        int grade = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(q + 8)..]);
        p = q + 16;
        entry = new RankEntry(rank, characterId, server, name, legion, classId, points, stats, change, grade);
        return true;
    }

    private static bool TryReadString(ReadOnlySpan<byte> frame, ref int p, out string text)
    {
        text = "";
        if (p >= frame.Length)
        {
            return false;
        }

        int length = frame[p];
        if (p + 1 + length > frame.Length)
        {
            return false;
        }

        text = Encoding.UTF8.GetString(frame.Slice(p + 1, length));
        p += 1 + length;
        return true;
    }
}
