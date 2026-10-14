using System.Windows;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// Swaps the color theme and base font size at runtime. Every window's XAML refers to
/// {DynamicResource Brush.X}/Color.X/FontSize.Base rather than literal colors, and those keys live
/// in one merged dictionary per theme (Ui/Themes/*.xaml) - replacing that dictionary repaints
/// every open window, including the click-through overlay chips. Program.cs applies the saved
/// choice before the first window exists; MainWindow re-applies it when Settings are saved.
/// MeterSettings.Theme/FontSize had a Settings dropdown for a long time without anything reading
/// them - this is what finally does.
/// </summary>
public static class ThemeManager
{
    public static readonly IReadOnlyList<string> Themes = new[] { "Dark", "Light", "Midnight", "Elyos", "Asmodian", "HighContrast" };

    public const string DefaultTheme = "Dark";

    /// <summary>Every token a theme must define - SelfCheck verifies each dictionary against this list.</summary>
    public static readonly IReadOnlyList<string> Tokens = new[]
    {
        "Window", "TitleBar", "Panel", "RowAlt", "Control", "ControlRaised", "ControlHover", "ControlHoverStrong",
        "Border", "ScrollThumb", "CheckBorder", "CheckBorderHover", "TextDisabled", "TextMuted", "TextSubtle", "Text",
        "Accent", "RowEnemy", "OverlayBg", "OverlayEnemy", "OverlayText", "Warning", "WarningSoft", "Danger", "DangerSoft",
    };

    private static ResourceDictionary? _active;

    /// <summary>Unknown names (a settings file from a build with different themes) fall back to Dark
    /// rather than leaving every brush unresolved - an unresolved DynamicResource renders as nothing.</summary>
    public static string Normalize(string? theme) =>
        Themes.FirstOrDefault(t => string.Equals(t, theme, StringComparison.OrdinalIgnoreCase)) ?? DefaultTheme;

    public static double FontSizeFor(string? fontSize) => fontSize?.ToLowerInvariant() switch
    {
        "small" => 11.0,
        "large" => 14.0,
        _ => 12.0,
    };

    /// <summary>The chosen theme plus the user's own colours and font size from the settings.</summary>
    public static void Apply(Application app, MeterSettings settings)
    {
        ThemeFile? look = ThemeFile.Resolve(settings); // "Personal" or a saved theme file; null for a built-in theme
        Apply(app, settings.Theme, settings.FontSize, look?.ToOverrides(), look?.Bar, look?.FontSize, look?.Classes);
    }

    public static void Apply(Application app, string? theme, string? fontSize, ThemeOverrides? overrides = null, string? bar = null, double? fontPoints = null, IReadOnlyDictionary<string, string>? classColors = null)
    {
        ResourceDictionary dictionary = Build(theme, overrides);
        ClassBrushes.SetClassOverrides(classColors);
        ClassBrushes.SetBarOverride(TryParse(bar));
        if (_active is not null)
        {
            app.Resources.MergedDictionaries.Remove(_active);
        }

        app.Resources.MergedDictionaries.Add(dictionary);
        _active = dictionary;
        double size = FontPointsFor(fontSize, fontPoints);
        app.Resources["FontSize.Base"] = size;
        AddOverlayFontSizes(app.Resources, size);
    }

    public static double FontPointsFor(string? fontSize, double? fontPoints) =>
        fontPoints is double pt ? Math.Clamp(pt, MinFontPoints, MaxFontPoints) : FontSizeFor(fontSize);

    /// <summary>A theme's dictionary with the user's own colours laid over it. Not applied to
    /// anything: the theme editor's preview hangs it on its own panel, Apply on the whole application.
    /// The background colour drives the surfaces first; the single parts the user set win after it.</summary>
    public static ResourceDictionary Build(string? theme, ThemeOverrides? own)
    {
        ResourceDictionary dictionary = Load(Normalize(theme));
        // switch and PVE/PVP button are not in every theme file: they follow the accent unless set
        Set(dictionary, "Switch", (Color)dictionary["Color.Accent"]);
        Set(dictionary, "Toggle", (Color)dictionary["Color.Accent"]);
        if (own is null)
        {
            return dictionary;
        }

        Color? bg = TryParse(own.Background);
        if (bg is Color b)
        {
            ApplyBackground(dictionary, b);
        }

        Color window = (Color)dictionary["Color.Window"];
        if (TryParse(own.Panel) is Color panel)
        {
            Set(dictionary, "Panel", panel);
            Set(dictionary, "RowAlt", Mix(panel, window, 0.5));
        }

        if (TryParse(own.Control) is Color control)
        {
            Color toward = IsDark(control) ? Colors.White : Colors.Black;
            Set(dictionary, "Control", control);
            Set(dictionary, "ControlRaised", Mix(control, toward, 0.05));
            Set(dictionary, "ControlHover", Mix(control, toward, 0.05));
            Set(dictionary, "ControlHoverStrong", Mix(control, toward, 0.12));
        }

        if (TryParse(own.TitleBar) is Color title)
        {
            Set(dictionary, "TitleBar", title);
        }

        if (TryParse(own.Switch) is Color sw)
        {
            Set(dictionary, "Switch", sw);
        }

        if (TryParse(own.Toggle) is Color toggle)
        {
            Set(dictionary, "Toggle", toggle);
        }

        if (TryParse(own.Text) is Color f)
        {
            ApplyText(dictionary, f, bg ?? window);
        }

        return dictionary;
    }

