using AionDPS.ChatLog;
using AionDPS.Combat.Sources;

namespace AionDPS.Combat;

/// <summary>
/// Avoided-attack and kill lines, verbatim from real OriginAion Chat.logs (English: Alhamdulilah's
/// 2026-09 log; German: Badigadi's Chat.ger.sin.log), plus the DefenseStats/PvpStats math on top.
/// Third-person forms share the templates the first-person lines confirmed ("X parried Y's
/// attack." occurs 1.044 times in the English corpus with real names on both sides).
/// </summary>
public static class SelfCheckDefense
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Avoided attacks + kills (verbatim EN/DE Chat.log lines):");
        var parser = new ChatLogParser();
        var lines = new[]
        {
            "2026.09.07 20:30:01 : You evaded Steel Rose Sorcerer's attack. ",
            "2026.09.07 20:30:02 : You parried Kobold Peon's attack. ",
            "2026.09.07 20:30:02 : You parried Kobold Peon's attack. ",
            "2026.09.07 20:30:03 : You blocked Paladin's attack with the protective shield effect. ",
            "2026.09.07 20:30:04 : You resisted Steel Rose Scout's Poison Fluid. ",
            "2026.09.07 20:30:05 : Badigadi parried Steel Rose Sorcerer's attack. ",
            "2026.09.07 20:30:06 : Sardine evaded Steel Rose Scout's Fang Strike. ",
            "2026.09.07 20:30:07 : Ezzz's attack was blocked by the protective shield effect cast on Kiazaki. ",
            "2026.09.07 20:30:08 : Steel Rose Sorcerer inflicted 812 damage on You. ",
            "2026.09.07 20:30:09 : You inflicted 4.210 damage on Xiaoeight. ",
            "2026.09.07 20:30:10 : You have defeated Xiaoeight. ",
            "2026.09.07 20:30:10 : You have defeated Xiaoeight. ",
            "2026.09.07 20:30:11 : Sniggy was killed by Ulgorn Raider's attack. ",
            "2026.09.07 20:30:12 : Ulgorn Raider has died. ",
            "2026.09.07 20:31:01 : Ihr weicht dem Angriff von Troll-Beobachter aus. ",
            "2026.09.07 20:31:02 : Ihr pariert den Angriff von Susu-Arbeiter. ",
            "2026.09.07 20:31:03 : Kiazaki pariert den Angriff von Söldner der Stahlrose. ",
            "2026.09.07 20:31:04 : Ihr widersteht Eispfeil von Zauberer der Stahlrose. ",
            "2026.09.07 20:31:05 : Ihr habt Rayth besiegt. ",
            "2026.09.07 20:31:06 : Kiazaki hat Rayth im Abyss besiegt. ",
            "2026.09.07 20:31:07 : Souky ist gestorben. ",
        };

        List<DamageEvent> damage = parser.Parse(lines);
        List<AvoidEvent> avoids = parser.DrainAvoids();
        List<KillEvent> kills = parser.DrainKills();
        int you = parser.Names.GetOrAssignId("You");
        int Id(string name) => parser.Names.GetOrAssignId(name);

        bool avoidCount = avoids.Count == 12;
        bool repeatedParryCounted = avoids.Count(a => a.Kind == AvoidKind.Parry && a.TargetObjectId == you && a.SourceObjectId == Id("Kobold Peon")) == 2;
        bool kinds = avoids.Count(a => a.Kind == AvoidKind.Dodge) == 3
            && avoids.Count(a => a.Kind == AvoidKind.Parry) == 5
            && avoids.Count(a => a.Kind == AvoidKind.Block) == 2
            && avoids.Count(a => a.Kind == AvoidKind.Resist) == 2;
        // 5 English "You …" lines + 3 German "Ihr …" lines all land on the one local-player id.
        bool selfCanonical = avoids.Count(a => a.TargetObjectId == you) == 8;
        bool germanSelf = avoids.Any(a => a.Kind == AvoidKind.Dodge && a.TargetObjectId == you && a.SourceObjectId == Id("Troll-Beobachter"));
        bool shieldBlockSides = avoids.Any(a => a.Kind == AvoidKind.Block && a.SourceObjectId == Id("Ezzz") && a.TargetObjectId == Id("Kiazaki"));
        bool skillsCaptured = avoids.Any(a => a.Kind == AvoidKind.Resist && a.Skill == "Poison Fluid") && avoids.Any(a => a.Kind == AvoidKind.Dodge && a.Skill == "Fang Strike")
            && avoids.Any(a => a.Kind == AvoidKind.Resist && a.Skill == "Eispfeil");
        bool damageUntouched = damage.Count == 2;

        bool killCount = kills.Count == 6;
        bool defeatedDeduped = kills.Count(k => k.VictimObjectId == Id("Xiaoeight")) == 1 && kills.Single(k => k.VictimObjectId == Id("Xiaoeight")).KillerObjectId == you;
        bool killedBy = kills.Any(k => k.VictimObjectId == Id("Sniggy") && k.KillerObjectId == Id("Ulgorn Raider"));
        bool diedNoKiller = kills.Any(k => k.VictimObjectId == Id("Ulgorn Raider") && k.KillerObjectId is null)
            && kills.Any(k => k.VictimObjectId == Id("Souky") && k.KillerObjectId is null);
        bool germanKills = kills.Any(k => k.VictimObjectId == Id("Rayth") && k.KillerObjectId == you)
            && kills.Any(k => k.VictimObjectId == Id("Rayth") && k.KillerObjectId == Id("Kiazaki"));

        var players = new HashSet<int> { you, Id("Xiaoeight"), Id("Sniggy"), Id("Kiazaki"), Id("Badigadi"), Id("Sardine"), Id("Ezzz"), Id("Rayth"), Id("Souky") };
        Dictionary<int, DefenseSummary> defense = DefenseStats.ByDefender(avoids, damage);
        DefenseSummary yours = defense[you];
        bool defenseMath = yours.Dodges == 2 && yours.Parries == 3 && yours.Blocks == 1 && yours.Resists == 2 && yours.HitsTaken == 1
            && yours.IncomingAttacks == 9 && Math.Abs((yours.AvoidRatePercent ?? 0) - 100.0 * 8 / 9) < 0.01
            && yours.Display.StartsWith("D 2 · P 3 · B 1 · R 2 (89%)");

        Dictionary<int, PvpSummary> pvp = PvpStats.ByPlayer(kills, damage, players.Contains);
        bool pvpMath = pvp[you].Kills == 2 && pvp[you].Deaths == 0 && pvp[you].MaxHit == 4210
            && pvp[Id("Xiaoeight")].Deaths == 1 && pvp[Id("Kiazaki")].Kills == 1 && pvp[Id("Souky")].Deaths == 1
            && !pvp.ContainsKey(Id("Ulgorn Raider")) && pvp[you].Display.StartsWith("K 2 / D 0 · max ");

        Console.WriteLine($"  -> 12 avoided attacks recognised (EN+DE), repeat within a second counted twice: {avoidCount && repeatedParryCounted}");
        Console.WriteLine($"  -> kinds: 3 dodges, 5 parries, 2 blocks, 2 resists: {kinds}");
        Console.WriteLine($"  -> \"You\"/\"Ihr\" both resolve to the local player: {selfCanonical && germanSelf}");
        Console.WriteLine($"  -> shield-block line has attacker/defender the right way round: {shieldBlockSides}");
        Console.WriteLine($"  -> resisted/evaded skill names captured: {skillsCaptured}");
        Console.WriteLine($"  -> damage lines still parse as damage, not as avoids: {damageUntouched}");
        Console.WriteLine($"  -> 6 kills, repeated defeat broadcast counted once: {killCount && defeatedDeduped}");
        Console.WriteLine($"  -> \"was killed by\" names the killer, \"has died\"/\"ist gestorben\" do not: {killedBy && diedNoKiller}");
        Console.WriteLine($"  -> German \"habt/hat … (im …) besiegt\" recognised: {germanKills}");
        Console.WriteLine($"  -> defense tally and rate for the local player: {defenseMath}");
        Console.WriteLine($"  -> PvP kills/deaths/max hit: {pvpMath}");
        return avoidCount && repeatedParryCounted && kinds && selfCanonical && germanSelf && shieldBlockSides && skillsCaptured
            && damageUntouched && killCount && defeatedDeduped && killedBy && diedNoKiller && germanKills && defenseMath && pvpMath;
    }
}
