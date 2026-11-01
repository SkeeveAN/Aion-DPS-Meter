using System.Buffers.Binary;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Aion2;

/// <summary>Where a stock lies: the three places the crafting tool counts (the container numbers are the game's).</summary>
[Flags]
public enum StockSources
{
    None = 0,
    Inventory = 1,
    CharacterStorage = 2,
    AccountStorage = 4,
    All = Inventory | CharacterStorage | AccountStorage,
}

/// <summary>
/// What the local player owns, read from the data stream: the inventory, the character storage and the account (server) storage.
/// <para>At world entry the server sends one big frame (0x1156) with every container: <c>... | item id u32 | quantity u64 | container u8 | slot u8 | ...</c>
/// (container 1 = the cube of the inventory with the currencies, 15 = character storage, 2 = account storage, 11 = the worn gear, 17 = a second gear set).
/// Gear carries more bytes after the slot, so a record is found by its anchor (a known item id, a quantity that fits, a known container, the kind byte 0..2
/// ten bytes before the item), not by a fixed size; checked on 2026-10-21 against screenshots of all three containers (30/118 slots exact).</para>
/// <para>Later changes of the inventory arrive in 0x1B56: one record per slot with the new quantity (kind 0 new slot, 1 changed, 2 gone). A sort in the game
/// resends the whole inventory (one frame of many records). Moving something between the inventory and a storage sends nothing the meter can read,
/// so a whole listing is compared with what the single frames had added up to: an item that is more (or less) in the inventory than they explain was
/// taken from (or put into) the character storage first, then the account storage. The storages are therefore exact at login and after a sort.</para>
/// </summary>
public static class Aion2Stock
{
    public const int InventoryContainer = 1;
    public const int CharacterStorageContainer = 15;
    public const int AccountStorageContainer = 2;
    private const int FirstSyntheticSlot = 10_000;

    private static readonly object Gate = new();
    private static readonly Dictionary<int, Dictionary<int, (int Item, long Quantity)>> Containers = new();
    private static int _synthetic = FirstSyntheticSlot;
    // When an item with a term runs out (UTC ms), by container and slot; only what the game's records carried.
    private static readonly Dictionary<(int Container, int Slot), long> Expiry = new();

    /// <summary>Raised after the stock changed (on the thread that decoded the frame).</summary>
    public static event Action? Changed;

    /// <summary>When the last full frame (login) was read; null before the first.</summary>
    public static DateTime? SnapshotAt { get; private set; }

    /// <summary>When the inventory was last listed in full (login or a sort): the storages are exact up to then.</summary>
    public static DateTime? ListedAt { get; private set; }

    /// <summary>True once a login frame was read in this run.</summary>
    public static bool HasData => SnapshotAt is not null;

