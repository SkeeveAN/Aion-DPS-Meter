using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>The chain skills of the client's Skill table: a skill and the follow-ups the game allows for a few seconds after it (assets/aion2/skills/skill_chains.json,
/// built by Tools/aion2-dat/build_skill_chains.py).</summary>
public static class Aion2SkillChains
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "skills", "skill_chains.json");
    private static Dictionary<int, (int Next, int WindowMs)[]>? _table;

    /// <summary>The follow-ups of a skill with their window in milliseconds; empty when it has none.</summary>
    public static IReadOnlyList<(int Next, int WindowMs)> After(int skillId)
    {
        _table ??= Load();
        return _table.TryGetValue(skillId, out var follow) ? follow : Array.Empty<(int, int)>();
    }

    private static Dictionary<int, (int, int)[]> Load()
    {
        var table = new Dictionary<int, (int, int)[]>();
        try
        {
            if (File.Exists(FilePath))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                {
                    table[int.Parse(p.Name)] = p.Value.EnumerateArray().Select(a => (a[0].GetInt32(), a[1].GetInt32())).ToArray();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException or InvalidOperationException)
        {
            // no table: no chains are counted
        }

        return table;
    }
}
