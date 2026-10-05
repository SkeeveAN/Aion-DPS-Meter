using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>The translucent class colours behind a player's row; the same hues as the website (class pairs share a hue family - Cleric/Chanter yellow, Gladiator/Templar blue, Assassin/Ranger green, Sorcerer/Spiritmaster purple - in a lighter and a deeper shade)'s meter
/// (app.js CLASS_META), so a class reads the same colour in both.</summary>
internal static class ClassBrushes
{
    private static readonly Dictionary<string, Brush> Cache = new();

    private static readonly Dictionary<string, string> Colors = new(StringComparer.Ordinal)
    {
        ["Cleric"] = "#ffe27a",
        ["Chanter"] = "#e0a020",
        ["Templar"] = "#2f5fd0",
        ["Gladiator"] = "#5aa9ff",
        ["Brawler"] = "#e07a5f",
        ["Assassin"] = "#7ee08f",
        ["Ranger"] = "#2f9e57",
        ["Sorcerer"] = "#b98af5",
        ["Spiritmaster"] = "#8a4fd8",
    };

    private const string Unknown = "#9fb3c8";

    public static Brush For(string? className)
    {
        string key = className is not null && Colors.ContainsKey(className) ? className : "";
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out Brush? brush))
            {
                var color = (Color)ColorConverter.ConvertFromString(key.Length > 0 ? Colors[key] : Unknown);
                brush = new SolidColorBrush(Color.FromArgb(0x47, color.R, color.G, color.B)); // 28 % like the website
                brush.Freeze();
                Cache[key] = brush;
            }

            return brush;
        }
    }
}
