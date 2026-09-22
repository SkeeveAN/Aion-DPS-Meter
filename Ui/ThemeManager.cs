using System.Windows;

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

    public static void Apply(Application app, string? theme, string? fontSize)
    {
        ResourceDictionary dictionary = Load(Normalize(theme));
        if (_active is not null)
        {
            app.Resources.MergedDictionaries.Remove(_active);
        }

        app.Resources.MergedDictionaries.Add(dictionary);
        _active = dictionary;
        app.Resources["FontSize.Base"] = FontSizeFor(fontSize);
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
