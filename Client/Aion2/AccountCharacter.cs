using System.Buffers.Binary;
using System.Text;

namespace AionDPS.Aion2;

/// <summary>
/// One character of the own account as the character list of the login (<c>0x2636</c>) shows it - for every character of the account, not only the one that
/// logs in. Only the basics are read: what the game's character selection shows. The list holds more (the visible equipment with its enchant levels, the
/// place the character logged out at), which is not used.
/// </summary>
/// <param name="Name">The character name.</param>
/// <param name="ClassCode">4 * class id + class bits (see <see cref="Protocol.Aion2SkillNames.ClassFromCode"/>).</param>
/// <param name="Faction">1 Elyos, 2 Asmodian (the byte behind the class code).</param>
/// <param name="Level">The character level.</param>
/// <param name="ItemLevel">The item level ("Gegenstandsstufe", the number beside the helmet the selection screen shows) - not the combat power, which only NC's character pages have.</param>
/// <param name="Legion">The legion of the character, null when it has none.</param>
/// <param name="LastOnlineMs">When the character was last online (ms since 1970); the time of the list for the character that is online now, 0 = unknown.</param>
/// <param name="CreatedMs">When the character was created (ms since 1970), 0 = unknown.</param>
/// <param name="ServerId">The server of the character (1304 = Kaisinel), 0 = not found.</param>
/// <param name="CharacterId">The character id the game's own character search knows it by, 0 = not found.</param>
public sealed record AccountCharacter(
    string Name, int ClassCode, int Faction, int Level, int ItemLevel, string? Legion, long LastOnlineMs, long CreatedMs, int ServerId, int CharacterId);

/// <summary>
/// Reads the characters out of the character list frame. An entry is: a length-prefixed name, then the class code (u32), the faction (1 or 2), one byte,
/// the level (u8), seven zero bytes, the item level (u32), 20 bytes, the length-prefixed legion, the time last online (u64), the place logged out at
/// (three floats and a subzone id), the creation time (u64), the visible equipment (260 bytes per piece) and a tail with the character id, the server id
/// and a time. Checked on the 52 character lists of the login dumps of 19 October 2026 (12 characters of three accounts) against the character selection
/// screens of two accounts (level, item level, legion and the time "Vor 16 Min." matched on every character).
/// </summary>
public static class AccountCharacterList
{
    private const long EpochMin = 1_700_000_000_000, EpochMax = 2_200_000_000_000;

    /// <summary>The characters of a character list frame (opcode included), in the order of the list; empty when the frame holds none.</summary>
    public static List<AccountCharacter> Parse(ReadOnlySpan<byte> frame)
    {
        var starts = new List<(int NameAt, int After, string Name)>();
        for (int i = 2; i + 60 < frame.Length; i++)
        {
            if (!TryName(frame, i, out string name))
            {
                continue;
            }

            int after = i + 1 + frame[i];
            if (after + 60 > frame.Length)
            {
                continue;
            }

            uint code = BinaryPrimitives.ReadUInt32LittleEndian(frame[after..]);
            if (code < 1 || (code - 1) / 4 is < 1 or > 9 || frame[after + 4] is not (1 or 2) || frame[after + 6] is < 1 or > 200)
            {
                continue;
            }

            starts.Add((i, after, name));
            i = after;
        }

        var characters = new List<AccountCharacter>();
        for (int s = 0; s < starts.Count; s++)
        {
            (int nameAt, int after, string name) = starts[s];
            int end = s + 1 < starts.Count ? starts[s + 1].NameAt : frame.Length;
            int classCode = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[after..]);
            int faction = frame[after + 4];
            int level = frame[after + 6];
            int itemLevel = (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(frame[(after + 14)..]));

            int legionAt = after + 38;
            int legionLength = frame[legionAt];
            string? legion = null;
            if (legionLength is > 0 and <= 40 && legionAt + 1 + legionLength <= end && Utf8.IsValid(frame.Slice(legionAt + 1, legionLength)))
            {
                legion = Encoding.UTF8.GetString(frame.Slice(legionAt + 1, legionLength));
            }
            bool layout = legionLength == 0 || legion is not null; // a legion that is no text means the entry is laid out differently: the times are then not read
            int block = legionAt + 1 + (legion is null ? 0 : legionLength); // the time last online, then the place, then the creation time
            long lastOnline = 0, created = 0;
            if (layout && block + 32 <= end)
            {
                lastOnline = Plausible(BinaryPrimitives.ReadUInt64LittleEndian(frame[block..]));
                created = Plausible(BinaryPrimitives.ReadUInt64LittleEndian(frame[(block + 24)..]));
            }

            // the tail: character id (u32) | 00 00 | server id (u16) | 00 00 00 00 | a time (u64)
            int serverId = 0, characterId = 0;
            for (int k = block + 32; k + 20 <= end; k++)
            {
                uint id = BinaryPrimitives.ReadUInt32LittleEndian(frame[k..]);
                ushort server = BinaryPrimitives.ReadUInt16LittleEndian(frame[(k + 6)..]);
                if (id is > 0 and < 100_000_000 && frame[k + 4] == 0 && frame[k + 5] == 0 && server is >= 1001 and <= 2999
                    && BinaryPrimitives.ReadUInt32LittleEndian(frame[(k + 8)..]) == 0
                    && Plausible(BinaryPrimitives.ReadUInt64LittleEndian(frame[(k + 12)..])) > 0)
                {
                    characterId = (int)id;
                    serverId = server;
                    break;
                }
            }

            characters.Add(new AccountCharacter(name, classCode, faction, level, itemLevel, legion, lastOnline, created, serverId, characterId));
        }

        return characters;
    }

    private static long Plausible(ulong ms) => ms is >= EpochMin and <= EpochMax ? (long)ms : 0;

    private static bool TryName(ReadOnlySpan<byte> frame, int at, out string name)
    {
        name = "";
        int length = frame[at];
        if (length is < 2 or > 24 || at + 1 + length > frame.Length)
        {
            return false;
        }

        ReadOnlySpan<byte> bytes = frame.Slice(at + 1, length);
        if (!Utf8.IsValid(bytes))
        {
            return false;
        }

        string candidate = Encoding.UTF8.GetString(bytes);
        foreach (char c in candidate)
        {
            if (!char.IsLetterOrDigit(c))
            {
                return false;
            }
        }

        name = candidate;
        return true;
    }

    private static class Utf8
    {
        public static bool IsValid(ReadOnlySpan<byte> bytes) => System.Text.Unicode.Utf8.IsValid(bytes);
    }
}
