using System.IO;
using System.Text.Json;
using AionDPS.Ui;
using AionDPS.Update;

namespace AionDPS.Aion2;

/// <summary>One material of a recipe: the item, how many a craft uses, and the alternative the client lets the player use instead (none for most).</summary>
public sealed record CraftComponent(int Item, int Count, IReadOnlyList<(int Item, int Count)> Alternatives);

/// <summary>
/// One crafting recipe of the client (assets/aion2/craft/recipes.json, built by Tools/aion2-dat/build_recipes.py): the client has one per faction.
/// <see cref="Learn"/> Combo marks the hidden recipe whose product is the first material of a main recipe (<see cref="ComboRecipe"/>); Advancement and Auto are the
/// ones the player sees. <see cref="ComboChance"/> is the client's value (per 10,000).
/// </summary>
public sealed record CraftRecipe(int Id, bool Elyos, string Category, string SubCategory, string Tab, int Level, string Learn, string Type, int Order,
    int Product, int Count, int ComboChance, int ComboRecipe, IReadOnlyList<CraftComponent> Components)
{
    public bool Hidden => Learn == "Combo";
}

/// <summary>The recipes of the client, found by id or by what they make.</summary>
public static class Aion2CraftData
{
    private static readonly string FilePath = AssetStore.Path("aion2", "craft", "recipes.json");
    private static List<CraftRecipe>? _all;
    private static Dictionary<int, CraftRecipe>? _byId;
    private static Dictionary<(int Product, bool Elyos), CraftRecipe>? _producers;
    private static Dictionary<int, CraftRecipe>? _counterpart;

    public static IReadOnlyList<CraftRecipe> All => _all ??= Load();

    /// <summary>The recipes a faction can use (Elyos or Asmodians), the hidden combo recipes left out: what the game's recipe list shows.</summary>
    public static IEnumerable<CraftRecipe> Visible(bool elyos) => All.Where(r => r.Elyos == elyos && !r.Hidden);

    public static CraftRecipe? Find(int id)
    {
        _ = All;
        return _byId!.GetValueOrDefault(id);
    }

    /// <summary>The recipe that makes an item for a faction (the first when several do), or null when the item is a raw material.</summary>
    public static CraftRecipe? ProducerOf(int item, bool elyos)
    {
        _ = All;
        return _producers!.GetValueOrDefault((item, elyos));
    }

    /// <summary>
    /// A recipe for the faction asked: the recipe itself when it already is one, else its twin of the other faction (the client lists every recipe twice, with the
    /// same category, level and order). A plan made on an Elyos character thus works on an Asmodian twink.
    /// </summary>
    public static CraftRecipe? ForFaction(int recipeId, bool elyos)
    {
        CraftRecipe? recipe = Find(recipeId);
        if (recipe is null || recipe.Elyos == elyos)
        {
            return recipe;
        }

        return _counterpart!.GetValueOrDefault(recipeId) ?? recipe;
    }

