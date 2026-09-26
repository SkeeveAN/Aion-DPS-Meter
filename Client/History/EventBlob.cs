using System.IO;
using System.IO.Compression;
using System.Text.Json;
using AionDPS.Combat;

namespace AionDPS.History;

/// <summary>Compact JSON + gzip for a raw DamageEvent list plus its object-id-&gt;name map -
/// originally FightStore's own private nested type (one blob per row in its SQLite table), now
/// shared with SessionFile's own file-based save/load (Main.MenuApp.SaveSession/LoadSession),
/// which needs the exact same round-trip but to a standalone file instead of a database column.
/// Ticks+Kind rather than ISO strings: the Chat.log timestamps are local, kind-unspecified values
/// and must come back exactly so.</summary>
public static class EventBlob
{
    private sealed record Dto(long T, int K, int S, int G, long A, bool H, string? Sk, bool C);

    private sealed record Payload(List<Dto> Events, Dictionary<string, string> Names);

    public static byte[] Pack(IReadOnlyList<DamageEvent> events, IReadOnlyDictionary<int, string> names)
    {
        var payload = new Payload(
            events.Select(e => new Dto(e.Timestamp.Ticks, (int)e.Timestamp.Kind, e.SourceObjectId, e.TargetObjectId, e.Amount, e.IsHeal, e.Skill, e.IsCritical)).ToList(),
            names.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value));
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            JsonSerializer.Serialize(gzip, payload);
        }

        return output.ToArray();
    }

    public static (List<DamageEvent> Events, Dictionary<int, string> Names) Unpack(byte[] blob)
    {
        using var input = new MemoryStream(blob);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        Payload payload = JsonSerializer.Deserialize<Payload>(gzip) ?? new Payload(new List<Dto>(), new Dictionary<string, string>());
        var events = payload.Events
            .Select(d => new DamageEvent(new DateTime(d.T, (DateTimeKind)d.K), d.S, d.G, d.A, d.H, d.Sk, d.C))
            .ToList();
        var names = payload.Names.ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value);
        return (events, names);
    }
}
