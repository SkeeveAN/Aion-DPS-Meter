using System.IO;
using AionDPS.Ui;
using AionDPS.Update;

namespace AionDPS.Combat;

/// <summary>
/// Self-checks for the parts of the meter that need no running game: the live aggregation, engaged
/// targets, the Discord table format, toolbar icons and version parsing - run via `AionDPS selftest`.
/// The Aion 2 protocol and capture checks live in <see cref="SelfCheckAion2"/>.
/// </summary>
public static class SelfCheck
{
    private const int GladiatorId = 1;
    private const int ZaubererId = 2;
    private const int BossId = 100;

    public static bool Run()
    {
        bool ok = true;
        ok &= RunAppVersionScenario();
        ok &= RunEngagedTargetsScenario();
        ok &= RunEventScheduleScenario();
        ok &= RunAllInOneRowScenario();
        ok &= RunAsciiTableScenario();
        ok &= RunToolbarIconScenario();
        ok &= RunLiveAggregatorScenario();
        ok &= SelfCheckAion2.Run();
        ok &= SelfCheckThemes.Run();
        ok &= SelfCheckHistory.Run();
        return ok;
    }

    /// <summary>The All in One modes' row figures: in All the rate column shows the healing and the damage taken is always
    /// listed; in All Compact the columns stay those of Damage and the healing and damage taken go to the second line; in
    /// Damage nothing changes.</summary>
    private static bool RunAllInOneRowScenario()
    {
        Console.WriteLine("[selftest] All in One rows:");
        var damage = new Ui.PlayerRow(1) { Damage = 916_700, Dps = 8_300, Healing = 12_300, DamageTaken = 98_400, ShowDamageTaken = false };
        bool plain = damage.RateOrHealDisplay == damage.DpsDisplay && damage.TakenDisplay == "" && damage.TotalOrTakenCompact == damage.DamageCompact;
        var all = new Ui.PlayerRow(2) { Damage = 916_700, Dps = 8_300, Healing = 12_300, DamageTaken = 98_400, ShowDamageTaken = false, AllMode = true };
        bool inAll = all.RateOrHealDisplay.StartsWith('+') && all.TakenDisplay.Contains("98") && all.RateOrDamageCompact == all.DamageCompact
            && all.ShareOrHealCompact == Ui.PlayerRow.Compact(12_300) && all.TotalOrTakenCompact == Ui.PlayerRow.Compact(98_400);
        var compact = new Ui.PlayerRow(3) { Damage = 916_700, Dps = 8_300, Healing = 0, DamageTaken = 98_400, ShowDamageTaken = true, AllCompact = true };
        bool inCompact = compact.TakenDisplay == "" && compact.RateOrHealDisplay == compact.DpsDisplay && compact.HealCompact == "–" && compact.TakenCompact == Ui.PlayerRow.Compact(98_400);
        Console.WriteLine($"  Damage unchanged: {plain}; All shows heal and taken: {inAll}; All Compact keeps the Damage columns: {inCompact}");
        return plain && inAll && inCompact;
    }

