using System.Windows.Media;
using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>The colour picker's maths: a colour goes to hue/saturation/brightness and back unchanged.</summary>
public static class SelfCheckColorPicker
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Colour picker (hue, saturation, brightness):");
        bool roundTrip = true;
        foreach (string hex in new[] { "#FFBA42", "#0E1820", "#5AA9FF", "#FFFFFF", "#000000", "#E07A5F", "#2F9E57", "#B98AF5", "#FF0000", "#00FF00", "#0000FF" })
        {
            Color color = ThemeManager.TryParse(hex)!.Value;
            ColorPickerPanel.ToHsv(color, out double h, out double s, out double v);
            roundTrip &= ColorPickerPanel.FromHsv(h, s, v) == color;
        }

        ColorPickerPanel.ToHsv(Colors.Red, out double redHue, out _, out _);
        ColorPickerPanel.ToHsv(Color.FromRgb(0, 0, 255), out double blueHue, out _, out _);
        bool hues = redHue == 0 && blueHue == 240 && ColorPickerPanel.FromHsv(120, 1, 1) == Color.FromRgb(0, 255, 0);
        Console.WriteLine($"  -> eleven colours go through hue/saturation/brightness and back unchanged={roundTrip}, red 0 / green 120 / blue 240 degrees={hues}: {(roundTrip && hues ? "OK" : "FAILED")}");
        return roundTrip && hues;
    }
}
