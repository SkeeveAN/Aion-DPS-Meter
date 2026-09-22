using System.IO;

namespace AionDPS.Aion2.Capture;

/// <summary>
/// Whether the Npcap driver is present. Checked before anything touches SharpPcap: its native
/// wpcap.dll load fails with a bare DllNotFoundException otherwise, which is a worse message than
/// "install Npcap". Detection is by the files the Npcap installer places, not the registry, so it
/// works unprivileged. The meter never installs the driver itself - a kernel driver is the user's
/// call, so the UI links to npcap.com instead.
/// </summary>
public sealed class NpcapAvailability
{
    private NpcapAvailability(bool installed, string? location)
    {
        IsInstalled = installed;
        Location = location;
    }

    public bool IsInstalled { get; }

    public string? Location { get; }

    public static string DownloadUrl => "https://npcap.com/#download";

    public static NpcapAvailability Detect()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new NpcapAvailability(false, null);
        }

        string system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string npcapDir = Path.Combine(system32, "Npcap");
        if (File.Exists(Path.Combine(npcapDir, "wpcap.dll")) && File.Exists(Path.Combine(npcapDir, "Packet.dll")))
        {
            return new NpcapAvailability(true, npcapDir);
        }

        // WinPcap-compatible mode installs the DLLs directly into System32.
        if (File.Exists(Path.Combine(system32, "wpcap.dll")))
        {
            return new NpcapAvailability(true, system32);
        }

        return new NpcapAvailability(false, null);
    }
}
