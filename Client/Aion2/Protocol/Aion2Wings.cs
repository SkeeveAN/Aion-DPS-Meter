using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>A wing or wing skin as the profile shows it: its name in a language, its grade (Common ... Special)
/// and the path of its 64 px icon (null when none was extracted).</summary>
public sealed record Aion2WingInfo(string Name, string Grade, string? IconPath);

/// <summary>
/// Wing item id / wing skin id -> names, grade and icon, from assets/aion2/wings/wings.json (the table the website
/// uses: {"30500200": {"names": {"en": "Ancient Aullaeu Wings", ...}, "icon": "Icon_WingE_002", "grade": "Unique"}})
/// and the pictures in assets/aion2/wings/icons.
/// </summary>
public static class Aion2Wings
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "wings");
    private static IReadOnlyDictionary<int, (string Grade, string? Icon, Dictionary<string, string> Names)>? _table;

    public static Aion2WingInfo? Find(int id, string language)
    {
        var table = _table ??= Load();
        if (!table.TryGetValue(id, out var entry))
        {
            return null;
        }

        string name = entry.Names.GetValueOrDefault(language) ?? entry.Names.GetValueOrDefault("en") ?? entry.Names.Values.FirstOrDefault() ?? $"#{id}";
        string? icon = null;
        if (entry.Icon is { Length: > 0 })
        {
            string path = Path.Combine(Root, "icons", entry.Icon + ".png");
            icon = File.Exists(path) ? path : null;
        }

        return new Aion2WingInfo(name, entry.Grade, icon);
    }

    private static IReadOnlyDictionary<int, (string, string?, Dictionary<string, string>)> Load()
    {
        var table = new Dictionary<int, (string, string?, Dictionary<string, string>)>();
        try
        {
            string file = Path.Combine(Root, "wings.json");
            if (File.Exists(file))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
                foreach (JsonProperty entry in document.RootElement.EnumerateObject())
                {
                    if (!int.TryParse(entry.Name, out int id))
                    {
                        continue;
                    }

                    var names = entry.Value.GetProperty("names").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
                    table[id] = (
                        entry.Value.TryGetProperty("grade", out JsonElement grade) ? grade.GetString() ?? "Common" : "Common",
                        entry.Value.TryGetProperty("icon", out JsonElement icon) ? icon.GetString() : null,
                        names);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException)
        {
            // No table: no wing chips.
        }

        return table;
    }
}
