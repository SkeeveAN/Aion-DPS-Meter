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
                    if (int.TryParse(key, out int id) && !token.Contains('/'))
                    {
                        table[id] = token;
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
