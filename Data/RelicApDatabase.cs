namespace AionSniffer.Data;

/// <summary>
/// The Abyss Point value of the 16 "Ancient" relics, i.e. what the in-game Relic Appraiser pays
/// for each when exchanged ("Exchange Core" / "Add All Relics Owned"). Values transcribed from
/// that dialog by the user; item ids confirmed against ItemDatabase (assets/items/items_en_4x.json),
/// where all 16 sit in one contiguous block, 186000051-186000066, in exactly the reverse of the
/// order the dialog lists them.
///
/// Why this table exists at all: relics carry no AP until they are exchanged, so Chat.log never
/// shows AP for them -- confirmed against a real 18k-line session, which contains loot lines for
/// 22 relics (Kisame 12, the local player 10) and not a single AP line of any kind. Without this
/// mapping, a group's actual AP haul is invisible to the meter, which is what the user asked to
/// fix: relic loot counts toward the AP total of whoever picked it up.
///
/// Consequence worth knowing: an AP figure that includes relics is "AP earned, once exchanged",
/// not "AP in your pocket right now". Should a future client build start logging the exchange
/// itself as an AP gain, the two would need reconciling -- today they cannot collide, because
/// nothing in the log reports it.
/// </summary>
public static class RelicApDatabase
{
    private static readonly IReadOnlyDictionary<int, int> ApByItemId = new Dictionary<int, int>
    {
        // Crown (the top tier: 2.400 / 4.800 / 7.200 / 9.600)
        [186000054] = 2_400, // Lesser Ancient Crown
        [186000053] = 4_800, // Ancient Crown
        [186000052] = 7_200, // Greater Ancient Crown
        [186000051] = 9_600, // Major Ancient Crown

        // Goblet (1.200 / 2.400 / 3.600 / 4.800)
        [186000058] = 1_200, // Lesser Ancient Goblet
        [186000057] = 2_400, // Ancient Goblet
        [186000056] = 3_600, // Greater Ancient Goblet
        [186000055] = 4_800, // Major Ancient Goblet

        // Seal (600 / 1.200 / 1.800 / 2.400)
        [186000062] = 600,   // Lesser Ancient Seal
        [186000061] = 1_200, // Ancient Seal
        [186000060] = 1_800, // Greater Ancient Seal
        [186000059] = 2_400, // Major Ancient Seal

        // Icon (the bottom tier: 300 / 600 / 900 / 1.200)
        [186000066] = 300,   // Lesser Ancient Icon
        [186000065] = 600,   // Ancient Icon
        [186000064] = 900,   // Greater Ancient Icon
        [186000063] = 1_200, // Major Ancient Icon
    };

    /// <summary>Total AP for <paramref name="quantity"/> copies of an item, or 0 when the item is
    /// not a relic -- a plain 0 rather than null so callers can add it unconditionally, since
    /// "not a relic" and "a relic worth nothing" are not two different cases here.</summary>
    public static long ApFor(int itemId, long quantity = 1) =>
        ApByItemId.TryGetValue(itemId, out int ap) ? ap * quantity : 0;

    /// <summary>True for the 16 relic ids -- lets callers tell "relic worth counting" from
    /// ordinary loot without reading anything into a zero.</summary>
    public static bool IsRelic(int itemId) => ApByItemId.ContainsKey(itemId);

    /// <summary>Every relic id with its AP value, for the selftest to check the table against the
    /// Relic Appraiser dialog without reflection.</summary>
    public static IReadOnlyDictionary<int, int> All => ApByItemId;
}
