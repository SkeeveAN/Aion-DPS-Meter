using System.IO;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Aion2;

/// <summary>
/// A local text log for the pet farming overlay (never uploaded), next to the settings (%AppData%\Aion DPS Meter\pet-farm.log):
/// every monster name the screen reader found that leads to no pet, and every pet soul the player got with the monsters that died
/// right before it. A soul that none of those monsters should drop is marked UNEXPECTED - that is the monster the pet table does
/// not know yet, and the line is what to report. Trimmed when it grows.
/// </summary>
public static class PetFarmLog
{
    private static readonly object Gate = new();
    private static readonly List<(int EntityId, int NpcId, DateTime At)> Deaths = new();
    private static (int ItemId, DateTime At) _lastSoul;
    private static readonly HashSet<string> LoggedNames = new();
    private static readonly TimeSpan SoulWindow = TimeSpan.FromSeconds(6);

    /// <summary>A replay or a test writes to its own file instead of the real log.</summary>
    public static string? PathOverride { get; set; }

    private static string Path_ => PathOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aion DPS Meter", "pet-farm.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                string path = Path_;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > 400_000)
                {
                    File.WriteAllLines(path, File.ReadLines(path).TakeLast(2000));
                }

                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>A monster died (its hit points reached 0).</summary>
    public static void NoteDeath(int entityId, int npcId, DateTime at)
    {
        lock (Gate)
        {
            Deaths.RemoveAll(d => d.EntityId == entityId); // the game repeats the last hit-point frame
            Deaths.Add((entityId, npcId, at));
            if (Deaths.Count > 12)
            {
                Deaths.RemoveAt(0);
            }
        }
    }

    /// <summary>The player got a pet soul (inventory change): logs it with the monsters that died shortly before.</summary>
    public static void NoteSoul(int itemId, int petId, int quantity, DateTime at)
    {
        if (quantity <= 0)
        {
            return; // the bag slot emptied again, no soul received
        }

        List<(int EntityId, int NpcId, DateTime At)> recent;
        lock (Gate)
        {
            if (_lastSoul.ItemId == itemId && at - _lastSoul.At < TimeSpan.FromSeconds(2))
            {
                return; // the same change announced twice
            }

            _lastSoul = (itemId, at);
            recent = Deaths.Where(d => at - d.At is { } age && age >= TimeSpan.Zero && age <= SoulWindow).ToList();
        }

        string pet = Aion2Pets.PetName(petId, "en") ?? "?";
        bool expected = recent.Any(d => Aion2Pets.PetsOfNpc(d.NpcId).Contains(petId));
        string killed = recent.Count == 0
            ? "no monster died just before"
            : string.Join(", ", recent.Select(d => $"{Aion2Npcs.NameOf(d.NpcId, "en") ?? "?"} [{d.NpcId}]"));
        Write($"{(expected ? "SOUL" : "SOUL UNEXPECTED")} {pet} (pet {petId}, item {itemId}) x{quantity} after: {killed}");
    }

    private static readonly HashSet<int> LoggedNpcs = new();

    /// <summary>A monster the player marked that leads to no pet: logged once per monster and session, so the log lists the monsters the table lacks.</summary>
    public static void NoteUnknownMonster(int npcId)
    {
        bool first;
        lock (Gate)
        {
            first = LoggedNpcs.Add(npcId);
        }

        if (first)
        {
            Write($"SEEN no pet for the marked monster '{Aion2Npcs.NameOf(npcId, "en") ?? "?"}' [{npcId}]");
        }
    }

    /// <summary>A name read from the screen that is no pet monster: logged once per name and session, so the log lists what the table lacks.</summary>
    public static void NoteUnknownName(string name)
    {
        bool first;
        lock (Gate)
        {
            first = LoggedNames.Add(Aion2Pets.Fold(name));
        }

        if (first)
        {
            Write($"SEEN no pet for the monster name '{name}'");
        }
    }
}
