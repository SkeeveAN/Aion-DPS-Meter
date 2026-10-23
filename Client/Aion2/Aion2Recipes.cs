using System.Buffers.Binary;

namespace AionDPS.Aion2;

/// <summary>
/// The crafting recipes the character has learned: the login frame 0x7e8d is <c>opcode | n (varint) | n x ( recipe id u32 | 0 u32 )</c>, the ids are those of
/// <see cref="CraftRecipe"/>. The list only comes at login and character change; a recipe learned during the session is not seen until the next login.
/// </summary>
public static class Aion2Recipes
{
    public const int Opcode = 0x7e8d;

    /// <summary>The crafting level-up frame <c>0x7d8d | profession u8 | level u16 (absolute) | ...</c> (docs/protocol/inventar-quest-frames.md).</summary>
    public const int LevelUpOpcode = 0x7d8d;

    /// <summary>The recipe category of a profession id of the level-up frame: 1 weapon smithing, 2 armor (tailoring), 3 jewelcrafting, 5 cooking are proven by screenshots; 4 alchemy follows that order and is not proven.</summary>
    private static readonly Dictionary<int, string> CategoryOf = new() { [1] = "Blacksmithing", [2] = "Tailoring", [3] = "Jewelcrafting", [4] = "Alchemy", [5] = "Cooking" };

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

    /// <summary>
    /// A crafting level-up: the character learns every recipe of the profession that is learnt by level ("Auto") up to the new level - no other frame tells it (a recipe is only
    /// learnt when the level rises). On the recordings' login lists every profession's learned "Auto" recipes ended below the lowest level not yet reached (all five professions), so
    /// the level is the whole rule. Without a login list nothing is known and nothing is added. Returns how many recipes were new.
    /// </summary>
    public static int NoteLevelUp(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 5 || !CategoryOf.TryGetValue(frame[2], out string? category))
        {
            return 0;
        }

        int level = BinaryPrimitives.ReadUInt16LittleEndian(frame[3..]);
        lock (Gate)
        {
            if (_known is null)
            {
                return 0;
            }

            int added = 0;
            foreach (CraftRecipe recipe in Aion2CraftData.All)
            {
                if (recipe.Category == category && recipe.Learn == "Auto" && recipe.Level <= level && _known.Add(recipe.Id))
                {
                    added++;
                }
            }

            return added;
        }
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
