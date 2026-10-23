using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2;

/// <summary>
/// The characters the character lists of earlier logins showed, kept in a file on this PC between two starts. A login only lists the account of its own
/// server, so this is how a character of another server (Massha on Agnita while Aahz logs in on Kaisinel) stays known. It is only read by the first-start
/// wizard and never uploaded: the uploads still carry only the account of the current login.
/// </summary>
public static class Aion2RememberedCharacters
{
    public sealed record Entry(string Name, int ClassCode, int Faction, int Level, int ServerId, long SeenMs);

    private static readonly object Gate = new();

    public static string StorePath => Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "aion2-characters-seen.json");

    /// <summary>Replaces what is remembered for the servers of <paramref name="listed"/> (a login lists a whole server account) and keeps the other servers.</summary>
    public static void Note(IReadOnlyList<AccountCharacter> listed, string? path = null)
    {
        if (listed.Count == 0)
        {
            return;
        }

        path ??= StorePath;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (Gate)
        {
            var servers = listed.Where(c => c.ServerId > 0).Select(c => c.ServerId).ToHashSet();
            var kept = Load(path).Where(e => !servers.Contains(e.ServerId)).ToList();
            kept.AddRange(listed.Where(c => c.ServerId > 0).Select(c => new Entry(c.Name, c.ClassCode, c.Faction, c.Level, c.ServerId, now)));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(kept));
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A convenience only.
            }
        }
    }

    public static List<Entry> Load(string? path = null)
    {
        try
        {
            path ??= StorePath;
            return File.Exists(path) ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }
}
