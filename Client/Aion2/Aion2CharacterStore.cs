using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2;

/// <summary>The local player's character data as kept on disk: what the last login/zone change sent.</summary>
public sealed class Aion2SavedCharacter
{
    public string Name { get; set; } = "";
    public int ClassCode { get; set; }
    public int Level { get; set; }
    public int ServerId { get; set; }
    public DateTime SavedAt { get; set; }
    public List<SavedItem> Equipment { get; set; } = new();
    public List<SavedSkill> Skills { get; set; } = new();
    public List<SavedBoard> Daevanion { get; set; } = new();
    public List<SavedSpecies> Species { get; set; } = new();
    public List<SavedTitle> Titles { get; set; } = new();
    public List<SavedPet> Pets { get; set; } = new();

    public sealed record SavedPet(int Id, int Level, int Progress);

    public sealed record SavedItem(int Slot, int ItemId, int Enchant);

    public sealed record SavedSkill(int Id, int Level, int BaseLevel, bool Stigma = false, bool Equipped = false);

    public sealed record SavedBoard(int Board, List<int> Nodes);

    public sealed record SavedSpecies(int Id, int Level, long Progress, List<SavedEffect> Effects);

    public sealed record SavedEffect(int Page, int Slot, int Stat, long Value, int Kind = 0);

    public sealed record SavedTitle(int Slot, int TitleId);
}

/// <summary>
/// Keeps the local player's character data in %AppData%\Aion DPS Meter\aion2-character.json, so the
/// Character view and the profile upload have data even when the meter is started in the middle of
/// a session - the game only sends it at login and on a zone change. A new login or zone change
/// replaces the file. A missing or unreadable file simply means "nothing saved".
/// </summary>
public static class Aion2CharacterStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aion DPS Meter", "aion2-character.json");

    /// <summary>The character windows of other players, next to the own character file.</summary>
    public static string InspectedPathFor(string characterPath) => Path.Combine(Path.GetDirectoryName(characterPath)!, "aion2-inspected.json");

    /// <summary>Character windows opened in the last 30 days (at most 300), so a restart or an update does not
    /// lose what was read before it was uploaded.</summary>
    public static List<Aion2InspectedPlayer> LoadInspected(string path)
    {
        try
        {
            var list = File.Exists(path) ? JsonSerializer.Deserialize<List<Aion2InspectedPlayer>>(File.ReadAllText(path)) : null;
            return (list ?? new()).Where(p => p.ReceivedAt > DateTime.Now.AddDays(-30)).ToList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public static void SaveInspected(string path, IReadOnlyList<Aion2InspectedPlayer> players)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(players.OrderByDescending(p => p.ReceivedAt).Take(300).ToList(), Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A convenience only.
        }
    }

    public static Aion2SavedCharacter? Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Aion2SavedCharacter>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(string path, Aion2SavedCharacter character)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Write-then-replace: a crash mid-write must not leave a half file behind.
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(character, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Saving is a convenience; the meter itself keeps working without it.
        }
    }
}
