using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// One row of the look "Own" of the DPS overlay: the chip of the overlay (class colour bar, team stripe, dark background) with the lines of the template of
/// the mode in use. The rows of the overlay take the template that is in use (<see cref="Use"/>), the preview of the settings page gives its own.
/// </summary>
public sealed class OwnRowView : Border
{
    private const double CharWidth = 0.62;
    private static ParsedTemplate _current = OverlayTemplate.Parse(OverlayTemplate.DefaultText);
    private static bool _shareBars = true;
    private static DataTemplate? _rowTemplate;
    private readonly ProgressBar _bar = new() { Margin = new Thickness(-8, 0, -8, 0), IsHitTestVisible = false };
    private readonly StackPanel _lines = new() { VerticalAlignment = VerticalAlignment.Center };
    private PlayerRow? _row;
    private bool _pending;

    /// <summary>Raised by the settings page when the player saved his modes: the overlay reads them again.</summary>
    public static event Action? ModesSaved;

    public static void RaiseModesSaved() => ModesSaved?.Invoke();

    /// <summary>Raised when the template in use or the bars switch changed: every row draws itself again.</summary>
    public static event Action? Changed;

    /// <summary>The template of the row the overlay draws (the text of the mode in use; empty text: the simple default row).</summary>
    public static void Use(string? templateText, bool shareBars)
    {
        _current = OverlayTemplate.Parse(string.IsNullOrWhiteSpace(templateText) ? OverlayTemplate.DefaultText : templateText);
        _shareBars = shareBars;
        Changed?.Invoke();
    }

    /// <summary>The template of the rows of the overlay (what the items control of the overlay takes).</summary>
    public static DataTemplate RowTemplate => _rowTemplate ??= new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(OwnRowView)) };

    /// <summary>A template of its own (the preview of the settings page); null: the one in use.</summary>
    public ParsedTemplate? Template { get; set; }

    /// <summary>A field of that template to draw a frame around (the field the cursor stands in), null for none.</summary>
    public TemplateField? Marked { get; set; }

    /// <summary>The bars switch of its own (the preview); null: the one in use.</summary>
    public bool? ShareBars { get; set; }

    public OwnRowView()
    {
        Margin = new Thickness(0, 0, 0, 1);
        Padding = new Thickness(8, 3, 8, 3);
        MinHeight = 40;
        SetResourceReference(BackgroundProperty, "Brush.OverlayBg");
        if (Application.Current?.TryFindResource("TeamFillBar") is Style bar)
        {
            _bar.Style = bar;
        }

        var grid = new Grid();
        grid.Children.Add(_bar);
        if (Application.Current?.TryFindResource("TeamStripe") is Style stripe)
        {
            grid.Children.Add(new Border { Style = stripe, Margin = new Thickness(-8, -3, 0, -3) });
        }

        grid.Children.Add(_lines);
        Child = grid;
        DataContextChanged += (_, _) => Attach(DataContext as PlayerRow);
        Loaded += (_, _) =>
        {
            Changed += Redraw;
            Attach(DataContext as PlayerRow);
        };
        Unloaded += (_, _) =>
        {
            Changed -= Redraw;
            Attach(null);
        };
    }

    private void Attach(PlayerRow? row)
    {
        if (_row is not null)
        {
            _row.PropertyChanged -= OnRowChanged;
        }

        _row = row;
        if (_row is not null)
        {
            _row.PropertyChanged += OnRowChanged;
        }

        Redraw();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_pending)
        {
            return;
        }

        // many properties of a row change in one refresh: drawn once afterwards
        _pending = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _pending = false;
            Redraw();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>Draws the row again from its data and the template.</summary>
    public void Redraw()
    {
        _lines.Children.Clear();
        PlayerRow? row = _row;
        if (row is null)
        {
            return;
        }

        bool bars = ShareBars ?? _shareBars;
        _bar.Visibility = bars ? Visibility.Visible : Visibility.Collapsed;
        _bar.Value = row.FillPercent;
        _bar.Foreground = row.ClassBrush;
        SetResourceReference(BackgroundProperty, row.IsEnemy ? "Brush.OverlayEnemy" : "Brush.OverlayBg");

        ParsedTemplate template = Template ?? _current;
        StackPanel? line = null;
        StackPanel Line()
        {
            line = new StackPanel { Orientation = Orientation.Horizontal, MinHeight = 16 };
            _lines.Children.Add(line);
            return line;
        }

        foreach (TemplateItem item in template.Items)
        {
            if (item.NewLine)
            {
                Line();
                continue;
            }

            line ??= Line();
            if (item.Tab)
            {
                line.Children.Add(new Border { Width = 4 * CharWidth * 12 });
            }
            else if (item.Literal is { } text)
            {
                line.Children.Add(Text(text, 12, false, false, 0, false));
            }
            else if (item.Field is { } field)
            {
                double size = Math.Clamp(12 + field.Size, 6, 30);
                UIElement shown = Text(OverlayTemplate.ValueOf(field, row), size, field.Bold, field.Right, field.Width, true);
                if (ReferenceEquals(field, Marked) || (Marked is not null && field.Start == Marked.Start && field.Length == Marked.Length))
                {
                    shown = new Border { BorderBrush = (Brush)(Application.Current?.TryFindResource("Brush.Accent") ?? Brushes.Gold), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Margin = new Thickness(-2, 0, -2, 0), Padding = new Thickness(1, 0, 1, 0), Child = shown };
                }

                line.Children.Add(shown);
            }
        }
    }

    private static TextBlock Text(string text, double size, bool bold, bool right, int width, bool field)
    {
        var block = new TextBlock
        {
            Text = text, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = right ? TextAlignment.Right : TextAlignment.Left, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        if (width > 0)
        {
            block.Width = width * CharWidth * size;
        }

        _ = field;
        return block;
    }
}
