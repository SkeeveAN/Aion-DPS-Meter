using System.Windows;
using AionDPS.Aion2;
using AionDPS.Combat;
using AionDPS.Upload;

namespace AionDPS.Ui;

// The last instance of the live meter page: the boss fights of the instance as they were counted, kept when the meter is cleared (see LastInstanceStore).
public partial class MainWindow
{
    private readonly LastInstanceStore _lastInstance = new();
    private int _lastCapturedEvents = -1;
    private readonly Dictionary<(int Target, DateTime Start), (long Total, DateTime Since)> _bossGrowth = new();

    /// <summary>Takes the boss fights the meter holds (every run of every boss since the last Clear) into the store; called every second and just before the meter is cleared.</summary>
    private void CaptureLastInstance()
    {
        if (_historyMode || _mobBossEntries.Count == 0 || _source?.Entities is not Aion2EntityDirectory directory)
        {
            return;
        }

        int count = _aggregator.Events.Count;
        if (count == _lastCapturedEvents)
        {
            return;
        }

        _lastCapturedEvents = count;
        var bosses = new List<BossSnapshot>();
        foreach (var entry in _mobBossEntries.Where(e => !e.IsPlayer))
        {
            var runs = MobBossRowsFor(entry.TargetId, entry.Name).ToList();
            for (int i = 0; i < runs.Count; i++)
            {
                if (runs[i].Damage > 0 && SnapshotBoss(directory, entry.TargetId, runs[i].Name, runs[i].Tag, i == runs.Count - 1) is { } boss)
                {
                    bosses.Add(boss);
                }
            }
        }

        if (bosses.Count > 0)
        {
            string title = Aion2.Protocol.Aion2Zones.NameOf(_lastZoneSeen, LocalizationManager.Instance.Language) ?? "";
            _lastInstance.Replace(title, bosses.OrderByDescending(b => b.Start).ToList());
        }
    }

    /// <summary>One run of one boss: the players who hit it (and its adds) with their damage, as the boss filter would list them.</summary>
    private BossSnapshot? SnapshotBoss(Aion2EntityDirectory directory, int targetId, string name, MobBossTag tag, bool lastRun)
    {
        List<DamageEvent> hits = ShownFightHits(_aggregator.Events.Where(ev => !ev.IsHeal), targetId, tag.WindowStart, tag.WindowEnd);
        var onBoss = hits.Where(e => e.TargetObjectId == targetId).ToList();
        if (onBoss.Count == 0)
        {
            return null;
        }

        DateTime start = onBoss.Min(e => e.Timestamp), end = onBoss.Max(e => e.Timestamp);
        double seconds = BossFight.SecondsOnTarget(hits, targetId);
        var bySource = hits.GroupBy(e => e.SourceObjectId).ToDictionary(g => g.Key, g => (Damage: g.Sum(e => (long)e.Amount), Hits: g.Count()));
        var rows = new List<PlayerRow>();
        foreach (var (id, value) in bySource.Where(kv => IsPlayerName(kv.Key)).OrderByDescending(kv => kv.Value.Damage))
        {
            // a stranger passing by with one single hit is no row (as in the meter's own list)
            if (id != directory.LocalPlayerId && value.Hits <= 1 && directory.NameFor(id) is { } known && known.StartsWith("Player #", StringComparison.Ordinal))
            {
                continue;
            }

            var row = new PlayerRow(id);
            ApplyIdentity(row, id);
            row.Damage = value.Damage;
            row.Dps = seconds > 0 ? value.Damage / seconds : null;
            row.Faction = directory.FactionOf(id) ?? "";
            rows.Add(row);
        }

        long total = rows.Sum(r => r.Damage), max = rows.Count > 0 ? rows.Max(r => r.Damage) : 0;
        int rank = 0;
        foreach (PlayerRow row in rows)
        {
            row.Rank = ++rank;
            row.SharePercent = total > 0 ? 100.0 * row.Damage / total : 0;
            row.FillPercent = max > 0 ? 100.0 * row.Damage / max : 0;
        }

        bool wiped = name.Contains("\U0001F480", StringComparison.Ordinal);
        bool killed = lastRun && !wiped && BossFightFinished(targetId);
        // a fight is "running" while its damage keeps growing (a change within the last 15 seconds)
        var key = (targetId, start);
        long damageOnBoss = onBoss.Sum(e => (long)e.Amount);
        if (!_bossGrowth.TryGetValue(key, out var seen) || seen.Total != damageOnBoss)
        {
            _bossGrowth[key] = seen = (damageOnBoss, DateTime.UtcNow);
        }

        return new BossSnapshot
        {
            TargetId = targetId, Name = name, Start = start, End = end, Seconds = seconds, Killed = killed, Wiped = wiped,
            Running = lastRun && !killed && !wiped && (DateTime.UtcNow - seen.Since).TotalSeconds < 15, Total = total, Rows = rows,
        };
    }

    /// <summary>An upload answered with the number of the fight: the boss of the last instance that started then gets its link.</summary>
    private void NoteEncounterLink(EncounterUploadRequest request, UploadResult result)
    {
        if (result is { Success: true, EncounterId: int id })
        {
            _lastInstance.SetEncounterId(request.StartedAt, id);
        }
    }

    /// <summary>The copy buttons under a boss of the last instance: 0 the link, 1 the chat text (one part per click), 2 the table for Discord (with the link).</summary>
    private void CopyBossOfLastInstance(BossSnapshot boss, int what)
    {
        switch (what)
        {
            case 0 when boss.Link.Length > 0:
                CopyTextToClipboardIfAny(boss.Link, "There is no link yet.");
                break;
            case 1:
                CopyChatLineChunk(BuildDmgChatLine(boss.Rows, boss.Name), "No damage has been recorded yet.");
                break;
            case 2:
            {
                string table = DamageTable(boss.Rows);
                if (table.Length > 0 && boss.Link.Length > 0)
                {
                    table += "\n" + boss.Link;
                }

                CopyTextToClipboardIfAny(table, "No damage has been recorded yet.");
                break;
            }
        }
    }
}
