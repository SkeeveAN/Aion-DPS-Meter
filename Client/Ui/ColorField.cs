using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// A colour entry for the settings: a swatch that opens a colour picker (square and hue bar), a hex box and a
/// button back to "no own colour" (the design's colour). <see cref="Hex"/> is "#RRGGBB" or "" for none.
/// </summary>
public sealed class ColorField : StackPanel
{
    private readonly Border _swatch = new() { Width = 26, Height = 22, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand };
    private readonly TextBox _box = new() { Width = 78, Margin = new Thickness(8, 0, 0, 0), MaxLength = 7, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _hint = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), FontStyle = FontStyles.Italic };
    private readonly Popup _popup = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom };
    private readonly ColorPickerPanel _picker = new();
    private bool _updating;
    private string _hex = "";

    public event EventHandler? Changed;

    public ColorField()
    {
        Orientation = Orientation.Horizontal;
        _swatch.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        _swatch.MouseLeftButtonUp += (_, _) => { SyncPicker(); _popup.IsOpen = true; };
        _box.TextChanged += (_, _) =>
        {
            _hint.Visibility = _box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (!_updating)
            {
                Commit(_box.Text.Trim(), fromBox: true);
            }
        };

        var reset = new Button { Content = "✕", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(7, 1, 7, 1), ToolTip = LocalizationManager.Instance["Settings.Custom.UseTheme"] };
        reset.Click += (_, _) => Commit("", fromBox: false);

        _picker.Picked += color => Commit($"#{color.R:X2}{color.G:X2}{color.B:X2}", fromBox: false);

        var content = new Border { Padding = new Thickness(8), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = _picker };
        content.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        content.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        _popup.Child = content;
        _popup.PlacementTarget = _swatch;

        Children.Add(_swatch);
        // the default value (the theme's own colour) shows dimmed in the empty field, so a colour can be started from it
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        Children.Add(new Grid { Children = { _box, _hint } });
        Children.Add(reset);
        Children.Add(_popup);
        Paint();
    }

    private string _default = "";

    /// <summary>The colour the field has while it holds none of its own (shown dimmed); "" shows nothing.</summary>
    public string DefaultHex
    {
        get => _default;
        set { _default = Normalize(value); Paint(keepBox: true); }
    }

    public string Hex
    {
        get => _hex;
        set { _hex = Normalize(value); Paint(); }
    }

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
        Color? shown = color ?? ThemeManager.TryParse(_default);
        _swatch.Background = shown is Color c ? new SolidColorBrush(c) : Brushes.Transparent;
        _swatch.Opacity = color is null && shown is not null ? 0.55 : 1;
        _swatch.Child = shown is null ? new TextBlock { Text = "–", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6 } : null;
        _hint.Text = _default;
        if (!keepBox)
        {
            _box.Text = _hex;
        }

        _hint.Visibility = _box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _updating = false;
    }

    /// <summary>The picker shows the field's colour (its own, else the default, else grey) when it opens.</summary>
    private void SyncPicker()
    {
        _updating = true;
        _picker.SetColor(ThemeManager.TryParse(_hex) ?? ThemeManager.TryParse(_default) ?? Colors.Gray);
        _updating = false;
    }
}
