using System.IO;
using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.History;

namespace AionDPS.Combat;

/// <summary>
/// Test mode "aion2-buff-dump": plays a recording through the decoder (no window, no network) and writes, for every boss fight, the effects that ran on each
/// player (uptime, who gave them, icon, buff/debuff type) as JSON. Used to design the buff bar of the encounter table.
/// </summary>
public static class BuffDump
{
    public static void Run(string recordingPath, string outPath, int serverPort, string? ownName)
    {
        using var source = new Aion2PacketCombatSource(Aion2Protocol.Load());
        (source.Entities as Aion2EntityDirectory)?.SetConfiguredLocalName(ownName);
        var events = new List<DamageEvent>();
        var stays = new SessionRecorder(zone => Aion2Zones.IsInstance(zone) ? SessionKind.Instance : SessionKind.Map);
        if (source.Entities is Aion2EntityDirectory directory)
        {
            stays.Identify = id => (directory.NameFor(id) ?? directory.MonsterNameFor(id), directory.ClassOf(id));
        }

        // the fights are cut at the end of the replay, when the whole effect log is there
        foreach (TcpSegmentLite segment in Read(recordingPath, serverPort))
        {
            source.Ingest(segment.Segment);
            var polled = source.Poll(false);
            events.AddRange(polled.Damage);
            stays.Feed(polled);
        }

        if (events.Count == 0 || source.Entities is not Aion2EntityDirectory dir)
        {
            Console.WriteLine("aion2-buff-dump: no damage in this recording");
            return;
        }

        stays.CloseCurrent(events[^1].Timestamp);
        var fights = new List<object>();
        foreach (SessionInfo stay in stays.Sessions)
        {
            foreach (BossAttempt attempt in SessionAnalyzer.Analyze(stay).Attempts)
            {
                DateTime from = attempt.Start, to = attempt.End;
                var inFight = stay.Data.Events.Where(e => e.Timestamp >= from && e.Timestamp <= to).ToList();
                var extras = dir.Combat.Slice(from, to, id => source.Entities.NameFor(id), _ => true);
                var players = inFight.Where(e => e.SourceObjectId != attempt.TargetId && dir.ClassOf(e.SourceObjectId) is not null)
                    .GroupBy(e => e.SourceObjectId).Select(g => g.Key).ToList();
                var rows = new List<object>();
                foreach (int id in players)
                {
                    var mine = inFight.Where(e => e.SourceObjectId == id).ToList();
                    var report = RotationAnalysis.Build(extras, id, from, to, mine, i => source.Entities.NameFor(i));
                    rows.Add(new
                    {
                        name = source.Entities.NameFor(id),
                        cls = dir.ClassOf(id),
                        damage = mine.Where(e => !e.IsHeal).Sum(e => e.Amount),
                        heal = mine.Where(e => e.IsHeal).Sum(e => e.Amount),
                        buffs = report.Buffs.Select(b => new
                        {
                            skillId = b.SkillId,
                            name = b.Name,
                            uptime = Math.Round(b.UptimePercent, 1),
                            fromSelf = Math.Round(b.FromYouPercent, 1),
                            applications = b.Applications,
                            givers = b.Givers,
                            passive = Aion2Artwork.IsPassiveEffect(b.SkillId),
                            beneficial = Aion2Artwork.IsBeneficialEffect(b.SkillId),
                            icon = Aion2Artwork.EffectIconPath(b.SkillId),
                        }).ToList(),
                    });
                }

                fights.Add(new { boss = attempt.Name, start = from, seconds = (to - from).TotalSeconds, killed = attempt.Killed, players = rows });
            }
        }

        File.WriteAllText(outPath, JsonSerializer.Serialize(fights, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"aion2-buff-dump: {fights.Count} fight(s) written to {outPath}");
    }

    private readonly record struct TcpSegmentLite(Aion2.Capture.TcpSegment Segment);

    private static IEnumerable<TcpSegmentLite> Read(string path, int port)
    {
        foreach (Aion2.Capture.TcpSegment s in Aion2.Capture.SegmentRecording.Read(path))
        {
            bool fromServer = s.Source.EndsWith(":" + port, StringComparison.Ordinal);
            if (fromServer || s.Destination.EndsWith(":" + port, StringComparison.Ordinal))
            {
                yield return new TcpSegmentLite(s with { FromServer = fromServer });
            }
        }
    }
}
