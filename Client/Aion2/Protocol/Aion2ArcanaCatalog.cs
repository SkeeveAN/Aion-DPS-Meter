using System.IO;
using System.Text.Json;
using AionDPS.Update;

namespace AionDPS.Aion2.Protocol;

/// <summary>An effect an arcana set gives from a number of pieces on (texts in the game client's languages).</summary>
public sealed record Aion2ArcanaSetEffect(int Pieces, IReadOnlyDictionary<string, string> Text);

/// <summary>An arcana set: its name and its effects.</summary>
public sealed record Aion2ArcanaSet(IReadOnlyDictionary<string, string> Name, IReadOnlyList<Aion2ArcanaSetEffect> Effects);

/// <summary>
/// What the game's tables say about arcana, from assets/aion2/items/arcana.json (Tools/aion2-dat/build_arcana.py): the experience a
/// piece needs per level and the effects of the sets. The set is the last four digits of the item id (810130001 = set 1 "Primal Vigor",
/// the grade sits one digit before). Only sets whose effects were checked against the game are in the file; a piece of another set
/// simply shows no effect.
/// </summary>
public static class Aion2ArcanaCatalog
{
    private static readonly string FilePath = AssetStore.Path("aion2", "items", "arcana.json");
    private static Dictionary<int, int[]>? _exp;
    private static Dictionary<int, Aion2ArcanaSet>? _sets;

    /// <summary>The five pieces everyone knows (Grail, Parchment, Compass, Bell, Mirror) are the card types 1-5 of the item id (8101.., 8105..).
    /// The other card types use other experience tables, which are not read.</summary>
    public static bool IsBaseCard(int itemId) => itemId / 100000 % 100 is >= 1 and <= 5 && itemId / 10_000_000 == 81;

    public static int SetOf(int itemId) => itemId % 10000;

    /// <summary>The experience to collect from <paramref name="level"/> to the next level; null when unknown or the level is the last one.</summary>
    public static int? ExpToNext(int itemId, int grade, int level)
    {
        Load();
        if (!IsBaseCard(itemId) || _exp is null || !_exp.TryGetValue(grade, out int[]? table) || level < 0 || level >= table.Length || table[level] <= 0)
        {
            return null;
        }

        return table[level];
    }

    public static Aion2ArcanaSet? SetFor(int itemId)
    {
        Load();
        return _sets is not null && _sets.TryGetValue(SetOf(itemId), out Aion2ArcanaSet? set) ? set : null;
    }

    /// <summary>The text of a language table in <paramref name="language"/>, English when it has none.</summary>
    public static string Pick(IReadOnlyDictionary<string, string> texts, string language) =>
        texts.TryGetValue(language, out string? t) && t.Length > 0 ? t : texts.GetValueOrDefault("en") ?? "";

    private static void Load()
    {
        if (_exp is not null)
        {
            return;
        }

        var exp = new Dictionary<int, int[]>();
        var sets = new Dictionary<int, Aion2ArcanaSet>();
        try
        {
            if (File.Exists(FilePath))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                foreach (JsonProperty g in doc.RootElement.GetProperty("expPerLevel").EnumerateObject())
                {
                    exp[int.Parse(g.Name)] = g.Value.EnumerateArray().Select(v => v.GetInt32()).ToArray();
                }

                foreach (JsonProperty s in doc.RootElement.GetProperty("sets").EnumerateObject())
                {
                    Dictionary<string, string> Texts(JsonElement e) => e.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).ToDictionary(p => p.Name, p => p.Value.GetString()!);
                    var effects = s.Value.GetProperty("effects").EnumerateArray()
                        .Select(e => new Aion2ArcanaSetEffect(e.GetProperty("pieces").GetInt32(), Texts(e.GetProperty("text"))))
                        .ToList();
                    sets[int.Parse(s.Name)] = new Aion2ArcanaSet(Texts(s.Value.GetProperty("name")), effects);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            // Missing or broken table: no experience limit and no set effects are shown, nothing else is affected.
        }

        _sets = sets;
        _exp = exp;
    }
}
