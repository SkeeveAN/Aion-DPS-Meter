namespace AionDPS.Ui;

/// <summary>"Tool active / inactive" of the settings: which tools are switched off, what a switched-off tool does not do any more.</summary>
public static class ToolSwitch
{
    /// <summary>Every tool of the Tools menu, in the order of the menu. (id, name key)</summary>
    public static readonly (string Id, string NameKey, string DescKey)[] All =
    {
        ("checklist", "Main.MenuTools.Checklist", "Tool.Desc.checklist"),
        ("attributes", "Main.MenuTools.Attributes", "Tool.Desc.attributes"),
        ("statcalc", "Main.MenuTools.StatCalc", "Tool.Desc.statcalc"),
        ("loot", "Main.MenuTools.Loot", "Tool.Desc.loot"),
        ("craft", "Main.MenuTools.Craft", "Tool.Desc.craft"),
        ("petmap", "Settings.Nav.PetMap", "Tool.Desc.petmap"),
        ("combat", "Settings.Nav.Combat", "Tool.Desc.combat"),
        ("assist", "Main.MenuTools.Assist", "Tool.Desc.assist"),
        ("dummy", "Main.MenuTools.DummyTest", "Tool.Desc.dummy"),
        ("analyzer", "Main.MenuTools.Analyzer", "Tool.Desc.analyzer"),
        ("timetable", "Settings.Nav.Timetable", "Tool.Desc.timetable"),
        ("boss", "Settings.Nav.Boss", "Tool.Desc.boss"),
        ("theme", "Main.MenuTools.ThemeEditor", "Tool.Desc.theme"),
        ("stream", "Main.MenuTools.Stream", "Tool.Desc.stream"),
        ("network", "Settings.Nav.Network", "Tool.Desc.network"),
    };

    /// <summary>The ids as the pet map's pages ("petmap.pets", "petmap.gather") belong to the pet map.</summary>
    public static bool IsOn(MeterSettings settings, string id)
    {
        int dot = id.IndexOf('.');
        string tool = dot < 0 ? id : id[..dot];
        return true; // there is no "tool active" switch any more: every tool is there, and an overlay is on only by having been added in the overlay designer
    }

    /// <summary>The tool a hotkey belongs to (null: a hotkey of the program itself).</summary>
    public static string? OfHotkey(HotkeyAction action) => action switch
    {
        HotkeyAction.ToggleTimetable => "timetable",
        HotkeyAction.TogglePetMap or HotkeyAction.TogglePetArrows or HotkeyAction.ToggleGatherArrows => "petmap",
        HotkeyAction.ToggleWorldBossOverlay => "boss",
        HotkeyAction.ToggleNetworkOverlay => "network",
        HotkeyAction.ToggleCombatOverlay => "combat",
        HotkeyAction.ToggleAssistOverlay => "assist",
        HotkeyAction.ToggleLootOverlay => "loot",
        HotkeyAction.ToggleCraftOverlay => "craft",
        _ => null,
    };

    /// <summary>Switches the overlays of the switched-off tools off (in memory; the settings window saves them).</summary>
    public static void Enforce(MeterSettings s)
    {
        // nothing: no tool is switched off, so no overlay is switched off with it
    }
}
