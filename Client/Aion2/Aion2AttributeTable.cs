using System.IO;
using System.Text.Json;
using AionDPS.Update;

namespace AionDPS.Aion2;

/// <summary>One effect of an attribute or Lord value: the game's stat name it shows ("DamageRatio" = "Attack +"), the stat of the stat page it adds to (null when that stat is not listed
/// there) and whether the bonus lowers the stat (cooldown, MP cost).</summary>
public sealed record AttributeEffect(string Name, string? Stat = null, bool Lowers = false);

/// <summary>One node of the attribute ring: a main attribute or a Lord value, the id of its value in the stat frame (0x4936) and what it does.</summary>
public sealed record AttributeNode(string Key, int Id, bool Lord, IReadOnlyList<AttributeEffect> Effects);

/// <summary>An effect summed over all nodes: "LP +" from Vitality and from Life are one line.</summary>
public sealed record AttributeBonus(AttributeEffect Effect, double Percent);

/// <summary>A node's share in one stat of the stat page.</summary>
public sealed record AttributeSource(AttributeNode Node, int Points, double Percent, bool Lowers);

/// <summary>
/// What the attributes and Lord values of a character do. Every point of a node gives 0.1 % on each of its effects (checked on Might, Agility, Precision, Vitality, Willpower and
/// all ten Lords against the game's detail windows; Intelligence has no screenshot, the same factor is taken for it). The texts - descriptions, stat names, tooltips - are the
/// game's own, per language, from assets/aion2/stats/attributes.json (made by Tools/aion2-dat/build_attributes.py).
/// </summary>
public static class Aion2AttributeTable
{
    public const double PercentPerPoint = 0.1;

    private static AttributeEffect E(string name, string? stat = null, bool lowers = false) => new(name, stat, lowers);

    /// <summary>The six main attributes (clockwise from the top: Might, Agility, Precision, Willpower, Intelligence, Vitality), then the ten Lords (clockwise from the top left).</summary>
    public static readonly IReadOnlyList<AttributeNode> Nodes = new[]
    {
        new AttributeNode("str", 1, false, new[] { E("DamageRatio", "WeaponFixingDamage") }),
        new AttributeNode("dex", 2, false, new[] { E("EvasionRatio", "ArmorEvasion"), E("BlockRatio", "Block"), E("CriticalResistRatio", "CriticalResist") }),
        new AttributeNode("agi", 5, false, new[] { E("AccuracyRatio", "WeaponAccuracy"), E("CriticalRatio", "Critical") }),
        new AttributeNode("wis", 6, false, new[] { E("AbnormalResistance") }),
        new AttributeNode("int", 3, false, new[] { E("AbnormalAccuracy") }),
        new AttributeNode("con", 4, false, new[] { E("MaxHPRatio", "HPMax") }),
        new AttributeNode("justice", 7, true, new[] { E("DefenseRatio", "ArmorDefense"), E("Perfect", "Perfect") }),
        new AttributeNode("destruction", 13, true, new[] { E("DamageRatio", "WeaponFixingDamage"), E("PerfectResist", "PerfectResist") }),
        new AttributeNode("death", 14, true, new[] { E("CriticalRatio", "Critical"), E("IgnoreRestoration", "IgnoreRestoration") }),
        new AttributeNode("wisdom", 15, true, new[] { E("MPUseIncrease", null, true), E("HardHit", "HardHit") }),
        new AttributeNode("destiny", 16, true, new[] { E("MaxMPRatio", "MPMax"), E("IronWall", "IronWall") }),
        new AttributeNode("space", 17, true, new[] { E("MoveSpeed", "MoveSpeed"), E("BlockRatio", "Block") }),
        new AttributeNode("time", 11, true, new[] { E("CombatSpeed", "CombatSpeed"), E("HardHitResist") }),
        new AttributeNode("life", 10, true, new[] { E("MaxHPRatio", "HPMax"), E("Restoration", "Restoration") }),
        new AttributeNode("illusion", 9, true, new[] { E("CoolTimeIncrease", null, true), E("IgnoreIronWall", "IgnoreIronWall") }),
        new AttributeNode("freedom", 8, true, new[] { E("AccuracyRatio", "WeaponAccuracy"), E("EvasionRatio", "ArmorEvasion") }),
    };

