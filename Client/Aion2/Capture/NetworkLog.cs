using System.IO;

namespace AionDPS.Aion2.Capture;

/// <summary>
/// A local text log of the network analysis (never uploaded), next to the settings (%AppData%\Aion DPS Meter\network.log):
/// every stall of the game connection with what the PC saw during it, and every time the router or the internet did not answer.
/// What the overlay shows is gone a minute later; this is what to look at after a freeze. Trimmed when it grows.
/// </summary>
public static class NetworkLog
{
    private static readonly object Gate = new();

    /// <summary>A test writes to its own file instead of the real log.</summary>
    public static string? PathOverride { get; set; }

    public static string Path_ => PathOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aion DPS Meter", "network.log");

    public static void Write(string message, DateTime? at = null)
    {
        try
        {
            lock (Gate)
            {
                string path = Path_;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > 400_000)
                {
                    File.WriteAllLines(path, File.ReadLines(path).TakeLast(2000));
                }

                File.AppendAllText(path, $"{at ?? DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
