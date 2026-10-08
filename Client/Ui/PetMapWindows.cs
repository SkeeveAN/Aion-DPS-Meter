using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// A small transparent overlay that can be placed and scaled on its own: locked it is click-through, unlocked it takes clicks, can be dragged by
/// its body and scaled with the grip in its corner. Place and size are kept per overlay (the pet map and the pet list are two of them).
/// </summary>
public abstract class MovableOverlay : Window
{
    private const double MinScale = 0.5, MaxScale = 3.0;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Border _frame;
    private readonly Thumb _grip;
    private readonly Border _bodyHost = new();
    private readonly Func<(double? Left, double? Top, double Scale)> _load;
    private readonly Action<double, double, double> _save;
    private readonly Action<double?, double?> _saveSize;
    private bool _locked = true;
    private readonly OverlayEdgeResize _edges;

    protected MovableOverlay(string title, UIElement body, Func<(double? Left, double? Top, double Scale)> load, Action<double, double, double> save, double defaultLeft, double defaultTop,
        Func<(double? Width, double? Height)> loadSize, Action<double?, double?> saveSize)
    {
        Title = title;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false; // the game keeps the keyboard
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        _load = load;
        _save = save;
        _saveSize = saveSize;

        var saved = load();
        SetScale(saved.Scale);
        _grip = new Thumb
        {
            Width = 12, Height = 12, Cursor = Cursors.SizeNWSE, Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Right,
            Template = GripTemplate(),
        };
        AttachGripResize(_grip, SetScale, () => _scale.ScaleX);
        _grip.DragCompleted += (_, _) => SavePlace();
        var stack = new DockPanel { LastChildFill = true }; // the grip stays visible however small the frame is pulled; the body is cut off
        _bodyHost.Child = body;
        _bodyHost.ClipToBounds = true;
        DockPanel.SetDock(_grip, Dock.Bottom);
        stack.Children.Add(_grip);
        stack.Children.Add(_bodyHost);
        _frame = new Border { Child = stack, LayoutTransform = _scale, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(2), Background = Brushes.Transparent };
        _frame.MouseLeftButtonDown += (_, e) =>
        {
            if (!_locked && e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
                SavePlace();
            }
        };
        var size = loadSize();
        if (size.Width is > 0) { _frame.Width = size.Width.Value; }
        if (size.Height is > 0) { _frame.Height = size.Height.Value; }
        _edges = new OverlayEdgeResize(this, _frame, () => _scale.ScaleX, () => { }, SavePlace);
        Content = _edges.Wrap(_frame);
        Left = saved.Left ?? defaultLeft;
        Top = saved.Top ?? defaultTop;
        ApplyLocked(true);
    }

    public bool IsLocked => _locked;

    /// <summary>The content of the overlay (set by the derived window once it has built it).</summary>
    protected UIElement? Body
    {
        set => _bodyHost.Child = value;
    }

    /// <summary>Applies the lock only when it changed (the controller asks every tick).</summary>
    public void ApplyLockedIfChanged(bool locked)
    {
        if (locked != _locked)
        {
            ApplyLocked(locked);
        }
    }

    /// <summary>Resize by the corner grip: the bottom right corner follows the mouse (measured in this window, whose top left stays put -
    /// the change of the grip itself would move with the window and make the scaling jitter).</summary>
    private void AttachGripResize(System.Windows.Controls.Primitives.Thumb grip, Action<double> setScale, Func<double> getScale)
    {
        double startScale = 1, startW = 1, startH = 1, offX = 0, offY = 0;
        grip.DragStarted += (_, _) =>
        {
            var p = Mouse.GetPosition(this);
            startScale = getScale();
            startW = Math.Max(1, ActualWidth);
            startH = Math.Max(1, ActualHeight);
            offX = ActualWidth - p.X;
            offY = ActualHeight - p.Y;
        };
        grip.DragDelta += (_, _) =>
        {
            var p = Mouse.GetPosition(this);
            double ratio = ((p.X + offX) / startW + (p.Y + offY) / startH) / 2;
            setScale(startScale * Math.Max(0.1, ratio));
        };
    }

