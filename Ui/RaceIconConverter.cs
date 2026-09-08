using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace AionSniffer.Ui;

/// <summary>
/// Faction name -> its emblem, the same assets/races/icons/*.png files the settings dialog uses.
/// Deliberately a sibling of ClassIconConverter rather than a parameter on it: the two icon sets
/// live in different folders and a null here means "faction unknown", which is a normal state
/// (nobody has healed or fought this player yet) rather than a missing asset.
/// </summary>
public sealed class RaceIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string faction || faction.Length == 0)
        {
            return null;
        }

        string path = Path.Combine(AppContext.BaseDirectory, "assets", "races", "icons", $"{faction}.png");
        return File.Exists(path) ? new BitmapImage(new Uri(path, UriKind.Absolute)) : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
