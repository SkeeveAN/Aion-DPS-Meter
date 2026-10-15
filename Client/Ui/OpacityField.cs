using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace AionDPS.Ui;

/// <summary>The opacity of one overlay: a label, a slider from 0 to 100 and a number field that follow each other (0 is allowed: fully see-through).</summary>
public sealed class OpacityField : Grid
{
    private readonly Slider _slider;
    private readonly TextBox _box;
    private bool _syncing;

    /// <summary>The user changed the value.</summary>
    public event Action? Changed;

    public OpacityField()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock { Text = LocalizationManager.Instance["Settings.PetMap.Opacity"], VerticalAlignment = VerticalAlignment.Center };
        _slider = new Slider
        {
            Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, SmallChange = 5, LargeChange = 10,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 8, 2),
        };
        if (Application.Current?.TryFindResource("ThemedSlider") is Style style)
        {
            _slider.Style = style;
        }

        _box = new TextBox { Width = 42, TextAlignment = TextAlignment.Right, MaxLength = 3, VerticalContentAlignment = VerticalAlignment.Center };
        _box.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
        _box.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        _box.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        var percent = new TextBlock { Text = " %", VerticalAlignment = VerticalAlignment.Center };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(_box);
        right.Children.Add(percent);
        SetColumn(_slider, 1);
        SetColumn(right, 2);
        Children.Add(label);
        Children.Add(_slider);
        Children.Add(right);
        _slider.ValueChanged += (_, _) =>
        {
            if (!_syncing)
            {
                _syncing = true;
                _box.Text = ((int)_slider.Value).ToString(CultureInfo.InvariantCulture);
                _syncing = false;
                Changed?.Invoke();
            }
        };
        _box.TextChanged += (_, _) =>
        {
            if (!_syncing && int.TryParse(_box.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int v))
            {
                _syncing = true;
                _slider.Value = Math.Clamp(v, 0, 100);
                _syncing = false;
                Changed?.Invoke();
            }
        };
        _box.LostFocus += (_, _) => _box.Text = ((int)_slider.Value).ToString(CultureInfo.InvariantCulture);
        Percent = 100;
    }

    /// <summary>0 to 100.</summary>
    public double Percent
    {
        get => Math.Round(_slider.Value);
        set
        {
            _slider.Value = Math.Clamp(double.IsFinite(value) ? value : 100, 0, 100);
            _box.Text = ((int)_slider.Value).ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>0 to 1, as the settings keep it.</summary>
    public double Fraction
    {
        get => Percent / 100.0;
        set => Percent = value * 100.0;
    }
}
