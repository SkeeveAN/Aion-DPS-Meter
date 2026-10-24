using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>The instance timer frame (0x0061): real frames of the debug recording of 2026-10-21 (Aahz, Fire Temple, zone 600022, 20:22 to 20:35).</summary>
public static class SelfCheckInstanceTimer
{
    private const string Entry = "0061d627090001c3a1b35fa1010000f316b45fa1010000";
    private const string Limit = "0061d6270900022517b45fa1010000a505eb5fa1010000";
    private const string Leave = "0061d627090003fbfabd5fa1010000bb22c75fa1010000";
    private const string Closing = "0061d627090004ba6bbf5fa1010000ca92bf5fa1010000";

    public static bool Run()
    {
        Console.WriteLine("[selftest] Instance timer (0x0061: entry wait, limit, time to leave, closing):");
        DateTime seen = new(2026, 10, 21, 18, 22, 57, DateTimeKind.Utc);
        bool Read(string hex, out InstanceTimer t) => Aion2InstanceTimer.TryParse(Convert.FromHexString(hex), seen, out t);

        bool entry = Read(Entry, out InstanceTimer e) && e.MapId == 600022 && e.Phase == InstancePhase.Entry && e.Length == TimeSpan.FromSeconds(30);
        bool limit = Read(Limit, out InstanceTimer l) && l.Phase == InstancePhase.Limit && l.Length == TimeSpan.FromSeconds(3600);
        bool leave = Read(Leave, out InstanceTimer v) && v.Phase == InstancePhase.Leave && v.Length == TimeSpan.FromSeconds(600);
        bool closing = Read(Closing, out InstanceTimer c) && c.Phase == InstancePhase.Closing && c.Length == TimeSpan.FromSeconds(10);
        bool remaining = limit && l.Remaining(l.StartUtc.AddMinutes(10)) == TimeSpan.FromMinutes(50) && l.Remaining(l.EndUtc.AddSeconds(5)) == TimeSpan.Zero;

        // a frame that is not read: truncated, an unknown phase, an end before the start
        byte[] bad = Convert.FromHexString(Limit);
        bool truncated = !Aion2InstanceTimer.TryParse(bad.AsSpan(0, bad.Length - 1), seen, out _);
        bad[6] = 9;
        bool phase = !Aion2InstanceTimer.TryParse(bad, seen, out _);
        byte[] backwards = Convert.FromHexString(Limit);
        Array.Copy(backwards, 7, backwards, 15, 8); // the end equals the start
        bool inverted = !Aion2InstanceTimer.TryParse(backwards, seen, out _);

        Aion2InstanceTimer.Reset();
        bool empty = Aion2InstanceTimer.Last is null;
        if (limit)
        {
            Aion2InstanceTimer.Receive(l);
        }

        bool kept = Aion2InstanceTimer.Last?.Phase == InstancePhase.Limit;
        Aion2InstanceTimer.Reset();

        bool ok = entry && limit && leave && closing && remaining && truncated && phase && inverted && empty && kept;
        Console.WriteLine($"  -> phases 30 s / 3600 s / 600 s / 10 s: {entry && limit && leave && closing}; time left: {remaining}; truncated, unknown phase, end before start not read: {truncated && phase && inverted}; last reading kept: {empty && kept}");
        Console.WriteLine(ok ? "[selftest] Instance timer OK" : "[selftest] Instance timer FAILED");
        return ok;
    }
}
