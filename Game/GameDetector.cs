using System.Diagnostics;

namespace AionDPS.Game;

/// <summary>
/// Tells which game's client process is currently running, for <see cref="GameDetectionMode.Automatic"/>.
/// Keys off each client's own executable name - Windows drops the extension for
/// <see cref="Process.ProcessName"/>, so "AION.bin" (classic Aion's 32-bit executable, see
/// SettingsWindow's own bin64\game.dll/AION.bin install-folder check) surfaces as "AION", and
/// "Aion2.exe" (the only place that name is actually stated anywhere public - found in a competing
/// Aion 2 tool's own process lookup, since there is no client to check it against yet, see the Aion
/// 2 expansion plan) surfaces as "Aion2". Only ever lists processes, same "look, don't touch" rule
/// Aion2PacketCombatSource already follows for packet capture - nothing here reads from, writes to,
/// or otherwise touches either game process.
/// </summary>
public static class GameDetector
{
    private const string Aion1ProcessName = "AION";
    private const string Aion2ProcessName = "Aion2";

    /// <summary>Null when neither client's process is currently running - callers keep whatever
    /// game was last active rather than falling back to a default the moment both clients close.</summary>
    public static GameKind? Detect()
    {
        if (IsRunning(Aion2ProcessName))
        {
            return GameKind.Aion2;
        }

        if (IsRunning(Aion1ProcessName))
        {
            return GameKind.Aion;
        }

        return null;
    }

    private static bool IsRunning(string processName)
    {
        Process[] processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }
}
