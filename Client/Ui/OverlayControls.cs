using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AionDPS.Ui;

/// <summary>The controls every overlay has in its tool's window when that window is built in code: the overlay's own opacity and "reset size and position".</summary>
public static class OverlayControls
{
    /// <summary>The opacity row of the overlay named by its window title; a change is written to the settings file a moment after the last one.</summary>
    public static OpacityField OpacityRow(string title, double fallback)
    {
        var field = new OpacityField { Margin = new Thickness(0, 6, 0, 0) };
        field.Fraction = MeterSettings.Load().OverlayOpacities.GetValueOrDefault(title, fallback);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            MeterSettings s = MeterSettings.Load();
            s.OverlayOpacities[title] = field.Fraction;
            s.Save();
        };
        field.Changed += () =>
        {
            timer.Stop();
            timer.Start();
        };
        return field;
    }

    public static Button ResetButton(string title) => ResetButton(title, () => OverlayPlaces.Reset(title));

    public static Button ResetButton(string title, Action reset)
    {
        var button = new Button { Content = LocalizationManager.Instance["Settings.Overlay.ResetPlace"], HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(12, 3, 12, 3) };
        button.Click += (_, _) => reset();
        return button;
    }
}
