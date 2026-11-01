using System.IO;
using System.Text;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// Notes the raw bytes of an arcana entry of the login list that the decoder could not read (the piece then shows no experience,
/// Lord or skills). One line per piece and session, at most 16 KB in the file, only on this computer and never uploaded: the player
/// can send the file so the layout of that entry can be added to the decoder.
/// </summary>
internal static class ArcanaUnreadLog
{
    private const int MaxBytes = 16 * 1024;
    private const int Window = 160;
    private static readonly object Gate = new();
    private static readonly HashSet<int> Noted = new();

    public static string Path => System.IO.Path.Combine(Ui.MeterSettings.DataDirectory, "arcana-unread.log");

    /// <param name="sizePos">The entry's size byte; the dump starts 14 bytes before it (at the level byte's neighbourhood).</param>
    public static void Note(int itemId, int slot, ReadOnlySpan<byte> frame, int sizePos)
    {
        try
        {
            lock (Gate)
            {
                if (!Noted.Add(itemId) || (File.Exists(Path) && new FileInfo(Path).Length > MaxBytes))
                {
                    return;
                }

                int from = Math.Max(0, sizePos - 14);
                int count = Math.Min(Window, frame.Length - from);
                var hex = new StringBuilder(count * 2);
                for (int i = 0; i < count; i++)
                {
                    hex.Append(frame[from + i].ToString("x2"));
                }

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} item {itemId} slot {slot} size byte at +14 of the dump: {hex}\n");
            }
        }
        catch
        {
            // a diagnostic must never disturb the reading of the login
        }
    }
}