    private static ControlTemplate GripTemplate()
    {
        var template = new ControlTemplate(typeof(Thumb));
        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        var path = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        path.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M11,1 L1,11 M11,5 L5,11 M11,9 L9,11"));
        path.SetValue(Shape.StrokeProperty, new SolidColorBrush(Color.FromRgb(0x9D, 0xB3, 0xC2)));
        path.SetValue(Shape.StrokeThicknessProperty, 1.0);
        grid.AppendChild(path);
        template.VisualTree = grid;
        return template;
    }

    private void SetScale(double scale)
    {
        scale = Math.Clamp(double.IsFinite(scale) ? scale : 1.0, MinScale, MaxScale);
        _scale.ScaleX = scale;
        _scale.ScaleY = scale;
    }

    private void SavePlace()
    {
        _save(Left, Top, _scale.ScaleX);
        double? height = !double.IsNaN(_frame.Height) ? _frame.Height : !double.IsInfinity(_frame.MaxHeight) ? _frame.MaxHeight : null;
        _saveSize(double.IsNaN(_frame.Width) ? null : _frame.Width, height);
    }

    /// <summary>Locked: click-through, no outline, no grip. Unlocked: clickable and outlined, so it can be found, moved and scaled.</summary>
    public void ApplyLocked(bool locked)
    {
        _locked = locked;
        // Unlocked, the frame has the height it was pulled to (the sample rows fill it); locked, that height is only the most it may take,
        // so a short list ends at its last row without empty space below.
        if (locked && !double.IsNaN(_frame.Height))
        {
            _frame.MaxHeight = _frame.Height;
            _frame.Height = double.NaN;
        }
        else if (!locked && !double.IsInfinity(_frame.MaxHeight))
        {
            _frame.Height = _frame.MaxHeight;
            _frame.MaxHeight = double.PositiveInfinity;
        }

        _grip.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        _edges.Show(!locked);
        _frame.Cursor = locked ? Cursors.Arrow : Cursors.SizeAll;
        _frame.BorderBrush = locked ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(0xFF, 0xBA, 0x42));
        _frame.Background = locked ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0));
        ApplyClickThrough();
    }

    private void ApplyClickThrough()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        if (handle != IntPtr.Zero)
        {
            using var native = new NativeOverlay(this);
            native.SetClickThrough(_locked);
        }
    }

    /// <summary>Shows or hides the window; the click-through style is set again after the first show (WPF may recreate the handle).</summary>
    public void ShowOverlay(bool visible)
    {
        if (!visible)
        {
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        if (!IsVisible)
        {
            Show();
            ApplyClickThrough();
        }
    }

    public void RaiseToFront()
    {
        if (IsVisible)
        {
            NativeOverlay.KeepOnTop(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        }
    }
}

/// <summary>The colours of the chosen pets (the same on the map and in the list).</summary>
public static class PetMapPalette
{
    private static readonly string[] Hex = { "#4D9BFF", "#FF8A3D", "#5BD07A", "#E05BD0", "#FFE04D", "#4DDCE0", "#FF5A6E", "#B58BFF" };

    public static Color Of(int index) => (Color)ColorConverter.ConvertFromString(Hex[index % Hex.Length]);

    /// <summary>The colour of a kind of collectible (shown as a diamond).</summary>
    public static Color OfGather(string kind) => (Color)ColorConverter.ConvertFromString(kind switch
    {
        "Od" => "#7FE3FF",
        "Herb" => "#3DDC84",
        "Food" => "#FFC24D",
        "Ore" => "#C9CED6",
        "Wood" => "#B5835A",
        "Gemstone" => "#E08CFF",
        "Fragment" => "#FF7B7B",
        _ => "#FFFFFF",
    });
}

/// <summary>
/// The pet map: the game's own world map (transparent sea), centred on the player, with a dot for every spawn point of the chosen pets' monsters,
/// each pet in its own colour. No text on it. The view glides to each new position the game reports.
/// </summary>
public sealed class PetMapWindow : MovableOverlay
{
    public const double ViewSize = 440;
    private readonly Canvas _canvas = new() { Width = ViewSize, Height = ViewSize, ClipToBounds = true, Background = Brushes.Transparent };
    private readonly Dictionary<string, BitmapSource?> _tiles = new();
    private readonly List<string> _tileOrder = new();
    private readonly List<Image> _tileImages = new();
    private readonly List<Ellipse> _dots = new();
    private readonly List<Ellipse> _liveDots = new();
    private readonly List<Polygon> _gatherMarks = new();
    private readonly Polygon _me;

    public PetMapWindow() : base("Aion DPS Pet Map", new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Right - ViewSize - 40, 120, LoadSize, SaveSize)
    {
        _me = new Polygon
        {
            Points = new PointCollection { new Point(0, -11), new Point(8, 9), new Point(0, 5), new Point(-8, 9) },
            Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xBA, 0x42)), Stroke = Brushes.Black, StrokeThickness = 1,
        };
        Canvas.SetLeft(_me, ViewSize / 2);
        Canvas.SetTop(_me, ViewSize / 2);
        _canvas.Children.Add(_me);
        Body = _canvas;
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.PetMapWidth, s.PetMapHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.PetMapWidth = width;
        s.PetMapHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.PetMapLeft, s.PetMapTop, s.PetMapScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.PetMapLeft = left;
        s.PetMapTop = top;
        s.PetMapScale = scale;
        s.Save();
    }

    /// <summary>How many tile pictures the last render put on the map (for tests).</summary>
    public int TilesShown => _tileImages.Count(i => i.Visibility == Visibility.Visible);

    public string TileDebug => string.Join(" ", _tiles.Select(kv => kv.Key + (kv.Value is null ? "=null" : "=ok")));

    private BitmapSource? Tile(Aion2MapInfo map, int col, int row)
    {
        string key = $"{map.Key}/{col}_{row}";
        if (_tiles.TryGetValue(key, out var cached))
        {
            return cached;
        }

        BitmapSource? bitmap = null;
        try
        {
            string folder = Aion2Maps.TileFolder(map);
            string jpg = System.IO.Path.Combine(folder, $"{col}_{row}.jpg");
            string mask = System.IO.Path.Combine(folder, $"{col}_{row}_a.png");
            if (File.Exists(jpg) && File.Exists(mask))
            {
                int decode = Math.Min(map.Tile, 1536);
                var color = new BitmapImage();
                color.BeginInit();
                color.UriSource = new Uri(jpg);
                color.DecodePixelWidth = decode;
                color.CacheOption = BitmapCacheOption.OnLoad;
                color.EndInit();
                color.Freeze();
                var alphaSource = new BitmapImage();
                alphaSource.BeginInit();
                alphaSource.UriSource = new Uri(mask);
                alphaSource.DecodePixelWidth = decode;
                alphaSource.CacheOption = BitmapCacheOption.OnLoad;
                alphaSource.EndInit();
                alphaSource.Freeze();
                bitmap = Combine(color, alphaSource);
            }
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or UriFormatException)
        {
            bitmap = null;
        }

        _tiles[key] = bitmap;
        _tileOrder.Add(key);
        while (_tileOrder.Count > 8)
        {
            _tiles.Remove(_tileOrder[0]);
            _tileOrder.RemoveAt(0);
        }

        return bitmap;
    }

    /// <summary>The colour picture with the mask as its alpha (the mask is stored at half size and stretched to the colour picture).</summary>
    private static BitmapSource Combine(BitmapSource color, BitmapSource mask)
    {
        var c = new FormatConvertedBitmap(color, PixelFormats.Bgra32, null, 0);
        var m = new FormatConvertedBitmap(mask, PixelFormats.Gray8, null, 0);
        int w = c.PixelWidth, h = c.PixelHeight;
        byte[] pixels = new byte[w * h * 4];
        c.CopyPixels(pixels, w * 4, 0);
        byte[] alpha = new byte[m.PixelWidth * m.PixelHeight];
        m.CopyPixels(alpha, m.PixelWidth, 0);
        for (int y = 0; y < h; y++)
        {
            int my = Math.Min(m.PixelHeight - 1, y * m.PixelHeight / h);
            for (int x = 0; x < w; x++)
            {
                int mx = Math.Min(m.PixelWidth - 1, x * m.PixelWidth / w);
                int i = (y * w + x) * 4;
                byte a = alpha[my * m.PixelWidth + mx];
                pixels[i] = (byte)(pixels[i] * a / 255); // premultiplied alpha
                pixels[i + 1] = (byte)(pixels[i + 1] * a / 255);
                pixels[i + 2] = (byte)(pixels[i + 2] * a / 255);
                pixels[i + 3] = a;
            }
        }

        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4);
        result.Freeze();
        return result;
    }

    /// <summary>
    /// Draws the map around (<paramref name="worldX"/>, <paramref name="worldY"/>): <paramref name="radiusMetres"/> to each side, the dots of
    /// <paramref name="points"/> in their pets' colours (<paramref name="colors"/> by pet id).
    /// </summary>
    public void Render(Aion2MapInfo map, double worldX, double worldY, double radiusMetres, double opacity,
        IReadOnlyList<(int PetId, float X, float Y)> points, IReadOnlyList<(int PetId, float X, float Y)> live, IReadOnlyDictionary<int, Color> colors,
        IReadOnlyList<(string Kind, float X, float Y)>? gather = null)
    {
        double ppu = ViewSize / 2 / (radiusMetres * 100.0);       // view pixels per world unit
        double k = ppu / map.Scale;                                // view pixels per native map pixel
        var (nx, ny) = Aion2Maps.ToNative(map, worldX, worldY);
        double halfNative = ViewSize / 2 / k;
        int c0 = Math.Max(0, (int)Math.Floor((nx - halfNative) / map.Tile)), c1 = Math.Min(map.Grid - 1, (int)Math.Floor((nx + halfNative) / map.Tile));
        int r0 = Math.Max(0, (int)Math.Floor((ny - halfNative) / map.Tile)), r1 = Math.Min(map.Grid - 1, (int)Math.Floor((ny + halfNative) / map.Tile));

        int used = 0;
        for (int col = c0; col <= c1; col++)
        {
            for (int row = r0; row <= r1; row++)
            {
                var source = Tile(map, col, row);
                if (source is null)
                {
                    continue;
                }

                if (used >= _tileImages.Count)
                {
                    var image = new Image { Stretch = Stretch.Fill, SnapsToDevicePixels = false };
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                    _tileImages.Add(image);
                    _canvas.Children.Insert(used, image);
                }

                var img = _tileImages[used++];
                img.Source = source;
                img.Opacity = opacity;
                img.Width = map.Tile * k;
                img.Height = map.Tile * k;
                Canvas.SetLeft(img, ViewSize / 2 + (col * map.Tile - nx) * k);
                Canvas.SetTop(img, ViewSize / 2 + (row * map.Tile - ny) * k);
                img.Visibility = Visibility.Visible;
            }
        }

        for (int i = used; i < _tileImages.Count; i++)
        {
            _tileImages[i].Visibility = Visibility.Collapsed;
        }

        int dot = 0;
        foreach ((int pet, float x, float y) in points)
        {
            double px = ViewSize / 2 + (x - worldX) * ppu, py = ViewSize / 2 + (y - worldY) * ppu;
            if (px < 5 || py < 5 || px > ViewSize - 5 || py > ViewSize - 5)
            {
                continue;
            }

            if (dot >= _dots.Count)
            {
                var e = new Ellipse { Width = 9, Height = 9, Stroke = Brushes.White, StrokeThickness = 1.2, Opacity = 0.6, IsHitTestVisible = false };
                _dots.Add(e);
                _canvas.Children.Add(e);
            }

            var d = _dots[dot++];
            d.Fill = new SolidColorBrush(colors.GetValueOrDefault(pet, Colors.White));
            Canvas.SetLeft(d, px - 4.5);
            Canvas.SetTop(d, py - 4.5);
            d.Visibility = Visibility.Visible;
        }

        for (int i = dot; i < _dots.Count; i++)
        {
            _dots[i].Visibility = Visibility.Collapsed;
        }

        // collectibles: diamonds in the colour of their kind (the pets are round)
        int mark = 0;
        foreach ((string kind, float x, float y) in gather ?? Array.Empty<(string, float, float)>())
        {
            double px = ViewSize / 2 + (x - worldX) * ppu, py = ViewSize / 2 + (y - worldY) * ppu;
            if (px < 6 || py < 6 || px > ViewSize - 6 || py > ViewSize - 6)
            {
                continue;
            }

            if (mark >= _gatherMarks.Count)
            {
                var g = new Polygon { Points = new PointCollection { new Point(0, -8), new Point(7, 0), new Point(0, 8), new Point(-7, 0) }, Stroke = Brushes.Black, StrokeThickness = 1.4, IsHitTestVisible = false };
                _gatherMarks.Add(g);
                _canvas.Children.Add(g);
            }

            var m = _gatherMarks[mark++];
            m.Fill = new SolidColorBrush(PetMapPalette.OfGather(kind));
            Canvas.SetLeft(m, px);
            Canvas.SetTop(m, py);
            m.Visibility = Visibility.Visible;
        }

        for (int i = mark; i < _gatherMarks.Count; i++)
        {
            _gatherMarks[i].Visibility = Visibility.Collapsed;
        }

        // the monsters really around the player (announced by the game): large, bright, over the general spawn points
        int liveDot = 0;
        foreach ((int pet, float x, float y) in live)
        {
            double px = ViewSize / 2 + (x - worldX) * ppu, py = ViewSize / 2 + (y - worldY) * ppu;
            if (px < 8 || py < 8 || px > ViewSize - 8 || py > ViewSize - 8)
            {
                continue;
            }

            if (liveDot >= _liveDots.Count)
            {
                var e = new Ellipse { Width = 17, Height = 17, Stroke = Brushes.White, StrokeThickness = 2.5, IsHitTestVisible = false, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 5, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.9 } };
                _liveDots.Add(e);
                _canvas.Children.Add(e);
            }

            var d = _liveDots[liveDot++];
            d.Fill = new SolidColorBrush(colors.GetValueOrDefault(pet, Colors.White));
            Canvas.SetLeft(d, px - 8.5);
            Canvas.SetTop(d, py - 8.5);
            d.Visibility = Visibility.Visible;
        }

        for (int i = liveDot; i < _liveDots.Count; i++)
        {
            _liveDots[i].Visibility = Visibility.Collapsed;
        }

        _canvas.Children.Remove(_me);
        _canvas.Children.Add(_me); // the player on top
    }
}

