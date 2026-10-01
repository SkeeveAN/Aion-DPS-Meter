using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2;

/// <summary>The local player's character data as kept on disk: what the last login/zone change sent.</summary>
public sealed class Aion2SavedCharacter
{
    public string Name { get; set; } = "";
    public int ClassCode { get; set; }
    public int Level { get; set; }
    public DateTime SavedAt { get; set; }
    public List<SavedItem> Equipment { get; set; } = new();
    public List<SavedSkill> Skills { get; set; } = new();
    public List<SavedBoard> Daevanion { get; set; } = new();

    public sealed record SavedItem(int Slot, int ItemId, int Enchant);

    public sealed record SavedSkill(int Id, int Level, int BaseLevel);

    public sealed record SavedBoard(int Board, List<int> Nodes);
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
