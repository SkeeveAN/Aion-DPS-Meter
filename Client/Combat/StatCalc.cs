namespace AionDPS.Combat;

/// <summary>The stats the calculator works with. Each one is a value of the stat frame (see <see cref="StatCalc.Spec"/>): a flat number, or a percentage the frame sends in hundredths.</summary>
public enum StatKey
{
    FixedAttack,
    GearAttack,
    MaxAttack,
    MinAttack,
    AttackRatio,
    WeaponAmp,
    AllAmp,
    PveAmp,
    BossAmp,
    CritAmp,
    Perfect,
    Smite,
    FrontAmp,
    BackAmp,
    PveAdd,
    BossAdd,
}

/// <summary>Where the player mostly hits from: the stat of that side counts, with the share of the hits that really land there.</summary>
public enum AttackSide
{
    Unknown,
    Front,
    Back,
}

/// <summary>What the calculator assumes about the fight: the rates (measured from the last fight or set by hand), and for the expert mode the buffs and the boss.</summary>
public sealed record StatCalcSetup(
    double CritRate,
    AttackSide Side,
    double SideRate,
    double AttackRatioBuff = 0,
    double AmpBuff = 0,
    double CritAmpBuff = 0,
    double SmiteBuff = 0,
    double BossResistance = 0,
    double CritBase = StatCalc.DefaultCritBase,
    double SmiteBonus = StatCalc.DefaultSmiteBonus,
    double CritResist = 0);

/// <summary>The damage index of a character as it is and with the changes, and what one more percent of damage costs in each stat.</summary>
public sealed record StatCalcResult(double Before, double After, double GainPercent, IReadOnlyList<StatCalcLever> Levers);

/// <summary>How much of one stat gives one more percent of damage (a flat stat in points, a percentage stat in percentage points); <see cref="Share"/> is that amount against the stat's current value (0 when it is 0).</summary>
public sealed record StatCalcLever(StatKey Key, double ForOnePercent, double Share);

/// <summary>
/// The stat calculator: what a change of stats does to the damage of the same hit against the same boss. It is an estimate in the manner of a damage chain - the weapon and the flat
/// attack, raised by the attack ratio, plus the flat PvE and boss attack, times the damage amplifications, times the average of crit, smite and the side bonus. The stats are the ones the stat
/// frame sends; the order and the way each one enters follow what the client's own stat texts say, not a formula the game has published, so the numbers are a guide (beta): the index says
/// "3.8 % more", never "this many points of damage". What the game does with a stat that the texts do not explain (the base crit multiplier, how much a smite adds) is a setting of the expert mode
/// and the thing the recordings sent from the calculator are meant to find out.
/// </summary>
public static class StatCalc
{
    public const double DefaultCritBase = 150;
    public const double DefaultSmiteBonus = 100;

    /// <summary>The client's own text says a crit chance is at most 50 %.</summary>
    public const double MaxCritChance = 50;

    /// <summary>A stat's id in the stat frame and whether the frame sends it in hundredths of a percent.</summary>
    public readonly record struct StatSpec(int Id, bool Percent);

    private static readonly Dictionary<StatKey, StatSpec> Specs = new()
    {
        [StatKey.FixedAttack] = new(19, false),   // FixingDamage, "additional attack"
        [StatKey.GearAttack] = new(317, false),   // WeaponFixingDamage, "attack"
        [StatKey.MaxAttack] = new(31, false),     // WeaponDamage, "max attack"
        [StatKey.MinAttack] = new(33, false),
        [StatKey.AttackRatio] = new(425, true),   // DamageRatio, "attack +"
        [StatKey.WeaponAmp] = new(32, true),      // AmplifyWeaponDamage
        [StatKey.AllAmp] = new(28, true),         // AmplifyAllDamage
        [StatKey.PveAmp] = new(379, true),        // PvEAmplifyDamage
        [StatKey.BossAmp] = new(520, true),       // BossNpcAmplifyDamage
        [StatKey.CritAmp] = new(44, true),        // AmplifyCriticalDamage
        [StatKey.Perfect] = new(442, true),       // Perfect
        [StatKey.Smite] = new(443, true),         // HardHit, "smite"
        [StatKey.FrontAmp] = new(589, true),      // AmplifyFrontAttack
        [StatKey.BackAmp] = new(102, true),       // AmplifyBackAttack
        [StatKey.PveAdd] = new(56, false),        // PvEAddDamage
        [StatKey.BossAdd] = new(50, false),       // BossNpcAddDamage
    };

