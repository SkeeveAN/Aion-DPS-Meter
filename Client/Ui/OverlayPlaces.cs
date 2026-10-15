namespace AionDPS.Ui;

/// <summary>
/// "Reset size and position" of one overlay: the place, the size and the scale it was given are cleared in the settings, and the open window takes
/// its start values again. An overlay is named by its window title; its settings have the same prefix (Left, Top, Width, Height, Scale).
/// </summary>
public static class OverlayPlaces
{
    public const string Assist = "Aion DPS Assist", Network = "Aion DPS Network", Dummy = "Aion DPS Dummy Test", WorldBoss = "Aion DPS World Boss", Hotkeys = "Aion DPS Hotkeys",
        Timetable = "Aion DPS Timetable", PetMap = "Aion DPS Pet Map", PetList = "Aion DPS Pet List", PetInfo = "Aion DPS Pet Farming",
        BossCast = "Aion DPS Boss Cast", BossHp = "Aion DPS Target Health", TargetOfTarget = "Aion DPS Combat";

    private static readonly Dictionary<string, string> Prefixes = new()
    {
        [Assist] = "AssistOverlay", [Network] = "NetworkOverlay", [Dummy] = "DummyOverlay", [WorldBoss] = "BossOverlay", [Hotkeys] = "HotkeyOverlay",
        [Timetable] = "Timetable", [PetMap] = "PetMap", [PetList] = "PetList", [PetInfo] = "PetFarm",
        [BossCast] = "CombatCastOverlay", [BossHp] = "CombatHpOverlay", [TargetOfTarget] = "CombatOverlay",
    };

    private static readonly Dictionary<string, string[]> CombatElements = new()
    {
        [BossCast] = new[] { CombatOverlayWindow.BossCastId },
        [BossHp] = new[] { CombatOverlayWindow.BossHpId },
        [TargetOfTarget] = new[] { CombatOverlayWindow.TargetIconId, CombatOverlayWindow.TargetNameId, CombatOverlayWindow.TargetHpId, CombatOverlayWindow.TargetEffectsId, CombatOverlayWindow.TargetBlockId },
    };

    /// <summary>An overlay was reset: its window (if open) puts the start values on again.</summary>
    public static event Action<string>? WasReset;

    public static void Reset(string title)
    {
        if (!Prefixes.TryGetValue(title, out string? prefix))
        {
            return;
        }

        var s = MeterSettings.Load();
        var type = typeof(MeterSettings);
        foreach (string part in new[] { "Left", "Top", "Width", "Height" })
        {
            var property = type.GetProperty(prefix + part);
            if (property is not null && property.PropertyType == typeof(double?))
            {
                property.SetValue(s, null);
            }
        }

        type.GetProperty(prefix + "Scale")?.SetValue(s, 1.0);
        if (CombatElements.TryGetValue(title, out string[]? ids))
        {
            foreach (string id in ids)
            {
                s.CombatElements.Remove(id);
            }
        }

        s.OverlayShifts.Remove(title);
        s.Save();
        WasReset?.Invoke(title);
    }
}
