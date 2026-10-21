using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// The name and grade of every item, from the client's Item table and its texts (assets/aion2/items/item_names.json:
/// {"110120003": [4, "Ludra's Blade of Extinction", "Ludras Vernichtungsklinge", ...]} - the grade, then English, German, French, Spanish,
/// Russian, Portuguese, Japanese, Korean; an empty text is the English one). <see cref="Aion2ItemCatalog"/> knows the gear only.
/// </summary>
public static class Aion2ItemNames
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "items", "item_names.json");
    private static readonly string[] Languages = { "en", "de", "fr", "es", "ru", "pt", "ja", "ko" };
    private static Dictionary<int, string[]>? _table;

    /// <summary>The item's name in a language (Chinese reads English), else English; null when the table does not know the item.</summary>
    public static string? NameOf(int itemId, string language)
    {
        if (!(_table ??= Load()).TryGetValue(itemId, out string[]? row))
        {
            return null;
        }

        int index = Array.IndexOf(Languages, language);
        return index >= 0 && row[1 + index].Length > 0 ? row[1 + index] : row[1];
    }

    /// <summary>True when the client's item table has the item.</summary>
    public static bool Knows(int itemId) => (_table ??= Load()).ContainsKey(itemId);

    /// <summary>The item's grade on the scale of the character window (1 Common ... 7 Special); 0 when unknown.</summary>
    public static int GradeOf(int itemId) => (_table ??= Load()).TryGetValue(itemId, out string[]? row) && int.TryParse(row[0], out int grade) ? grade : 0;

    private static Dictionary<int, string[]> Load()
    {
        var table = new Dictionary<int, string[]>();
        try
        {
            if (File.Exists(FilePath))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                foreach (JsonProperty entry in doc.RootElement.EnumerateObject())
                {
                    if (int.TryParse(entry.Name, out int id) && entry.Value.GetArrayLength() == 1 + Languages.Length)
                    {
                        table[id] = entry.Value.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Number ? e.GetInt32().ToString() : e.GetString() ?? "").ToArray();
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            // No table: the loot log shows the bare item ids.
        }

        return table;
    }
}
