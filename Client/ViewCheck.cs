using System.Diagnostics;
using System.IO;
using AionDPS.Aion2;
using AionDPS.Aion2.Capture;
using AionDPS.Aion2.Protocol;

namespace AionDPS;

/// <summary>
/// <c>aion2-view-check &lt;recording.jsonl&gt; [...]</c>: plays real recordings (see aion2-record) through the main window's
/// refresh logic - a window that is never shown, with a throw-away data folder, nothing sent and nothing written to the
/// real history - and then works out every view of the list (damage, heal, taken, all in one, PvP, each picked target and
/// run) twice: the way the meter does it, and by the full rescan of all events (<c>ForceFullRefresh</c>). The two must give
/// the same rows to the last digit; the report says where they do not, and what each way costs.
/// </summary>
internal static class ViewCheck
{
    private const int ServerPort = 13328;

    public static void Run(string[] args)
    {
        var files = args.Skip(1).Where((a, i) => !a.StartsWith("--", StringComparison.Ordinal) && !(i > 0 && args[i].StartsWith("--", StringComparison.Ordinal))).ToList();
        if (files.Count == 0)
        {
            Console.WriteLine("Usage: AionDPS aion2-view-check <recording.jsonl> [more recordings] [--targets N] [--tick SECONDS]");
            return;
        }

        int maxTargets = OptionValue(args, "--targets", 6);
        int tickSeconds = OptionValue(args, "--tick", 2);

        string dataDir = Path.Combine(Path.GetTempPath(), "aiondps-view-check");
        Directory.CreateDirectory(dataDir);
        Environment.SetEnvironmentVariable("AIONDPS_DATA_DIR", dataDir);
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        string report = Path.Combine(Path.GetTempPath(), "aion2-view-check.txt");
        using var file = new StreamWriter(report, false) { AutoFlush = true };
        void Say(string line)
        {
            Console.WriteLine(line);
            file.WriteLine(line);
        }

        var app = new System.Windows.Application();
        var settings = Ui.MeterSettings.Load();
        Ui.ThemeManager.Apply(app, settings.Theme, settings.FontSize);
        Ui.MainWindow.Headless = true;

        int failures = 0;
        foreach (string path in files)
        {
            failures += CheckRecording(path, maxTargets, tickSeconds, Say);
        }

        Say(failures == 0 ? "aion2-view-check: ALL VIEWS EQUAL" : $"aion2-view-check: {failures} VIEW(S) DIFFER");
        Say($"aion2-view-check: report also in {report}");
        Environment.Exit(failures == 0 ? 0 : 1);
    }

