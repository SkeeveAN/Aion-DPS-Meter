using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// A skill's icon: assets/aion2/skills/skill_icon_tokens.json maps a skill's first four digits
/// (skill id / 10000: 1528 for Bittercold Wind 15280000) to the client's icon name
/// (ICON_SO_SKILL_025), and assets/aion2/skills/icons holds those icons at 48 px, exported from the
/// game client (UI/Resource/Texture/Skill). A spirit's attacks share their summon's four digits, so
/// they show its icon. Null when there is none.
/// </summary>
public static class Aion2SkillIcons
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "skills", "icons");
    private static readonly string TokensPath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "skills", "skill_icon_tokens.json");
    private static IReadOnlyDictionary<int, string>? _tokens;

    public static string? PathFor(int skillId)
    {
        if (skillId <= 0 || !Tokens().TryGetValue(skillId / 10000, out string? token))
        {
            return null;
        }

        string path = Path.Combine(Folder, token + ".png");
        return File.Exists(path) ? path : null;
    }

    private static IReadOnlyDictionary<(string? Class, string Name), string>? _byName;

    /// <summary>
    /// The icon of a skill known only by its English name, for fights stored without skill ids (the fight history
    /// keeps the name only). A class's own skill wins over the same name elsewhere; null when the name has no icon.
    /// </summary>
    public static string? PathForName(string? englishName, string? className)
    {
        if (string.IsNullOrEmpty(englishName))
        {
            return null;
        }

        if (_byName is null)
        {
            var index = new Dictionary<(string?, string), string>();
            var tokens = Tokens();
            foreach ((int id, string name) in Aion2SkillNames.Load().OrderBy(p => p.Key))
            {
                if (id >= 10_000_000 && tokens.TryGetValue(id / 10000, out string? token))
                {
                    index.TryAdd((Aion2SkillNames.ClassOf(id), name), token);
                    index.TryAdd((null, name), token);
                }
            }

            _byName = index;
        }

        if ((_byName.TryGetValue((className, englishName), out string? found) || _byName.TryGetValue((null, englishName), out found))
            && Path.Combine(Folder, found + ".png") is string path && File.Exists(path))
        {
            return path;
        }

        return null;
    }

    private static IReadOnlyDictionary<int, string> Tokens()
    {
        if (_tokens is { } cached)
        {
            return cached;
        }

        var table = new Dictionary<int, string>();
        try
        {
            if (File.Exists(TokensPath))
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(TokensPath)) ?? new();
                foreach ((string key, string token) in raw)
                {
                    // An item-skill (a Theostone) names its icon with the item folder in front: "Item/ETC/Icon_Item_...".
                    if (int.TryParse(key, out int id))
                    {
                        table[id] = token.Contains('/') ? token[(token.LastIndexOf('/') + 1)..] : token;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // No table: no icons.
        }

        _tokens = table;
        return table;
    }
}
