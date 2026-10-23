using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2;

/// <summary>One kind of item picked up on one map in one session: how many, in how many pickups, the first and the last.</summary>
public sealed class LootItem
{
    public int ItemId { get; set; }
    public long Count { get; set; }
    public int Picks { get; set; }
    public DateTime First { get; set; }
    public DateTime Last { get; set; }
}

public sealed class LootMap
{
    public int Zone { get; set; }
    public List<LootItem> Items { get; set; } = new();
}

public sealed class LootSession
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public List<LootMap> Maps { get; set; } = new();
}

/// <summary>
/// What the player picked up, by session and map; only on this computer, nothing is sent anywhere. Three frames tell it:
/// 0x1E56 is a drop appearing on the ground (<c>opcode | 01 | monster u32 | x y z f32 | x y z f32 | u32 | loot id u32 at 35 | item id u32 at 39 | count u32 at 43</c>),
/// 0x1F56 is a drop leaving the ground (<c>opcode | 01 | monster u32 | u32 | loot id u32 at 11 | owner u32 | ... | last byte 1 = picked up by its owner, 0 = went otherwise; checked on 1,546 drops of the two own characters, 2026-10-23</c>) and 0x1B56 an item landing in
/// the inventory (<c>opcode | 01 | kind (0 new slot, 1 more of it, 2 gone) | ... | item id u32 at 13</c>). A drop that left the ground counts when the same item landed in the inventory
/// within 1.5 s of it, either way round: another player's pickup, and a drop that vanished, never put anything into the inventory. 0x2136 names the map. The layouts
/// were first seen in StacysView's fork of this meter (2026-10-17, MIT) and checked here on the recordings.
/// </summary>
public static class Aion2Loot
{
    public const int GroundItemOpcode = 0x1e56;
    public const int GroundGoneOpcode = 0x1f56;
    private const int MaxDrops = 500;
    private const int MaxSessions = 60;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(30);

    private static readonly object Gate = new();
    private static readonly List<LootSession> Stored = new();
    private static readonly Dictionary<uint, (int Item, long Count)> Drops = new();
    private static readonly Queue<uint> DropOrder = new();
    private static readonly List<(int Item, DateTime At)> Inventory = new();
    private static readonly List<(int Item, long Count, DateTime At)> Waiting = new();
    private static int _zone;
    private static LootSession? _active;
    private static DateTime _lastPickup;
    private static bool _persist;
    private static bool _dirty;
    private static DateTime _savedAt = DateTime.MinValue;

    /// <summary>Raised (on the capturing thread) when a pickup was counted or the log was changed.</summary>
    public static event Action? Changed;

