using System.IO;
using AionSniffer.ChatLog;
using AionSniffer.Data;
using AionSniffer.Ui;
using AionSniffer.Update;

namespace AionSniffer.Combat;

/// <summary>
/// Verifies the DPS/iDPS math, the SM_ATTACK multi-hit parser, the skill database, and the
/// live-aggregation wiring against synthetic and real reference data, all without needing a live
/// capture -- run via `dotnet run -- selftest`. Includes the Gladiator/Zauberer "ALL view"
/// thought experiment that motivated distinguishing DPS from iDPS in the first place, and the
/// real numbers pulled from a public myaion.eu boss-fight session (see README) to ground-truth
/// the iDPS formula.
/// </summary>
public static class SelfCheck
{
    private const int GladiatorId = 1;
    private const int ZaubererId = 2;
    private const int BossId = 100;

    public static bool Run()
    {
        bool ok = true;
        ok &= RunGladiatorVsZaubererScenario();
        ok &= RunMyAionReplayScenario();
        ok &= RunSkillDatabaseScenario();
        ok &= RunItemDatabaseScenario();
        ok &= RunAppVersionScenario();
        ok &= RunFactionResolverScenario();
        ok &= RunAsciiTableScenario();
        ok &= RunLiveAggregatorScenario();
        ok &= RunChatLogParserScenario();
        ok &= RunChatLogRealWorldPatternsScenario();
        ok &= RunRelicApScenario();
        ok &= RunGermanChatLogScenario();
        return ok;
    }

    /// <summary>
    /// German lines taken verbatim from a real OriginAion Chat.log written by another player's
    /// German client (a Cleric grouped with a Spiritmaster). Until that file existed, the German
    /// patterns were derived from the client's own string table and had never met real output;
    /// four shapes turned out to be missing entirely, and each one below is here because it was
    /// silently dropped:
    ///
    /// - present tense ("X fügt Y durch Z N Schaden zu und ..."), used by skills that do more than
    ///   damage. The parser only knew the perfect form.
    /// - redirected damage. This is the important one: when a protective effect moves damage onto
    ///   someone else, the ordinary damage line reports 0 and the real number appears ONLY in the
    ///   "Ein Schutzeffekt überträgt ..." line. The meter showed the protected player taking 42
    ///   hits for zero damage while ~21.500 damage went unrecorded.
    /// - damage-over-time ticks, which name the skill but not its caster. German logs the cast
    ///   first, so the cast is what makes the ticks attributable.
    /// - a second "stored it in your cube" wording, and quantities written with a thousands dot
    ///   ("Ihr habt 1.634 [item:...] erhalten"), which the qty pattern rejected outright.
    /// </summary>
    private static bool RunGermanChatLogScenario()
    {
        var lines = new[]
        {
            "2026.09.07 00:50:00 : Suno hat Goldur durch Benutzung von Seelenflut I 1.234 Schaden zugefügt. ",
            "2026.09.07 00:50:01 : Suno fügt Goldur durch Magische Umkehr VII 1.304 Schaden zu und löst einige der magischen Verstärkungen auf. ",
            "2026.09.07 00:50:02 : Goldur hat Suno durch Benutzung von Durchdringende Welle I 0 Schaden zugefügt. ",
            "2026.09.07 00:50:02 : Ein Schutzeffekt überträgt die von Goldur bei Suno angerichteten 439 Schaden auf Erdgeist. ",
            "2026.09.07 00:50:03 : Suno hat Kette der Erde V eingesetzt und Goldur erleidet fortwährend Schaden. ",
            "2026.09.07 00:50:04 : Goldur erhält durch Kette der Erde V 87 Schaden. ",
            "2026.09.07 00:50:05 : Goldur erhält durch Erosion VI 386 Schaden. ",
            // Verbatim from the German Assassin's client during the Sauro Supply Base run (the
            // thousands dot and the crit prefix glued to the target's name are both as-logged).
            // This shape carried 427.217 damage in that single run and matched nothing before.
            "2026.09.07 20:57:03 : Gardenführer Achradim erhält durch Euren Einsatz von Siegelgravur V 380 Schaden und den Effekt 'Siegelgravur'. ",
            "2026.09.07 20:57:16 : Kritischer Treffer!Gardenführer Achradim erhält durch Euren Einsatz von Siegelangriff IV 1.262 Schaden und den Effekt 'Siegelgravur'. ",
            "2026.09.07 00:50:06 : Ihr habt durch Licht der Verjüngung V 512 TP wiederhergestellt. ",
            "2026.09.07 00:50:07 : Kojima hat 618 TP wiederhergestellt, weil Ihr Blitz-Wiederherstellung VII benutzt habt. ",
        };

        var parser = new ChatLogParser();
        var events = parser.Parse(lines);
        long DamageBy(string name) => events
            .Where(e => !e.IsHeal && parser.Names.NameFor(e.SourceObjectId) == name)
            .Sum(e => e.Amount);
        long DamageTo(string name) => events
            .Where(e => !e.IsHeal && parser.Names.NameFor(e.TargetObjectId) == name)
            .Sum(e => e.Amount);

        Console.WriteLine("[selftest] German Chat.log (verbatim lines from a real German client):");

        // 1.234 perfect + 1.304 present tense + 87 attributed DoT tick.
        bool sunoOk = DamageBy("Suno") == 2_625;

        // The zero-damage line plus the 439 it was redirected for -- and the redirect lands on the
        // spirit that absorbed it, not on the player it was aimed at.
        bool redirectOk = DamageBy("Goldur") == 439 && DamageTo("Erdgeist") == 439 && DamageTo("Suno") == 0;

        // "Erosion VI" was never announced in this excerpt, so its caster is unknown and the tick
        // must NOT be credited to whoever happened to cast something else.
        bool unannouncedDotDropped = !events.Any(e => e.Amount == 386);

        bool healsOk = events.Count(e => e.IsHeal) == 2
            && events.Any(e => e.IsHeal && e.Amount == 512)
            && events.Any(e => e.IsHeal && e.Amount == 618);

        Console.WriteLine($"  -> perfect + present tense + attributed DoT all counted: {sunoOk}");
        Console.WriteLine($"  -> redirected damage recorded, and charged to the absorber: {redirectOk}");
        Console.WriteLine($"  -> DoT tick with no known caster left uncounted: {unannouncedDotDropped}");
        // The rider-effect shape ("X erhält durch Euren Einsatz von Y N Schaden und den Effekt
        // 'Z'."), German's rendering of English's rune-carve line -- target-first word order, so
        // no widening of DamagePatternDe could ever have caught it. Both lines are the local
        // player's own hits, so both must be credited to "You" and land on ONE target name: a
        // leaked "Kritischer Treffer!" prefix would split Achradim into two mobs.
        bool riderEffectOk = DamageBy("You") == 380 + 1_262
            && DamageTo("Gardenführer Achradim") == 380 + 1_262
            && !events.Any(e => (parser.Names.NameFor(e.TargetObjectId) ?? "").Contains("Kritischer Treffer"));

        Console.WriteLine($"  -> self-heal and heal-by-you-on-another both counted: {healsOk}");
        Console.WriteLine($"  -> rider-effect hits (\"...N Schaden und den Effekt 'X'\") counted, crit prefix stripped: {riderEffectOk}");

        return sunoOk && redirectOk && unannouncedDotDropped && healsOk && riderEffectOk;
    }

