namespace AionSniffer.Data;

/// <summary>
/// The Abyss Point value of the 16 "Ancient" relics, i.e. what the in-game Relic Appraiser pays
/// for each when exchanged ("Exchange Core" / "Add All Relics Owned"). Values transcribed from
/// that dialog by the user; item ids confirmed against ItemDatabase (assets/items/items_en_4x.json),
/// where all 16 sit in one contiguous block, 186000051-186000066, in exactly the reverse of the
/// order the dialog lists them.
///
/// Why this table exists at all: relics carry no AP until they are exchanged, and a relic pickup
/// is logged as plain loot with no value attached, so the AP a group actually hauled out is
/// invisible to the meter without this mapping. It is also the only way to see it for OTHER
/// players: the client reports AP gains ("You have gained N Abyss Points.") for the local player
/// alone, so everyone else's AP can only be reconstructed from what they picked up.
///
/// Two things this figure is NOT. It is "AP once exchanged", not AP already earned -- the relics
/// may still be sitting in the bag. And it will double-count against the exchange itself: the
/// Relic Appraiser's payout arrives as an ordinary AP gain line (the user's own dialog offered
/// 12.900 AP for 12 relics), which the personal-stat parser adds on top of the relic value
/// already counted here. An earlier version of this comment claimed the log contained no AP lines
/// at all and therefore no collision was possible; that was wrong -- the real session has 36 of
/// them, they just say "Abyss Points" rather than "AP". PlayerRow.RelicAp keeps the relic share
/// visible in the UI for exactly this reason, rather than burying it in one total.
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