    public static StatSpec Spec(StatKey key) => Specs[key];

    public static IEnumerable<StatKey> Keys => Specs.Keys;

    /// <summary>
    /// How much a critical hit does against a plain one: the median over the skills of the median critical hit against the median plain hit of the same skill on the same monster from the same
    /// side (a hit with a Perfect, Double or Parry flag or with several strikes is left out). It holds everything that acts on a crit - the character's stats and the monster's resistance -
    /// so it is better than any rule from the stats; null when fewer than three skills have three hits of each kind.
    /// </summary>
    public static double? MeasureCritRatio(IReadOnlyList<DamageEvent> hits)
    {
        const HitKinds Special = HitKinds.Parry | HitKinds.Perfect | HitKinds.Double;
        var groups = new Dictionary<(int Skill, int Target, int Side), (List<double> Crit, List<double> Plain)>();
        foreach (DamageEvent e in hits)
        {
            if (e.IsHeal || e.IsTick || e.Estimated || e.Amount <= 0 || e.HitCount != 1 || ((e.Kinds ?? HitKinds.None) & Special) != 0)
            {
                continue;
            }

            var key = (e.SkillId, e.TargetObjectId, (int)((e.Kinds ?? HitKinds.None) & (HitKinds.Back | HitKinds.Front)));
            if (!groups.TryGetValue(key, out var lists))
            {
                lists = (new List<double>(), new List<double>());
                groups[key] = lists;
            }

            (e.IsCritical ? lists.Crit : lists.Plain).Add(e.Amount);
        }

        var ratios = groups.Values.Where(g => g.Crit.Count >= 3 && g.Plain.Count >= 3).Select(g => Median(g.Crit) / Median(g.Plain)).OrderBy(r => r).ToList();
        return ratios.Count >= 3 ? Median(ratios) : null;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
    }

    /// <summary>The side a character's own stats favour: the one whose direction bonus (front or back amplification) is the larger; unknown when neither has one or they are equal.</summary>
    public static AttackSide SideFromStats(IReadOnlyDictionary<StatKey, double> stats)
    {
        double front = Get(stats, StatKey.FrontAmp), back = Get(stats, StatKey.BackAmp);
        return front > back && front > 0 ? AttackSide.Front : back > front && back > 0 ? AttackSide.Back : AttackSide.Unknown;
    }

    /// <summary>The values of a stat frame as the calculator's table (percentages in percent); a stat the frame did not send is 0.</summary>
    public static Dictionary<StatKey, double> FromFrame(IReadOnlyDictionary<int, int> frame)
    {
        var values = new Dictionary<StatKey, double>();
        foreach ((StatKey key, StatSpec spec) in Specs)
        {
            double raw = frame.TryGetValue(spec.Id, out int v) ? v : 0;
            values[key] = spec.Percent ? raw / 100.0 : raw;
        }

        return values;
    }

    /// <summary>Whether the frame carries what the calculator needs at least: the attack of a physical weapon (a magical character's attack has other ids, not known yet).</summary>
    public static bool IsSupported(IReadOnlyDictionary<int, int> frame) =>
        frame.TryGetValue(Specs[StatKey.GearAttack].Id, out int gear) && gear > 0 && frame.TryGetValue(Specs[StatKey.MaxAttack].Id, out int max) && max > 0;

    private static double Get(IReadOnlyDictionary<StatKey, double> values, StatKey key) => values.TryGetValue(key, out double v) ? v : 0;

