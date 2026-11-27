using System.IO;
using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Update;

namespace AionDPS.Ui;

public sealed record GuideMechanic(string Id, int[] Codes, string Severity);

public sealed record GuideStep(string Mechanic, int OffsetMs);

public sealed record GuideChain(int[] Trigger, int? MinHp, int? MaxHp, GuideStep[] Steps);

public sealed record GuideGate(int HpPercent, string Mechanic);

public sealed record BossGuide(string Id, int[] NpcIds, GuideMechanic[] Mechanics, GuideGate[] Gates, GuideChain[] Chains);

/// <summary>One line of the forecast under the boss cast bar: what comes next, with a countdown (<see cref="SecondsLeft"/>) or the hit points it is tied to (<see cref="Badge"/>).</summary>
public sealed record ForecastRowData(string Title, string Action, string Badge, double? SecondsLeft, bool Wipe);

/// <summary>The boss guides of assets/aion2/bosses/boss_guides.json: the skill ids of a boss named as mechanics.</summary>
public static class BossGuides
{
    private static IReadOnlyList<BossGuide>? _cache;

    public static void Reload() => _cache = null;

    public static IReadOnlyList<BossGuide> All => _cache ??= Load();

    public static BossGuide? For(int? npcId) => npcId is int id ? All.FirstOrDefault(g => g.NpcIds.Contains(id)) : null;

    public static GuideMechanic? MechanicOf(BossGuide guide, int skillId) => guide.Mechanics.FirstOrDefault(m => m.Codes.Contains(skillId));

    public static string Title(BossGuide g, string mechanic) => LocalizationManager.Instance[$"Guide.{g.Id}.{mechanic}.T"];

    public static string Action(BossGuide g, string mechanic) => LocalizationManager.Instance[$"Guide.{g.Id}.{mechanic}.A"];

    private static IReadOnlyList<BossGuide> Load()
    {
        var list = new List<BossGuide>();
        try
        {
            string path = AssetStore.Path("aion2", "bosses", "boss_guides.json");
            if (!File.Exists(path))
            {
                return list;
            }

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (JsonElement b in doc.RootElement.GetProperty("bosses").EnumerateArray())
            {
                var mechanics = b.GetProperty("mechanics").EnumerateArray()
                    .Select(m => new GuideMechanic(m.GetProperty("id").GetString()!, Ints(m.GetProperty("codes")), m.GetProperty("severity").GetString() ?? "dodge")).ToArray();
                var gates = b.GetProperty("hpGates").EnumerateArray().Select(g => new GuideGate(g.GetProperty("hpPercent").GetInt32(), g.GetProperty("mechanic").GetString()!)).ToArray();
                var chains = b.GetProperty("chains").EnumerateArray().Select(c => new GuideChain(
                    Ints(c.GetProperty("trigger")),
                    c.TryGetProperty("minHpPercent", out JsonElement mn) ? mn.GetInt32() : null,
                    c.TryGetProperty("maxHpPercent", out JsonElement mx) ? mx.GetInt32() : null,
                    c.GetProperty("steps").EnumerateArray().Select(s => new GuideStep(s.GetProperty("mechanic").GetString()!, s.GetProperty("offsetMs").GetInt32())).ToArray())).ToArray();
                list.Add(new BossGuide(b.GetProperty("id").GetString()!, Ints(b.GetProperty("npcIds")), mechanics, gates, chains));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // a missing or broken file: the bar works without the guide
        }

        return list;
    }

    private static int[] Ints(JsonElement array) => array.EnumerateArray().Select(e => e.GetInt32()).ToArray();
}

/// <summary>
/// Remembers which fixed chain a boss started (the casts it was seen to make) and says what comes next: the pending steps of the chains with a countdown, and the next
/// mechanic tied to the boss's hit points. Runs on the UI thread, fed by each refresh of the overlay.
/// </summary>
public static class BossForecast
{
    private sealed record Pending(string Mechanic, int Chain, DateTime Due);

