namespace AionDPS.Aion2;

/// <summary>
/// The teams of a 10 against 10 arena fight, as the game announces them a few seconds after the zone frame (opcode 0x19e3, seen in the two fights of
/// 2026-10-31 13:32 and 13:35): one block of ten members, then a second block of ten; each member is <c>...| name (length-prefixed UTF-8) | u32 | faction u8
/// (1 Elyos, 2 Asmodian) | level u8 | ...</c>. The first block is Team Blue, the second Team Red: in both fights the block with the local player (Red on the
/// result screen both times) came second and the history lists Blue on the left, Red on the right in the same order. Both fights had the local player in Red,
/// so "first block = Blue" is not yet tested against a fight in Blue.
/// </summary>
public static class Aion2ArenaTeams
{
    public const int Opcode = 0x19e3;

    /// <summary>The names of the two blocks; false unless the frame names exactly twenty players.</summary>
    public static bool TryParse(ReadOnlySpan<byte> frame, out List<string> blue, out List<string> red)
    {
        blue = new List<string>();
        red = new List<string>();
        var names = new List<string>();
        for (int k = 2; k < frame.Length - 8; k++)
        {
            int length = frame[k];
            if (length < 2 || length > 24 || k + 1 + length + 6 > frame.Length)
            {
                continue;
            }

            byte faction = frame[k + 1 + length + 4];
            byte level = frame[k + 1 + length + 5];
            if (faction is not (1 or 2) || level is 0 or > 60)
            {
                continue;
            }

            string name;
            try
            {
                name = System.Text.Encoding.UTF8.GetString(frame.Slice(k + 1, length));
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (name.Length == 0 || name.Any(char.IsControl) || name.Contains('\uFFFD'))
            {
                continue;
            }

            names.Add(name);
            k += length; // the next member starts behind this one's name
        }

        if (names.Count != 20)
        {
            return false;
        }

        blue = names.Take(10).ToList();
        red = names.Skip(10).ToList();
        return true;
    }
}
