using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// The average hit of a skill, from assets/aion2/protocol/skill_hit_average.json (made by <c>aion2-skill-averages</c> out of
/// recordings: every hit a player landed on a monster, one skill at a time). It is what turns a world boss's hit notices - which
/// name attacker, target and skill but no damage - into an estimated amount. Players differ in gear, so the figure is the
/// typical hit of the skill, not of the player; on a crowd every player is off in both directions and the ranking holds.
/// A skill the table has too few hits of falls back to its family (the skill id without its last digit: talent variants of one skill).
/// </summary>
public static class Aion2HitAverages
{
    /// <summary>Hits a table row needs to be trusted.</summary>
    public const int MinSamples = 8;

    private sealed record Row(long Average, int Samples);

    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "protocol", "skill_hit_average.json");
    private static Dictionary<int, Row>? _skills;
    private static Dictionary<int, Row>? _families;
    private static HashSet<int>? _bossNpcs;

    /// <summary>The average hit of the skill, or null when the table does not know it well enough.</summary>
    public static long? AverageOf(int skillId)
    {
        Load();
        if (_skills!.TryGetValue(skillId, out Row? row) && row.Samples >= MinSamples)
        {
            return row.Average;
        }

        return _families!.TryGetValue(skillId / 10, out Row? family) && family.Samples >= MinSamples ? family.Average : null;
    }

    /// <summary>Whether the table is there at all (a build without the asset estimates nothing).</summary>
    public static bool Available
    {
        get
        {
            Load();
            return _skills!.Count > 0;
        }
    }

    /// <summary>The bosses whose hits are estimated: the world bosses of the field maps (assets/aion2/bosses/field_bosses.json),
    /// the only fights where so many players are around that the server sends their hit notices without the damage.</summary>
    public static bool IsEstimatedBoss(int npcId)
    {
        lock (typeof(Aion2HitAverages))
        {
            _bossNpcs ??= Aion2FieldBosses.Maps.SelectMany(m => m.Bosses).Select(b => b.Npc).ToHashSet();
            return _bossNpcs.Contains(npcId);
        }
    }

    private static void Load()
    {
        if (_skills is not null)
        {
            return;
        }

        lock (typeof(Aion2HitAverages))
        {
            if (_skills is not null)
            {
                return;
            }

            var skills = new Dictionary<int, Row>();
            var families = new Dictionary<int, Row>();
            try
            {
                if (File.Exists(FilePath))
                {
                    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                    Read(doc.RootElement.GetProperty("skills"), skills);
                    Read(doc.RootElement.GetProperty("families"), families);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                // No table: nothing is estimated, the meter keeps working.
            }

            _families = families;
            _skills = skills;
        }
    }

    private static void Read(JsonElement element, Dictionary<int, Row> into)
    {
        foreach (JsonProperty entry in element.EnumerateObject())
        {
            if (int.TryParse(entry.Name, out int id))
            {
                into[id] = new Row(entry.Value.GetProperty("avg").GetInt64(), entry.Value.GetProperty("n").GetInt32());
            }
        }
    }
}