    private static int OptionValue(string[] args, string name, int fallback)
    {
        int at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out int value) ? value : fallback;
    }

    private const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

    /// <summary>A window fed with the recording up to tick <paramref name="stopAtTick"/>; also the tick at which the list of events was longest
    /// (a map change empties it, so the end of a recording is rarely the interesting part).</summary>
    private static (Ui.MainWindow Window, Aion2PacketCombatSource Source, int PeakTick, int PeakCount, long Segments) Play(string path, int tickSeconds, int stopAtTick, Action<string> say)
    {
        var type = typeof(Ui.MainWindow);
        var window = new Ui.MainWindow();
        // The constructor starts the real capture; this run reads the recording only.
        (type.GetField("_liveSource", Flags)!.GetValue(window) as IDisposable)?.Dispose();
        var source = new Aion2PacketCombatSource(Aion2Protocol.Load());
        type.GetField("_source", Flags)!.SetValue(window, source);
        type.GetField("_liveSource", Flags)!.SetValue(window, source);
        type.GetField("_autoReset", Flags)!.SetValue(window, false);
        var tick = type.GetMethod("OnPollTimerTick", Flags)!;
        var aggregator = type.GetField("_aggregator", Flags)!.GetValue(window)!;
        var events = (System.Collections.IList)aggregator.GetType().GetProperty("Events")!.GetValue(aggregator)!;

        DateTime bucketStart = DateTime.MinValue;
        long segments = 0;
        int ticks = 0, peakTick = 0, peak = 0;
        void Tick()
        {
            tick.Invoke(window, new object?[] { null, EventArgs.Empty });
            ticks++;
            if (events.Count > peak)
            {
                peak = events.Count;
                peakTick = ticks;
            }
        }

        foreach (TcpSegment segment in SegmentRecording.Read(path))
        {
            bool fromServer = segment.Source.EndsWith(":" + ServerPort, StringComparison.Ordinal);
            if (!fromServer && !segment.Destination.EndsWith(":" + ServerPort, StringComparison.Ordinal))
            {
                continue;
            }

            segments++;
            source.Ingest(segment with { FromServer = fromServer });
            if (bucketStart == DateTime.MinValue)
            {
                bucketStart = segment.Timestamp;
            }
            else if ((segment.Timestamp - bucketStart).TotalSeconds >= tickSeconds)
            {
                Tick();
                bucketStart = segment.Timestamp;
                if (ticks >= stopAtTick)
                {
                    return (window, source, peakTick, peak, segments);
                }
            }
        }

        Tick();
        return (window, source, peakTick, peak, segments);
    }

    private static int CheckRecording(string path, int maxTargets, int tickSeconds, Action<string> say)
    {
        say($"=== {Path.GetFileName(path)} ({new FileInfo(path).Length / 1048576.0:F1} MB)");
        var type = typeof(Ui.MainWindow);
        var load = Stopwatch.StartNew();
        var scout = Play(path, tickSeconds, int.MaxValue, say);
        scout.Source.Dispose();
        if (scout.PeakCount == 0)
        {
            say("no combat events in this recording, nothing to compare");
            return 0;
        }

        // Second pass: stop where the list was longest.
        var (window, source, _, _, segments) = Play(path, tickSeconds, scout.PeakTick, say);
        var refresh = type.GetMethod("RefreshRows", Flags)!;
        var rows = (System.Collections.Generic.IEnumerable<Ui.PlayerRow>)type.GetField("_rows", Flags)!.GetValue(window)!;
        var aggregator = type.GetField("_aggregator", Flags)!.GetValue(window)!;
        var events = (System.Collections.IList)aggregator.GetType().GetProperty("Events")!.GetValue(aggregator)!;
        say($"loaded: {segments} segments up to the longest stay, {events.Count} events in {load.Elapsed.TotalSeconds:F1} s (two passes), heap {GC.GetTotalMemory(true) / 1048576.0:F0} MB");

        // The entries of the target list: "All" has no tag; the others carry target and, for a numbered run, its window.
        System.Windows.Controls.ComboBox filter = window.MobBossFilter;
        var items = filter.Items.OfType<System.Windows.Controls.ComboBoxItem>().ToList();
        var allItem = items.FirstOrDefault(i => i.Tag is null);
        var picked = items.Where(i => i.Tag is not null).Take(maxTargets).ToList();
        say($"target list: {items.Count - (allItem is null ? 0 : 1)} entries, {picked.Count} checked");

        var modeField = type.GetField("_mode", Flags)!;
        var pvpField = type.GetField("_pvpOnly", Flags)!;
        var modeType = modeField.FieldType;
        var forceFull = type.GetProperty("ForceFullRefresh", Flags)!;
        long Slot(Func<object?> body)
        {
            // best of three: the first run pays for the JIT and the caches
            long best = long.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                var watch = Stopwatch.StartNew();
                body();
                best = Math.Min(best, watch.ElapsedTicks);
            }

            return best * 1000 / Stopwatch.Frequency;
        }

        var snapshotProps = new[] { "Damage", "Dps", "Healing", "Absorbed", "DamageTaken", "IsEstimated", "Deaths", "Rank", "SharePercent", "FillPercent", "AbsorbedFillPercent", "Name", "ClassName", "Faction", "ShareDisplay", "TakenDisplay", "RateOrHealDisplay", "TotalOrTakenCompact" };
        var props = snapshotProps.Select(n => typeof(Ui.PlayerRow).GetProperty(n)!).ToArray();
        List<string> Snapshot() => rows.OrderBy(r => r.ObjectId).Select(r => r.ObjectId + "|" + string.Join("|", props.Select(p => Format(p.GetValue(r))))).ToList();

        int differing = 0;
        say("view | rows | differ | ms now | ms rescan");
        foreach (string mode in new[] { "Damage", "Heal", "Taken", "All", "AllCompact" })
        {
            foreach (bool pvp in mode == "Damage" ? new[] { false, true } : new[] { false })
            {
                var choices = new List<(string Name, System.Windows.Controls.ComboBoxItem? Item)>();
                if (!pvp)
                {
                    choices.Add(("all targets", allItem));
                    choices.AddRange(picked.Select(i => ($"{i.Content}", (System.Windows.Controls.ComboBoxItem?)i)));
                }
                else
                {
                    choices.Add(("PvP", allItem));
                }

                foreach (var (name, item) in choices)
                {
                    modeField.SetValue(window, Enum.Parse(modeType, mode));
                    pvpField.SetValue(window, pvp);
                    filter.SelectedItem = item ?? allItem; // fires the window's own selection handler
                    pvpField.SetValue(window, pvp);
                    forceFull.SetValue(window, false);
                    refresh.Invoke(window, null);
                    long nowMs = Slot(() => refresh.Invoke(window, null));
                    var fast = Snapshot();
                    forceFull.SetValue(window, true);
                    refresh.Invoke(window, null);
                    long fullMs = Slot(() => refresh.Invoke(window, null));
                    var full = Snapshot();
                    forceFull.SetValue(window, false);

                    int different = fast.Count != full.Count ? Math.Max(fast.Count, full.Count) : fast.Zip(full).Count(pair => pair.First != pair.Second);
                    string label = $"{mode}{(pvp ? " PvP" : "")} / {Truncate(name, 38)}";
                    say($"{label,-56} | {fast.Count,4} | {different,6} | {nowMs,6} | {fullMs,9}");
                    if (different > 0)
                    {
                        differing++;
                        foreach (string line in DescribeDifferences(fast, full).Take(4))
                        {
                            say("    " + line);
                        }
                    }
                }
            }
        }

        modeField.SetValue(window, Enum.Parse(modeType, "Damage"));
        pvpField.SetValue(window, false);
        source.Dispose();
        return differing;
    }

    private static string Format(object? value) => value switch
    {
        double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        null => "null",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];

    private static IEnumerable<string> DescribeDifferences(List<string> fast, List<string> full)
    {
        var byId = full.ToDictionary(l => l.Split('|')[0]);
        foreach (string line in fast)
        {
            string id = line.Split('|')[0];
            if (!byId.TryGetValue(id, out string? other))
            {
                yield return $"row {id} only in the meter's list";
            }
            else if (other != line)
            {
                yield return $"row {id}: meter  {line}";
                yield return $"        rescan {other}";
            }
        }

        foreach (string id in byId.Keys.Except(fast.Select(l => l.Split('|')[0])))
        {
            yield return $"row {id} only in the rescan";
        }
    }
}