    /// <summary>
    /// Checks the relic AP table against the Relic Appraiser dialog the user transcribed it from:
    /// four relic types, four tiers each, every tier of a type worth a fixed multiple (1x/2x/3x/4x)
    /// of its own base -- 300 for Icon, 600 for Seal, 1.200 for Goblet, 2.400 for Crown. Worth
    /// asserting because the table is hand-entered id-by-id: a transposed digit would silently
    /// misprice one relic forever, and the ids run in the REVERSE order of the dialog's listing
    /// (186000051 is the most valuable, 186000066 the least), which is exactly the kind of detail
    /// a later edit gets backwards. The totals below are the real haul from the user's own
    /// session, counted independently from his Chat.log: Kisame 19.800 AP, the local player 11.100.
    /// </summary>
    private static bool RunRelicApScenario()
    {
        Console.WriteLine("[selftest] Relic AP table:");

        bool allSixteenPresent = RelicApDatabase.All.Count == 16;

        // Ordered cheapest-to-dearest within each type, which is descending item id.
        var tiers = new (string Type, int Base, int[] Ids)[]
        {
            ("Icon", 300, new[] { 186000066, 186000065, 186000064, 186000063 }),
            ("Seal", 600, new[] { 186000062, 186000061, 186000060, 186000059 }),
            ("Goblet", 1_200, new[] { 186000058, 186000057, 186000056, 186000055 }),
            ("Crown", 2_400, new[] { 186000054, 186000053, 186000052, 186000051 }),
        };

        bool tiersOk = true;
        foreach (var (type, baseAp, ids) in tiers)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                long expected = (long)baseAp * (i + 1);
                long actual = RelicApDatabase.ApFor(ids[i]);
                if (actual != expected)
                {
                    Console.WriteLine($"  !! {type} tier {i + 1} (id {ids[i]}): expected {expected} AP, got {actual}");
                    tiersOk = false;
                }
            }
        }

        // Kisame's real haul: 3 Lesser Goblet, 3 Lesser Seal, 2 Lesser Icon, 1 Greater Crown,
        // 1 Ancient Icon, 1 Lesser Crown, 1 Greater Goblet.
        long kisame = RelicApDatabase.ApFor(186000058, 3) + RelicApDatabase.ApFor(186000062, 3)
            + RelicApDatabase.ApFor(186000066, 2) + RelicApDatabase.ApFor(186000052)
            + RelicApDatabase.ApFor(186000065) + RelicApDatabase.ApFor(186000054)
            + RelicApDatabase.ApFor(186000056);

        // The local player's: 3 Lesser Icon, 1 each of Ancient Goblet, Greater Seal, Lesser Seal,
        // Ancient Icon, Lesser Goblet, Major Seal, Major Icon.
        long you = RelicApDatabase.ApFor(186000066, 3) + RelicApDatabase.ApFor(186000057)
            + RelicApDatabase.ApFor(186000060) + RelicApDatabase.ApFor(186000062)
            + RelicApDatabase.ApFor(186000065) + RelicApDatabase.ApFor(186000058)
            + RelicApDatabase.ApFor(186000059) + RelicApDatabase.ApFor(186000063);

        bool haulOk = kisame == 19_800 && you == 11_100;
        bool nonRelicIsZero = RelicApDatabase.ApFor(186000936) == 0 && !RelicApDatabase.IsRelic(186000936);

        Console.WriteLine($"  -> all 16 relics present: {allSixteenPresent}");
        Console.WriteLine($"  -> every tier is its type's 1x/2x/3x/4x multiple: {tiersOk}");
        Console.WriteLine($"  -> real session haul reproduces (Kisame {kisame}, You {you}): {haulOk}");
        Console.WriteLine($"  -> an ordinary looted item is not priced as a relic: {nonRelicIsZero}");

        return allSixteenPresent && tiersOk && haulOk && nonRelicIsZero;
    }

    /// <summary>
    /// Gladiator hits a training dummy every second for 300s (constant weaving); Zauberer hits
    /// theirs only every 60s with a big nuke. Demonstrates why "DPS" (ALL view, wall-clock) alone
    /// is misleading, and what "active-only" tries (and, for a lone isolated hit, fails) to fix.
    /// </summary>
    private static bool RunGladiatorVsZaubererScenario()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var events = new List<DamageEvent>();

        for (int i = 0; i < 300; i++)
        {
            events.Add(new DamageEvent(start.AddSeconds(i), GladiatorId, BossId, 1000, false));
        }

        for (int i = 0; i < 5; i++)
        {
            events.Add(new DamageEvent(start.AddSeconds(i * 60), ZaubererId, BossId, 50_000, false));
        }

        double? gladWall = DpsCalculator.AllDpsWallClock(events, GladiatorId);
        double? zaubWall = DpsCalculator.AllDpsWallClock(events, ZaubererId);
        double? gladActive = DpsCalculator.AllDpsActiveOnly(events, GladiatorId, TimeSpan.FromSeconds(3));
        double? zaubActive = DpsCalculator.AllDpsActiveOnly(events, ZaubererId, TimeSpan.FromSeconds(3));

        Console.WriteLine("[selftest] Gladiator/Zauberer ALL-view scenario:");
        Console.WriteLine($"  Gladiator: wallclock={Fmt(gladWall)} active={Fmt(gladActive)} (300 hits x 1000 dmg, 1s apart)");
        Console.WriteLine($"  Zauberer:  wallclock={Fmt(zaubWall)} active={Fmt(zaubActive)} (5 hits x 50000 dmg, 60s apart)");

        // Expectation: both spans have more than one hit, so wall-clock is well-defined (not
        // null) for both here -- assert that explicitly so a regression fails loudly instead of
        // a lifted "null > null is false" comparison quietly passing or failing for the wrong
        // reason. Then: on a raw wall-clock "ALL" reading the bursty Zauberer is NOT obviously
        // behind the sustained Gladiator (~1042 vs ~1003) -- this is the exact unfairness the
        // user's original example was about.
        bool wallClockLooksUnfair = gladWall is double gw && zaubWall is double zw && zw > gw * 0.9;

        // Expectation: with a 3s idle threshold, every one of the Gladiator's 1s gaps counts
        // (constant activity), so a real rate comes out. Every one of the Zauberer's 60s gaps
        // is excluded outright -- there's no gap short enough to count, so there is no active
        // time to divide by. AllDpsActiveOnly returns null for that rather than the raw damage
        // total (an earlier version did that, and it reads as a UI bug -- "250,000 iDPS" with no
        // context looks broken, not "undefined"; a real build would show "n/a" here instead).
        bool gladiatorActiveIsReasonable = gladActive is double g && g > 900 && g < 1100;
        bool zaubererActiveIsUndefined = zaubActive is null;

        Console.WriteLine($"  -> wall-clock makes the bursty caster look competitive: {wallClockLooksUnfair}");
        Console.WriteLine($"  -> active-only correctly rates the sustained gladiator: {gladiatorActiveIsReasonable}");
        Console.WriteLine($"  -> active-only correctly reports \"undefined\" for the isolated-burst caster: {zaubererActiveIsUndefined}");

        return wallClockLooksUnfair && gladiatorActiveIsReasonable && zaubererActiveIsUndefined;
    }

    private static string Fmt(double? v) => v.HasValue ? v.Value.ToString("F1") : "n/a";

    /// <summary>
    /// Replays the real numbers from myaion.eu's public session /PvESession/1716393: player
    /// "Strohmie" dealt 84,360,277 damage to one boss at a displayed rate of 438,288. Feeding
    /// synthetic hits spanning the implied ~192.48s duration should reproduce that rate via
    /// <see cref="DpsCalculator.TargetIDps"/>.
    /// </summary>
    private static bool RunMyAionReplayScenario()
    {
        const long totalDamage = 84_360_277;
        const int expectedIDps = 438_288;
        double impliedDurationSeconds = (double)totalDamage / expectedIDps; // ~192.48s

        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        const int hitCount = 30;
        long perHit = totalDamage / hitCount;
        long remainder = totalDamage - perHit * hitCount;

        var events = new List<DamageEvent>();
        for (int i = 0; i < hitCount; i++)
        {
            double t = i * impliedDurationSeconds / (hitCount - 1);
            long amount = perHit + (i == hitCount - 1 ? remainder : 0);
            events.Add(new DamageEvent(start.AddSeconds(t), GladiatorId, BossId, amount, false));
        }

        double? iDps = DpsCalculator.TargetIDps(events, BossId, GladiatorId);
        Console.WriteLine("[selftest] myaion.eu replay (Strohmie vs. boss, /PvESession/1716393):");
        Console.WriteLine($"  expected iDPS={expectedIDps}, computed iDPS={Fmt(iDps)}");

        bool matches = iDps is double v && Math.Abs(v - expectedIDps) < 1.0;
        Console.WriteLine($"  -> matches within rounding: {matches}");
        return matches;
    }

    /// <summary>
    /// The Discord table format. Cheap to check and easy to break silently: an unaligned column or
    /// a missing fence only shows up once someone has already pasted it into the group chat.
    /// </summary>
    private static bool RunAsciiTableScenario()
    {
        string table = AsciiTable.Render(
            new[] { "Name", "Damage" },
            new List<IReadOnlyList<string>>
            {
                new[] { "Alhamdulilah", "2.352.666" },
                new[] { "kyuubi", "56.828" },
            },
            new[] { false, true });

        var lines = table.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();

        bool fenced = lines[0] == "```" && lines[^1] == "```";
        bool hasRule = lines.Count > 2 && lines[2].StartsWith("---");

        // Every body line has to start at the same column, which is the whole point of the format.
        var body = lines.Skip(1).Take(lines.Count - 2).ToList();
        bool aligned = body.Select(l => l.IndexOf(' ')).Distinct().Count() >= 1
            && body.All(l => l.Length >= "Alhamdulilah".Length);

        // Right-aligned numbers end at the same column; left-aligned names start at the same one.
        bool numbersRightAligned = body[0].EndsWith("Damage") && body[^1].EndsWith("56.828");

        bool emptyStaysEmpty = AsciiTable.Render(new[] { "A" }, new List<IReadOnlyList<string>>(), new[] { false }).Length == 0;

        Console.WriteLine("[selftest] Discord ASCII table:");
        Console.WriteLine($"  -> wrapped in a code fence: {fenced}");
        Console.WriteLine($"  -> header rule present: {hasRule}");
        Console.WriteLine($"  -> columns padded to a common width: {aligned}");
        Console.WriteLine($"  -> numeric column right-aligned: {numbersRightAligned}");
        Console.WriteLine($"  -> no rows produces no empty frame: {emptyStaysEmpty}");

        return fenced && hasRule && aligned && numbersRightAligned && emptyStaysEmpty;
    }

    /// <summary>
    /// Friend/foe separation, built from the shape of a real Terath Dredgion fight: two six-player
    /// teams, a healer on each side, and damage only ever crossing between them.
    ///
    /// The last case is the one that matters. It reproduces what a second Aion client writing into
    /// the same Chat.log actually did on 2026-09-08: both clients were in the same Dredgion on
    /// OPPOSITE sides, and both narrate their own character as "You", so "You" healed members of
    /// both teams. Following those edges merged all twelve players into one team where everyone was
    /// everyone's ally. FactionResolver ignores heals involving "You" for exactly this reason, and
    /// this check fails if that ever gets "simplified" back in.
    /// </summary>
    private static bool RunFactionResolverScenario()
    {
        const int you = 1, ourHealer = 2, ourDps = 3, theirHealer = 4, theirDps = 5, mob = 6;
        var names = new Dictionary<int, string>
        {
            [you] = "You", [ourHealer] = "Askila", [ourDps] = "Kisame",
            [theirHealer] = "Mortelle", [theirDps] = "Neodein", [mob] = "Terath Vanquisher",
        };
        var start = new DateTime(2026, 9, 8, 20, 0, 0, DateTimeKind.Utc);

        var events = new List<DamageEvent>
        {
            new(start, ourHealer, ourDps, 500, IsHeal: true),          // third person: proof of alliance
            new(start.AddSeconds(1), theirHealer, theirDps, 500, IsHeal: true),
            new(start.AddSeconds(2), ourDps, theirDps, 900, IsHeal: false),
            new(start.AddSeconds(3), theirDps, you, 900, IsHeal: false),

            // The poison edges: "You" (really two different people) healing both sides.
            new(start.AddSeconds(4), you, ourDps, 300, IsHeal: true),
            new(start.AddSeconds(5), you, theirHealer, 300, IsHeal: true),
        };

        var sides = FactionResolver.Resolve(
            events,
            id => names.GetValueOrDefault(id),
            id => id != mob,
            you,
            new HashSet<string> { "Askila" });

        bool ownSideFound = sides.GetValueOrDefault(ourHealer) == Side.Own
            && sides.GetValueOrDefault(ourDps) == Side.Own
            && sides.GetValueOrDefault(you) == Side.Own;
        bool enemiesFound = sides.GetValueOrDefault(theirDps) == Side.Enemy;
        bool enemyHealerNotAdopted = sides.GetValueOrDefault(theirHealer) != Side.Own;

        Console.WriteLine("[selftest] Friend/foe separation (Dredgion-shaped, two clients on one log):");
        Console.WriteLine($"  -> own side identified from a third-person heal + anchor: {ownSideFound}");
        Console.WriteLine($"  -> a player who fought us is marked enemy: {enemiesFound}");
        Console.WriteLine($"  -> \"You\" healing the enemy healer does NOT make them an ally: {enemyHealerNotAdopted}");

        // PvE: no enemy players at all, and a group member who neither heals nor is healed. Taken
        // from a real Sauro Supply Base run, where the local player's own name appeared as its own
        // row (narrated in third person by the second client) with no heal edge to anything. An
        // earlier version only considered players that had a heal edge, so that row -- provably in
        // the group, it is in the loot lines -- stayed unclassified and showed no emblem.
        const int lootOnly = 7;
        var pveNames = new Dictionary<int, string>(names) { [lootOnly] = "Alhamdulilah" };
        var pveEvents = new List<DamageEvent>
        {
            new(start, ourHealer, ourDps, 500, IsHeal: true),
            new(start.AddSeconds(1), ourDps, mob, 900, IsHeal: false),
            new(start.AddSeconds(2), lootOnly, mob, 900, IsHeal: false),
        };

        var pveSides = FactionResolver.Resolve(
            pveEvents,
            id => pveNames.GetValueOrDefault(id),
            id => id != mob,
            you,
            new HashSet<string> { "Askila", "Alhamdulilah" });

        bool healerlessMemberIsOwn = pveSides.GetValueOrDefault(lootOnly) == Side.Own;
        bool noEnemiesInPve = !pveSides.Values.Contains(Side.Enemy);

        Console.WriteLine($"  -> a group member with no heal edge is still our side: {healerlessMemberIsOwn}");
        Console.WriteLine($"  -> a PvE run produces no enemies at all: {noEnemiesInPve}");

        return ownSideFound && enemiesFound && enemyHealerNotAdopted
            && healerlessMemberIsOwn && noEnemiesInPve;
    }

    /// <summary>
    /// Version parsing for the update check. Cheap to test and easy to get subtly wrong, and the
    /// failure mode is invisible: a build that quietly believes it is newer than every release
    /// never offers an update again, and nobody notices until someone asks why they are still on
    /// an old version.
    ///
    /// The revision case is the real trap. The assembly reports "0.5.1.0" while the release it was
    /// built from is tagged "v0.5.1" -- compared as-is, System.Version says 0.5.1.0 &gt; 0.5.1 and
    /// every installed copy would consider itself ahead of the release forever.
    /// </summary>
    private static bool RunAppVersionScenario()
    {
        Console.WriteLine("[selftest] Update version comparison:");
        Console.WriteLine($"  running build reports \"{AppVersion.Text}\" -> {AppVersion.Current}");

        bool tagPrefixStripped = AppVersion.Parse("v0.5.1") == new Version(0, 5, 1)
            && AppVersion.Parse("0.5.1") == new Version(0, 5, 1);
        bool prereleaseSuffixDropped = AppVersion.Parse("v1.0.0-beta.2") == new Version(1, 0, 0);
        bool revisionIgnored = AppVersion.Parse("0.5.1.0") == AppVersion.Parse("v0.5.1");
        bool shortFormPadded = AppVersion.Parse("v2.0") == new Version(2, 0, 0);
        bool garbageIsNotNewer = AppVersion.Parse("nightly") == new Version(0, 0, 0);
        bool ordersCorrectly = AppVersion.Parse("v0.5.2") > AppVersion.Parse("v0.5.1")
            && AppVersion.Parse("v0.10.0") > AppVersion.Parse("v0.9.9");

        // The running build must know its own version. This started out as a throwaway line in the
        // output above and immediately failed: Current was declared before Text, so it parsed a
        // null and every build reported 0.0.0 -- meaning every release looks newer and the update
        // notice never goes away. Asserted, not just printed, so the ordering cannot silently
        // regress.
        bool knowsItsOwnVersion = AppVersion.Current > new Version(0, 0, 0)
            && AppVersion.Current == AppVersion.Parse(AppVersion.Text);

        Console.WriteLine($"  -> \"v\" prefix stripped: {tagPrefixStripped}");
        Console.WriteLine($"  -> prerelease suffix dropped: {prereleaseSuffixDropped}");
        Console.WriteLine($"  -> assembly's trailing .0 revision does not read as newer: {revisionIgnored}");
        Console.WriteLine($"  -> two-part tag padded to three: {shortFormPadded}");
        Console.WriteLine($"  -> unparsable tag cannot outrank a real one: {garbageIsNotNewer}");
        Console.WriteLine($"  -> newer releases compare greater (incl. 10 > 9): {ordersCorrectly}");
        Console.WriteLine($"  -> the running build knows its own version: {knowsItsOwnVersion}");

        return tagPrefixStripped && prereleaseSuffixDropped && revisionIgnored
            && shortFormPadded && garbageIsNotNewer && ordersCorrectly && knowsItsOwnVersion;
    }

    /// <summary>
    /// Item table sanity, with Origin Codex as the source of truth. The three checks are the ones
    /// that would have caught what was actually wrong: a real Sauro run dropped two items no
    /// retail 4.x dump contains (they showed as bare "Item #186000936"), and the tier a Sauren Bow
    /// was labelled with did not match what the server calls it -- the grid said "Legendary" for
    /// an item OriginAion calls EPIC, and the Discord table shared that word onward.
    /// </summary>
    private static bool RunItemDatabaseScenario()
    {
        var table = ItemDatabase.Load();

        Console.WriteLine("[selftest] ItemDatabase load (assets/items/items_origincdx_4x.json):");
        Console.WriteLine($"  loaded {table.Count} items");

        bool loadedSomething = table.Count > 80_000; // expect ~91.7k; loose bound for a rescrape

        // Server-specific items: present in Origin Codex, absent from every retail 4.x dump.
        bool originOnlyItemsResolve =
            ItemDatabase.DisplayName(186_000_936) == "Cosmic Fragment" &&
            ItemDatabase.DisplayName(186_000_938) == "Eternity Comet";

        // One real drop per tier from that same run, checked against the name the server uses.
        bool tiersNamedLikeTheServer =
            ItemDatabase.GradeOf(112_101_454) == ItemGrade.Mythic &&   // Sauro Commander's Pauldrons
            ItemDatabase.GradeOf(101_701_258) == ItemGrade.Epic &&     // Sauren Bow
            ItemDatabase.GradeOf(111_601_448) == ItemGrade.Unique &&   // Sauro Guard's Gauntlets
            ItemDatabase.GradeOf(188_052_576) == ItemGrade.Legend &&   // Veteran's Composite Manastone Bundle
            ItemDatabase.GradeOf(186_000_238) == ItemGrade.Rare;       // Conqueror's Mark

        bool unknownIdStaysNull = ItemDatabase.GradeOf(999_999_999) is null;

        Console.WriteLine($"  -> table loaded with a plausible size: {loadedSomething}");
        Console.WriteLine($"  -> OriginAion-only items resolve to a name: {originOnlyItemsResolve}");
        Console.WriteLine($"  -> real Sauro drops carry the server's own tier names: {tiersNamedLikeTheServer}");
        Console.WriteLine($"  -> unresolved id reports null rather than guessing Common: {unknownIdStaysNull}");

        return loadedSomething && originOnlyItemsResolve && tiersNamedLikeTheServer && unknownIdStaysNull;
    }

    /// <summary>
    /// Verifies SkillDatabase actually finds and parses assets/skills/skills_en_4x.json at
    /// runtime -- this exercises the real deployment path (AppContext.BaseDirectory + the
    /// csproj's CopyToOutputDirectory setting for assets/), not just the JSON parsing in
    /// isolation. Skill 1 ("Basic Sword Training") is the first row in that file.
    /// </summary>
    private static bool RunSkillDatabaseScenario()
    {
        var table = SkillDatabase.Load();
        string knownName = SkillDatabase.DisplayName(1);
        string unknownName = SkillDatabase.DisplayName(999_999_999);

        Console.WriteLine("[selftest] SkillDatabase load (assets/skills/skills_en_4x.json):");
        Console.WriteLine($"  loaded {table.Count} skills; skill 1 = \"{knownName}\"; unknown id -> \"{unknownName}\"");

        bool loadedSomething = table.Count > 500; // expect ~974; loose bound so minor rescrapes don't break this
        bool knownResolved = knownName == "Basic Sword Training";
        bool unknownFallsBack = unknownName.Contains("unknown");

        Console.WriteLine($"  -> table loaded with a plausible size: {loadedSomething}");
        Console.WriteLine($"  -> skill 1 resolved to its real name: {knownResolved}");
        Console.WriteLine($"  -> unknown id falls back cleanly instead of throwing: {unknownFallsBack}");

        if (!loadedSomething || !knownResolved)
        {
            Console.WriteLine($"  (looked for assets at: {Path.Combine(AppContext.BaseDirectory, "assets", "skills", "skills_en_4x.json")})");
        }

        return loadedSomething && knownResolved && unknownFallsBack;
    }

    /// <summary>
    /// Verifies the LiveAggregator end to end at the object level: two attackers hitting the same
    /// boss, checking the per-source totals AND the rendered DPS column -- specifically that a
    /// source with only one attributed hit (the normal state of the first line of output on every
    /// real run) shows "n/a" rather than its damage total dressed up as a rate. Events are built
    /// by hand rather than parsed, so this stays a test of the aggregator alone.
    /// </summary>
    private static bool RunLiveAggregatorScenario()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var aggregator = new LiveAggregator();

        aggregator.IngestEvents(new[]
        {
            new DamageEvent(start, GladiatorId, BossId, 1000, IsHeal: false),
            new DamageEvent(start, GladiatorId, BossId, 1200, IsHeal: false),
            new DamageEvent(start.AddSeconds(2), GladiatorId, BossId, 1100, IsHeal: false),
            new DamageEvent(start.AddSeconds(1), ZaubererId, BossId, 50_000, IsHeal: false),
        });

        Console.WriteLine("[selftest] LiveAggregator wiring (hand-built DamageEvents):");
        Console.WriteLine($"  {aggregator.Summarize()}");

        int gladiatorEvents = aggregator.Events.Count(e => e.SourceObjectId == GladiatorId);
        long gladiatorTotal = aggregator.Events.Where(e => e.SourceObjectId == GladiatorId).Sum(e => e.Amount);
        long zaubererTotal = aggregator.Events.Where(e => e.SourceObjectId == ZaubererId).Sum(e => e.Amount);

        bool gladiatorEventCountOk = gladiatorEvents == 3; // two at t=0, one two seconds later
        bool gladiatorTotalOk = gladiatorTotal == 1000 + 1200 + 1100;
        bool zaubererTotalOk = zaubererTotal == 50_000;

        // Caught by review: zauberer has exactly one attributed hit here, which is the normal
        // state of the very first line of live output on every real capture run, not an edge
        // case. Summarize() must show "n/a" for a DPS rate that has no time span to be computed
        // from -- not the raw 50,000 damage total masquerading as "50000 DPS", which is the exact
        // bug already fixed once for AllDpsActiveOnly and had quietly regressed via
        // AllDpsWallClock. Assert on the actual rendered string, not just the underlying totals:
        // that's what let the bug hide behind a passing test the first time.
        string summary = aggregator.Summarize();
        bool zaubererShowsNotAvailable = summary.Contains("0x00000002: 50000 dmg (n/a DPS)");
        bool zaubererDoesNotShowTotalAsRate = !summary.Contains("(50000 DPS)");
        double? gladiatorWallDps = DpsCalculator.AllDpsWallClock(aggregator.Events, GladiatorId);
        bool gladiatorDpsIsReal = gladiatorWallDps is double d && d > 0;

        Console.WriteLine($"  -> gladiator's 3 hits all recorded: {gladiatorEventCountOk}");
        Console.WriteLine($"  -> gladiator total damage correct: {gladiatorTotalOk}");
        Console.WriteLine($"  -> zauberer total damage correct: {zaubererTotalOk}");
        Console.WriteLine($"  -> zauberer's single hit renders as \"n/a\" DPS, not a fake rate: {zaubererShowsNotAvailable && zaubererDoesNotShowTotalAsRate}");
        Console.WriteLine($"  -> gladiator (multiple hits, real time span) still gets a real DPS number: {gladiatorDpsIsReal}");

        return gladiatorEventCountOk && gladiatorTotalOk && zaubererTotalOk
            && zaubererShowsNotAvailable && zaubererDoesNotShowTotalAsRate && gladiatorDpsIsReal;
    }

    /// <summary>
    /// Verifies ChatLogParser against real lines taken verbatim from an OriginAion Chat.log
    /// (2026-08-23), plus a synthetic duplicate-with-interleaving block modeled on a pattern
    /// found in that same real file: two clients sharing one Chat.log each logged an identical
    /// broadcast line, but a combat line from the OTHER client landed physically between the two
    /// copies (see ChatLogParser remarks).
    ///
    /// This assertion was inverted deliberately: it used to require that the repeated Barracuda
    /// DAMAGE line be counted once, which is what made the parser silently drop a third of the
    /// user's own damage on real data (see ChatLogParser.Parse remarks for the measurement).
    /// Identical damage lines in one second are real repeat hits and must all count; the
    /// broadcast guard now only applies to non-combat lines.
    /// </summary>
    private static bool RunChatLogParserScenario()
    {
        var lines = new[]
        {
            "2026.08.23 21:31:08 : Ulgorn Raider inflicted 1 damage on Training Dummy. ",
            "2026.08.23 21:31:09 : Ulgorn Raider inflicted 1 damage on Training Dummy. ",
            "2026.08.23 21:31:10 : Training Dummy evaded Ulgorn Raider's attack. ",
            "2026.08.23 21:32:07 : You inflicted 1 damage on Training Dummy. ",
            "2026.08.23 21:32:17 : You are too far from the target to use that skill. ",
            "2026.08.23 21:32:04 : Sniggy has logged in. ",
            "2026.08.23 21:31:08 : [3.LFG] [charname:Shera;1.0000 0.6941 0.6941]: [cmd:Shera;ZauPvKJjMpNiscgHo48bYhTuRUkaUzjxBls+lJtRs9s=]HOLD GROUP ",
            // Duplicate-with-interleaving, modeled on real lines 96/97/98 of that capture.
            "2026.08.23 21:32:16 : Barracuda inflicted 500 damage on Training Dummy. ",
            "2026.08.23 21:32:16 : Ulgorn Raider inflicted 1 damage on Training Dummy. ",
            "2026.08.23 21:32:16 : Barracuda inflicted 500 damage on Training Dummy. ",
        };

        var parser = new ChatLogParser();
        var events = parser.Parse(lines);

        Console.WriteLine("[selftest] ChatLogParser (real OriginAion Chat.log lines + synthetic dedup case):");
        Console.WriteLine($"  parsed {events.Count} damage events from {lines.Length} lines");

        // 3 Ulgorn Raider hits + 1 "You" hit + BOTH Barracuda hits: two identical damage lines in
        // one second are two real hits, even with an unrelated line between them (that is exactly
        // how a fast weapon reads in a log with one-second resolution).
        bool countOk = events.Count == 6;
        int ulgornHitCount = events.Count(e => parser.Names.NameFor(e.SourceObjectId) == "Ulgorn Raider");
        bool ulgornCountOk = ulgornHitCount == 3;
        bool evadeSkipped = !events.Any(e => parser.Names.NameFor(e.SourceObjectId) == "Training Dummy");
        bool youResolved = events.Any(e => parser.Names.NameFor(e.SourceObjectId) == "You" && e.Amount == 1);
        int ulgornId = parser.Names.GetOrAssignId("Ulgorn Raider");
        int youId = parser.Names.GetOrAssignId("You");
        bool stableDistinctIds = ulgornId != youId && ulgornId == parser.Names.GetOrAssignId("Ulgorn Raider");
        long barracudaTotal = events
            .Where(e => parser.Names.NameFor(e.SourceObjectId) == "Barracuda")
            .Sum(e => e.Amount);
        bool repeatedDamageLineCountedTwice = barracudaTotal == 1000; // both hits, not deduped to 500

        Console.WriteLine($"  -> event count correct (repeat hits kept, non-combat dedup unaffected): {countOk}");
        Console.WriteLine($"  -> non-damage lines (evade/too-far/login/LFG) produced no events: {evadeSkipped}");
        Console.WriteLine($"  -> \"You\" resolved to a real damage event: {youResolved}");
        Console.WriteLine($"  -> name registry gives stable, distinct ids: {stableDistinctIds}");
        Console.WriteLine($"  -> the interleaved Ulgorn Raider hit still counted (all 3 present, not just 2): {ulgornCountOk}");
        Console.WriteLine($"  -> identical damage line repeated in the same second counted twice: {repeatedDamageLineCountedTwice}");

        return countOk && ulgornCountOk && evadeSkipped && youResolved && stableDistinctIds && repeatedDamageLineCountedTwice;
    }

    /// <summary>
    /// Verifies ChatLogParser against real lines taken verbatim from a ~44k-line OriginAion play
    /// session (2026-08-24) -- a much richer sample than RunChatLogParserScenario's small,
    /// hand-picked one. This is what surfaced the "." thousands-separator number format, every
    /// heal phrasing, incoming damage, and the reflected-damage-to-self line whose naive parse
    /// split the local player's identity into "You" and "you" (see ChatLogParser's remarks) --
    /// found by running the regexes against the full file and cross-checking the resulting
    /// distinct-name list, not by reasoning about any single line in isolation. This test locks
    /// in exactly that finding so it can't silently regress.
    /// </summary>
    private static bool RunChatLogRealWorldPatternsScenario()
    {
        var lines = new[]
        {
            "2026.08.24 22:08:37 : Critical Hit!You inflicted 1.911 damage on Icy Kalgolem by using Force Blast I. ",
            "2026.08.24 22:08:38 : Critical Hit! You inflicted 1.022 critical damage on Icy Kalgolem. ",
            "2026.08.24 22:08:39 : Icy Kalgolem has inflicted 994 damage on you by using Power Attack. ",
            "2026.08.24 22:08:40 : You received 172 damage from Icy Kalgolem. ",
            // Found by terminal_windows against a SECOND, larger real Chat.log after this test's
            // first version shipped without these two lines: the "Critical Hit!" prefix is real on
            // both incoming-damage patterns too, not just the outgoing one covered above. Missed
            // initially because the first validation sample happened not to include an incoming
            // crit -- exactly the kind of gap only a bigger real sample exposes.
            "2026.08.24 22:08:39 : Critical Hit!Sparky has inflicted 999 damage on you by using Wing Buffet. ",
            "2026.08.24 22:08:40 : Critical Hit!You received 888 damage from Sparky. ",
            // A hit that applies a rider on landing says "damage and the <X> effect on" instead of
            // the plain "damage on" -- verbatim shape from a real Sauro Supply Base run, where the
            // group's Assassin lost 427.217 damage (27% of his total, 1014 lines) to this because
            // DamagePattern required the literal "damage on" and dropped every Rune Carve hit.
            "2026.08.24 22:08:40 : Vanquisher inflicted 568 damage and the rune carve effect on Icy Kalgolem by using Rune Carve V. ",
            "2026.08.24 22:08:41 : Your attack on Torch Spirit Iprita was reflected and inflicted 246 damage on you. ",
            "2026.08.24 22:08:42 : You restored 95 of Hestika's HP by using Major Recovery Potion. ",
            "2026.08.24 22:08:43 : Mortelle recovered 156 HP by using Healing Light I. ",
            "2026.08.24 22:08:44 : Thai recovered 195 HP because Inss used Healing Light I. ",
            "2026.08.24 22:08:45 : You recovered 157 HP because Gsghost used Healing Light VI on you. ",
            // Deliberately unattributed DoT-tick lines (see ChatLogParser's "Known, deliberate
            // gaps") -- must NOT produce events, since neither names a real source character.
            "2026.08.24 22:08:46 : Drakan Crewhand received 2.649 damage due to the effect of Spray Drana Acid. ",
            "2026.08.24 22:08:47 : You receive 56 damage due to Fire Strike. ",
        };

        var parser = new ChatLogParser();
        var events = parser.Parse(lines);
        string? NameOf(int id) => parser.Names.NameFor(id);

        // Found by terminal_windows against the same second, larger real session: LiveAggregator
        // (and, separately, MainWindow's row-building code) summed ALL events including heals into
        // the damage total, while the DPS *rate* next to it already filtered heals out via
        // DpsCalculator -- so a pure healer rendered as e.g. "Potion: 3526 dmg (n/a DPS)", a
        // damage number with no matching rate, because the total and the rate were silently
        // computed over two different event sets. Wiring a real LiveAggregator here, with this
        // heal-bearing event set, is what actually exercises that consumer-side bug -- the parser
        // itself was never at fault (IsHeal was always set correctly), so a parser-only test
        // couldn't have caught this.
        var aggregator = new LiveAggregator();
        aggregator.IngestEvents(events);
        string summary = aggregator.Summarize(NameOf);
        bool healersExcludedFromDamageSummary =
            !summary.Contains("Hestika") && !summary.Contains("Mortelle") &&
            !summary.Contains("Inss") && !summary.Contains("Gsghost") && !summary.Contains("Thai");

        Console.WriteLine("[selftest] ChatLogParser real-world patterns (crit/skill/incoming/reflect/heal variants):");
        Console.WriteLine($"  parsed {events.Count} events from {lines.Length} lines");

        bool critWithSkillOk = events.Any(e => !e.IsHeal && e.Amount == 1911
            && NameOf(e.SourceObjectId) == "You" && NameOf(e.TargetObjectId) == "Icy Kalgolem");
        bool critWordOk = events.Any(e => !e.IsHeal && e.Amount == 1022
            && NameOf(e.SourceObjectId) == "You" && NameOf(e.TargetObjectId) == "Icy Kalgolem");
        bool incomingSkillOk = events.Any(e => !e.IsHeal && e.Amount == 994
            && NameOf(e.SourceObjectId) == "Icy Kalgolem" && NameOf(e.TargetObjectId) == "You");
        bool incomingBasicOk = events.Any(e => !e.IsHeal && e.Amount == 172
            && NameOf(e.SourceObjectId) == "Icy Kalgolem" && NameOf(e.TargetObjectId) == "You");
        bool critIncomingSkillOk = events.Any(e => !e.IsHeal && e.Amount == 999
            && NameOf(e.SourceObjectId) == "Sparky" && NameOf(e.TargetObjectId) == "You");
        bool critIncomingBasicOk = events.Any(e => !e.IsHeal && e.Amount == 888
            && NameOf(e.SourceObjectId) == "Sparky" && NameOf(e.TargetObjectId) == "You");
        bool runeCarveOk = events.Any(e => !e.IsHeal && e.Amount == 568
            && NameOf(e.SourceObjectId) == "Vanquisher" && NameOf(e.TargetObjectId) == "Icy Kalgolem");
        bool reflectOk = events.Any(e => !e.IsHeal && e.Amount == 246
            && NameOf(e.SourceObjectId) == "Torch Spirit Iprita" && NameOf(e.TargetObjectId) == "You");
        bool healOtherOk = events.Any(e => e.IsHeal && e.Amount == 95
            && NameOf(e.SourceObjectId) == "You" && NameOf(e.TargetObjectId) == "Hestika");
        bool healSelfOtherCharacterOk = events.Any(e => e.IsHeal && e.Amount == 156
            && NameOf(e.SourceObjectId) == "Mortelle" && NameOf(e.TargetObjectId) == "Mortelle");
        bool healByOtherThirdPersonOk = events.Any(e => e.IsHeal && e.Amount == 195
            && NameOf(e.SourceObjectId) == "Inss" && NameOf(e.TargetObjectId) == "Thai");
        bool healByOtherOnYouOk = events.Any(e => e.IsHeal && e.Amount == 157
            && NameOf(e.SourceObjectId) == "Gsghost" && NameOf(e.TargetObjectId) == "You");

        // The actual bug this test was written to lock in: every "You" reference above (attacker
        // in some events, target in others, via three differently-phrased "on you" patterns) must
        // resolve to the SAME id -- not a second "you"/"You" split from case differences in the raw
        // log text, which is exactly what happened before ReflectedDamagePattern existed.
        int youId = parser.Names.GetOrAssignId("You");
        bool noIdentitySplit = !events.Any(e => e.SourceObjectId != youId && NameOf(e.SourceObjectId) == "You")
            && !events.Any(e => e.TargetObjectId != youId && NameOf(e.TargetObjectId) == "You")
            && parser.Names.NameFor(youId) == "You";

        // Direct check for the exact failure mode terminal_windows found: an unstripped "Critical
        // Hit!" prefix leaking into a captured name (either side, either incoming pattern). Checked
        // separately from noIdentitySplit above because a regression here wouldn't necessarily
        // produce a string equal to "You" -- e.g. "Critical Hit!You" is a different string, so it
        // wouldn't trip that check, but it's exactly as wrong.
        bool noCriticalHitLeak = !events.Any(e =>
            (NameOf(e.SourceObjectId) ?? "").Contains("Critical Hit") || (NameOf(e.TargetObjectId) ?? "").Contains("Critical Hit"));

        bool dotLinesProducedNoEvents = events.Count == 12; // the 2 unattributed DoT lines above must not add events

        Console.WriteLine($"  -> crit + skill, grouped number 1.911 -> 1911: {critWithSkillOk}");
        Console.WriteLine($"  -> crit, \"critical damage\" wording, grouped number 1.022 -> 1022: {critWordOk}");
        Console.WriteLine($"  -> incoming skill damage (has inflicted...on you): {incomingSkillOk}");
        Console.WriteLine($"  -> incoming basic damage (received...from): {incomingBasicOk}");
        Console.WriteLine($"  -> incoming CRIT skill damage (has inflicted...on you), prefix stripped: {critIncomingSkillOk}");
        Console.WriteLine($"  -> incoming CRIT basic damage (received...from), prefix stripped: {critIncomingBasicOk}");
        Console.WriteLine($"  -> reflected damage, correctly attributed to the mob, not \"Your attack...\": {reflectOk}");
        Console.WriteLine($"  -> hit carrying a rider effect (\"damage and the rune carve effect on\") counted: {runeCarveOk}");
        Console.WriteLine($"  -> heal-other (You restored...of X's HP): {healOtherOk}");
        Console.WriteLine($"  -> self-heal for a NAMED character, not just You: {healSelfOtherCharacterOk}");
        Console.WriteLine($"  -> heal-by-other, third person: {healByOtherThirdPersonOk}");
        Console.WriteLine($"  -> heal-by-other, first person with \"on you\": {healByOtherOnYouOk}");
        Console.WriteLine($"  -> no You/you identity split across any of the above: {noIdentitySplit}");
        Console.WriteLine($"  -> no \"Critical Hit!\" prefix leaked into any captured name: {noCriticalHitLeak}");
        Console.WriteLine($"  -> unattributed DoT-tick lines correctly produced no events: {dotLinesProducedNoEvents}");
        Console.WriteLine($"  -> LiveAggregator.Summarize excludes pure healers from the damage list: {healersExcludedFromDamageSummary}");
        Console.WriteLine($"    {summary}");

        return critWithSkillOk && critWordOk && incomingSkillOk && incomingBasicOk
            && critIncomingSkillOk && critIncomingBasicOk && reflectOk && runeCarveOk
            && healOtherOk && healSelfOtherCharacterOk && healByOtherThirdPersonOk && healByOtherOnYouOk
            && noIdentitySplit && noCriticalHitLeak && dotLinesProducedNoEvents && healersExcludedFromDamageSummary;
    }
}
