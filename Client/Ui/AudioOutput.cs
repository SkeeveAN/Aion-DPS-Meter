using Windows.Devices.Enumeration;
using Windows.Media.Devices;
using Windows.Media.Playback;

namespace AionDPS.Ui;

/// <summary>One audio output the system offers, as the settings list it.</summary>
public sealed record AudioDevice(string Id, string Name);

/// <summary>The audio output all sounds of the meter (voice and notification sounds) play on: the chosen device, else the Windows default.</summary>
public static class AudioOutput
{
    /// <summary>The chosen device (<c>DeviceInformation.Id</c>); empty = the Windows default device. Set when the settings are loaded or saved.</summary>
    public static string DeviceId { get; set; } = "";

    /// <summary>The audio outputs the system has; empty when they cannot be listed.</summary>
    public static IReadOnlyList<AudioDevice> Devices()
    {
        try
        {
            var found = Task.Run(async () => await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector())).GetAwaiter().GetResult();
            return found.Select(d => new AudioDevice(d.Id, d.Name)).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Array.Empty<AudioDevice>();
        }
    }

    /// <summary>Points a player at a device (empty or gone = it stays on the Windows default).</summary>
    public static async Task ApplyAsync(MediaPlayer player, string deviceId)
    {
        if (deviceId.Length == 0)
        {
            return;
        }

        try
        {
            player.AudioDevice = await DeviceInformation.CreateFromIdAsync(deviceId);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // the device is not connected (any more): the Windows default plays it
        }
    }
}