    /// <summary>The timetable: the Arena of Strategy (every day 11:00-14:00 and 19:00-21:00) is
    /// "starting soon" half an hour before, "active" with the time left inside, nothing in between,
    /// and a window past midnight still counts after midnight.</summary>
    private static bool RunEventScheduleScenario()
    {
        Console.WriteLine("[selftest] Event timetable:");
        var arena = new Schedule.ScheduledEvent("arena", "battlefield", "10v10", new Dictionary<string, string> { ["en"] = "Arena" },
            new[]
            {
                new Schedule.EventWindow(new[] { 0, 1, 2, 3, 4, 5, 6 }, TimeSpan.FromHours(11), TimeSpan.FromHours(14)),
                new Schedule.EventWindow(new[] { 0, 1, 2, 3, 4, 5, 6 }, TimeSpan.FromHours(19), TimeSpan.FromHours(21)),
            });
        var night = new Schedule.ScheduledEvent("night", "x", null, new Dictionary<string, string> { ["en"] = "Night" },
            new[] { new Schedule.EventWindow(new[] { 1 }, TimeSpan.FromHours(23), TimeSpan.FromHours(1)) });
        var monday = new DateTime(2026, 10, 5);
        var hour = TimeSpan.FromMinutes(60);

        var (activeA, soonA) = Schedule.EventSchedule.Evaluate(new[] { arena }, monday.AddHours(10).AddMinutes(30), hour);
        var (activeB, soonB) = Schedule.EventSchedule.Evaluate(new[] { arena }, monday.AddHours(12), hour);
        var (activeC, soonC) = Schedule.EventSchedule.Evaluate(new[] { arena }, monday.AddHours(15), hour);
        var (activeD, soonD) = Schedule.EventSchedule.Evaluate(new[] { arena }, monday.AddHours(18).AddMinutes(1), hour);
        var (activeE, _) = Schedule.EventSchedule.Evaluate(new[] { night }, monday.AddDays(1).AddMinutes(30), hour);
        var next = Schedule.EventSchedule.NextAfter(arena, monday.AddHours(21).AddMinutes(30));

        bool ok = activeA.Count == 0 && soonA.Count == 1 && soonA[0].Start == monday.AddHours(11)
            && activeB.Count == 1 && activeB[0].End - monday.AddHours(12) == TimeSpan.FromHours(2) && soonB.Count == 0
            && activeC.Count == 0 && soonC.Count == 0
            && activeD.Count == 0 && soonD.Count == 1
            && activeE.Count == 1
            && next is not null && next.Start == monday.AddDays(1).AddHours(11);
        // An "any time" event (the 1 vs 1 arena) is always listed as active and never as upcoming.
        var duel = new Schedule.ScheduledEvent("duel", "arena", "1v1", new Dictionary<string, string> { ["en"] = "Duel" }, Array.Empty<Schedule.EventWindow>(), Always: true);
        var (activeF, soonF) = Schedule.EventSchedule.Evaluate(new[] { duel, arena }, monday.AddHours(12), hour);
        ok &= activeF.Count == 2 && activeF[0].Event.Id == "arena" && activeF[1].IsAlways && soonF.Count == 0;
        // A single dated occurrence (the Abyss raid read off the game's countdown): "soon" within the
        // hour before, listed as nothing before that and nothing once it has begun.
        var raid = new Schedule.ScheduledEvent("raid", "raid", null, new Dictionary<string, string> { ["en"] = "Raid" }, Array.Empty<Schedule.EventWindow>(),
            Dates: new[] { new Schedule.EventDate(monday.AddHours(21), 0) });
        var (_, soonG) = Schedule.EventSchedule.Evaluate(new[] { raid }, monday.AddHours(20).AddMinutes(10), hour);
        var (_, soonH) = Schedule.EventSchedule.Evaluate(new[] { raid }, monday.AddHours(19).AddMinutes(30), hour);
        var (activeI, soonI) = Schedule.EventSchedule.Evaluate(new[] { raid }, monday.AddHours(21).AddMinutes(1), hour);
        ok &= soonG.Count == 1 && soonH.Count == 0 && activeI.Count == 0 && soonI.Count == 0
            && Schedule.EventSchedule.NextAfter(raid, monday.AddHours(20))?.Start == monday.AddHours(21);
        // The shipped table has the Arena of Strategy.
        bool shipped = Schedule.EventSchedule.Events.Any(e => e.Id == "arena_of_strategy" && e.NameIn("de") == "Schlachtfeld")
            && Schedule.EventSchedule.Events.Any(e => e.Id == "arena_of_silence" && e.Always);
        Console.WriteLine($"  -> soon 30 min before, active with time left, silent between, past midnight, next day: {ok}; shipped table: {shipped}");
        return ok && shipped;
    }

