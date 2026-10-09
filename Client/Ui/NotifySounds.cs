using System.IO;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>The short notification sounds (assets/sounds/*.wav) of the timetable reminders: the list for the settings and playing one at a volume.</summary>
public static class NotifySounds
{
    /// <summary>The ids of the sounds, in the order the settings offer them.</summary>
    public static readonly string[] All = { "chime", "bell", "ping", "blip", "drop", "horn" };

    private static readonly List<MediaPlayer> Playing = new();

    /// <summary>Plays a sound (volume 0 to 100); an unknown id plays nothing.</summary>
    public static void Play(string id, int volume)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "sounds", id + ".wav");
        if (!All.Contains(id) || !File.Exists(path) || volume <= 0)
        {
            return;
        }

        var player = new MediaPlayer { Volume = Math.Clamp(volume, 0, 100) / 100.0 };
        void Done()
        {
            player.Close();
            Playing.Remove(player);
        }

        player.MediaEnded += (_, _) => Done();
        player.MediaFailed += (_, _) => Done();
        Playing.Add(player); // kept until it ends, else it would be collected mid-sound
        player.Open(new Uri(path));
        player.Play();
    }
}
