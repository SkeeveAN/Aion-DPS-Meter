using System.Text.Json;

namespace AionSniffer.Ui;

/// <summary>
/// Settings for the meter UI. Scoped deliberately to what the tool actually has a data source
/// for right now. MyAion's settings dialog (the reference the user shared) has a lot more:
/// legion/position columns, loot+kinah tracking, auto-upload to a backend, a donation goal --
/// none of that has a packet source wired up yet, so it's left out here rather than added as
/// inert checkboxes that would silently do nothing.
/// </summary>
public sealed class MeterSettings
{
    // Target (NPC) bar
    public bool ShowAutoDetectedRankedBosses { get; set; } = true;
    public bool ShowManuallySelectedTarget { get; set; } = true;
    public bool ShowAutoDetectedTarget { get; set; } = true;

    // Players list columns
    public bool ShowDps { get; set; } = true;
    public bool ShowLevel { get; set; } = true;

    // Targets list filters (which NPC ranks are shown at all)
    public bool ShowPlayers { get; set; } = true;
    public bool ShowMinionNpcs { get; set; } = true;
    public bool ShowCommonNpcs { get; set; } = true;
    public bool ShowEliteNpcs { get; set; } = true;
    public bool ShowHeroicNpcs { get; set; } = true;
    public bool ShowLegendaryNpcs { get; set; } = true;

    // User interface
    public string Theme { get; set; } = "Dark";
    public string FontSize { get; set; } = "Medium";

    /// <summary>SharpPcap device name chosen in the Network settings dialog. Persisted, but not
    /// yet consumed anywhere -- the console entry point still takes its device index from the
    /// command line (see README's UI-placeholders section).</summary>
    public string? SelectedCaptureDeviceName { get; set; }

    private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "meter-settings.json");

    public static MeterSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<MeterSettings>(File.ReadAllText(SettingsPath));
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to defaults -- a corrupt settings file should not block the app from starting.
        }

        return new MeterSettings();
    }

    public void Save()
    {
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