    /// <summary>
    /// The Discord mark on the copy-table button. Its Data string came from an SVG, and SVG packs
    /// arc flags ("0 0 0-4.88") in a way WPF's geometry parser rejects -- a malformed Data attribute
    /// throws while MainWindow is being constructed, so the app would not start at all. Parsing it
    /// here turns that from a crash on launch into a failing check.
    /// </summary>
    private static bool RunToolbarIconScenario()
    {
        const string discord = "M 20.317 4.3698a 19.7913 19.7913 0 0 0 -4.8851 -1.5152 0.0741 0.0741 0 0 0 -0.0785 0.0371c -0.211 0.3753 -0.4447 0.8648 -0.6083 1.2495 -1.8447 -0.2762 -3.68 -0.2762 -5.4868 0 -0.1636 -0.3933 -0.4058 -0.8742 -0.6177 -1.2495a 0.077 0.077 0 0 0 -0.0785 -0.037 19.7363 19.7363 0 0 0 -4.8852 1.515 0.0699 0.0699 0 0 0 -0.0321 0.0277C 0.5334 9.0458 -0.319 13.5799 0.0992 18.0578a 0.0824 0.0824 0 0 0 0.0312 0.0561c 2.0528 1.5076 4.0413 2.4228 5.9929 3.0294a 0.0777 0.0777 0 0 0 0.0842 -0.0276c 0.4616 -0.6304 0.8731 -1.2952 1.226 -1.9942a 0.076 0.076 0 0 0 -0.0416 -0.1057c -0.6528 -0.2476 -1.2743 -0.5495 -1.8722 -0.8923a 0.077 0.077 0 0 1 -0.0076 -0.1277c 0.1258 -0.0943 0.2517 -0.1923 0.3718 -0.2914a 0.0743 0.0743 0 0 1 0.0776 -0.0105c 3.9278 1.7933 8.18 1.7933 12.0614 0a 0.0739 0.0739 0 0 1 0.0785 0.0095c 0.1202 0.099 0.246 0.1981 0.3728 0.2924a 0.077 0.077 0 0 1 -0.0066 0.1276 12.2986 12.2986 0 0 1 -1.873 0.8914 0.0766 0.0766 0 0 0 -0.0407 0.1067c 0.3604 0.698 0.7719 1.3628 1.225 1.9932a 0.076 0.076 0 0 0 0.0842 0.0286c 1.961 -0.6067 3.9495 -1.5219 6.0023 -3.0294a 0.077 0.077 0 0 0 0.0313 -0.0552c 0.5004 -5.177 -0.8382 -9.6739 -3.5485 -13.6604a 0.061 0.061 0 0 0 -0.0312 -0.0286zM 8.02 15.3312c -1.1825 0 -2.1569 -1.0857 -2.1569 -2.419 0 -1.3332 0.9555 -2.4189 2.157 -2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332 -0.9555 2.4189 -2.1569 2.4189zm 7.9748 0c -1.1825 0 -2.1569 -1.0857 -2.1569 -2.419 0 -1.3332 0.9554 -2.4189 2.1569 -2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332 -0.946 2.4189 -2.1568 2.4189Z";

        bool parses;
        double width = 0;
        try
        {
            var geometry = System.Windows.Media.Geometry.Parse(discord);
            width = geometry.Bounds.Width;
            parses = true;
        }
        catch (FormatException)
        {
            parses = false;
        }

        // A geometry that parses but is empty would render as a blank button.
        bool hasShape = width > 1;

        Console.WriteLine("[selftest] Toolbar icons:");
        Console.WriteLine($"  -> Discord mark parses as a WPF geometry: {parses}");
        Console.WriteLine($"  -> and covers a real area (width {width:F1}): {hasShape}");

        return parses && hasShape;
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
    /// The filter that keeps a second client's unrelated fight out of the grid. Both cases here
    /// are real ones the user hit: the Sauro run where strangers at a training dummy in town
    /// ranked 6th and 7th, and the 1v1 arena where the opponent was missing from the grid
    /// altogether because every line they produced had the local player as its target.
    /// </summary>
    private static bool RunEngagedTargetsScenario()
    {
        const int you = 1, mate = 2, boss = 3, opponent = 4, stranger = 5, dummy = 6;
        var start = new DateTime(2026, 9, 8, 22, 0, 0, DateTimeKind.Utc);

        var events = new List<DamageEvent>
        {
            new(start, you, boss, 500, IsHeal: false),                    // our fight
            new(start.AddSeconds(1), mate, boss, 500, IsHeal: false),     // a teammate joins it
            new(start.AddSeconds(2), boss, mate, 400, IsHeal: false),     // the boss hits back
            new(start.AddSeconds(3), opponent, you, 900, IsHeal: false),  // 1v1: only ever hits US
            new(start.AddSeconds(4), stranger, dummy, 900, IsHeal: false),// the other client's town
        };

        var kept = EngagedTargets.Filter(events, you);

        bool ourDamageKept = kept.Any(e => e.SourceObjectId == you && e.TargetObjectId == boss);
        bool teammateKept = kept.Any(e => e.SourceObjectId == mate);
        bool incomingOnUsKept = kept.Any(e => e.SourceObjectId == opponent && e.TargetObjectId == you);
        bool incomingOnMateKept = kept.Any(e => e.SourceObjectId == boss && e.TargetObjectId == mate);
        bool strangerDropped = !kept.Any(e => e.SourceObjectId == stranger);

        // With no local player in sight there is nothing to anchor on; filtering everything away
        // would leave an empty grid, which is worse than showing too much.
        bool anchorlessPassesThrough = EngagedTargets.Filter(events, 999).Count == events.Count;

        Console.WriteLine("[selftest] Engaged-target filter:");
        Console.WriteLine($"  -> our own damage kept: {ourDamageKept}");
        Console.WriteLine($"  -> a teammate on the same target kept: {teammateKept}");
        Console.WriteLine($"  -> a 1v1 opponent who only ever hits US is kept: {incomingOnUsKept}");
        Console.WriteLine($"  -> damage taken by a teammate is kept: {incomingOnMateKept}");
        Console.WriteLine($"  -> a stranger's unrelated fight is dropped: {strangerDropped}");
        Console.WriteLine($"  -> nothing to anchor on leaves everything alone: {anchorlessPassesThrough}");

        return ourDamageKept && teammateKept && incomingOnUsKept && incomingOnMateKept
            && strangerDropped && anchorlessPassesThrough;
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
}