    /// <summary>Forgets everything (a new connection, the test modes).</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            Containers.Clear();
            Expiry.Clear();
            _synthetic = FirstSyntheticSlot;
            SnapshotAt = null;
            ListedAt = null;
        }

        Changed?.Invoke();
    }

    /// <summary>The login frame (0x1156, with its opcode): replaces everything.</summary>
    public static void NoteSnapshot(ReadOnlySpan<byte> frame, DateTime at)
    {
        // the login frame is always 15 KB or more; a smaller one of that opcode would be something else
        if (frame.Length < 5000)
        {
            return;
        }

        List<Record> records = Read(frame);
        if (records.Count == 0)
        {
            return;
        }

        lock (Gate)
        {
            Containers.Clear();
            Expiry.Clear();
            _synthetic = FirstSyntheticSlot;
            foreach (Record r in records)
            {
                if (r.Kind != 2 && r.Quantity > 0)
                {
                    SlotsOf(r.Container)[r.Slot] = (r.Item, r.Quantity);
                    SetExpiry(r);
                }
            }

            SnapshotAt = at;
            ListedAt = at;
        }

        Changed?.Invoke();
    }

    /// <summary>An inventory change (0x1B56, with its opcode).</summary>
    public static void NoteChange(ReadOnlySpan<byte> frame, DateTime at)
    {
        List<Record> records = Read(frame);
        if (records.Count == 0)
        {
            return;
        }

        lock (Gate)
        {
            if (SnapshotAt is null)
            {
                // Nothing to add to before the login frame: a recording that starts later has no stock to follow. Only the Odyle energy items are kept
                // (what the meter saw arrive is a number the website may show, marked as not complete).
                foreach (Record r in records.Where(r => IsOdyleItem(r.Item)))
                {
                    Dictionary<int, (int Item, long Quantity)> slots = SlotsOf(r.Container);
                    if (r.Kind == 2 || r.Quantity == 0)
                    {
                        slots.Remove(r.Slot);
                        Expiry.Remove((r.Container, r.Slot));
                    }
                    else
                    {
                        slots[r.Slot] = (r.Item, r.Quantity);
                        SetExpiry(r);
                    }
                }

                return;
            }

            List<Record> inventory = records.Where(r => r.Container == InventoryContainer).ToList();
            // a sort resends the inventory: many records of kind 1 on consecutive slots (eight new items picked up at once are kind 0)
            bool listing = inventory.Count >= 8 && inventory.All(r => r.Kind == 1) && inventory.Max(r => r.Slot) - inventory.Min(r => r.Slot) + 1 == inventory.Count;
            Dictionary<int, long>? before = listing ? Totals(InventoryContainer) : null;
            foreach (Record r in records)
            {
                Dictionary<int, (int Item, long Quantity)> slots = SlotsOf(r.Container);
                if (r.Kind == 2 || r.Quantity == 0)
                {
                    slots.Remove(r.Slot);
                    Expiry.Remove((r.Container, r.Slot));
                }
                else
                {
                    slots[r.Slot] = (r.Item, r.Quantity);
                    SetExpiry(r);
                }
            }

            if (listing && before is not null)
            {
                int first = inventory.Min(r => r.Slot);
                Dictionary<int, (int Item, long Quantity)> slots = SlotsOf(InventoryContainer);
                var listed = inventory.Select(r => r.Slot).ToHashSet();
                foreach (int slot in slots.Keys.Where(s => s >= first && !listed.Contains(s)).ToList())
                {
                    slots.Remove(slot);
                }

                Settle(before, Totals(InventoryContainer));
                ListedAt = at;
            }
        }

        Changed?.Invoke();
    }

    /// <summary>How many of an item lie in the chosen places.</summary>
    public static long Count(int item, StockSources sources)
    {
        long total = 0;
        lock (Gate)
        {
            foreach ((int container, StockSources source) in Places)
            {
                if ((sources & source) != 0 && Containers.TryGetValue(container, out var slots))
                {
                    total += slots.Values.Where(v => v.Item == item).Sum(v => v.Quantity);
                }
            }
        }

        return total;
    }

    /// <summary>Every item of the chosen places with its quantity.</summary>
    public static Dictionary<int, long> Totals(StockSources sources)
    {
        var totals = new Dictionary<int, long>();
        lock (Gate)
        {
            foreach ((int container, StockSources source) in Places)
            {
                if ((sources & source) != 0)
                {
                    Add(totals, container);
                }
            }
        }

        return totals;
    }

    private static void SetExpiry(Record r)
    {
        if (r.Expires > 0)
        {
            Expiry[(r.Container, r.Slot)] = r.Expires;
        }
        else
        {
            Expiry.Remove((r.Container, r.Slot));
        }
    }

    /// <summary>One stack of an Odyle energy item: where it lies and when it runs out (UTC ms, 0 = no term seen).</summary>
    public sealed record OdyleStack(int Item, long Quantity, StockSources Place, long ExpiresMs);

    /// <summary>True for the items that recharge Odyle energy (small and large, bound or not, with or without a term): the game's English name says "Odyle Energy".</summary>
    public static bool IsOdyleItem(int item) => Aion2ItemNames.NameOf(item, "en") is { } name && name.Contains("Odyle Energy", StringComparison.Ordinal);

    /// <summary>The energy one piece recharges: 10 for the small item, 40 for the large one (as the item texts of the game say).</summary>
    public static int OdyleUnitOf(int item) => Aion2ItemNames.NameOf(item, "en") is { } name && name.Contains("Small", StringComparison.Ordinal) ? 10 : 40;

    /// <summary>Every stack of Odyle energy items in the inventory and both storages.</summary>
    public static List<OdyleStack> OdyleStacks()
    {
        var stacks = new List<OdyleStack>();
        lock (Gate)
        {
            foreach ((int container, StockSources source) in Places)
            {
                if (!Containers.TryGetValue(container, out var slots))
                {
                    continue;
                }

                foreach ((int slot, (int item, long quantity)) in slots.OrderBy(x => x.Key))
                {
                    if (IsOdyleItem(item))
                    {
                        stacks.Add(new OdyleStack(item, quantity, source, Expiry.GetValueOrDefault((container, slot))));
                    }
                }
            }
        }

        return stacks;
    }

    private static readonly (int Container, StockSources Source)[] Places =
    {
        (InventoryContainer, StockSources.Inventory),
        (CharacterStorageContainer, StockSources.CharacterStorage),
        (AccountStorageContainer, StockSources.AccountStorage),
    };

    private static Dictionary<int, long> Totals(int container)
    {
        var totals = new Dictionary<int, long>();
        Add(totals, container);
        return totals;
    }

    private static void Add(Dictionary<int, long> totals, int container)
    {
        if (Containers.TryGetValue(container, out var slots))
        {
            foreach ((int item, long quantity) in slots.Values)
            {
                totals[item] = totals.GetValueOrDefault(item) + quantity;
            }
        }
    }

    private static Dictionary<int, (int Item, long Quantity)> SlotsOf(int container)
    {
        if (!Containers.TryGetValue(container, out var slots))
        {
            Containers[container] = slots = new();
        }

        return slots;
    }

    /// <summary>A full listing of the inventory differs from the sum of the single frames by what was moved to or from a storage.</summary>
    private static void Settle(Dictionary<int, long> before, Dictionary<int, long> after)
    {
        foreach (int item in before.Keys.Union(after.Keys))
        {
            long delta = after.GetValueOrDefault(item) - before.GetValueOrDefault(item);
            if (delta > 0)
            {
                foreach (int container in new[] { CharacterStorageContainer, AccountStorageContainer })
                {
                    delta -= Take(container, item, delta);
                }
            }
            else if (delta < 0)
            {
                SlotsOf(CharacterStorageContainer)[_synthetic++] = (item, -delta);
            }
        }
    }

    private static long Take(int container, int item, long wanted)
    {
        if (wanted <= 0 || !Containers.TryGetValue(container, out var slots))
        {
            return 0;
        }

        long taken = 0;
        foreach (int slot in slots.Where(s => s.Value.Item == item).Select(s => s.Key).OrderBy(s => s).ToList())
        {
            if (taken >= wanted)
            {
                break;
            }

            long here = Math.Min(slots[slot].Quantity, wanted - taken);
            taken += here;
            if (slots[slot].Quantity - here <= 0)
            {
                slots.Remove(slot);
            }
            else
            {
                slots[slot] = (item, slots[slot].Quantity - here);
            }
        }

        return taken;
    }

    private readonly record struct Record(int Kind, int Container, int Slot, int Item, long Quantity, long Expires);

    /// <summary>The records of a frame (opcode, then the body), found by their anchor; see the class remarks.</summary>
    private static List<Record> Read(ReadOnlySpan<byte> frame)
    {
        var records = new List<Record>();
        ReadOnlySpan<byte> body = frame.Length > 2 ? frame[2..] : default;
        var seen = new HashSet<(int, int)>();
        for (int i = 10; i + 14 <= body.Length; i++)
        {
            int item = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(body[i..]));
            byte container = body[i + 12];
            if (item < 100_000_000 || body[i - 10] > 2 || container is not (1 or 2 or 4 or 11 or 15 or 17)
                || body[i + 9] != 0 || body[i + 10] != 0 || body[i + 11] != 0 || !Aion2ItemNames.Knows(item))
            {
                continue;
            }

            long quantity = unchecked((long)BinaryPrimitives.ReadUInt64LittleEndian(body[(i + 4)..]));
            int slot = body[i + 13];
            if (seen.Add((container, slot)))
            {
                // four bytes after the slot, then the term as UTC ms (0 or absent for an item without one); only a date of the game's years counts
                long expires = i + 26 <= body.Length ? unchecked((long)BinaryPrimitives.ReadUInt64LittleEndian(body[(i + 18)..])) : 0;
                records.Add(new Record(body[i - 10], container, slot, item, quantity, expires is > 1_700_000_000_000 and < 2_300_000_000_000 ? expires : 0));
            }

            i += 13;
        }

        return records;
    }
}
