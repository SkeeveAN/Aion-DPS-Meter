using System.IO;
using AionDPS.Aion2;
using AionDPS.Aion2.Capture;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;

namespace AionDPS;

/// <summary>
/// <c>aion2-stagger-check &lt;recording.jsonl&gt; [...]</c>: plays recordings through the decoder (no window, nothing written) and reports, for every
/// boss whose stagger bar was read often enough, how much of the bar's fall <see cref="StaggerShare"/> credits to a player and how much stays unassigned.
/// </summary>
internal static class StaggerCheck
{
    private const int ServerPort = 13328;
    private static readonly (DateTime From, DateTime To) Whole = (new DateTime(2000, 1, 1), new DateTime(2100, 1, 1));

    public static void Run(string[] args)
    {
        var files = args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        if (files.Count == 0)
        {
            Console.WriteLine("Usage: AionDPS aion2-stagger-check <recording.jsonl> [more recordings]");
            return;
        }

        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        long assignedAll = 0, totalAll = 0;
        var allMisses = new List<StaggerMiss>();
        foreach (string path in files)
        {
            var source = new Aion2PacketCombatSource(Aion2Protocol.Load());
            var hits = new List<DamageEvent>();
            int segments = 0;
            foreach (TcpSegment segment in SegmentRecording.Read(path))
            {
                bool fromServer = segment.Source.EndsWith(":" + ServerPort, StringComparison.Ordinal);
                if (!fromServer && !segment.Destination.EndsWith(":" + ServerPort, StringComparison.Ordinal))
                {
                    continue;
                }

                source.Ingest(segment with { FromServer = fromServer });
                if (++segments % 500 == 0)
                {
                    hits.AddRange(source.Poll(false).Damage);
                }
            }

            hits.AddRange(source.Poll(false).Damage);
            var directory = (Aion2EntityDirectory)source.Entities!;
            FightExtras extras = directory.Combat.Slice(Whole.From, Whole.To, _ => null);
            Console.WriteLine($"{Path.GetFileName(path)}: {hits.Count} hits, {extras.Staggers.Count} stagger readings");
            foreach (var group in extras.Staggers.GroupBy(s => s.Entity).Where(g => g.Count() >= 300).OrderByDescending(g => g.Count()).Take(8))
            {
                var misses = new List<StaggerMiss>();
                StaggerShareResult result = StaggerShare.Compute(hits, extras.Staggers, group.Key, Whole.From, Whole.To, Aion2StaggerTable.ValueOf, misses);
                foreach (StaggerMiss m in misses) { allMisses.Add(m); }
                assignedAll += result.Assigned;
                totalAll += result.Total;
                double percent = result.Total == 0 ? 0 : 100.0 * result.Assigned / result.Total;
                string top = string.Join(", ", result.BySource.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{directory.NameFor(kv.Key) ?? "#" + kv.Key} {kv.Value}"));
                Console.WriteLine($"  boss {group.Key}: bar fell {result.Total}, assigned {result.Assigned} ({percent:0.0} %), {result.BySource.Count} players; top: {top}");
            }

            source.Dispose();
        }

        if (args.Contains("--why"))
        {
            Why(allMisses);
        }

        Console.WriteLine($"aion2-stagger-check: assigned {assignedAll} of {totalAll} ({(totalAll == 0 ? 0 : 100.0 * assignedAll / totalAll):0.0} %)");
    }

    /// <summary>What the unassigned falls have in common: no hit in the packet, only skills the table does not know, or hits whose values do not fit.</summary>
    private static void Why(List<StaggerMiss> misses)
    {
        int none = 0, unknownOnly = 0, mismatch = 0, dropSum = 0;
        var byKind = new Dictionary<string, int>();
        var unknownSkills = new Dictionary<int, int>();
        var combos = new Dictionary<string, int>();
        foreach (StaggerMiss m in misses)
        {
            dropSum += m.Drop;
            if (m.Candidates.Count == 0)
            {
                none += m.Drop;
            }
            else if (m.Candidates.All(c => c.Value == 0))
            {
                unknownOnly += m.Drop;
                foreach (var c in m.Candidates) { unknownSkills[c.Skill] = unknownSkills.GetValueOrDefault(c.Skill) + m.Drop; }
            }
            else
            {
                mismatch += m.Drop;
                string key = $"drop {m.Drop} vs hits " + string.Join("+", m.Candidates.Select(c => $"{c.Skill}={c.Value}").Take(4));
                combos[key] = combos.GetValueOrDefault(key) + 1;
            }
        }

        var sizes = misses.Where(m => m.Candidates.Count == 0).GroupBy(m => m.Drop).OrderByDescending(g => g.Sum(m => m.Drop)).Take(10).Select(g => $"{g.Key}x{g.Count()}");
        Console.WriteLine("  drops without a hit in the packet, biggest share first (size x count): " + string.Join(", ", sizes));
        Console.WriteLine("  of them 1-3 points: " + misses.Count(m => m.Candidates.Count == 0 && m.Drop <= 3) + ", 4-20: " + misses.Count(m => m.Candidates.Count == 0 && m.Drop is > 3 and <= 20) + ", over 20: " + misses.Count(m => m.Candidates.Count == 0 && m.Drop > 20) + " (" + misses.Where(m => m.Candidates.Count == 0 && m.Drop > 20).Sum(m => m.Drop) + " of the bar)");
        Console.WriteLine($"unassigned falls: {misses.Count} ({dropSum} of bar): no hit in the packet {none}, only skills not in the table {unknownOnly}, hits that do not fit {mismatch}");
        Console.WriteLine("  skills not in the table (bar lost): " + string.Join(", ", unknownSkills.OrderByDescending(kv => kv.Value).Take(12).Select(kv => $"{kv.Key}:{kv.Value}")));
        Console.WriteLine("  hits that do not fit (most common): ");
        foreach (var kv in combos.OrderByDescending(kv => kv.Value).Take(12)) { Console.WriteLine($"    {kv.Value}x {kv.Key}"); }
    }
}
