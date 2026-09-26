using System.IO;
using System.IO.Compression;
using System.Text.Json;
using AionDPS.Combat;
using AionDPS.Combat.Sources;

namespace AionDPS.History;

/// <summary>
/// "Save Session"/"Load Session" (App menu) - a standalone, portable snapshot of the CURRENT live
/// session (every damage/avoid/kill event plus personal stat totals) as one file the user can
/// keep or hand off, independent of Chat.log (which keeps growing/changing and isn't itself a
/// stable "this is what I was looking at" artifact). Deliberately separate from FightStore/
/// FightHistory: that's an automatic per-FIGHT archive the meter keeps for itself; this is a
/// manual, whole-SESSION export the user explicitly asks for. Loot isn't included yet - a
/// separate INotifyPropertyChanged row type (LootRow), not a plain serializable record like
/// everything below, so it was left out of this first version rather than complicating it.
/// </summary>
public static class SessionFile
{
    public const string Extension = ".aiondps";

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aion DPS Meter", "Sessions");

    private sealed record EventDto(long T, int K, int S, int G, long A, bool H, string? Sk, bool C);

    private sealed record AvoidDto(long T, int K, int S, int G, int Kind, string? Sk);

    private sealed record KillDto(long T, int K, int? Killer, int Victim, bool VictimIsPlayer);

    private sealed record PersonalStatsDto(long Exp, long Ap, long Gp, long Kinah);

    private sealed record Payload(
        int Version,
        DateTime SavedAt,
        List<EventDto> Events,
        List<AvoidDto> Avoids,
        List<KillDto> Kills,
        Dictionary<string, string> Names,
        PersonalStatsDto Stats);

    public static void Save(
        string path,
        IReadOnlyList<DamageEvent> events,
        IReadOnlyList<AvoidEvent> avoids,
        IReadOnlyList<KillEvent> kills,
        IReadOnlyDictionary<int, string> names,
        long exp, long ap, long gp, long kinah)
    {
        var payload = new Payload(
            1,
            DateTime.Now,
            events.Select(e => new EventDto(e.Timestamp.Ticks, (int)e.Timestamp.Kind, e.SourceObjectId, e.TargetObjectId, e.Amount, e.IsHeal, e.Skill, e.IsCritical)).ToList(),
            avoids.Select(a => new AvoidDto(a.Timestamp.Ticks, (int)a.Timestamp.Kind, a.SourceObjectId, a.TargetObjectId, (int)a.Kind, a.Skill)).ToList(),
            kills.Select(k => new KillDto(k.Timestamp.Ticks, (int)k.Timestamp.Kind, k.KillerObjectId, k.VictimObjectId, k.VictimIsPlayer)).ToList(),
            names.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            new PersonalStatsDto(exp, ap, gp, kinah));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream output = File.Create(path);
        using var gzip = new GZipStream(output, CompressionLevel.Fastest);
        JsonSerializer.Serialize(gzip, payload);
    }

    public sealed record LoadedSession(
        List<DamageEvent> Events,
        List<AvoidEvent> Avoids,
        List<KillEvent> Kills,
        Dictionary<int, string> Names,
        long Exp, long Ap, long Gp, long Kinah,
        DateTime SavedAt);

    public static LoadedSession Load(string path)
    {
        using FileStream input = File.OpenRead(path);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        Payload payload = JsonSerializer.Deserialize<Payload>(gzip)
            ?? throw new InvalidDataException("Not a valid Aion DPS session file.");

        var events = payload.Events
            .Select(d => new DamageEvent(new DateTime(d.T, (DateTimeKind)d.K), d.S, d.G, d.A, d.H, d.Sk, d.C))
            .ToList();
        var avoids = payload.Avoids
            .Select(d => new AvoidEvent(new DateTime(d.T, (DateTimeKind)d.K), d.S, d.G, (AvoidKind)d.Kind, d.Sk))
            .ToList();
        var kills = payload.Kills
            .Select(d => new KillEvent(new DateTime(d.T, (DateTimeKind)d.K), d.Killer, d.Victim, d.VictimIsPlayer))
            .ToList();
        var names = payload.Names.ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value);

        return new LoadedSession(events, avoids, kills, names,
            payload.Stats.Exp, payload.Stats.Ap, payload.Stats.Gp, payload.Stats.Kinah, payload.SavedAt);
    }
}