/// <summary>The list that belongs to the pet map: the chosen pets with their colour, name, level (or "new") and the distance to the nearest spawn point.</summary>
public sealed class PetListWindow : MovableOverlay
{
    private readonly StackPanel _rows = new() { MinWidth = 250 };
    private string _shown = "";

    public PetListWindow() : base("Aion DPS Pet List", new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Right - 300, 580, LoadSize, SaveSize)
    {
        var panel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x0E, 0x18, 0x20)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x29, 0x45, 0x57)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), Child = _rows,
        };
        Body = panel;
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.PetListWidth, s.PetListHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.PetListWidth = width;
        s.PetListHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.PetListLeft, s.PetListTop, s.PetListScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.PetListLeft = left;
        s.PetListTop = top;
        s.PetListScale = scale;
        s.Save();
    }

    /// <summary>Rows: colour, name, level text, distance text (empty when unknown).</summary>
    public void Render(IReadOnlyList<(Color Color, string Name, string Level, string Distance)> rows)
    {
        string key = string.Join("\n", rows.Select(r => $"{r.Color}|{r.Name}|{r.Level}|{r.Distance}"));
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        _rows.Children.Clear();
        foreach (var (color, name, level, distance) in rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var dot = new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(color), Stroke = Brushes.White, StrokeThickness = 1.5, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var n = new TextBlock { Text = name, FontWeight = FontWeights.Bold, FontSize = 12.5, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            var l = new TextBlock { Text = level, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x7A)), VerticalAlignment = VerticalAlignment.Center };
            var d = new TextBlock { Text = distance, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(0x9D, 0xB3, 0xC2)), VerticalAlignment = VerticalAlignment.Center, MinWidth = 52, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(n, 1);
            Grid.SetColumn(l, 2);
            Grid.SetColumn(d, 3);
            grid.Children.Add(dot);
            grid.Children.Add(n);
            grid.Children.Add(l);
            grid.Children.Add(d);
            _rows.Children.Add(grid);
        }
    }
}
