using System.IO;
using System.Text;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>One pet of the local player as the login frame lists it: its level (1 to 3) and, below the top level, the progress towards the next.</summary>
public sealed record Aion2PetState(int PetId, int Level, int Progress);

/// <summary>
/// The pets of the pet window and which monsters belong to them (assets/aion2/pets/pets.json, built by Tools/aion2-dat/build_pets.py
/// from the client's own tables): a pet is named after the model of the monsters that drop its soul, so the monster the game shows
/// with a name leads to the pet, and the pet to the level the login frame reported. Used by the pet farming overlay; never uploaded.
/// </summary>
public static class Aion2Pets
{
    /// <summary>Progress that a pet needs to reach the next level, by its current level (seen in the pet window: 22/25, 32/75). Level 3 is the top.</summary>
    public const int TopLevel = 3;

    public static int ProgressNeeded(int level) => level switch { 1 => 25, 2 => 75, _ => 0 };

    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "pets", "pets.json");
    private static Catalog? _catalog;

    /// <summary>The species of a pet as the pet window groups them: Cognia (Intellect), Fera (Feral), Natura (Nature), Varia (Trans), Specia (Special).</summary>
    public static string? SpeciesOf(int petId) => Data.PetSpecies.TryGetValue(petId, out string? s) ? s : null;

    /// <summary>The species in the order of the pet window.</summary>
    public static readonly string[] Species = { "Cognia", "Fera", "Natura", "Varia", "Specia" };

    /// <summary>The pets of one species whose monsters have spawn points on a map (the ones the pet map can show), by name in a language.</summary>
    public static IReadOnlyList<(int PetId, string Name)> MapPetsOf(string species, string language) =>
        Data.PetSpecies.Where(kv => kv.Value == species && Data.PetsWithSpawns.Contains(kv.Key))
            .Select(kv => (kv.Key, PetName(kv.Key, language) ?? $"#{kv.Key}"))
            .OrderBy(p => p.Item2, StringComparer.CurrentCultureIgnoreCase).ToList();

    private sealed class Catalog
    {
        public Dictionary<int, string> PetSpecies { get; } = new();
        public HashSet<int> PetsWithSpawns { get; } = new();
        public Dictionary<int, Dictionary<string, string>> PetNames { get; } = new();
        public Dictionary<int, string> PetKeys { get; } = new();
        public Dictionary<int, int[]> PetsOfNpc { get; } = new();
        public Dictionary<int, List<int>> NpcsOfPet { get; } = new();
        public Dictionary<int, int> PetOfSoul { get; } = new();
        /// <summary>Monster name (folded, any language) -> the pets of the monsters with that name.</summary>
        public Dictionary<string, HashSet<int>> PetsOfName { get; } = new();
    }

    private static Catalog Data => _catalog ??= Load();

    /// <summary>Every pet of the pet window (pet ids).</summary>
    public static IReadOnlyCollection<int> AllPetIds => Data.PetNames.Keys;

    /// <summary>The pet's name in a language, else English.</summary>
    public static string? PetName(int petId, string language) =>
        Data.PetNames.TryGetValue(petId, out var names) ? names.GetValueOrDefault(language) ?? names.GetValueOrDefault("en") : null;

    /// <summary>The pet's picture (assets/aion2/pets/icons/<key>.png, 64 px); null when none was extracted for it.</summary>
    public static string? PetIconPath(int petId)
    {
        if (!Data.PetKeys.TryGetValue(petId, out string? key))
        {
            return null;
        }

        string path = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "pets", "icons", key + ".png");
        return File.Exists(path) ? path : null;
    }

    /// <summary>The pets whose soul this monster drops (usually one).</summary>
    public static IReadOnlyList<int> PetsOfNpc(int npcId) => Data.PetsOfNpc.TryGetValue(npcId, out int[]? pets) ? pets : Array.Empty<int>();

    /// <summary>The monsters (NPC ids) that drop this pet's soul, spawn point or not.</summary>
    public static IReadOnlyList<int> NpcsOfPet(int petId) => Data.NpcsOfPet.TryGetValue(petId, out var npcs) ? npcs : Array.Empty<int>();

    public static int? PetOfSoulItem(int itemId) => Data.PetOfSoul.TryGetValue(itemId, out int pet) ? pet : null;

    /// <summary>The pet(s) behind a monster name read from the screen, in any language the meter knows; empty when the name is not a pet monster.</summary>
    public static IReadOnlyCollection<int> PetsOfMonsterName(string name) =>
        Data.PetsOfName.TryGetValue(Fold(name), out var pets) ? pets : Array.Empty<int>();

    /// <summary>Every monster name that leads to a pet (folded), for the fuzzy match of what the text recognition read.</summary>
    public static IReadOnlyCollection<string> KnownMonsterNames => Data.PetsOfName.Keys;

    /// <summary>Lower case, no accents, letters and digits only: what the text recognition and the game's text agree on.</summary>
    public static string Fold(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    private static Catalog Load()
    {
        var catalog = new Catalog();
        try
        {
            if (!File.Exists(FilePath))
            {
                return catalog;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            foreach (var pet in doc.RootElement.GetProperty("pets").EnumerateObject())
            {
                var names = new Dictionary<string, string>();
                foreach (var n in pet.Value.GetProperty("names").EnumerateObject())
                {
                    names[n.Name] = n.Value.GetString() ?? "";
                }

                catalog.PetNames[int.Parse(pet.Name)] = names;
                if (pet.Value.TryGetProperty("key", out var key) && key.GetString() is { Length: > 0 } keyText)
                {
                    catalog.PetKeys[int.Parse(pet.Name)] = keyText;
                }

                catalog.PetSpecies[int.Parse(pet.Name)] = pet.Value.TryGetProperty("category", out var c) ? c.GetString() switch
                {
                    "Intellect" => "Cognia",
                    "Feral" => "Fera",
                    "Nature" => "Natura",
                    "Trans" => "Varia",
                    "Special" => "Specia",
                    _ => "",
                } : "";
            }

            foreach (var m in doc.RootElement.GetProperty("monsters").EnumerateObject())
            {
                catalog.PetsOfNpc[int.Parse(m.Name)] = m.Value.ValueKind == JsonValueKind.Array
                    ? m.Value.EnumerateArray().Select(v => v.GetInt32()).ToArray()
                    : new[] { m.Value.GetInt32() };
            }

            foreach ((int npc, int[] pets) in catalog.PetsOfNpc)
            {
                foreach (int pet in pets)
                {
                    if (!catalog.NpcsOfPet.TryGetValue(pet, out var list))
                    {
                        catalog.NpcsOfPet[pet] = list = new List<int>();
                    }

                    list.Add(npc);
                }
            }

            foreach (var s in doc.RootElement.GetProperty("souls").EnumerateObject())
            {
                catalog.PetOfSoul[int.Parse(s.Name)] = s.Value.GetInt32();
            }

            // the pets that have at least one spawn point on a map (spawns.json: {"maps": {map: {npcId: [[x,y,z]...]}}})
            string spawnsPath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "pets", "spawns.json");
            if (File.Exists(spawnsPath))
            {
                using var spawns = JsonDocument.Parse(File.ReadAllText(spawnsPath));
                foreach (var map in spawns.RootElement.GetProperty("maps").EnumerateObject())
                {
                    foreach (var npc in map.Value.EnumerateObject())
                    {
                        if (catalog.PetsOfNpc.TryGetValue(int.Parse(npc.Name), out int[]? pets))
                        {
                            catalog.PetsWithSpawns.UnionWith(pets);
                        }
                    }
                }
            }

            foreach ((int npcId, int[] petIds) in catalog.PetsOfNpc)
            {
                foreach (string language in new[] { "en", "de", "fr", "es", "ru", "pt", "ja", "ko" })
                {
                    string? name = Aion2Npcs.NameOf(npcId, language);
                    if (name is null)
                    {
                        continue;
                    }

                    string folded = Fold(name);
                    if (folded.Length == 0)
                    {
                        continue;
                    }

                    if (!catalog.PetsOfName.TryGetValue(folded, out var set))
                    {
                        catalog.PetsOfName[folded] = set = new HashSet<int>();
                    }

                    set.UnionWith(petIds);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or FormatException)
        {
            // No catalog: the overlay simply stays empty.
        }

        return catalog;
    }
}
