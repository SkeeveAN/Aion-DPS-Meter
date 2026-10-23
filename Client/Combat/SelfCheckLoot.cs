using System.Buffers.Binary;
using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>The loot log: a drop on the ground, the item landing in the inventory and the drop leaving the ground make a pickup, in either order; a drop that vanished and
/// another player's pickup make none. The frames are built to the layouts seen on the recordings of 2026-10-09/10 (see <see cref="Aion2Loot"/>).</summary>
public static class SelfCheckLoot
{
    internal static byte[] Ground(uint lootId, int item, uint count)
    {
        var f = new byte[67];
        f[0] = 0x1e;
        f[1] = 0x56;
        f[2] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(35), lootId);
        BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(39), (uint)item);
        BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(43), count);
        return f;
    }

    internal static byte[] Gone(uint lootId, byte lastByte)
    {
        var f = new byte[24];
        f[0] = 0x1f;
        f[1] = 0x56;
        f[2] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(11), lootId);
        f[^1] = lastByte;
        return f;
    }

    internal static byte[] Inventory(byte kind, int item)
    {
        var f = new byte[43];
        f[0] = 0x1b;
        f[1] = 0x56;
        f[2] = 1;
        f[3] = kind;
        BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(13), (uint)item);
        return f;
    }

    public static bool Run()
    {
        Console.WriteLine("[selftest] Loot log (ground drop, inventory, pickup):");
        Aion2Loot.Clear();
        Aion2Loot.NoteZone(1010);
        DateTime t = new(2026, 10, 17, 20, 0, 0, DateTimeKind.Local);
        const int soul = 534660001, stone = 513560001;

        // inventory first, then the drop leaves the ground
        Aion2Loot.NoteGroundItem(Ground(1, soul, 1));
        Aion2Loot.NoteInventory(Inventory(0, soul), t);
        Aion2Loot.NoteGroundGone(Gone(1, 1), t.AddMilliseconds(100));
        // the drop leaves the ground first, then the inventory frame
        Aion2Loot.NoteGroundItem(Ground(2, soul, 2));
        Aion2Loot.NoteGroundGone(Gone(2, 1), t.AddSeconds(2));
        Aion2Loot.NoteInventory(Inventory(1, soul), t.AddSeconds(2).AddMilliseconds(80));
        // a drop that vanished, and another player's pickup (no inventory frame): not counted
        Aion2Loot.NoteGroundItem(Ground(3, stone, 1));
        Aion2Loot.NoteGroundGone(Gone(3, 0), t.AddSeconds(4));
        Aion2Loot.NoteGroundItem(Ground(4, stone, 1));
        Aion2Loot.NoteGroundGone(Gone(4, 1), t.AddSeconds(6));
        // the same item in the inventory long after the other's pickup: still not the pickup
        Aion2Loot.NoteInventory(Inventory(0, stone), t.AddSeconds(30));
        // a second map
        Aion2Loot.NoteZone(20);
        Aion2Loot.NoteGroundItem(Ground(5, stone, 3));
        Aion2Loot.NoteInventory(Inventory(0, stone), t.AddSeconds(40));
        Aion2Loot.NoteGroundGone(Gone(5, 1), t.AddSeconds(40).AddMilliseconds(50));

        var sessions = Aion2Loot.Sessions;
        LootSession? session = sessions.FirstOrDefault();
        LootMap? verteron = session?.Maps.FirstOrDefault(m => m.Zone == 1010);
        LootMap? abyss = session?.Maps.FirstOrDefault(m => m.Zone == 20);
        bool one = sessions.Count == 1 && session!.Maps.Count == 2;
        bool souls = verteron?.Items.Count == 1 && verteron.Items[0] is { ItemId: soul, Count: 3, Picks: 2 };
        bool stones = abyss?.Items.Count == 1 && abyss.Items[0] is { ItemId: stone, Count: 3, Picks: 1 };
        Console.WriteLine($"  -> one session with two maps: {one}; 2 pickups = 3 souls on the first map, either order of the two frames: {souls}; the stones of the second map only (a vanished drop and another's pickup left out): {stones}");

        // half an hour without a pickup starts the next session
        Aion2Loot.NoteGroundItem(Ground(6, soul, 1));
        Aion2Loot.NoteInventory(Inventory(0, soul), t.AddHours(2));
        Aion2Loot.NoteGroundGone(Gone(6, 1), t.AddHours(2).AddMilliseconds(30));
        bool second = Aion2Loot.Sessions.Count == 2 && Aion2Loot.Sessions[0].Start > Aion2Loot.Sessions[1].Start;
        Console.WriteLine($"  -> the next pickup after two hours is a new session: {second}");

        string name = Aion2.Protocol.Aion2ItemNames.NameOf(soul, "en") ?? "";
        string german = Aion2.Protocol.Aion2ItemNames.NameOf(soul, "de") ?? "";
        bool named = name.StartsWith("Soul:") && german.Length > 0 && Aion2.Protocol.Aion2ItemNames.GradeOf(110120003) == 4;
        Console.WriteLine($"  -> an item is named from the client's table in English and German ({name} / {german}), a gear piece has its grade: {named}");
        Aion2Loot.Clear();
        return one && souls && stones && second && named;
    }
}
