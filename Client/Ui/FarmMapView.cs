using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AionDPS.Ui;

/// <summary>Links from the settings page to the rest of the app (set by the main window): the overlay designer and the dashboard.</summary>
public static class FarmMapLinks
{
    /// <summary>Starts the overlay designer from the hub: the game comes to the front, the hub goes to the background and returns with "Done".</summary>
    public static Action? OpenDesigner { get; set; }

    /// <summary>Shows the dashboard of the hub (the widget was put on it).</summary>
    public static Action? ShowDashboard { get; set; }
}

/// <summary>
/// The farm map as a widget and as an overlay: one picture of the last "Generate images" at a time, the same one in both (they follow each other), with a small header
/// that names the map and has ‹ › to change the picture. On the overlay the header shows only while the overlay designer is on.
/// </summary>
public static class FarmMapView
{
    public const string Id = "farmmap", LegendId = "farmlegend";

    private static IReadOnlyList<FarmImage>? _images;
    private static readonly Dictionary<string, BitmapImage> Cache = new();
    private static int _index;

    /// <summary>Set by the host of an overlay while it builds the body: the header of an overlay shows in the designer only.</summary>
    public static bool ForOverlay { get; set; }

    /// <summary>The picture shown changed, or the set of pictures was replaced.</summary>
    public static event Action? Changed;

    static FarmMapView()
    {
        FarmMapStore.Changed += () =>
        {
            _images = null;
            Cache.Clear();
            _index = 0;
            Changed?.Invoke();
        };
    }

    private static IReadOnlyList<FarmImage> Images => _images ??= FarmMapStore.Load();

    public static bool HasImages => Images.Count > 0;

    /// <summary>Whether the map overlay and the list overlay (or widget) both are on / on a dashboard.</summary>
    public static IReadOnlyList<string> Ids => new[] { Id, LegendId };

    /// <summary>The next (+1) or the previous (-1) picture; past the last one it starts again.</summary>
    public static void Step(int direction)
    {
        int n = Images.Count;
        if (n == 0)
        {
            return;
        }

        _index = (_index + direction + n) % n;
        Changed?.Invoke();
    }

    /// <summary>Shows the picture of a map (when the player enters it); nothing changes when there is none for it.</summary>
    public static void ShowKey(string key)
    {
        int i = Images.ToList().FindIndex(x => x.Key == key);
        if (i >= 0 && i != _index)
        {
            _index = i;
            Changed?.Invoke();
        }
    }

    public static UIElement Body()
    {
        var loc = LocalizationManager.Instance;
        var images = Images;
        if (images.Count == 0)
        {
            var empty = new TextBlock { Text = loc["Settings.FarmMap.Empty"], TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
            return empty;
        }

        _index = Math.Clamp(_index, 0, images.Count - 1);
        var current = images[_index];
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bool header = !ForOverlay || OverlayDesigner.Active;
        if (header)
        {
            var bar = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = images.Count > 1 ? $"{current.Name}  ·  {_index + 1}/{images.Count}" : current.Name, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            Grid.SetColumn(name, 1);
            bar.Children.Add(Arrow("‹", -1, 0));
            bar.Children.Add(name);
            bar.Children.Add(Arrow("›", 1, 2));
            grid.Children.Add(bar);
        }

        var image = new Image { Source = Load(current.Path), Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        Grid.SetRow(image, 1);
        grid.Children.Add(image);
        return grid;
    }


    /// <summary>The list of the spots of the picture shown, as text: number, monster, pet, how many, and what the player has of the pet.</summary>
    public static UIElement LegendBody()
    {
        var loc = LocalizationManager.Instance;
        var images = Images;
        if (images.Count == 0)
        {
            return Body(); // the same words as the map
        }

        _index = Math.Clamp(_index, 0, images.Count - 1);
        var current = images[_index];
        var gold = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
        var red = new SolidColorBrush(Color.FromRgb(0xFF, 0x4A, 0x4A));
        var stack = new StackPanel();
        var name = new TextBlock { Text = current.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        var sub = new TextBlock { Text = current.Legend.Subtitle, FontSize = 11, Margin = new Thickness(0, 0, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        stack.Children.Add(name);
        stack.Children.Add(sub);
        foreach (var spot in current.Legend.Spots)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var badge = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = gold, BorderThickness = new Thickness(1.5), BorderBrush = spot.Unsafe ? red : Brushes.Black,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 8, 0),
                Child = new TextBlock { Text = spot.Number.ToString(), FontWeight = FontWeights.Bold, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var lines = new StackPanel();
            foreach (var line in spot.Lines)
            {
                var one = new Grid();
                one.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                one.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                string text = string.Equals(line.Mob, line.Pet, StringComparison.OrdinalIgnoreCase) ? $"{line.Mob}  ×{line.Count}" : $"{line.Mob}  ({line.Pet})  ×{line.Count}";
                var left = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
                left.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
                var right = new TextBlock { Text = line.Status, FontSize = 11, Margin = new Thickness(10, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                if (line.Missing)
                {
                    right.Foreground = red;
                }
                else
                {
                    right.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
                }

                Grid.SetColumn(right, 1);
                one.Children.Add(left);
                one.Children.Add(right);
                lines.Children.Add(one);
            }

            if (spot.Unsafe)
            {
                lines.Children.Add(new TextBlock { Text = loc["Settings.FarmMap.Img.Unsafe"], FontSize = 11, Foreground = red, TextWrapping = TextWrapping.Wrap });
            }

            Grid.SetColumn(lines, 1);
            row.Children.Add(badge);
            row.Children.Add(lines);
            stack.Children.Add(row);
        }

        var signs = new TextBlock { Text = (current.Legend.GuardsKnown ? loc["Settings.FarmMap.Img.Guards"] : loc["Settings.FarmMap.Img.NoGuards"]) + "  ·  " + loc["Settings.FarmMap.Img.Elite"], FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        signs.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        stack.Children.Add(signs);
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = stack };
    }

    private static UIElement Arrow(string glyph, int direction, int column)
    {
        var text = new TextBlock { Text = glyph, FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(8, -4, 8, -2), VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        var button = new Border { Child = text, Background = Brushes.Transparent, Cursor = Cursors.Hand, MinWidth = 28 };
        Grid.SetColumn(button, column);
        button.MouseLeftButtonDown += (_, e) => e.Handled = true; // the overlay is not dragged by the click
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Step(direction);
        };
        return button;
    }

    private static BitmapImage? Load(string path)
    {
        if (Cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.EndInit();
            bmp.Freeze();
            Cache[path] = bmp;
            return bmp;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
