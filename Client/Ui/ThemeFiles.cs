using System.IO;
using System.Text.Json;

namespace AionDPS.Ui;

/// <summary>
/// The user's own look as a file (.aiondps-theme, JSON): colours, bar colours per class and the font size. The theme editor writes one on every
/// save into the "themes" folder of the settings directory; the Settings list offers each one by its file name. <see cref="MeterSettings.Theme"/>
/// holds "Dark" or "File:name".
/// </summary>
public sealed class ThemeFile
{
    public const string Extension = ".aiondps-theme";
    public const string FilePrefix = "File:";

    /// <summary>The name the user gave the theme (also the file name in the themes folder).</summary>
    public string? Name { get; set; }
    public string? Background { get; set; }
    public string? TitleBar { get; set; }
    public string? Panel { get; set; }
    public string? Control { get; set; }
    public string? Switch { get; set; }
    public string? Toggle { get; set; }
    public string? Bar { get; set; }
    public string? Text { get; set; }
    public double? FontSize { get; set; }
    public Dictionary<string, string>? Classes { get; set; }

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public static string Folder => Path.Combine(MeterSettings.DataDirectory, "themes");

    public ThemeOverrides ToOverrides() => new(Background, TitleBar, Panel, Control, Switch, Toggle, Text);

    public static string SelectionFor(string fileName) => FilePrefix + fileName;

    public static string PathFor(string name) => Path.Combine(Folder, name + Extension);

    /// <summary>A usable file name from what the user typed; "" when nothing is left.</summary>
    public static string CleanName(string? typed)
    {
        string name = string.Concat((typed ?? "").Where(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) < 0)).Trim().TrimEnd('.');
        return name.Length > 40 ? name[..40].Trim() : name;
    }

    public static bool IsReserved(string name) =>
        string.Equals(ThemeManager.DefaultTheme, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>The names of the saved theme files, alphabetical.</summary>
    public static IReadOnlyList<string> List()
    {
        try
        {
            return Directory.Exists(Folder)
                ? Directory.GetFiles(Folder, "*" + Extension).Select(Path.GetFileNameWithoutExtension).Where(n => n is { Length: > 0 }).Select(n => n!).Order(StringComparer.CurrentCultureIgnoreCase).ToList()
                : Array.Empty<string>();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }

    public static ThemeFile? Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<ThemeFile>(File.ReadAllText(path), ReadOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool Write(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, WriteOptions));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The look the settings choose: a saved theme file's, or null for the built-in theme. A file that has gone missing counts as the built-in theme.</summary>
    public static ThemeFile? Resolve(MeterSettings s) =>
        s.Theme.StartsWith(FilePrefix, StringComparison.Ordinal) ? Read(PathFor(s.Theme[FilePrefix.Length..])) : null;
}
