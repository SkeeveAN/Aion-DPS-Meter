using System.IO;

namespace AionDPS.Ui;

/// <summary>A short text log of what the automatic boss upload decided (queued, started, sent, why not),
/// next to the settings, so "it did not upload by itself" can be traced afterwards. Trimmed when it grows.</summary>
public static class AutoUploadLog
{
    private static readonly object Gate = new();

    private static string Path_ => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aion DPS Meter", "auto-upload.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                string path = Path_;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > 200_000)
                {
                    File.WriteAllLines(path, File.ReadLines(path).TakeLast(500));
                }

                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
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
