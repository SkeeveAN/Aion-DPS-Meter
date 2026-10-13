using System.IO;
using System.IO.Compression;
using System.Text.Json;
using AionDPS.Combat;

namespace AionDPS.History;

/// <summary>Compact JSON + gzip for the extras of a fight (skills used, effects, stagger), kept in their own column beside the events.</summary>
public static class ExtrasBlob
{
    private sealed record Use(long T, int A, int S);

    private sealed record Buff(long T, int K, int G, int I, int S, int C, int D);

    private sealed record Stagger(long T, int E, int C, int M, bool B);

    private sealed record Payload(List<Use> U, List<Buff> B, List<Stagger> S, Dictionary<string, string> N);

    public static byte[] Pack(FightExtras extras)
    {
        var payload = new Payload(
            extras.Uses.Select(u => new Use(u.At.Ticks, u.Actor, u.SkillId)).ToList(),
            extras.Buffs.Select(b => new Buff(b.At.Ticks, (int)b.Kind, b.Target, b.Instance, b.SkillId, b.Caster, b.DurationMs)).ToList(),
            extras.Staggers.Select(s => new Stagger(s.At.Ticks, s.Entity, s.Current, s.Max, s.Broke)).ToList(),
            extras.Names.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value));
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            JsonSerializer.Serialize(gzip, payload);
        }

        return output.ToArray();
    }

    public static FightExtras Unpack(byte[] blob)
    {
        using var input = new MemoryStream(blob);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        Payload? payload = JsonSerializer.Deserialize<Payload>(gzip);
        if (payload is null)
        {
            return FightExtras.Empty;
        }

        DateTime At(long ticks) => new(ticks, DateTimeKind.Local);
        return new FightExtras(
            payload.U.Select(u => new SkillUse(At(u.T), u.A, u.S)).ToList(),
            payload.B.Select(b => new BuffEvent(At(b.T), (BuffKind)b.K, b.G, b.I, b.S, b.C, b.D)).ToList(),
            payload.S.Select(s => new StaggerEvent(At(s.T), s.E, s.C, s.M, s.B)).ToList(),
            payload.N.ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value));
    }
}
