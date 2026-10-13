using System.IO;
using System.IO.Compression;
using System.Text.Json;
using AionDPS.Combat;
using AionDPS.Combat.Sources;

namespace AionDPS.History;

/// <summary>
/// "Save Session"/"Load Session" (App menu) - a standalone, portable snapshot of the CURRENT live
/// session (every damage/avoid/kill event plus personal stat totals) as one file the user can
/// keep or hand off, independent of the live capture (which keeps changing and isn't itself a
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

    /// <summary>What makes a file one stay (a SessionInfo) and not a whole meter session: where and when, the shields, who was which class.</summary>
    private sealed record HpDto(long Highest, List<long> At, List<long> Hp, List<long> Resets);

    private sealed record RageDto(long At, int Entity, long Engaged);

    private sealed record StayDto(int ZoneId, int Kind, long Start, long? End, List<EventDto> Shields, Dictionary<string, string> Classes,
        Dictionary<string, HpDto>? Hp = null, List<RageDto>? Rages = null);

    private sealed record Payload(
        int Version,
        DateTime SavedAt,
        List<EventDto> Events,
        List<AvoidDto> Avoids,
        List<KillDto> Kills,
        Dictionary<string, string> Names,
        Aion2.Aion2DirectorySnapshot? Directory = null,
        StayDto? Stay = null);

    private static EventDto ToDto(DamageEvent e) => new(e.Timestamp.Ticks, (int)e.Timestamp.Kind, e.SourceObjectId, e.TargetObjectId, e.Amount, e.IsHeal, e.Skill, e.IsCritical);

    private static DamageEvent FromDto(EventDto d) => new(new DateTime(d.T, (DateTimeKind)d.K), d.S, d.G, d.A, d.H, d.Sk, d.C);

    /// <summary>Writes one stay (from the session manager) as a file the same Load can read back; the extra part is what a stay needs.</summary>
    public static void SaveStay(string path, SessionInfo stay)
    {
        var payload = new Payload(
            2,
            DateTime.Now,
            stay.Data.Events.Select(ToDto).ToList(),
            new List<AvoidDto>(),
            new List<KillDto>(),
            stay.Names.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            null,
            new StayDto(
                stay.ZoneId,
                (int)stay.Kind,
                stay.Start.Ticks,
                stay.End?.Ticks,
                stay.Data.Shields.Select(ToDto).ToList(),
                stay.Classes.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                stay.Hp.ToDictionary(
                    kv => kv.Key.ToString(),
                    kv => new HpDto(kv.Value.Highest, kv.Value.Samples.Select(x => x.At.Ticks).ToList(), kv.Value.Samples.Select(x => x.Hp).ToList(),
                        kv.Value.Resets.Select(r => r.Ticks).ToList())),
                stay.Enrages.Select(m => new RageDto(m.Timestamp.Ticks, m.EntityId, m.EngagedAt.Ticks)).ToList()));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream output = File.Create(path);
        using var gzip = new GZipStream(output, CompressionLevel.Fastest);
        JsonSerializer.Serialize(gzip, payload);
    }

    /// <summary>The stay part of a file written by <see cref="SaveStay"/>.</summary>
    public sealed record StoredStay(int ZoneId, SessionKind Kind, DateTime Start, DateTime? End, List<DamageEvent> Shields, Dictionary<int, string> Classes,
        Dictionary<int, TargetHp> Hp, List<EnrageMark> Enrages);

    public static void Save(
        string path,
        IReadOnlyList<DamageEvent> events,
        IReadOnlyList<AvoidEvent> avoids,
        IReadOnlyList<KillEvent> kills,
        IReadOnlyDictionary<int, string> names,
        Aion2.Aion2DirectorySnapshot? directory = null)
    {
        var payload = new Payload(
            1,
            DateTime.Now,
            events.Select(e => new EventDto(e.Timestamp.Ticks, (int)e.Timestamp.Kind, e.SourceObjectId, e.TargetObjectId, e.Amount, e.IsHeal, e.Skill, e.IsCritical)).ToList(),
            avoids.Select(a => new AvoidDto(a.Timestamp.Ticks, (int)a.Timestamp.Kind, a.SourceObjectId, a.TargetObjectId, (int)a.Kind, a.Skill)).ToList(),
            kills.Select(k => new KillDto(k.Timestamp.Ticks, (int)k.Timestamp.Kind, k.KillerObjectId, k.VictimObjectId, k.VictimIsPlayer)).ToList(),
            names.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            directory);

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
        DateTime SavedAt,
        Aion2.Aion2DirectorySnapshot? Directory = null,
        StoredStay? Stay = null);

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

        StoredStay? stay = payload.Stay is { } s
            ? new StoredStay(
                s.ZoneId,
                (SessionKind)s.Kind,
                new DateTime(s.Start),
                s.End is long end ? new DateTime(end) : null,
                s.Shields.Select(FromDto).ToList(),
                s.Classes.ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value),
                (s.Hp ?? new Dictionary<string, HpDto>()).ToDictionary(
                    kv => int.Parse(kv.Key),
                    kv => new TargetHp(
                        kv.Value.Highest,
                        kv.Value.At.Zip(kv.Value.Hp, (at, hp) => new Aion2.HpSample(new DateTime(at), hp)).ToList(),
                        kv.Value.Resets.Select(r => new DateTime(r)).ToList())),
                (s.Rages ?? new List<RageDto>()).Select(r => new EnrageMark(new DateTime(r.At), r.Entity, new DateTime(r.Engaged))).ToList())
            : null;
        return new LoadedSession(events, avoids, kills, names, payload.SavedAt, payload.Directory, stay);
    }
}
