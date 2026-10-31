using System.IO;
using System.Text.Json;
using AionDPS.Update;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// What a monster skill paints on the floor, in one word (circle, cone, line, triangle, ring, ringarc, cross, target, wide), from
/// assets/aion2/skills/skill_shapes.json ({"1801650": "circle", …}). The client has no name for many boss skills (they read "Attack"),
/// so the cast bar names the shape instead: "Attack to the front". The zones are the client's own SkillEffectFilter rows, as the
/// Waffle meter published them (one word each: its first zone that is not arena-wide).
/// </summary>
public static class Aion2SkillShapes
{
    private static readonly string FilePath = AssetStore.Path("aion2", "skills", "skill_shapes.json");
    private static IReadOnlyDictionary<int, string>? _shapes;

    /// <summary>The shape word of a skill, or null when the table does not know it. A missing id borrows from its family (the id without the last digit)
    /// only when every known sibling agrees: a variant can be a different mechanic, and a wrong shape is worse than none.</summary>
    public static string? KindOf(int skillId)
    {
        var shapes = Load();
        if (shapes.TryGetValue(skillId, out string? kind))
        {
            return kind;
        }

        string? family = null;
        int baseId = skillId / 10 * 10;
        for (int variant = 0; variant < 10; variant++)
        {
            if (!shapes.TryGetValue(baseId + variant, out string? sibling))
            {
                continue;
            }

            if (family is not null && family != sibling)
            {
                return null;
            }

            family = sibling;
        }

        return family;
    }

    private static IReadOnlyDictionary<int, string> Load()
    {
        if (_shapes is { } cached)
        {
            return cached;
        }

        var table = new Dictionary<int, string>();
        try
        {
            if (File.Exists(FilePath))
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) ?? new();
                foreach ((string key, string kind) in raw)
                {
                    if (int.TryParse(key, out int id))
                    {
                        table[id] = kind;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // No table: the plain "Attack" stays.
        }

        _shapes = table;
        return table;
    }
}
