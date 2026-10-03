using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>The translucent class colours behind a player's row; the same hues as the website's meter
/// (app.js CLASS_META), so a class reads the same colour in both.</summary>
internal static class ClassBrushes
{
    private static readonly Dictionary<string, Brush> Cache = new();

    private static readonly Dictionary<string, string> Colors = new(StringComparer.Ordinal)
    {
        ["Cleric"] = "#ffd166",
        ["Chanter"] = "#06d6a0",
        ["Templar"] = "#5b8fb9",
        ["Gladiator"] = "#ef476f",
        ["Brawler"] = "#e07a5f",
        ["Assassin"] = "#9b5de5",
        ["Ranger"] = "#80ed99",
        ["Sorcerer"] = "#4cc9f0",
        ["Spiritmaster"] = "#7c9eff",
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
