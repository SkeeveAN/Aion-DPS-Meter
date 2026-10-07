using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>A worn title as the profile shows it: its name in a language and its grade (Common ... Special).</summary>
public sealed record Aion2TitleInfo(string Grade, string Name);

/// <summary>
/// Title id -> grade and names in the client's languages, from assets/aion2/titles/titles.json (the same table the
/// website's profile uses: {"11010001": {"g": "Common", "n": {"en": "Draped in Sky", "de": "..."}}}).
/// </summary>
public static class Aion2Titles
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "titles", "titles.json");
    private static IReadOnlyDictionary<int, (string Grade, Dictionary<string, string> Names)>? _table;

    public static Aion2TitleInfo? Find(int titleId, string language)
    {
        var table = _table ??= Load();
        if (!table.TryGetValue(titleId, out var entry))
        {
            return null;
        }

        string name = entry.Names.GetValueOrDefault(language) ?? entry.Names.GetValueOrDefault("en") ?? entry.Names.Values.FirstOrDefault() ?? $"#{titleId}";
        return new Aion2TitleInfo(entry.Grade, name);
    }

    private static IReadOnlyDictionary<int, (string, Dictionary<string, string>)> Load()
    {
        var table = new Dictionary<int, (string, Dictionary<string, string>)>();
        try
        {
            if (File.Exists(FilePath))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(FilePath));
                foreach (JsonProperty entry in document.RootElement.EnumerateObject())
                {
                    if (!int.TryParse(entry.Name, out int id))
                    {
                        continue;
                    }

                    var names = entry.Value.GetProperty("n").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
                    table[id] = (entry.Value.TryGetProperty("g", out JsonElement grade) ? grade.GetString() ?? "Common" : "Common", names);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException)
        {
            // No table: no title chips.
        }

        return table;
    }
}