    /// <summary>The stat page of the game as far as the attribute ring reaches into it: group (a CHARACTERSTATUS subtitle) and the stats in its order.</summary>
    public static readonly IReadOnlyList<(string Group, string[] Stats)> StatGroups = new (string, string[])[]
    {
        ("Bookmark", new[] { "WeaponFixingDamage", "ArmorDefense", "WeaponAccuracy", "ArmorEvasion", "Critical", "CriticalResist", "HPMax", "MPMax", "CombatSpeed", "MoveSpeed" }),
        ("Attack", new[] { "DefensePierce", "SealStoneAddDamage", "CriticalAddDamage", "CriticalDamageDefense", "BackAttackDamage", "BackAttackDefense", "FrontAttackDamage", "FrontAttackDefense",
                           "AmplifyAllDamage", "DecreaseDamage", "AmplifyWeaponDamage", "AmplifyCriticalDamage", "DecreaseCriticalDamage" }),
        ("Resist", new[] { "AdditionalHitRate", "BackAttackCritical", "BackAttackCriticalResist", "FrontAttackCritical", "FrontAttackCriticalResist", "BlockPierce", "Block",
                           "IgnoreIronWall", "IronWall", "IgnoreRestoration", "Restoration", "Perfect", "PerfectResist", "HardHit" }),
    };

    public static AttributeNode Node(string key) => Nodes.First(n => n.Key == key);

    /// <summary>The bonus of one node in percent: 0.1 per point.</summary>
    public static double Percent(int points) => points * PercentPerPoint;

    /// <summary>All effects over all nodes with points, summed per effect, the biggest first.</summary>
    public static IReadOnlyList<AttributeBonus> Totals(Func<AttributeNode, int> points)
    {
        var sums = new Dictionary<string, (AttributeEffect Effect, double Percent)>();
        foreach (AttributeNode node in Nodes)
        {
            int own = points(node);
            if (own <= 0)
            {
                continue;
            }

            foreach (AttributeEffect effect in node.Effects)
            {
                sums[effect.Name] = sums.TryGetValue(effect.Name, out var s) ? (s.Effect, s.Percent + Percent(own)) : (effect, Percent(own));
            }
        }

        return sums.Values.OrderByDescending(s => s.Percent).Select(s => new AttributeBonus(s.Effect, s.Percent)).ToList();
    }

    /// <summary>Which nodes add to a stat of the stat page, and how much each.</summary>
    public static IReadOnlyList<AttributeSource> Sources(string stat, Func<AttributeNode, int> points)
    {
        var list = new List<AttributeSource>();
        foreach (AttributeNode node in Nodes)
        {
            int own = points(node);
            foreach (AttributeEffect effect in node.Effects.Where(e => e.Stat == stat))
            {
                if (own > 0)
                {
                    list.Add(new AttributeSource(node, own, Percent(own), effect.Lowers));
                }
            }
        }

        return list;
    }

    // ---------------------------------------------------------------- texts

    private static readonly string FilePath = AssetStore.Path("aion2", "stats", "attributes.json");
    private static Dictionary<string, Dictionary<string, string>>? _texts;

    /// <summary>A text of the game ("desc.str", "stat.Block", "tip.Block", "grp.Attack") in a language; English when the client has no text in it (Chinese), the key when there is none at all.</summary>
    public static string Text(string key, string language)
    {
        var texts = _texts ??= Load();
        return texts.TryGetValue(key, out var row) ? row.GetValueOrDefault(language) ?? row.GetValueOrDefault("en") ?? key : key;
    }

    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        var table = new Dictionary<string, Dictionary<string, string>>();
        try
        {
            if (File.Exists(FilePath))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(FilePath));
                foreach (JsonProperty entry in document.RootElement.EnumerateObject())
                {
                    table[entry.Name] = entry.Value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // No texts: the page shows the keys.
        }

        return table;
    }
}