    /// <summary>The overlay texts' sizes scaled by the base size (12 pt = their standard sizes).</summary>
    public static void AddOverlayFontSizes(ResourceDictionary resources, double basePoints)
    {
        foreach (double pt in OverlayPictures.TextSizes)
        {
            resources[OverlayPictures.SizeKey(pt)] = pt * basePoints / 12.0;
        }
    }

    public const double MinFontPoints = 9;
    public const double MaxFontPoints = 20;

    /// <summary>"#RRGGBB" (or "#AARRGGBB") to a colour; null for empty or invalid text.</summary>
    public static Color? TryParse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        try
        {
            return (Color)ColorConverter.ConvertFromString(hex.Trim());
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static Color Mix(Color a, Color b, double amount) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * amount),
        (byte)Math.Round(a.G + (b.G - a.G) * amount),
        (byte)Math.Round(a.B + (b.B - a.B) * amount));

    private static bool IsDark(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) < 140;

    private static void Set(ResourceDictionary d, string token, Color c)
    {
        d[$"Color.{token}"] = c;
        d[$"Brush.{token}"] = new SolidColorBrush(c);
    }

    /// <summary>One background colour drives the surfaces: panels, rows, controls and borders step
    /// away from it toward white on a dark background and toward black on a light one.</summary>
    private static void ApplyBackground(ResourceDictionary d, Color bg)
    {
        Color toward = IsDark(bg) ? Colors.White : Colors.Black;
        Set(d, "Window", bg);
        Set(d, "TitleBar", IsDark(bg) ? Mix(bg, Colors.Black, 0.3) : Mix(bg, Colors.Black, 0.06));
        Set(d, "Panel", Mix(bg, toward, 0.06));
        Set(d, "RowAlt", Mix(bg, toward, 0.03));
        Set(d, "Control", Mix(bg, toward, 0.09));
        Set(d, "ControlRaised", Mix(bg, toward, 0.13));
        Set(d, "ControlHover", Mix(bg, toward, 0.13));
        Set(d, "ControlHoverStrong", Mix(bg, toward, 0.2));
        Set(d, "Border", Mix(bg, toward, 0.2));
        Set(d, "ScrollThumb", Mix(bg, toward, 0.28));
        Set(d, "CheckBorder", Mix(bg, toward, 0.2));
        Set(d, "CheckBorderHover", Mix(bg, toward, 0.35));
        Color overlay = Color.FromArgb(0x99, bg.R, bg.G, bg.B);
        d["Color.OverlayBg"] = overlay;
        d["Brush.OverlayBg"] = new SolidColorBrush(overlay);
    }

    private static void ApplyText(ResourceDictionary d, Color text, Color bg)
    {
        Set(d, "Text", text);
        Set(d, "OverlayText", text);
        Set(d, "TextSubtle", Mix(text, bg, 0.25));
        Set(d, "TextMuted", Mix(text, bg, 0.45));
        Set(d, "TextDisabled", Mix(text, bg, 0.62));
    }

    /// <summary>Loads a theme's compiled dictionary. Relative component URI, not a pack URI with the
    /// assembly name: the assembly is called "Aion DPS", and a space inside a pack authority is
    /// one more thing that can go wrong for no gain.</summary>
    public static ResourceDictionary Load(string theme) =>
        (ResourceDictionary)Application.LoadComponent(new Uri($"/Ui/Themes/{Normalize(theme)}.xaml", UriKind.Relative));

    /// <summary>Tokens a theme leaves undefined - empty for a complete theme.</summary>
    public static IReadOnlyList<string> MissingTokens(string theme)
    {
        ResourceDictionary dictionary = Load(theme);
        return Tokens
            .SelectMany(t => new[] { $"Color.{t}", $"Brush.{t}" })
            .Where(key => !dictionary.Contains(key))
            .ToList();
    }
}

/// <summary>The colours the user set over a theme ("#RRGGBB" or null for the theme's own).</summary>
public sealed record ThemeOverrides(string? Background, string? TitleBar, string? Panel, string? Control, string? Switch, string? Toggle, string? Text);
