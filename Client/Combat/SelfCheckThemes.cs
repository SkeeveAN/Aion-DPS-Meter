using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>
/// Every theme must define every token: a missing Brush.X does not throw at runtime, it silently
/// renders that element with no brush at all - a control that vanishes only when one particular
/// theme is picked is exactly the bug a checklist catches and a screenshot does not.
/// </summary>
public static class SelfCheckThemes
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Theme dictionaries (every theme defines every token):");
        bool ok = true;
        foreach (string theme in ThemeManager.Themes)
        {
            IReadOnlyList<string> missing;
            try
            {
                missing = ThemeManager.MissingTokens(theme);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  -> {theme}: failed to load ({ex.GetType().Name}: {ex.Message})");
                ok = false;
                continue;
            }

            bool complete = missing.Count == 0;
            Console.WriteLine($"  -> {theme} complete: {complete}{(complete ? "" : " (missing: " + string.Join(", ", missing) + ")")}");
            ok &= complete;
        }

        bool fallback = ThemeManager.Normalize("no-such-theme") == ThemeManager.DefaultTheme && ThemeManager.Normalize("light") == "Light";
        bool sizes = ThemeManager.FontSizeFor("Small") < ThemeManager.FontSizeFor("Medium") && ThemeManager.FontSizeFor("Medium") < ThemeManager.FontSizeFor("Large");
        Console.WriteLine($"  -> unknown theme name falls back to {ThemeManager.DefaultTheme}, names are case-insensitive: {fallback}");
        Console.WriteLine($"  -> font sizes ordered Small < Medium < Large: {sizes}");
        return ok && fallback && sizes;
    }
}
