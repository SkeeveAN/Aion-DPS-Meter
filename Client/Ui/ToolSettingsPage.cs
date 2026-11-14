using System.Windows;
using System.Windows.Controls;

namespace AionDPS.Ui;

/// <summary>
/// The page of one tool under Settings > Tools of the new interface: the switch that takes the tool out of the menus, then the tool's own settings (the same cards as the
/// Configuration tab of the tool). A tool with nothing more to set says so.
/// </summary>
public static class ToolSettingsPage
{
    public static ToolPage Build(string tool, Action<MeterSettings> applied, Func<string, UIElement?> panelOf, Func<UIElement?> craftConfig, Action? cleanupExtra)
    {
        if (tool is "petmap.pets" or "petmap.gather")
        {
            return PanelTab(panelOf(tool == "petmap.pets" ? "PanelPets" : "PanelGather"), () => cleanupExtra?.Invoke());
        }

        var loc = LocalizationManager.Instance;
        var c = new ConfigControls(applied);
        string nameKey = ToolSwitch.All.First(t => t.Id == tool).NameKey, descKey = ToolSwitch.All.First(t => t.Id == tool).DescKey;
        var stack = new StackPanel { Margin = new Thickness(14, 10, 14, 14) };
        UIElement? more = tool switch
        {
            "timetable" => ToolConfigs.Timetable(applied),
            "boss" => ToolConfigs.Boss(applied),
            "combat" => ToolConfigs.Combat(applied),
            "loot" => ToolConfigs.Loot(applied),
            "network" => ToolConfigs.Network(applied),
            "stream" => ToolConfigs.Stream(applied),
            "craft" => craftConfig(),
            "petmap" => ToolConfigs.PetMap(applied), // the pets and the gathering have their own tabs
            _ => null,
        };
        if (more is not null)
        {
            stack.Children.Add(more);
        }
        else
        {
            stack.Children.Add(ConfigControls.Note(loc["Settings.Tool.NoMore"]));
        }

        return new ToolPage { Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = stack }, Cleanup = cleanupExtra };
    }

    /// <summary>A page of the interactive map that is a panel of the settings window (the pets, the gathering), as a tab of its own.</summary>
    public static ToolPage PanelTab(UIElement? body, Action cleanup)
    {
        var stack = new StackPanel { Margin = new Thickness(14, 10, 14, 14) };
        if (body is not null)
        {
            stack.Children.Add(body);
        }

        return new ToolPage { Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = stack }, Cleanup = cleanup };
    }
}
