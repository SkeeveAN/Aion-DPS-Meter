using System.IO;
using System.Text.Json;
using AionDPS.Update;

namespace AionDPS.Aion2;

/// <summary>
/// How far one hit of a skill lowers a monster's stagger bar (the game's "Groggy guard energy"; the bar holds 1,200). The values are the
/// game's own: the client's skill tables name them in the effect type GroggyGuardDecreaseEnergy, 3,329 skills have one. They were checked
/// against the bar readings of recordings: for the 109 skills seen hitting a boss alone, the table and the measured drop were equal in every
/// case. A buff of the attacker (a Gladiator's) raises the drop by half, rounded down (5 becomes 7, 2 becomes 3); <see cref="Combat.StaggerShare"/> allows for it.
/// </summary>
public static class Aion2StaggerTable
{
    private static Dictionary<int, int>? _values;

    /// <summary>A test sets its own table.</summary>
    public static Dictionary<int, int>? Override { get; set; }

    /// <summary>The drop of the stagger bar one hit of the skill causes; 0 for a skill without one (or an unknown skill).</summary>
    public static int ValueOf(int skillId) => (Override ?? Values).GetValueOrDefault(skillId);

    private static Dictionary<int, int> Values => _values ??= Load();

    private static Dictionary<int, int> Load()
    {
        var values = new Dictionary<int, int>();
        try
        {
            string path = AssetStore.Path("aion2", "stagger", "skill_stagger.json");
            if (File.Exists(path))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (JsonProperty skill in doc.RootElement.GetProperty("skills").EnumerateObject())
                {
                    if (int.TryParse(skill.Name, out int id) && skill.Value.TryGetInt32(out int value))
                    {
                        values[id] = value;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException)
        {
        }

        return values;
    }
}
