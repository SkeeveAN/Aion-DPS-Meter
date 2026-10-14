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

    private static Color? _barOverride;
    private static Dictionary<string, Color> _classOverrides = new(StringComparer.Ordinal);

    /// <summary>The class names that have a colour, in the order the theme editor lists them.</summary>
    public static IReadOnlyList<string> Names { get; } = new[] { "Gladiator", "Templar", "Assassin", "Ranger", "Sorcerer", "Spiritmaster", "Cleric", "Chanter", "Brawler" };

    /// <summary>The user's own colours for single classes ("#RRGGBB" by class name); the one-colour-for-all setting wins over them.</summary>
    public static void SetClassOverrides(IReadOnlyDictionary<string, string>? colors)
    {
        var parsed = new Dictionary<string, Color>(StringComparer.Ordinal);
        if (colors is not null)
        {
            foreach (var (name, hex) in colors)
            {
                if (Colors.ContainsKey(name) && ThemeManager.TryParse(hex) is Color c)
                {
                    parsed[name] = c;
                }
            }
        }

        lock (Cache)
        {
            _classOverrides = parsed;
            Cache.Clear();
            SolidCache.Clear();
        }
    }

    /// <summary>The colour in use for a class: the one-for-all bar colour, the class's own colour or the standard one.</summary>
    private static Color Effective(string key) =>
        _barOverride ?? (key.Length > 0 && _classOverrides.TryGetValue(key, out Color own) ? own : (Color)ColorConverter.ConvertFromString(key.Length > 0 ? Colors[key] : Unknown));

    /// <summary>One colour for every class (the user's own bar colour), or null for the class colours.</summary>
    public static void SetBarOverride(Color? color)
    {
        lock (Cache)
        {
            _barOverride = color;
            Cache.Clear();
            SolidCache.Clear();
        }
    }

    private static readonly Dictionary<string, Brush> SolidCache = new();

    /// <summary>The class's own colour, whatever bar colour the user set (the theme editor's preview shows both).</summary>
    public static Color ClassColor(string? className) =>
        (Color)ColorConverter.ConvertFromString(className is not null && Colors.TryGetValue(className, out string? hex) ? hex : Unknown);

    /// <summary>The class colour without the translucency (a dot on the replay map).</summary>
    public static Brush Solid(string? className)
    {
        string key = className is not null && Colors.ContainsKey(className) ? className : "";
        lock (SolidCache)
        {
            if (!SolidCache.TryGetValue(key, out Brush? brush))
            {
                brush = new SolidColorBrush(Effective(key));
                brush.Freeze();
                SolidCache[key] = brush;
            }

            return brush;
        }
    }

    public static Brush For(string? className)
    {
        string key = className is not null && Colors.ContainsKey(className) ? className : "";
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out Brush? brush))
            {
                var color = Effective(key);
                brush = new SolidColorBrush(Color.FromArgb(0x6B, color.R, color.G, color.B)); // 42 %: the bar fills the whole row now
                brush.Freeze();
                Cache[key] = brush;
            }

            return brush;
        }
    }
}