    public static string StorePath => Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "aion2-loot.json");

    /// <summary>The map the player is on (0 = not known yet).</summary>
    public static int CurrentZone
    {
        get
        {
            lock (Gate)
            {
                return _zone;
            }
        }
    }

    /// <summary>The sessions, newest first (copies).</summary>
    public static IReadOnlyList<LootSession> Sessions
    {
        get
        {
            lock (Gate)
            {
                return Stored.Select(Copy).ToList();
            }
        }
    }

    /// <summary>The session that is running (a pickup less than half an hour ago, in this run of the meter), or null.</summary>
    public static LootSession? Current
    {
        get
        {
            lock (Gate)
            {
                return _active is { } active && DateTime.Now - _lastPickup < SessionGap ? Copy(active) : null;
            }
        }
    }

    private static LootSession Copy(LootSession s) => new()
    {
        Start = s.Start, End = s.End,
        Maps = s.Maps.Select(m => new LootMap { Zone = m.Zone, Items = m.Items.Select(i => new LootItem { ItemId = i.ItemId, Count = i.Count, Picks = i.Picks, First = i.First, Last = i.Last }).ToList() }).ToList(),
    };

    /// <summary>Starts keeping the log in <see cref="StorePath"/> (the live meter only: a replay or a test keeps nothing). Loads what is there.</summary>
    public static void EnablePersistence()
    {
        lock (Gate)
        {
            _persist = true;
            try
            {
                if (File.Exists(StorePath))
                {
                    var loaded = JsonSerializer.Deserialize<List<LootSession>>(File.ReadAllText(StorePath));
                    Stored.Clear();
                    Stored.AddRange((loaded ?? new()).Take(MaxSessions));
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // A broken file: start with an empty log.
            }
        }
    }

    /// <summary>Deletes the whole log (<paramref name="session"/> null) or one session.</summary>
    public static void Clear(LootSession? session = null)
    {
        lock (Gate)
        {
            if (session is null)
            {
                Stored.Clear();
                _active = null;
            }
            else
            {
                LootSession? found = Stored.FirstOrDefault(s => s.Start == session.Start);
                if (found is not null)
                {
                    Stored.Remove(found);
                    if (ReferenceEquals(found, _active))
                    {
                        _active = null;
                    }
                }
            }

            _dirty = true;
            Save(force: true);
        }

        Changed?.Invoke();
    }

    public static void NoteZone(int zone)
    {
        lock (Gate)
        {
            _zone = zone;
        }
    }

    /// <summary>A drop appeared on the ground (0x1E56).</summary>
    public static void NoteGroundItem(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 47)
        {
            return;
        }

        uint lootId = BinaryPrimitives.ReadUInt32LittleEndian(frame[35..]);
        int item = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[39..]));
        long count = BinaryPrimitives.ReadUInt32LittleEndian(frame[43..]);
        if (item <= 0 || count is <= 0 or > 1_000_000_000)
        {
            return;
        }

        lock (Gate)
        {
            if (Drops.TryAdd(lootId, (item, count)))
            {
                DropOrder.Enqueue(lootId);
                while (DropOrder.Count > MaxDrops)
                {
                    Drops.Remove(DropOrder.Dequeue());
                }
            }
        }
    }

    /// <summary>A drop left the ground (0x1F56): picked up (last byte 1) or went otherwise (0). One frame can carry several drops (20 bytes each, behind a 3 byte head).</summary>
    public static void NoteGroundGone(ReadOnlySpan<byte> frame, DateTime at)
    {
        if (frame.Length < 24 || frame[^1] != 1)
        {
            return;
        }

        int sets = Math.Min(frame[2], (frame.Length - 4) / 20);
        bool counted = false;
        for (int i = 0; i < sets; i++)
        {
            uint lootId = BinaryPrimitives.ReadUInt32LittleEndian(frame[(3 + 20 * i + 8)..]);
            lock (Gate)
            {
                if (!Drops.Remove(lootId, out var drop))
                {
                    continue;
                }

                int seen = Inventory.FindLastIndex(x => x.Item == drop.Item && Math.Abs((at - x.At).TotalSeconds) <= Window.TotalSeconds);
                if (seen >= 0)
                {
                    Inventory.RemoveAt(seen);
                    Add(drop.Item, drop.Count, at);
                    counted = true;
                }
                else
                {
                    // The inventory frame may come a moment after the drop's.
                    Waiting.RemoveAll(w => at - w.At > Window);
                    Waiting.Add((drop.Item, drop.Count, at));
                }
            }
        }

        if (counted)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>An item landed in the inventory (0x1B56 of kind 0, a new slot, or 1, more in a slot).</summary>
    public static void NoteInventory(ReadOnlySpan<byte> frame, DateTime at)
    {
        if (frame.Length < 17 || frame[3] > 1)
        {
            return;
        }

        int item = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[13..]));
        bool counted = false;
        lock (Gate)
        {
            Waiting.RemoveAll(w => at - w.At > Window);
            int waiting = Waiting.FindIndex(w => w.Item == item);
            if (waiting >= 0)
            {
                (int waitingItem, long count, DateTime pickedAt) = Waiting[waiting];
                Waiting.RemoveAt(waiting);
                Add(waitingItem, count, pickedAt);
                counted = true;
            }
            else
            {
                Inventory.RemoveAll(i => at - i.At > Window);
                Inventory.Add((item, at));
            }
        }

        if (counted)
        {
            Changed?.Invoke();
        }
    }

    // Under the lock.
    private static void Add(int item, long count, DateTime frameTime)
    {
        // The session's clock is the computer's: the frames carry the time they were captured at, which is the same clock.
        DateTime now = frameTime.Kind == DateTimeKind.Utc ? frameTime.ToLocalTime() : frameTime;
        if (_active is null || now - _lastPickup > SessionGap)
        {
            _active = new LootSession { Start = now, End = now };
            Stored.Insert(0, _active);
            while (Stored.Count > MaxSessions)
            {
                Stored.RemoveAt(Stored.Count - 1);
            }
        }

        _lastPickup = now;
        _active.End = now;
        LootMap map = _active.Maps.FirstOrDefault(m => m.Zone == _zone) ?? new LootMap { Zone = _zone };
        if (!_active.Maps.Contains(map))
        {
            _active.Maps.Add(map);
        }

        LootItem? entry = map.Items.FirstOrDefault(i => i.ItemId == item);
        if (entry is null)
        {
            entry = new LootItem { ItemId = item, First = now };
            map.Items.Add(entry);
        }

        entry.Count += count;
        entry.Picks++;
        entry.Last = now;
        _dirty = true;
        Save(force: false);
    }

    // Under the lock; at most every 5 s unless forced, so a farming spree does not write the file for every item.
    private static void Save(bool force)
    {
        if (!_persist || !_dirty || !force && DateTime.UtcNow - _savedAt < TimeSpan.FromSeconds(5))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(Stored));
            _dirty = false;
            _savedAt = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept in memory; the next save tries again.
        }
    }

    /// <summary>Writes what a pickup within the last 5 s left unsaved (the meter is closing).</summary>
    public static void Flush()
    {
        lock (Gate)
        {
            Save(force: true);
        }
    }
}
