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

    private sealed record Track(int E, long S, int[] T, int[] X, int[] Y);

    private sealed record Windup(long T, int A, int K, int G, int D, double F, int X, int Y, bool P);

    private sealed record Death(long T, int E,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] long? R = null);

    private sealed record Hp(int E, long S, int[] T, short[] P);

    private sealed record Reject(long T, int E, int S, int C);

    private sealed record Tgt(long T, int A, int G);

    private sealed record Payload(List<Use> U, List<Buff> B, List<Stagger> S, Dictionary<string, string> N,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] List<Track>? R = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] List<Windup>? W = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] List<Death>? D = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] List<Hp>? H = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] List<Reject>? J = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] List<Tgt>? K = null);

    public static byte[] Pack(FightExtras extras)
    {
        var payload = new Payload(
            extras.Uses.Select(u => new Use(u.At.Ticks, u.Actor, u.SkillId)).ToList(),
            extras.Buffs.Select(b => new Buff(b.At.Ticks, (int)b.Kind, b.Target, b.Instance, b.SkillId, b.Caster, b.DurationMs)).ToList(),
            extras.Staggers.Select(s => new Stagger(s.At.Ticks, s.Entity, s.Current, s.Max, s.Broke)).ToList(),
            extras.Names.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            extras.Tracks?.Select(t => new Track(t.Entity, t.Start.Ticks, t.T, t.X, t.Y)).ToList(),
            extras.Windups?.Select(w => new Windup(w.At.Ticks, w.Actor, w.SkillId, w.Target, w.DurationMs, w.Facing, w.X, w.Y, w.HasPosition)).ToList(),
            extras.Deaths?.Select(d => new Death(d.At.Ticks, d.Entity, d.RevivedAt?.Ticks)).ToList(),
            extras.Hp?.Select(h => new Hp(h.Entity, h.Start.Ticks, h.T, h.Permille)).ToList(),
            extras.Rejected?.Select(r => new Reject(r.At.Ticks, r.Entity, r.SkillId, r.Code)).ToList(),
            extras.Targets?.Select(t => new Tgt(t.At.Ticks, t.Actor, t.Target)).ToList());
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
            payload.N.ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value),
            payload.R?.Select(t => new PositionTrack(t.E, At(t.S), t.T, t.X, t.Y)).ToList(),
            payload.W?.Select(w => new WindupRecord(At(w.T), w.A, w.K, w.G, w.D, w.F, w.X, w.Y, w.P)).ToList(),
            payload.D?.Select(d => new DeathRecord(At(d.T), d.E, d.R is long revived ? At(revived) : null)).ToList(),
            payload.H?.Select(h => new HpSeries(h.E, At(h.S), h.T, h.P)).ToList(),
            payload.J?.Select(r => new RejectRecord(At(r.T), r.E, r.S, r.C)).ToList(),
            payload.K?.Select(t => new TargetChange(At(t.T), t.A, t.G)).ToList());
    }
}
