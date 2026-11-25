using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Data;

namespace AionDPS.Ui;

/// <summary>What the target power widget shows: the marked player's name, class and combat power, and how that power stands against the local player's own
/// (<see cref="Ratio"/> = target / own; null while the own power is unknown). Power 0 = the game has not told it yet.</summary>
public sealed record TargetPowerData(string Name, string? ClassName, int Power, double? Ratio);

/// <summary>
/// The combat power of the player the local player marked (tab or click), as a widget (after Stacy's fork, 2026-11-13, MIT: "Jeane · Spiritmaster · 195.426", green when the
/// target is weaker than the local player, red when stronger, yellow when about the same). The game itself tells both the mark and the power (the "player appeared" frame, layout
/// by SkeeveTV): nothing is looked up anywhere. No target, a monster, the own character, the arena's fixed number: nothing to show. After a death the target stays (the killer).
/// </summary>
public static class TargetPowerView
{
    /// <summary>The fixed number the 10 against 10 arena gives everybody: not a real power.</summary>
    public const int ArenaPower = 397_228;

    /// <summary>Within 5 % either way the two powers count as equal.</summary>
    public const double EqualBand = 0.05;

    /// <summary>Set by the main window: what the widget shows now (null = nothing).</summary>
    public static Func<TargetPowerData?>? Provider { get; set; }

    /// <summary>The marked entity when it is another player; null for a monster, an unknown player, the own character, the arena's fixed number. It stays after the local player died: the game then marks the killer, so the dead player sees who it was.</summary>
    public static TargetPowerData? Build(Aion2EntityDirectory entities)
    {
        if (entities.LocalTarget is not { } target || entities.IsLocalPlayer(target.EntityId) || !entities.IsKnownPlayer(target.EntityId))
        {
            return null;
        }

        string? name = entities.NameFor(target.EntityId);
        if (name is null || name.StartsWith("Player #", StringComparison.Ordinal))
        {
            return null;
        }

        int power = entities.CombatPowerOfName(name, target.EntityId) ?? 0;
        if (power == ArenaPower)
        {
            return null;
        }

        string? className = entities.ClassOf(target.EntityId)
            ?? (entities.SeenProfileOf(target.EntityId)?.ClassId is int id ? Aion2.Protocol.Aion2SkillNames.ClassFromCode(id * 4 + 1) : null);
        int own = entities.LocalCharacter?.Name is { Length: > 0 } ownName ? entities.CombatPowerOfName(ownName) ?? 0 : 0;
        return new TargetPowerData(name, className, power, power > 0 && own > 0 ? power / (double)own : null);
    }

    /// <summary>-1 = the target is weaker (green), 1 = stronger (red), 0 = about the same or unknown.</summary>
    public static int Side(double? ratio) => ratio is not double r ? 0 : r > 1 + EqualBand ? 1 : r < 1 - EqualBand ? -1 : 0;

    private static readonly Brush Weaker = Frozen(Color.FromRgb(0x5F, 0xD3, 0x8A));
    private static readonly Brush Stronger = Frozen(Color.FromRgb(0xF0, 0x6A, 0x6A));
    private static readonly Brush Even = Frozen(Color.FromRgb(0xF2, 0xD0, 0x6B));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>The widget's body: one line, name - class - power - the gap in percent, in the middle of the widget (its window is wider than the line). Nothing marked: a hint on the dashboard.</summary>
    public static UIElement Body(TargetPowerData? data, LocalizationManager loc)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        TextBlock Text(string text, double size, FontWeight weight, Brush? brush, string? resource, double left)
        {
            var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(left, 0, 0, 0) };
            if (brush is not null)
            {
                block.Foreground = brush;
            }
            else
            {
                block.SetResourceReference(TextBlock.ForegroundProperty, resource ?? "Brush.Text");
            }

            return block;
        }

        if (data is null)
        {
            line.Children.Add(Text(loc["TargetPower.Hint"], 12, FontWeights.Normal, null, "Brush.TextMuted", 0));
            return line;
        }

        Brush tone = data.Ratio is null ? Even : Side(data.Ratio) is > 0 ? Stronger : Side(data.Ratio) < 0 ? Weaker : Even;
        line.Children.Add(Text(data.Name, 14, FontWeights.SemiBold, null, "Brush.Text", 0));
        if (data.ClassName is not null)
        {
            line.Children.Add(Text(ClassCatalog.DisplayName(data.ClassName, loc.Language), 12, FontWeights.Normal, null, "Brush.TextMuted", 8));
        }

        line.Children.Add(Text(data.Power > 0 ? data.Power.ToString("N0", CultureInfo.CurrentCulture) : "…", 16, FontWeights.Bold, data.Ratio is null ? null : tone, "Brush.Text", 10));
        if (data.Ratio is double ratio)
        {
            line.Children.Add(Text((ratio >= 1 ? "+" : "−") + Math.Abs((ratio - 1) * 100).ToString("0", CultureInfo.CurrentCulture) + " %", 12, FontWeights.Normal, tone, null, 6));
        }

        return line;
    }
}
