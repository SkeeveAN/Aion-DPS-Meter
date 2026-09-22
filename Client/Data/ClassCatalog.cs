using System.IO;
using AionDPS.Game;

namespace AionDPS.Data;

/// <summary>
/// The playable classes of each game, by the internal English names this app uses everywhere
/// (icon files, ClassFilter tags, upload payloads, backend validation). Classic Aion's list is the
/// full 4.x roster - which of them a given private server actually offers is
/// Server/ServerClassAvailability's business. Aion 2 ships nine classes and, so far, no icon
/// files of our own, so <see cref="HasIcon"/> tells the UI when to fall back to text. Which of
/// the nine a given SERVER offers is Server/ServerClassAvailability's business (EU/NA launched
/// without Brawler).
/// </summary>
public static class ClassCatalog
{
    private static readonly string[] AionClasses =
    {
        "Aethertech", "Assassin", "Bard", "Chanter", "Cleric", "Gladiator", "Gunner",
        "Painter", "Ranger", "Sorcerer", "Spiritmaster", "Templar",
    };

    private static readonly string[] Aion2Classes =
    {
        "Assassin", "Brawler", "Chanter", "Cleric", "Elementalist", "Gladiator", "Ranger", "Sorcerer", "Templar",
    };

    public static IReadOnlyList<string> ClassesFor(GameKind game) => game == GameKind.Aion2 ? Aion2Classes : AionClasses;

    public static bool IsKnownClass(GameKind game, string className) => ClassesFor(game).Contains(className, StringComparer.Ordinal);

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
}
