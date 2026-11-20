using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2;

/// <summary>
/// How many Enhance Stones (item 930100023) each character has, kept between two starts in %AppData%\Aion DPS Meter\aion2-stones.json.
/// <see cref="Aion2Stock"/> only knows the character that is logged in; the checklist shows every character, so the reading of the login
/// (and of every later change) is written down under the character's name. The storages are exact at login and after a sort (see Aion2Stock).
/// The account storage is shared by all characters of the account: the newest reading of any character is the one to show.
/// </summary>
public static class Aion2Stones
{
    public const int ItemId = 930100023;

    public sealed record Reading(long Inventory, long CharacterStorage, long AccountStorage, DateTime SavedAt)
    {
        public long Total => Inventory + CharacterStorage + AccountStorage;
    }

    private static readonly object Gate = new();
    private static Dictionary<string, Reading>? _all;

    public static string StorePath => Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "aion2-stones.json");

    /// <summary>Writes down what the logged-in character has now (nothing before the login frame was read).</summary>
    public static void Capture(string name)
    {
        if (string.IsNullOrEmpty(name) || !Aion2Stock.HasData)
        {
            return;
        }

        var now = new Reading(
            Aion2Stock.Count(ItemId, StockSources.Inventory),
            Aion2Stock.Count(ItemId, StockSources.CharacterStorage),
            Aion2Stock.Count(ItemId, StockSources.AccountStorage),
            DateTime.UtcNow);
        lock (Gate)
        {
            Load();
            if (_all!.TryGetValue(name, out Reading? old) && old.Inventory == now.Inventory && old.CharacterStorage == now.CharacterStorage && old.AccountStorage == now.AccountStorage)
            {
                return;
            }

            _all[name] = now;
            Save();
        }
    }

    public static Reading? Get(string name)
    {
        lock (Gate)
        {
            Load();
            return _all!.GetValueOrDefault(name);
        }
    }

    /// <summary>The account storage as the newest reading of any character saw it; null when none was read.</summary>
    public static long? AccountStorage()
    {
        lock (Gate)
        {
            Load();
            return _all!.Values.OrderByDescending(r => r.SavedAt).FirstOrDefault()?.AccountStorage;
        }
    }

    private static void Load()
    {
        if (_all is not null)
        {
            return;
        }

        try
        {
            _all = File.Exists(StorePath)
                ? JsonSerializer.Deserialize<Dictionary<string, Reading>>(File.ReadAllText(StorePath)) ?? new()
                : new();
        }
        catch (Exception)
        {
            _all = new();
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(_all));
        }
        catch (Exception)
        {
            // a read-only profile folder: the numbers stay in memory
        }
    }
}
