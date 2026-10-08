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

    private sealed class Catalog
    {
        public Dictionary<int, Dictionary<string, string>> PetNames { get; } = new();
        public Dictionary<int, int> PetOfNpc { get; } = new();
        public Dictionary<int, int> PetOfSoul { get; } = new();
        /// <summary>Monster name (folded, any language) -> the pets of the monsters with that name.</summary>
        public Dictionary<string, HashSet<int>> PetsOfName { get; } = new();
    }

    private static Catalog Data => _catalog ??= Load();

    /// <summary>The pet's name in a language, else English.</summary>
    public static string? PetName(int petId, string language) =>
        Data.PetNames.TryGetValue(petId, out var names) ? names.GetValueOrDefault(language) ?? names.GetValueOrDefault("en") : null;

    public static int? PetOfNpc(int npcId) => Data.PetOfNpc.TryGetValue(npcId, out int pet) ? pet : null;

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
            }

            foreach (var m in doc.RootElement.GetProperty("monsters").EnumerateObject())
            {
                catalog.PetOfNpc[int.Parse(m.Name)] = m.Value.GetInt32();
            }

            foreach (var s in doc.RootElement.GetProperty("souls").EnumerateObject())
            {
                catalog.PetOfSoul[int.Parse(s.Name)] = s.Value.GetInt32();
            }

            foreach ((int npcId, int petId) in catalog.PetOfNpc)
            {
                foreach (string language in new[] { "en", "de", "fr", "es", "ru" })
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

                    set.Add(petId);
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
