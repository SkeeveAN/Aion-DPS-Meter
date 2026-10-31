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
                StaggerShareResult result = StaggerShare.Compute(hits, extras.Staggers, group.Key, Whole.From, Whole.To, Aion2StaggerTable.ValueOf);
                assignedAll += result.Assigned;
                totalAll += result.Total;
                double percent = result.Total == 0 ? 0 : 100.0 * result.Assigned / result.Total;
                string top = string.Join(", ", result.BySource.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{directory.NameFor(kv.Key) ?? "#" + kv.Key} {kv.Value}"));
                Console.WriteLine($"  boss {group.Key}: bar fell {result.Total}, assigned {result.Assigned} ({percent:0.0} %), {result.BySource.Count} players; top: {top}");
            }

            source.Dispose();
        }

        Console.WriteLine($"aion2-stagger-check: assigned {assignedAll} of {totalAll} ({(totalAll == 0 ? 0 : 100.0 * assignedAll / totalAll):0.0} %)");
    }
}
