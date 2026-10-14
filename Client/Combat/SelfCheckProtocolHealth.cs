using System.IO;
using AionDPS.Aion2.Capture;

namespace AionDPS.Combat;

/// <summary>The protocol health monitor: a family that goes silent while the others flow is reported, and reported as back when it returns.</summary>
public static class SelfCheckProtocolHealth
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Protocol health monitor:");
        string file = Path.Combine(Path.GetTempPath(), $"protocol-health-{Guid.NewGuid():N}.log");
        ProtocolHealth.PathOverride = file;
        ProtocolHealth.Reset();
        try
        {
            var known = new HashSet<int> { 0x0438, 0x008d, 0x0238, 0x2b38 };
            DateTime t = new(2026, 10, 14, 20, 0, 0);

            // Six healthy windows: damage, hp, cast and refresh frames all come.
            for (int w = 0; w < 6; w++)
            {
                Window(t, w, damage: 80, known);
            }

            bool quietWhileHealthy = !ProtocolHealth.Reports.Any(r => r.StartsWith("PROTOCOL BREAK"));

            // The patch moves the damage opcode: windows without 0x0438, the others keep flowing, and a new opcode shows up.
            for (int w = 6; w < 6 + ProtocolHealth.SilentWindows + 1; w++)
            {
                Window(t, w, damage: 0, known);
                ProtocolHealth.Note(0x5a38, 30, t.AddSeconds(w * 30 + 1), known);
            }

            bool broken = ProtocolHealth.Reports.Count(r => r.StartsWith("PROTOCOL BREAK SUSPECTED: 0x0438")) == 1;
            bool newOpcode = ProtocolHealth.Reports.Count(r => r.StartsWith("NEW OPCODE 0x5a38")) == 1;

            // Back again.
            int next = 6 + ProtocolHealth.SilentWindows + 1;
            Window(t, next, damage: 80, known);
            Window(t, next + 1, damage: 80, known);
            bool recovered = ProtocolHealth.Reports.Any(r => r.StartsWith("PROTOCOL RECOVERED: 0x0438"));

            // A quiet period in which everything is silent (the fight is over) is not a break.
            ProtocolHealth.Reset();
            for (int w = 0; w < 6; w++)
            {
                Window(t, w, damage: 80, known);
            }

            for (int w = 6; w < 14; w++)
            {
                ProtocolHealth.Note(0x0036, 20, t.AddSeconds(w * 30 + 1), known);
            }

            bool idleIsNoBreak = !ProtocolHealth.Reports.Any(r => r.StartsWith("PROTOCOL BREAK"));

            bool logWritten = File.Exists(file) && File.ReadAllText(file).Contains("PROTOCOL CENSUS");
            bool ok = quietWhileHealthy && broken && newOpcode && recovered && idleIsNoBreak && logWritten;
            Console.WriteLine($"  -> healthy quiet={quietWhileHealthy}, break reported once={broken}, new opcode={newOpcode}, recovered={recovered}, idle is no break={idleIsNoBreak}, census in log={logWritten}: {(ok ? "OK" : "FAILED")}");
            return ok;
        }
        finally
        {
            ProtocolHealth.PathOverride = null;
            ProtocolHealth.Reset();
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    private static void Window(DateTime t0, int index, int damage, IReadOnlySet<int> known)
    {
        DateTime at = t0.AddSeconds(index * 30);
        for (int i = 0; i < damage; i++)
        {
            ProtocolHealth.Note(0x0438, 30, at.AddMilliseconds(i * 10), known);
        }

        for (int i = 0; i < 90; i++)
        {
            ProtocolHealth.Note(0x0238, 30, at.AddMilliseconds(i * 11), known);
            ProtocolHealth.Note(0x008d, 20, at.AddMilliseconds(i * 12), known);
            ProtocolHealth.Note(0x2b38, 20, at.AddMilliseconds(i * 13), known);
        }
    }
}
