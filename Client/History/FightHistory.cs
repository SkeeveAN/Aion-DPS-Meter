using System.IO;
using System.Text.Json;

namespace AionDPS.History;

/// <summary>One boss fight of a stay, as the history keeps it.</summary>
public sealed record AttemptRecord(string Boss, DateTime Start, DateTime End, long Counted, bool Killed)
{
    public TimeSpan Duration => End - Start;
}

/// <summary>One stay in a map or instance with its boss fights (the lasting summary of a session: what the dashboard shows when the game is off).</summary>
public sealed record StayRecord(DateTime Start, DateTime End, int ZoneId, SessionKind Kind, int Hits, List<AttemptRecord> Attempts);

/// <summary>
/// The lasting history of the fights: every ended session adds its summary, and the recordings of earlier runs (the debug files) are worked through once in the
/// background (see <see cref="HistoryBuilder"/>) so the dashboard has real data from the start. Kept in %AppData%\\Aion DPS Meter\\fight-history.json.
/// </summary>
public static class FightHistory
{
    private sealed class Store
    {
        public List<StayRecord> Stays { get; set; } = new();
        public List<string> Files { get; set; } = new();
    }

    private static readonly object Gate = new();
    private static Store? _store;

    public static string StorePath => Path.Combine(Environment.GetEnvironmentVariable("AIONDPS_DATA_DIR") is { Length: > 0 } testDir ? testDir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aion DPS Meter"), "fight-history.json");

    private static Store Load()
    {
        lock (Gate)
        {
            if (_store is not null)
            {
                return _store;
            }

            try
            {
                if (File.Exists(StorePath))
                {
                    _store = JsonSerializer.Deserialize<Store>(File.ReadAllText(StorePath));
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // a damaged file: the history starts again
            }

            return _store ??= new Store();
        }
    }

    private static void Save()
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                File.WriteAllText(StorePath + ".tmp", JsonSerializer.Serialize(Load()));
                File.Move(StorePath + ".tmp", StorePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // the history stays in memory
            }
        }
    }

    /// <summary>The summary of a session (null for a stay without hits).</summary>
    public static StayRecord? Summarize(SessionInfo session)
    {
        if (session.Data.Events.Count == 0)
        {
            return null;
        }

        var attempts = new List<AttemptRecord>();
        try
        {
            foreach (BossAttempt a in SessionAnalyzer.Analyze(session).Attempts)
            {
                attempts.Add(new AttemptRecord(a.Name, a.Start, a.End, a.Counted, a.Killed));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // a stay that cannot be analysed keeps its times only
        }

        return new StayRecord(session.Start, session.End ?? session.Data.Events[^1].Timestamp, session.ZoneId, session.Kind, session.Data.Events.Count, attempts);
    }

    private static bool Same(StayRecord a, StayRecord b) => a.ZoneId == b.ZoneId && Math.Abs((a.Start - b.Start).TotalSeconds) < 30;

    /// <summary>Adds stays (one that is already there stays as it was).</summary>
    public static void Add(IEnumerable<StayRecord> stays)
    {
        Store store = Load();
        bool changed = false;
        lock (Gate)
        {
            foreach (StayRecord stay in stays)
            {
                if (!store.Stays.Any(s => Same(s, stay)))
                {
                    store.Stays.Add(stay);
                    changed = true;
                }
            }
        }

        if (changed)
        {
            Save();
        }
    }

    public static bool WasRead(string fileKey) => Load().Files.Contains(fileKey);

    public static void MarkRead(string fileKey)
    {
        lock (Gate)
        {
            Load().Files.Add(fileKey);
        }

        Save();
    }

    /// <summary>Everything known: the kept history and the sessions of this run (the sessions win where both have a stay), newest first. An open session is left out.</summary>
    public static List<StayRecord> All(IEnumerable<SessionInfo> live)
    {
        var result = new List<StayRecord>();
        foreach (SessionInfo session in live.Where(s => !s.IsOpen))
        {
            if (Summarize(session) is { } record)
            {
                result.Add(record);
            }
        }

        lock (Gate)
        {
            foreach (StayRecord stored in Load().Stays)
            {
                if (!result.Any(r => Same(r, stored)))
                {
                    result.Add(stored);
                }
            }
        }

        return result.OrderByDescending(r => r.Start).ToList();
    }
}
