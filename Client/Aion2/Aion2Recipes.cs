using System.Buffers.Binary;

namespace AionDPS.Aion2;

/// <summary>
/// The crafting recipes the character has learned: the login frame 0x7e8d is <c>opcode | n (varint) | n x ( recipe id u32 | 0 u32 )</c>, the ids are those of
/// <see cref="CraftRecipe"/>. The list only comes at login and character change; a recipe learned during the session is not seen until the next login.
/// </summary>
public static class Aion2Recipes
{
    public const int Opcode = 0x7e8d;

    private static readonly object Gate = new();
    private static HashSet<int>? _known;

    /// <summary>True once a list was read in this run.</summary>
    public static bool HasData
    {
        get { lock (Gate) { return _known is not null; } }
    }

    /// <summary>Forgets the list (a new connection, the test modes).</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _known = null;
        }
    }

    /// <summary>Reads the list frame (with its opcode); false when it does not have the layout.</summary>
    public static bool Note(ReadOnlySpan<byte> frame)
    {
        if (!TryParse(frame, out HashSet<int> ids))
        {
            return false;
        }

        lock (Gate)
        {
            _known = ids;
        }

        return true;
    }

    public static bool TryParse(ReadOnlySpan<byte> frame, out HashSet<int> ids)
    {
        ids = new HashSet<int>();
        int p = 2;
        long n = 0;
        int shift = 0;
        while (true)
        {
            if (p >= frame.Length || shift > 28)
            {
                return false;
            }

            byte b = frame[p++];
            n |= (long)(b & 0x7f) << shift;
            if ((b & 0x80) == 0)
            {
                break;
            }

            shift += 7;
        }

        if (frame.Length != p + 8 * n)
        {
            return false;
        }

        for (long i = 0; i < n; i++)
        {
            ids.Add((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[(p + (int)i * 8)..]));
        }

        return true;
    }

    /// <summary>Whether the recipe (or its twin of the other faction, since a list only holds the own faction's ids) was learned; true for all while no list is known.</summary>
    public static bool IsKnown(CraftRecipe recipe)
    {
        lock (Gate)
        {
            if (_known is null)
            {
                return true;
            }

            if (_known.Contains(recipe.Id))
            {
                return true;
            }
        }

        CraftRecipe? twin = Aion2CraftData.ForFaction(recipe.Id, !recipe.Elyos);
        lock (Gate)
        {
            return twin is not null && twin.Id != recipe.Id && _known!.Contains(twin.Id);
        }
    }
}
