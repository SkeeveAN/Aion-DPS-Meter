using System.IO;
using System.Text.Json;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>One line of the list of a spot: a monster (with its pet), how many stand there and what the player has of the pet.</summary>
public sealed record FarmLegendLine(string Mob, string Pet, int Count, string Status, bool Missing);

/// <summary>One numbered spot of the list.</summary>
public sealed record FarmLegendSpot(int Number, bool Unsafe, List<FarmLegendLine> Lines);

/// <summary>What the picture of a map says, as text: the overlay and the widget of the list show it as text, not as a picture.</summary>
public sealed record FarmLegend(string Subtitle, bool GuardsKnown, List<FarmLegendSpot> Spots);

/// <summary>One finished map: its key, its name (in the language of the player), the picture of the map alone (overlay, widget), the picture with the list (saving) and the list as text.</summary>
public sealed record FarmImage(string Key, string Name, string Path, string FullPath, FarmLegend Legend);

/// <summary>What the generator hands over for one map.</summary>
public sealed record FarmMade(string Key, string Name, byte[] MapPng, byte[] FullPng, FarmLegend Legend);

/// <summary>
/// The pictures of the last "Generate images": one folder in the data directory (farm-maps), numbered in the order they were made, with an index of the names and the lists.
/// A new run replaces all of them (their number can be another). The overlays and the widgets show these files and follow <see cref="Changed"/>.
/// </summary>
public static class FarmMapStore
{
    public static string Folder => Path.Combine(MeterSettings.DataDirectory, "farm-maps");

    private static string IndexPath => Path.Combine(Folder, "index.json");

    private sealed class Row
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string Map { get; set; } = "";
        public string Full { get; set; } = "";
        public FarmLegend Legend { get; set; } = new("", false, new());
    }

    /// <summary>The set of pictures was replaced (the overlays and the widgets read it again).</summary>
    public static event Action? Changed;

    /// <summary>Replaces every picture with the new ones.</summary>
    public static IReadOnlyList<FarmImage> Replace(IReadOnlyList<FarmMade> images)
    {
        Directory.CreateDirectory(Folder);
        foreach (string old in Directory.GetFiles(Folder, "*.png"))
        {
            File.Delete(old);
        }

        var rows = new List<Row>();
        for (int i = 0; i < images.Count; i++)
        {
            string map = $"{i + 1:00}_{images[i].Key}.png", full = $"{i + 1:00}_{images[i].Key}_full.png";
            File.WriteAllBytes(Path.Combine(Folder, map), images[i].MapPng);
            File.WriteAllBytes(Path.Combine(Folder, full), images[i].FullPng);
            rows.Add(new Row { Key = images[i].Key, Name = images[i].Name, Map = map, Full = full, Legend = images[i].Legend });
        }

        File.WriteAllText(IndexPath, JsonSerializer.Serialize(rows));
        Changed?.Invoke();
        return Load();
    }

    /// <summary>The pictures as they are now (empty when none were made).</summary>
    public static IReadOnlyList<FarmImage> Load()
    {
        var result = new List<FarmImage>();
        try
        {
            if (File.Exists(IndexPath))
            {
                foreach (Row row in JsonSerializer.Deserialize<List<Row>>(File.ReadAllText(IndexPath)) ?? new())
                {
                    string map = Path.Combine(Folder, row.Map), full = Path.Combine(Folder, row.Full);
                    if (File.Exists(map) && File.Exists(full))
                    {
                        result.Add(new FarmImage(row.Key, row.Name, map, full, row.Legend));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // nothing usable: no pictures
        }

        return result;
    }

    /// <summary>Copies the pictures with their list into a folder, each named after its map in the player's language (MAP_NAME.png); returns how many were written.</summary>
    public static int SaveTo(string folder, IReadOnlyList<FarmImage> images)
    {
        Directory.CreateDirectory(folder);
        int written = 0;
        foreach (var image in images)
        {
            string name = string.Concat(image.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            File.Copy(image.FullPath, Path.Combine(folder, name + ".png"), true);
            written++;
        }

        return written;
    }

    /// <summary>The list of a plan as text data (the status of a pet in the language of the player).</summary>
    public static FarmLegend LegendOf(FarmPlan plan, string subtitle)
    {
        var loc = LocalizationManager.Instance;
        var spots = plan.Spots.Select(sp => new FarmLegendSpot(sp.Number, sp.Unsafe, sp.Lines.Select(l => new FarmLegendLine(
            l.Mob, l.Pet, l.Count,
            l.Missing ? loc["PetShop.Missing"] : string.Format(loc["PetFarm.Level"], l.Level) + $" · {l.Progress}/{Aion2Pets.ProgressNeeded(l.Level)}", l.Missing)).ToList())).ToList();
        return new FarmLegend(subtitle, plan.Guards.Count > 0, spots);
    }
}
