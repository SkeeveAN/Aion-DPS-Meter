using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// The building blocks of the configuration area of a tool page (the "Configuration" tab of the new interface): a card with a title, rows with a switch, a choice or a
/// slider. Every change is written to the settings at once (no Save button), as the switch of a page always did; <paramref name="applied"/> tells the running
/// meter that the settings changed.
/// </summary>
public sealed class ConfigControls
{
    private readonly Action<MeterSettings> _applied;

    public ConfigControls(Action<MeterSettings> applied) => _applied = applied;

    /// <summary>Loads the settings, lets <paramref name="change"/> alter them, saves and announces them.</summary>
    public void Change(Action<MeterSettings> change)
    {
        MeterSettings s = MeterSettings.Load();
        change(s);
        s.Save();
        _applied(s);
    }

    /// <summary>A card: a title and the rows below it.</summary>
    public static Border Card(string title, params UIElement[] rows)
    {
        var stack = new StackPanel();
        if (title.Length > 0)
        {
            var caption = new TextBlock { Text = title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            stack.Children.Add(caption);
        }

        foreach (UIElement row in rows)
        {
            stack.Children.Add(row);
        }

        var card = new Border { Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = stack };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    /// <summary>A grey note.</summary>
    public static TextBlock Note(string text, double top = 6) => ModuleNote(text, top);

    private static TextBlock ModuleNote(string text, double top)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, top, 0, 4) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        return block;
    }

    private static Grid Line(string label, string? hint, UIElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var name = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        texts.Children.Add(name);
        if (!string.IsNullOrEmpty(hint))
        {
            texts.Children.Add(ModuleNote(hint, 2));
        }

        Grid.SetColumn(control, 1);
        grid.Children.Add(texts);
        grid.Children.Add(control);
        control.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        return grid;
    }

    public static CheckBox SwitchBox(bool on)
    {
        var box = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
        if (Application.Current?.TryFindResource("SettingsSwitch") is Style style)
        {
            box.Style = style;
        }

        return box;
    }

    /// <summary>A row with a switch.</summary>
    public Grid Switch(string label, string? hint, Func<MeterSettings, bool> get, Action<MeterSettings, bool> set)
    {
        CheckBox box = SwitchBox(get(MeterSettings.Load()));
        box.Click += (_, _) => Change(s => set(s, box.IsChecked == true));
        return Line(label, hint, box);
    }

    /// <summary>A row with a drop-down list of (value, text) choices.</summary>
    public Grid Choice(string label, string? hint, IReadOnlyList<(string Value, string Text)> options, Func<MeterSettings, string> get, Action<MeterSettings, string> set, double width = 220)
    {
        var combo = new ComboBox { Width = width, Height = 28 };
        string current = get(MeterSettings.Load());
        foreach (var (value, text) in options)
        {
            var item = new ComboBoxItem { Content = text, Tag = value };
            combo.Items.Add(item);
            if (value == current)
            {
                combo.SelectedItem = item;
            }
        }

        combo.SelectedIndex = combo.SelectedIndex < 0 ? 0 : combo.SelectedIndex;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string value })
            {
                Change(s => set(s, value));
            }
        };
        return Line(label, hint, combo);
    }

    /// <summary>A slider with its value written next to it; <paramref name="format"/> turns the number into the text.</summary>
    public StackPanel SliderControl(int min, int max, int step, int value, Action<int> set, Func<int, string> format, double width = 150)
    {
        var slider = new Slider { Minimum = min, Maximum = max, TickFrequency = step, IsSnapToTickEnabled = true, SmallChange = step, LargeChange = step * 2, Width = width, Value = Math.Clamp(value, min, max), VerticalAlignment = VerticalAlignment.Center };
        if (Application.Current?.TryFindResource("ThemedSlider") is Style style)
        {
            slider.Style = style;
        }

        var text = new TextBlock { Text = format((int)slider.Value), Width = 74, TextAlignment = TextAlignment.Right, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        slider.ValueChanged += (_, _) =>
        {
            text.Text = format((int)slider.Value);
            set((int)slider.Value);
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(slider);
        panel.Children.Add(text);
        return panel;
    }

    /// <summary>A row with a slider.</summary>
    public Grid Slider(string label, string? hint, int min, int max, int step, Func<MeterSettings, int> get, Action<MeterSettings, int> set, Func<int, string> format)
        => Line(label, hint, SliderControl(min, max, step, get(MeterSettings.Load()), v => Change(s => set(s, v)), format));

    /// <summary>A line of controls with a name on the left (for one row of a list).</summary>
    public static Grid ListRow(UIElement first, UIElement name, params (UIElement Control, double Width)[] columns)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(first, 0);
        Grid.SetColumn(name, 1);
        grid.Children.Add(first);
        grid.Children.Add(name);
        int col = 2;
        foreach (var (control, width) in columns)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width > 0 ? new GridLength(width) : GridLength.Auto });
            Grid.SetColumn(control, col++);
            grid.Children.Add(control);
        }

        return grid;
    }

    /// <summary>A sound choice (no sound + the sounds of <see cref="NotifySounds"/>) with a volume slider and a play button.</summary>
    public (ComboBox Sound, Slider Volume, Button Test) SoundControls(string sound, int volume, Action<string> setSound, Action<int> setVolume)
    {
        var loc = LocalizationManager.Instance;
        var combo = new ComboBox { Height = 26, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        combo.Items.Add(new ComboBoxItem { Content = loc["Settings.Timetable.NoSound"], Tag = "" });
        foreach (string id in NotifySounds.All)
        {
            combo.Items.Add(new ComboBoxItem { Content = loc["Sound." + id], Tag = id });
        }

        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == sound) ?? combo.Items[0];
        var slider = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, SmallChange = 5, LargeChange = 10, Value = Math.Clamp(volume, 0, 100), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        if (Application.Current?.TryFindResource("ThemedSlider") is Style style)
        {
            slider.Style = style;
        }

        var test = new Button { Content = "▶", Width = 28, Height = 26, Padding = new Thickness(0), ToolTip = loc["Settings.Timetable.Test"] };
        string current = sound;
        void Enable()
        {
            test.IsEnabled = current.Length > 0;
            test.Opacity = test.IsEnabled ? 1.0 : 0.4;
        }

        combo.SelectionChanged += (_, _) =>
        {
            current = (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            Enable();
            setSound(current);
        };
        slider.ValueChanged += (_, _) => setVolume((int)slider.Value);
        test.Click += (_, _) => NotifySounds.Play(current, (int)slider.Value);
        Enable();
        return (combo, slider, test);
    }
}