    private static readonly List<Pending> Open = new();
    private static readonly HashSet<string> Seen = new();
    private static int _epoch = -1;
    private static int _boss = -1;
    private static readonly TimeSpan Late = TimeSpan.FromSeconds(3);
    private const int MostRows = 3;

    /// <summary>What comes next for the boss that was seen casting (empty: no boss with a guide in this fight).</summary>
    public static IReadOnlyList<ForecastRowData> Build(Aion2EntityDirectory dir, DateTime now)
    {
        if (dir.ZoneEpoch != _epoch)
        {
            _epoch = dir.ZoneEpoch;
            Open.Clear();
            Seen.Clear();
            _boss = -1;
        }

        foreach (CastView view in dir.Combat.Casts(now, TimeSpan.FromSeconds(2)))
        {
            CastNotice cast = view.Notice;
            if (dir.IsKnownPlayer(cast.Actor) || BossGuides.For(dir.NpcIdOf(cast.Actor)) is not { } guide)
            {
                continue;
            }

            _boss = cast.Actor;
            if (!Seen.Add($"{cast.Actor}:{cast.SkillId}:{cast.Start.Ticks}"))
            {
                continue;
            }

            int? hp = HpPercent(dir, cast.Actor);
            if (BossGuides.MechanicOf(guide, cast.SkillId) is { } happened)
            {
                Pending? first = Open.Where(p => p.Mechanic == happened.Id).OrderBy(p => p.Due).FirstOrDefault();
                if (first is not null)
                {
                    Open.Remove(first);
                }
            }

            for (int i = 0; i < guide.Chains.Length; i++)
            {
                GuideChain chain = guide.Chains[i];
                if (!chain.Trigger.Contains(cast.SkillId) || (chain.MinHp is int min && hp is int h1 && h1 < min) || (chain.MaxHp is int max && hp is int h2 && h2 > max))
                {
                    continue;
                }

                int index = i;
                Open.RemoveAll(p => p.Chain == index);
                foreach (GuideStep step in chain.Steps)
                {
                    Open.Add(new Pending(step.Mechanic, index, cast.Start.AddMilliseconds(step.OffsetMs)));
                }
            }
        }

        Open.RemoveAll(p => now - p.Due > Late);
        if (Seen.Count > 2000)
        {
            Seen.Clear();
        }

        var rows = new List<ForecastRowData>();
        BossGuide? current = _boss >= 0 ? BossGuides.For(dir.NpcIdOf(_boss)) : null;
        if (current is null || (_boss >= 0 && dir.HitPoints.Latest(_boss)?.Hp <= 0))
        {
            Open.Clear();
            return rows;
        }

        foreach (Pending p in Open.Where(p => p.Due > now).OrderBy(p => p.Due).Take(MostRows - 1))
        {
            rows.Add(new ForecastRowData(BossGuides.Title(current, p.Mechanic), BossGuides.Action(current, p.Mechanic), "", (p.Due - now).TotalSeconds, Severity(current, p.Mechanic)));
        }

        if (HpPercent(dir, _boss) is int pct)
        {
            GuideGate? gate = current.Gates.Where(g => g.HpPercent < pct).OrderByDescending(g => g.HpPercent).FirstOrDefault();
            if (gate is not null)
            {
                rows.Add(new ForecastRowData(BossGuides.Title(current, gate.Mechanic), BossGuides.Action(current, gate.Mechanic),
                    string.Format(LocalizationManager.Instance["Combat.Forecast.AtHp"], gate.HpPercent), null, Severity(current, gate.Mechanic)));
            }
        }

        return rows;
    }

    private static bool Severity(BossGuide g, string mechanic) => g.Mechanics.FirstOrDefault(m => m.Id == mechanic)?.Severity == "wipe";

    private static int? HpPercent(Aion2EntityDirectory dir, int boss)
    {
        long max = Aion2BossHp.RealMax(dir, boss) ?? 0;
        return dir.HitPoints.Latest(boss)?.Hp is long hp && max > 0 ? (int)Math.Clamp(Math.Round(100.0 * hp / max), 0, 100) : null;
    }
}
