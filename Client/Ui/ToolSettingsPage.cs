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
            "petmap" => PetMapStack(applied, panelOf),
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

    private static UIElement PetMapStack(Action<MeterSettings> applied, Func<string, UIElement?> panelOf)
    {
        var loc = LocalizationManager.Instance;
        var stack = new StackPanel();
        stack.Children.Add(ToolConfigs.PetMap(applied));
        foreach (var (panel, key) in new[] { ("PanelPets", "Settings.Nav.Pets"), ("PanelGather", "Settings.Nav.Gather") })
        {
            if (panelOf(panel) is { } body)
            {
                var head = new TextBlock { Text = loc[key], FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) };
                head.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
                stack.Children.Add(head);
                stack.Children.Add(body);
            }
        }

        return stack;
    }
}