    private static List<CraftRecipe> Load()
    {
        var list = new List<CraftRecipe>();
        try
        {
            if (File.Exists(FilePath))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                foreach (JsonElement r in doc.RootElement.GetProperty("recipes").EnumerateArray())
                {
                    var components = new List<CraftComponent>();
                    foreach (JsonElement k in r.GetProperty("k").EnumerateArray())
                    {
                        var alternatives = new List<(int, int)>();
                        if (k.GetArrayLength() > 2)
                        {
                            alternatives.AddRange(k[2].EnumerateArray().Select(a => (a[0].GetInt32(), a[1].GetInt32())));
                        }

                        components.Add(new CraftComponent(k[0].GetInt32(), k[1].GetInt32(), alternatives));
                    }

                    list.Add(new CraftRecipe(r.GetProperty("id").GetInt32(), r.GetProperty("race").GetString() == "L", r.GetProperty("cat").GetString() ?? "",
                        r.GetProperty("sub").GetString() ?? "", r.GetProperty("tab").GetString() ?? "", r.GetProperty("lv").GetInt32(), r.GetProperty("learn").GetString() ?? "",
                        r.GetProperty("type").GetString() ?? "", r.GetProperty("order").GetInt32(), r.GetProperty("p").GetInt32(), r.GetProperty("n").GetInt32(),
                        r.GetProperty("combo").GetInt32(), r.GetProperty("cr").GetInt32(), components));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            // No recipe file: the tool shows an empty list.
        }

        _byId = list.ToDictionary(r => r.Id);
        _counterpart = new();
        foreach (var group in list.GroupBy(r => (r.Category, r.SubCategory, r.Tab, r.Level, r.Learn, r.Type, r.Order)))
        {
            List<CraftRecipe> elyos = group.Where(r => r.Elyos).OrderBy(r => r.Id).ToList();
            List<CraftRecipe> asmodian = group.Where(r => !r.Elyos).OrderBy(r => r.Id).ToList();
            if (elyos.Count == asmodian.Count)
            {
                for (int i = 0; i < elyos.Count; i++)
                {
                    _counterpart[elyos[i].Id] = asmodian[i];
                    _counterpart[asmodian[i].Id] = elyos[i];
                }
            }
        }

        _producers = new();
        // Several recipes can make the same item (Saphirstaub: 1 gem -> 3, or 10 gems -> 30). The one that needs the least material per piece wins, then the lower level
        // (the smaller batch), so a small need is not rounded up to a big batch.
        foreach (IGrouping<(int, bool), CraftRecipe> group in list.GroupBy(r => (r.Product, r.Elyos)))
        {
            _producers[group.Key] = group.OrderBy(r => (double)r.Components.Sum(c => c.Count) / Math.Max(1, r.Count)).ThenBy(r => r.Level).ThenBy(r => r.Id).First();
        }

        return list;
    }
}

/// <summary>One line of the plan: a recipe the player wants to craft, how often, and whether it is for the whole account or one character.</summary>
public sealed class CraftEntry
{
    public int Recipe { get; set; }

    public int Count { get; set; } = 1;

    /// <summary>True: the entry shows for every character of this computer (a twink sees it too); false: only for <see cref="Character"/>.</summary>
    public bool AccountWide { get; set; } = true;

    public string Character { get; set; } = "";
}

/// <summary>The recipes the player marked (the plan), kept in a file next to the settings.</summary>
public static class CraftPlan
{
    private static readonly object Gate = new();
    private static List<CraftEntry>? _entries;

    public static string FilePath => Path.Combine(MeterSettings.DataDirectory, "crafting.json");

    /// <summary>Raised after the plan changed (add, remove, count, scope).</summary>
    public static event Action? Changed;

