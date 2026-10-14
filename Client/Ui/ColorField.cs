using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// A colour entry for the settings: a swatch that opens a palette with red/green/blue sliders, a hex box and a
/// button back to "no own colour" (the theme's colour). <see cref="Hex"/> is "#RRGGBB" or "" for none.
/// </summary>
public sealed class ColorField : StackPanel
{
    private static readonly string[] Presets =
    {
        "#0E1820", "#1A1A1A", "#000000", "#2B2B3C", "#3B1F2B", "#1F3B2B", "#F5F5F5", "#FFFFFF",
        "#FFBA42", "#FF5A4F", "#6FD69B", "#5AA9FF", "#B98AF5", "#E07A5F", "#9DB3C2", "#FFE27A",
    };

    private readonly Border _swatch = new() { Width = 26, Height = 22, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand };
    private readonly TextBox _box = new() { Width = 78, Margin = new Thickness(8, 0, 0, 0), MaxLength = 7, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Popup _popup = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom };
    private readonly Slider[] _sliders = { MakeSlider(), MakeSlider(), MakeSlider() };
    private bool _updating;
    private string _hex = "";

    public event EventHandler? Changed;

    public ColorField()
    {
        Orientation = Orientation.Horizontal;
        _swatch.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        _swatch.MouseLeftButtonUp += (_, _) => { SyncSliders(); _popup.IsOpen = true; };
        _box.TextChanged += (_, _) =>
        {
            if (!_updating)
            {
                Commit(_box.Text.Trim(), fromBox: true);
            }
        };

        var reset = new Button { Content = "✕", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(7, 1, 7, 1), ToolTip = LocalizationManager.Instance["Settings.Custom.UseTheme"] };
        reset.Click += (_, _) => Commit("", fromBox: false);

        var palette = new WrapPanel { Width = 200 };
        foreach (string preset in Presets)
        {
            var chip = new Border { Width = 20, Height = 20, Margin = new Thickness(2), CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(ThemeManager.TryParse(preset)!.Value), Cursor = System.Windows.Input.Cursors.Hand };
            chip.MouseLeftButtonUp += (_, _) => Commit(preset, fromBox: false);
            palette.Children.Add(chip);
        }

        var sliderPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (Slider slider in _sliders)
        {
            slider.ValueChanged += (_, _) =>
            {
                if (!_updating)
                {
                    Commit($"#{(int)_sliders[0].Value:X2}{(int)_sliders[1].Value:X2}{(int)_sliders[2].Value:X2}", fromBox: false);
                }
            };
            sliderPanel.Children.Add(slider);
        }

        var content = new Border { Padding = new Thickness(8), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = new StackPanel { Children = { palette, sliderPanel } } };
        content.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        content.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        _popup.Child = content;
        _popup.PlacementTarget = _swatch;

        Children.Add(_swatch);
        Children.Add(_box);
        Children.Add(reset);
        Children.Add(_popup);
        Paint();
    }

    public string Hex
    {
        get => _hex;
        set { _hex = Normalize(value); Paint(); }
    }

    private static Slider MakeSlider() => new() { Minimum = 0, Maximum = 255, SmallChange = 1, LargeChange = 16, Margin = new Thickness(0, 2, 0, 2) };

    private static string Normalize(string? hex) =>
        hex is { Length: 7 } && hex[0] == '#' && ThemeManager.TryParse(hex) is not null ? hex.ToUpperInvariant() : "";

    private void Commit(string hex, bool fromBox)
    {
        string normalized = Normalize(hex);
        if (fromBox && normalized.Length == 0 && hex.Length > 0)
        {
            return; // still typing
        }

        if (normalized == _hex)
        {
            return;
        }

        _hex = normalized;
        Paint(keepBox: fromBox);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Paint(bool keepBox = false)
    {
        _updating = true;
        Color? color = ThemeManager.TryParse(_hex);
        _swatch.Background = color is Color c ? new SolidColorBrush(c) : Brushes.Transparent;
        _swatch.Child = color is null ? new TextBlock { Text = "–", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6 } : null;
        if (!keepBox)
        {
            _box.Text = _hex;
        }

        _updating = false;
    }

    private void SyncSliders()
    {
        _updating = true;
        Color c = ThemeManager.TryParse(_hex) ?? Colors.Gray;
        _sliders[0].Value = c.R;
        _sliders[1].Value = c.G;
        _sliders[2].Value = c.B;
        _updating = false;
    }
}
