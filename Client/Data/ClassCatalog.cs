using System.IO;
using System.Text.Json;

namespace AionDPS.Data;

/// <summary>
/// The nine playable Aion 2 classes by the internal English names this app uses everywhere (icon
/// files, ClassFilter tags, upload payloads, backend validation). <see cref="HasIcon"/> tells the UI
/// when a class has no icon file of its own and a text badge is shown instead.
/// <see cref="DisplayName"/> is the one place these get translated for the player - everywhere
/// else stays the English wire format regardless of UI language.
/// </summary>
public static class ClassCatalog
{
    private static readonly string[] Aion2Classes =
    {
        "Assassin", "Brawler", "Chanter", "Cleric", "Elementalist", "Gladiator", "Ranger", "Sorcerer", "Templar",
    };

    public static IReadOnlyList<string> Classes => Aion2Classes;

    public static bool IsKnownClass(string className) => Aion2Classes.Contains(className, StringComparer.Ordinal);

    /// <summary>Short badge text for a class without an icon ("ELE", "FTR") - same abbreviations the
    /// website uses (Web-Frontend/app.js AION2_CLASS_ABBREVIATIONS).</summary>
    public static string Abbreviation(string className) => className switch
    {
        "Assassin" => "ASN",
        "Chanter" => "CHA",
        "Cleric" => "CLR",
        "Elementalist" => "ELE",
        "Brawler" => "BRW",
        "Gladiator" => "GLA",
        "Ranger" => "RNG",
        "Sorcerer" => "SOR",
        "Templar" => "TPL",
        _ => className.Length <= 3 ? className.ToUpperInvariant() : className[..3].ToUpperInvariant(),
    };

    /// <summary>Whether assets/classes/icons/{name}.png exists for this class.</summary>
    public static bool HasIcon(string className) =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "assets", "classes", "icons", className + ".png"));

    // Deliberately its own small table (assets/classes/class_names_i18n.json), not
    // assets/classes/class_names_multilang.json - that older file only covers 4 of the 8 UI
    // languages and, per the user's own investigation, is sorted alphabetically per language
    // rather than aligned to any class list, so matching it up by position or by the OLD
    // pre-rename English names (Gunslinger/Muse/Songweaver, since renamed to Gunner/Painter/Bard)
    // would silently mislabel classes. This table is keyed by the CURRENT English name instead.
    private static readonly Dictionary<string, Dictionary<string, string>> DisplayNames = LoadDisplayNames();

    private static Dictionary<string, Dictionary<string, string>> LoadDisplayNames()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "classes", "class_names_i18n.json");
            if (!File.Exists(path))
            {
                return new();
            }

            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A missing or corrupt table must not stop the meter from starting - callers fall
            // back to the raw (English) class name either way, same as LocalizationManager does.
            return new();
        }
    }

    /// <summary>Localized class name for display (Settings > Characters' class picker) - everything
    /// else in this class (Tag, icon lookup, Abbreviation, uploads) keeps using the raw English
    /// <paramref name="className"/> regardless; only what the player reads changes. Falls back to
    /// English, then to the raw name itself, for a language/class this table has no entry for.</summary>
    public static string DisplayName(string className, string languageCode) =>
        DisplayNames.TryGetValue(className, out var byLanguage)
            ? (byLanguage.TryGetValue(languageCode, out var s) ? s : byLanguage.GetValueOrDefault("en", className))
            : className;
}
