using System.Text.Json;

namespace Aion2G13Plugin;

public sealed class Rule
{
    /// <summary>"none", "hotkey" or "program".</summary>
    public string Kind { get; set; } = "none";
    public string Value { get; set; } = "";
}

public sealed class Settings
{
    public static readonly string[] Classes = { "Gladiator", "Templar", "Ranger", "Assassin", "Spiritmaster", "Sorcerer", "Cleric", "Chanter" };

    public string Link { get; set; } = "";
    public Dictionary<string, Rule> Rules { get; set; } = new();

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aion2G13Plugin", "settings.json");

    public Rule RuleFor(string cls)
    {
        if (!Rules.TryGetValue(cls, out var rule))
        {
            rule = new Rule();
            Rules[cls] = rule;
        }

        return rule;
    }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) is { } loaded)
            {
                return loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
