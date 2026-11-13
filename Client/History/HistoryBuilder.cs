using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AionDPS.Aion2;

namespace AionDPS.History;

/// <summary>
/// Works through the recordings of earlier runs (%AppData%\\Aion DPS Meter\\Debug\\aion2-*.jsonl) once: each file is replayed by a child process of the program
/// (<c>aion2-history</c>, no window, low priority - a replay changes global state the live capture must not share) and its stays and boss fights go into
/// <see cref="FightHistory"/>. A file is read once; the one still being written is left alone.
/// </summary>
public static class HistoryBuilder
{
    private static bool _running;

    public static string FileKey(FileInfo file) => file.Name + "|" + file.Length;

    /// <summary>Starts the background work (once at a time); <paramref name="done"/> is called when a file was added.</summary>
    public static void Start(Action? done)
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _ = Task.Run(() =>
        {
            try
            {
                string folder = Aion2.Capture.DebugRecording.FolderPath;
                if (!Directory.Exists(folder))
                {
                    return;
                }

                // the saved sessions (by hand or automatic) first: they are quick
                foreach (string dir in new[] { SessionFile.DefaultDirectory, SessionFile.DefaultAutoFolder }.Where(Directory.Exists).Distinct())
                {
                    foreach (FileInfo saved in new DirectoryInfo(dir).GetFiles("*" + SessionFile.Extension).OrderByDescending(f => f.LastWriteTime))
                    {
                        if (FightHistory.WasRead(FileKey(saved)))
                        {
                            continue;
                        }

                        try
                        {
                            var loaded = SessionFile.Load(saved.FullName);
                            var recorder = new SessionRecorder(_ => SessionKind.Map);
                            SessionInfo stay = Ui.SessionManagerWindow.Import(recorder, loaded);
                            if (FightHistory.Summarize(stay) is { } record)
                            {
                                FightHistory.Add(new[] { record });
                            }

                            FightHistory.MarkRead(FileKey(saved));
                            done?.Invoke();
                        }
                        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
                        {
                            // a file that cannot be read stays out
                        }
                    }
                }

                foreach (FileInfo file in new DirectoryInfo(folder).GetFiles("aion2-*.jsonl").OrderByDescending(f => f.LastWriteTime))
                {
                    if (DateTime.Now - file.LastWriteTime < TimeSpan.FromMinutes(3) || FightHistory.WasRead(FileKey(file)))
                    {
                        continue;
                    }

                    if (ReadOne(file.FullName))
                    {
                        FightHistory.MarkRead(FileKey(file));
                        done?.Invoke();
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // nothing to work through
            }
            finally
            {
                GameLog.Maintain();
                _running = false;
            }
        });
    }

    private static bool ReadOne(string path)
    {
        string output = Path.Combine(Path.GetTempPath(), "aion2-history-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            string own = Path.GetFileNameWithoutExtension(path);
            own = own[(own.LastIndexOf('-') + 1)..];
            var start = new ProcessStartInfo(Environment.ProcessPath ?? "", $"aion2-history \"{path}\" \"{output}\" \"{own}\"")
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            };
            using Process? child = Process.Start(start);
            if (child is null)
            {
                return false;
            }

            try
            {
                child.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // the priority stays
            }

            if (!child.WaitForExit(10 * 60 * 1000) || child.ExitCode != 0 || !File.Exists(output))
            {
                return false;
            }

            var stays = JsonSerializer.Deserialize<List<StayRecord>>(File.ReadAllText(output));
            if (stays is not null)
            {
                FightHistory.Add(stays);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            try
            {
                File.Delete(output);
            }
            catch (IOException)
            {
                // a temporary file stays
            }
        }
    }

    /// <summary>The child's work: replay a recording and write its stays with their boss fights as JSON.</summary>
    public static int RunChild(string path, string output, string? ownName)
    {
        const int serverPort = 13328;
        var protocol = Aion2.Protocol.Aion2Protocol.Load();
        using var source = new Aion2PacketCombatSource(protocol);
        (source.Entities as Aion2EntityDirectory)?.SetConfiguredLocalName(ownName);
        var stays = new SessionRecorder(zone => Aion2.Protocol.Aion2Zones.IsInstance(zone) ? SessionKind.Instance : SessionKind.Map);
        if (source.Entities is Aion2EntityDirectory directory)
        {
            stays.Identify = id => (directory.NameFor(id) ?? directory.MonsterNameFor(id), directory.ClassOf(id));
            stays.HpSource = (id, from, to) =>
            {
                var samples = directory.HitPoints.SamplesAround(id, from, to);
                return samples.Count < 2 ? null : new TargetHp(directory.HitPoints.HighestSeen(id) ?? 0, samples, directory.HitPoints.ResetsOf(id));
            };
        }

        // a replay must not write to the real pet-farm log
        PetFarmLog.PathOverride = Path.Combine(Path.GetTempPath(), "aion2-history-pet-farm.log");
        DateTime last = DateTime.MinValue;
        foreach (Aion2.Capture.TcpSegment segment in Aion2.Capture.SegmentRecording.Read(path))
        {
            bool fromServer = segment.Source.EndsWith(":" + serverPort, StringComparison.Ordinal);
            if (!fromServer && !segment.Destination.EndsWith(":" + serverPort, StringComparison.Ordinal))
            {
                continue;
            }

            source.Ingest(segment with { FromServer = fromServer });
            Combat.Sources.CombatBatch polled = source.Poll(false);
            foreach (var ev in polled.Damage)
            {
                if (ev.Timestamp > last)
                {
                    last = ev.Timestamp;
                }
            }

            stays.Feed(polled);
            if (source.Entities is Aion2EntityDirectory owners)
            {
                foreach ((int summon, int owner) in owners.DrainResolvedOwners())
                {
                    stays.Reattribute(summon, owner);
                }
            }
        }

        if (last > DateTime.MinValue)
        {
            stays.CloseCurrent(last);
        }

        var records = new List<StayRecord>();
        foreach (SessionInfo stay in stays.Sessions)
        {
            if (FightHistory.Summarize(stay) is { } record)
            {
                records.Add(record);
            }
        }

        File.WriteAllText(output, JsonSerializer.Serialize(records));
        return 0;
    }
}