    /// <summary>The entries a character sees: those for the whole account and its own.</summary>
    public static List<CraftEntry> For(string character)
    {
        lock (Gate)
        {
            return Entries().Where(e => e.AccountWide || string.Equals(e.Character, character, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    public static CraftEntry? Find(int recipe, string character)
    {
        lock (Gate)
        {
            return Entries().FirstOrDefault(e => e.Recipe == recipe && (e.AccountWide || string.Equals(e.Character, character, StringComparison.OrdinalIgnoreCase)));
        }
    }

    /// <summary>Puts the edited list of a character in place of what it saw before (the entries of the other characters stay): the order of the list is kept.</summary>
    public static void ReplaceFor(string character, IEnumerable<CraftEntry> edited)
    {
        lock (Gate)
        {
            List<CraftEntry> all = Entries();
            all.RemoveAll(e => e.AccountWide || string.Equals(e.Character, character, StringComparison.OrdinalIgnoreCase));
            all.AddRange(edited);
            Save();
        }

        Changed?.Invoke();
    }

    public static void Add(int recipe, string character, bool accountWide = true)
    {
        lock (Gate)
        {
            if (Find(recipe, character) is null)
            {
                Entries().Add(new CraftEntry { Recipe = recipe, Count = 1, AccountWide = accountWide || character.Length == 0, Character = character });
                Save();
            }
        }

        Changed?.Invoke();
    }

    public static void Remove(CraftEntry entry)
    {
        lock (Gate)
        {
            Entries().Remove(entry);
            Save();
        }

        Changed?.Invoke();
    }

    /// <summary>Saves after the page changed an entry's count or scope.</summary>
    public static void Touch()
    {
        lock (Gate)
        {
            Save();
        }

        Changed?.Invoke();
    }

    private static List<CraftEntry> Entries()
    {
        if (_entries is not null)
        {
            return _entries;
        }

        try
        {
            if (File.Exists(FilePath))
            {
                _entries = JsonSerializer.Deserialize<List<CraftEntry>>(File.ReadAllText(FilePath)) ?? new();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A broken file: start with an empty plan.
        }

        return _entries ??= new();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_entries ?? new(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not writable: the plan lives until the program ends.
        }
    }
}

/// <summary>One material of a recipe's list: how many its crafts need, and how many of them the stock covers.</summary>
public sealed record CraftLine(int Item, long Needed, long Have)
{
    public long Missing => Math.Max(0, Needed - Have);
}

/// <summary>What the plan needs for one recipe: its raw materials (intermediates already resolved) and what has to be made first (an item and how often its recipe is crafted).</summary>
public sealed record CraftResult(CraftEntry Entry, CraftRecipe Recipe, int Count, IReadOnlyList<CraftLine> Materials, IReadOnlyList<(int Item, long Crafts)> Intermediates)
{
    /// <summary>How much of the material is there, 0 to 100.</summary>
    public int Percent
    {
        get
        {
            long need = Materials.Sum(m => m.Needed);
            return need == 0 ? 100 : (int)(100 * Materials.Sum(m => Math.Min(m.Have, m.Needed)) / need);
        }
    }
}

/// <summary>Adds up the materials a plan needs and sets them against what the player has.</summary>
public static class CraftCalc
{
    private const int MaxDepth = 6;

    /// <summary>
    /// The materials of every entry, one after the other against the same stock: what the first recipes use is not there for the later ones. With <paramref name="resolve"/>
    /// an intermediate (a material another recipe makes: an ingot for a weapon, dust for a scroll) is taken from the stock first and the rest replaced by the
    /// materials of its own recipe, as often as it has to be crafted (rounded up to whole crafts); so the list shows the ore, not the ingots.
    /// </summary>
    public static List<CraftResult> Compute(IEnumerable<CraftEntry> entries, IReadOnlyDictionary<int, long> stock, bool resolve, bool elyos)
    {
        var spare = new Dictionary<int, long>(stock);
        var results = new List<CraftResult>();
        foreach (CraftEntry entry in entries)
        {
            if (entry.Count <= 0 || Aion2CraftData.ForFaction(entry.Recipe, elyos) is not { } recipe)
            {
                continue;
            }

            var need = new Dictionary<int, long>();
            var made = new Dictionary<int, long>();
            var covered = new Dictionary<int, long>();
            Demand(recipe, entry.Count, 0);
            var lines = new List<CraftLine>();
            foreach ((int item, long amount) in covered)
            {
                lines.Add(new CraftLine(item, amount, amount)); // an intermediate the stock already has: done, and it counts towards the percent
            }

            foreach ((int item, long needed) in need)
            {
                long have = Math.Min(spare.GetValueOrDefault(item), needed);
                spare[item] = spare.GetValueOrDefault(item) - have;
                lines.Add(new CraftLine(item, needed, have));
            }

            results.Add(new CraftResult(entry, recipe, entry.Count, lines, made.Select(m => (m.Key, m.Value)).ToList()));

            void Demand(CraftRecipe r, long times, int depth)
            {
                foreach (CraftComponent c in r.Components)
                {
                    long wanted = times * c.Count;
                    CraftRecipe? producer = resolve && depth < MaxDepth ? Aion2CraftData.ProducerOf(c.Item, r.Elyos) : null;
                    if (producer is null || producer.Id == r.Id)
                    {
                        need[c.Item] = need.GetValueOrDefault(c.Item) + wanted;
                        continue;
                    }

                    long fromStock = Math.Min(spare.GetValueOrDefault(c.Item), wanted);
                    spare[c.Item] = spare.GetValueOrDefault(c.Item) - fromStock;
                    if (fromStock > 0)
                    {
                        covered[c.Item] = covered.GetValueOrDefault(c.Item) + fromStock;
                    }

                    long rest = wanted - fromStock;
                    if (rest > 0)
                    {
                        long batches = (rest + producer.Count - 1) / producer.Count;
                        made[c.Item] = made.GetValueOrDefault(c.Item) + batches;
                        Demand(producer, batches, depth + 1);
                    }
                }
            }
        }

        return results;
    }

    /// <summary>All materials of the plan summed (what the overlay lists), the ones that miss most first.</summary>
    public static List<CraftLine> Total(IEnumerable<CraftResult> results) =>
        results.SelectMany(r => r.Materials).GroupBy(l => l.Item)
            .Select(g => new CraftLine(g.Key, g.Sum(l => l.Needed), g.Sum(l => l.Have)))
            .OrderByDescending(l => l.Missing).ThenBy(l => l.Item).ToList();
}