    /// <summary>The damage index of one stat table under a setup. Only the proportions between two indices mean something.</summary>
    public static double Index(IReadOnlyDictionary<StatKey, double> stats, StatCalcSetup setup)
    {
        double perfect = Math.Clamp(Get(stats, StatKey.Perfect) / 100, 0, 1);
        double max = Math.Max(Get(stats, StatKey.MaxAttack), 0);
        double min = Math.Clamp(Get(stats, StatKey.MinAttack), 0, max);
        double weapon = perfect * max + (1 - perfect) * (min + max) / 2;
        double weaponAmp = 1 + Math.Max(Get(stats, StatKey.WeaponAmp) / 100, -1);
        double baseAttack = Get(stats, StatKey.FixedAttack) + (Get(stats, StatKey.GearAttack) + weapon) * weaponAmp;
        double attack = baseAttack * (1 + Math.Max((Get(stats, StatKey.AttackRatio) + setup.AttackRatioBuff) / 100, -1)) + Get(stats, StatKey.PveAdd) + Get(stats, StatKey.BossAdd);

        double amplification = Math.Max(1 + (Get(stats, StatKey.AllAmp) + Get(stats, StatKey.PveAmp) + Get(stats, StatKey.BossAmp) + setup.AmpBuff - setup.BossResistance) / 100, 0);

        double critRate = Math.Clamp(setup.CritRate, 0, 100) / 100;
        double critMultiplier = Math.Max((setup.CritBase + Get(stats, StatKey.CritAmp) + setup.CritAmpBuff - setup.CritResist) / 100, 1);
        double crit = 1 + critRate * (critMultiplier - 1);

        double smiteChance = Math.Clamp((Get(stats, StatKey.Smite) + setup.SmiteBuff) / 100, 0, 1);
        double smite = 1 + smiteChance * setup.SmiteBonus / 100;

        double sideRate = Math.Clamp(setup.SideRate, 0, 100) / 100;
        double side = setup.Side switch
        {
            AttackSide.Front => 1 + sideRate * Math.Max(Get(stats, StatKey.FrontAmp) / 100, -1),
            AttackSide.Back => 1 + sideRate * Math.Max(Get(stats, StatKey.BackAmp) / 100, -1),
            _ => 1,
        };

        return attack * amplification * crit * smite * side;
    }

    /// <summary>The index before and after the changes (a change is added to the stat in the calculator's units), and the levers.</summary>
    public static StatCalcResult Calculate(IReadOnlyDictionary<StatKey, double> stats, IReadOnlyDictionary<StatKey, double> changes, StatCalcSetup setup)
    {
        double before = Index(stats, setup);
        var changed = new Dictionary<StatKey, double>(stats);
        foreach ((StatKey key, double delta) in changes)
        {
            changed[key] = Get(stats, key) + delta;
        }

        double after = Index(changed, setup);
        double gain = before > 0 ? (after / before - 1) * 100 : 0;
        return new StatCalcResult(before, after, gain, Levers(stats, setup));
    }

    /// <summary>For each stat the amount that adds one percent of damage, found from the effect of a small step (the chain is smooth): the cheapest in proportion to the stat's own size first. A stat that does nothing is left out.</summary>
    public static IReadOnlyList<StatCalcLever> Levers(IReadOnlyDictionary<StatKey, double> stats, StatCalcSetup setup)
    {
        double before = Index(stats, setup);
        var result = new List<StatCalcLever>();
        if (before <= 0)
        {
            return result;
        }

        foreach ((StatKey key, StatSpec spec) in Specs)
        {
            double step = spec.Percent ? 0.1 : Math.Max(1, Math.Abs(Get(stats, key)) * 0.01);
            var bumped = new Dictionary<StatKey, double>(stats) { [key] = Get(stats, key) + step };
            double gainPerStep = (Index(bumped, setup) / before - 1) * 100;
            if (gainPerStep <= 1e-9)
            {
                continue;
            }

            double forOne = step / gainPerStep;
            double current = Get(stats, key);
            result.Add(new StatCalcLever(key, forOne, current > 0 ? forOne / current : 0));
        }

        return result.OrderBy(l => l.Share > 0 ? l.Share : double.MaxValue).ThenBy(l => l.ForOnePercent).ToList();
    }
}
