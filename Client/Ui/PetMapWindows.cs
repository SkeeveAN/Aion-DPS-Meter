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
/// its body and scaled with the handles at its corners and edges. Place and size are kept per overlay (the pet map and the pet list are two of them).
/// </summary>
public abstract class MovableOverlay : Window
{
    private const double MinScale = 0.5, MaxScale = 3.0;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Border _frame;
    private readonly Border _bodyHost = new();
    private readonly Border _shiftHost = new();
    private readonly TranslateTransform _shift = new();
    private readonly Func<(double? Left, double? Top, double Scale)> _load;
    private readonly Action<double, double, double> _save;
    private readonly Action<double?, double?> _saveSize;
    private readonly Func<(double? Width, double? Height)> _loadSizeFn;
    private bool _locked = true;
    private readonly OverlayEdgeResize _edges;

    protected MovableOverlay(string title, UIElement body, Func<(double? Left, double? Top, double Scale)> load, Action<double, double, double> save, double defaultLeft, double defaultTop,
        Func<(double? Width, double? Height)> loadSize, Action<double?, double?> saveSize, OverlayEdgeMode edgeMode = OverlayEdgeMode.Crop)
    {
        Title = title;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AltTabHidden.Apply(this);
        ShowActivated = false; // the game keeps the keyboard
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        _load = load;
        _save = save;
        _saveSize = saveSize;
        _loadSizeFn = loadSize;

        var saved = load();
        SetScale(saved.Scale);
        _shiftHost.RenderTransform = _shift;
        _shiftHost.Child = body;
        _bodyHost.Child = _shiftHost;
        _bodyHost.ClipToBounds = true;
        var stack = new DockPanel { LastChildFill = true };
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
        if (edgeMode == OverlayEdgeMode.CropAnchored && MeterSettings.Load().OverlayShifts.TryGetValue(title, out double[]? shift) && shift.Length == 2)
        {
            _shift.X = shift[0];
            _shift.Y = shift[1];
        }

        _edges = new OverlayEdgeResize(this, _frame, () => _scale.ScaleX, () => { }, SavePlace, SetScale, edgeMode, (x, y) => { _shift.X = x; _shift.Y = y; });
        Content = _edges.Wrap(_frame);
        Left = saved.Left ?? defaultLeft;
        Top = saved.Top ?? defaultTop;
        ApplyLocked(true);
        _defaultLeft = defaultLeft;
        _defaultTop = defaultTop;
        Action<string> onReset = title => { if (title == Title) { Dispatcher.Invoke(ReloadPlace); } };
        OverlayPlaces.WasReset += onReset;
        Closed += (_, _) => OverlayPlaces.WasReset -= onReset;
        OverlayDocking.Attach(this, SavePlace);
    }

    private readonly double _defaultLeft, _defaultTop;
    private DateTime _opacityAt = DateTime.MinValue;

    /// <summary>The place, size and scale are read from the settings again (after "reset size and position").</summary>
    private void ReloadPlace()
    {
        var saved = _load();
        SetScale(saved.Scale);
        var size = _loadSizeFn();
        _frame.Width = size.Width is > 0 ? size.Width.Value : double.NaN;
        _frame.MaxHeight = double.PositiveInfinity;
        _frame.Height = size.Height is > 0 ? size.Height.Value : double.NaN;
        _shift.X = MeterSettings.Load().OverlayShifts.TryGetValue(Title, out double[]? shifted) && shifted.Length == 2 ? shifted[0] : 0;
        _shift.Y = shifted is { Length: 2 } ? shifted[1] : 0;
        Left = saved.Left ?? _defaultLeft;
        Top = saved.Top ?? _defaultTop;
        ApplyLocked(_locked);
    }

    public bool IsLocked => _locked;

    /// <summary>The height the frame may take in its own units (the scaling excluded): the height it was pulled to; without one, the screen's.</summary>
    protected double HeightLimit
    {
        get
        {
            double h = !double.IsNaN(_frame.Height) ? _frame.Height : !double.IsInfinity(_frame.MaxHeight) ? _frame.MaxHeight : SystemParameters.WorkArea.Height;
            return h / Math.Max(_scale.ScaleY, 0.1);
        }
    }

    /// <summary>The size the body really has (in its own units, the frame's scaling excluded).</summary>
    protected double BodyWidth => _bodyHost.ActualWidth;

    protected double BodyHeight => _bodyHost.ActualHeight;

    /// <summary>Takes the content out of a window that is never shown, to put it into a tool's own window (a live view): the window keeps rendering into it.</summary>
    public UIElement? DetachBody()
    {
        UIElement? body = _shiftHost.Child;
        _shiftHost.Child = null;
        return body;
    }

    /// <summary>The content of the overlay (set by the derived window once it has built it).</summary>
    protected UIElement? Body
    {
        set => _shiftHost.Child = value;
    }

    /// <summary>Applies the lock only when it changed (the controller asks every tick).</summary>
    public void ApplyLockedIfChanged(bool locked)
    {
        if (locked != _locked)
        {
            ApplyLocked(locked);
        }
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
        if (_shift.X != 0 || _shift.Y != 0)
        {
            var s = MeterSettings.Load();
            s.OverlayShifts[Title] = new[] { _shift.X, _shift.Y };
            s.Save();
        }
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

        if (!IsVisible || (DateTime.UtcNow - _opacityAt).TotalSeconds >= 2)
        {
            _opacityAt = DateTime.UtcNow;
            ApplyStoredOpacity(); // also while shown: a changed opacity in the settings takes effect within a moment
        }

        if (!IsVisible)
        {
            Show();
            ApplyClickThrough();
        }
    }

    private byte? _baseAlpha;

    /// <summary>A least opacity (0 to 1) the overlay keeps while set, whatever its own setting says (the pet list over the shop, where it sits on the game's own window text).</summary>
    public double? OpacityFloor { get; set; }

    /// <summary>The overlay's own opacity (0 to 1, set per overlay in its module; the key is its title) replaces the alpha of the body's dark background; <see cref="OpacityFloor"/> raises it.</summary>
    public void ApplyStoredOpacity()
    {
        if (_shiftHost.Child is not Border { Background: SolidColorBrush bg } body)
        {
            return;
        }

        _baseAlpha ??= bg.Color.A;
        double alpha = MeterSettings.Load().OverlayOpacities.TryGetValue(Title, out double v) ? Math.Clamp(v, 0, 1) : _baseAlpha.Value / 255.0;
        if (OpacityFloor is { } floor)
        {
            alpha = Math.Max(alpha, floor);
        }

        body.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), bg.Color.R, bg.Color.G, bg.Color.B));
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
        "Cotton" => "#F3EBD3",
        "Fragment" => "#FF7B7B",
        "Cube" => "#FFD84D",
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
    private readonly List<Polygon> _edgeArrows = new();
    private readonly Polygon _me;

    /// <summary>The way the player's arrow points, in degrees clockwise from straight up (the direction he moves in).</summary>
    public double MeHeading { get; set; }
    private readonly TextBlock _meUnknown;

    public PetMapWindow() : base("Aion DPS Pet Map", new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Right - ViewSize - 40, 120, LoadSize, SaveSize, OverlayEdgeMode.CropAnchored)
    {
        _me = new Polygon
        {
            Points = new PointCollection { new Point(0, -11), new Point(8, 9), new Point(0, 5), new Point(-8, 9) },
            Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xBA, 0x42)), Stroke = Brushes.Black, StrokeThickness = 1,
        };
        Canvas.SetLeft(_me, ViewSize / 2);
        Canvas.SetTop(_me, ViewSize / 2);
        _canvas.Children.Add(_me);
        _meUnknown = new TextBlock
        {
            Text = "?", Width = 28, TextAlignment = TextAlignment.Center, FontSize = 26, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xBA, 0x42)),
            IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Color = Colors.Black, Opacity = 1 },
        };
        Canvas.SetLeft(_meUnknown, ViewSize / 2 - 14);
        Canvas.SetTop(_meUnknown, ViewSize / 2 - 20);
        _canvas.Children.Add(_meUnknown);
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

        BitmapSource? bitmap = MapTiles.Load(map, col, row, Math.Min(map.Tile, 1536));
        _tiles[key] = bitmap;
        _tileOrder.Add(key);
        while (_tileOrder.Count > 8)
        {
            _tiles.Remove(_tileOrder[0]);
            _tileOrder.RemoveAt(0);
        }

        return bitmap;
    }

    /// <summary>
    /// Draws the map around (<paramref name="worldX"/>, <paramref name="worldY"/>): <paramref name="radiusMetres"/> to each side, the dots of
    /// <paramref name="points"/> in their pets' colours (<paramref name="colors"/> by pet id).
    /// </summary>
    public void Render(Aion2MapInfo map, double worldX, double worldY, double radiusMetres, double opacity,
        IReadOnlyList<(int PetId, float X, float Y)> points, IReadOnlyList<(int PetId, float X, float Y)> live, IReadOnlyDictionary<int, Color> colors,
        IReadOnlyList<(string Kind, float X, float Y)>? gather = null, bool positionKnown = true, bool petArrows = true, bool gatherArrows = true)
    {
        // the visible part of the canvas (the frame may be pulled smaller than the view): the player stays in the middle of it
        double vw = Math.Clamp(BodyWidth, 40, ViewSize), vh = Math.Clamp(BodyHeight, 40, ViewSize);
        double cx = vw / 2, cy = vh / 2;
        double ppu = ViewSize / 2 / (radiusMetres * 100.0);       // view pixels per world unit
        double k = ppu / map.Scale;                                // view pixels per native map pixel
        var (nx, ny) = Aion2Maps.ToNative(map, worldX, worldY);
        double halfNative = ViewSize / 2 / k; // generous: the tiles beyond the visible part are clipped
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
                Canvas.SetLeft(img, cx + (col * map.Tile - nx) * k);
                Canvas.SetTop(img, cy + (row * map.Tile - ny) * k);
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
            double px = cx + (x - worldX) * ppu, py = cy + (y - worldY) * ppu;
            if (px < 5 || py < 5 || px > vw - 5 || py > vh - 5)
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
            double px = cx + (x - worldX) * ppu, py = cy + (y - worldY) * ppu;
            if (px < 6 || py < 6 || px > vw - 6 || py > vh - 6)
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
            double px = cx + (x - worldX) * ppu, py = cy + (y - worldY) * ppu;
            if (px < 8 || py < 8 || px > vw - 8 || py > vh - 8)
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

        // a chosen pet with nothing in view: an arrow on the edge, in its colour, towards its nearest spawn point
        int arrow = 0;
        double halfX = cx, halfY = cy, reachX = halfX - 13, reachY = halfY - 13;
        // one pass over all points (not one pass per pet): per pet whether one is in view, and the nearest one out of view
        var seen = new Dictionary<int, (bool Visible, double BestSq, double Bx, double By)>();
        void Consider(int id, float x, float y)
        {
            if (!petArrows || !colors.ContainsKey(id))
            {
                return;
            }

            if (!seen.TryGetValue(id, out var st))
            {
                st = (false, double.MaxValue, 0, 0);
            }
            else if (st.Visible)
            {
                return; // one in view is enough: no arrow for this pet
            }

            double dx = (x - worldX) * ppu, dy = (y - worldY) * ppu;
            if (Math.Abs(dx) < halfX - 5 && Math.Abs(dy) < halfY - 5)
            {
                st.Visible = true;
            }
            else
            {
                double sq = dx * dx + dy * dy;
                if (sq < st.BestSq)
                {
                    st.BestSq = sq;
                    st.Bx = dx;
                    st.By = dy;
                }
            }

            seen[id] = st;
        }

        foreach (var (id, x, y) in points)
        {
            Consider(id, x, y);
        }

        foreach (var (id, x, y) in live)
        {
            Consider(id, x, y);
        }

        void PlaceArrow(Color color, double bx, double by)
        {
            double t = Math.Min(reachX / Math.Max(Math.Abs(bx), 1e-6), reachY / Math.Max(Math.Abs(by), 1e-6)); // where the ray to the point meets the square
            if (arrow >= _edgeArrows.Count)
            {
                var a = new Polygon { Points = new PointCollection { new Point(11, 0), new Point(-8, -8), new Point(-4, 0), new Point(-8, 8) }, Stroke = Brushes.White, StrokeThickness = 1.6, IsHitTestVisible = false, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.9 } };
                _edgeArrows.Add(a);
                _canvas.Children.Add(a);
            }

            var ar = _edgeArrows[arrow++];
            ar.Fill = new SolidColorBrush(color);
            ar.RenderTransform = new RotateTransform(Math.Atan2(by, bx) * 180 / Math.PI);
            Canvas.SetLeft(ar, halfX + bx * t);
            Canvas.SetTop(ar, halfY + by * t);
            ar.Visibility = Visibility.Visible;
        }

        foreach (var (pet, color) in colors)
        {
            if (!seen.TryGetValue(pet, out var st))
            {
                continue;
            }

            double bestSq = st.BestSq, bx = st.Bx, by = st.By;
            if (st.Visible || bestSq == double.MaxValue)
            {
                continue;
            }

            PlaceArrow(color, bx, by);
        }

        // gatherables: one arrow per kind with none in view, towards its nearest point
        if (gatherArrows && gather is not null)
        {
            var kinds = new Dictionary<string, (bool Visible, double BestSq, double Bx, double By)>();
            foreach ((string kind, float x, float y) in gather)
            {
                kinds.TryGetValue(kind, out var st);
                if (st == default)
                {
                    st = (false, double.MaxValue, 0, 0);
                }

                if (st.Visible)
                {
                    continue;
                }

                double dx = (x - worldX) * ppu, dy = (y - worldY) * ppu;
                if (Math.Abs(dx) < halfX - 5 && Math.Abs(dy) < halfY - 5)
                {
                    st.Visible = true;
                }
                else if (dx * dx + dy * dy < st.BestSq)
                {
                    st = (false, dx * dx + dy * dy, dx, dy);
                }

                kinds[kind] = st;
            }

            foreach (var (kind, st) in kinds)
            {
                if (!st.Visible && st.BestSq != double.MaxValue)
                {
                    PlaceArrow(PetMapPalette.OfGather(kind), st.Bx, st.By);
                }
            }
        }

        for (int i = arrow; i < _edgeArrows.Count; i++)
        {
            _edgeArrows[i].Visibility = Visibility.Collapsed;
        }

        Canvas.SetLeft(_me, cx);
        Canvas.SetTop(_me, cy);
        _me.RenderTransform = new RotateTransform(MeHeading);
        Canvas.SetLeft(_meUnknown, cx - 14);
        Canvas.SetTop(_meUnknown, cy - 20);
        // the player on top; a "?" instead of the arrow when the position is not known (the view then shows the last one)
        _me.Visibility = positionKnown ? Visibility.Visible : Visibility.Collapsed;
        _meUnknown.Visibility = positionKnown ? Visibility.Collapsed : Visibility.Visible;
        _canvas.Children.Remove(_me);
        _canvas.Children.Add(_me);
        _canvas.Children.Remove(_meUnknown);
        _canvas.Children.Add(_meUnknown);
    }
}

/// <summary>The list that belongs to the pet map: the chosen pets with their colour, name, level (or "new") and the distance to the nearest spawn point.</summary>
public sealed class PetListWindow : MovableOverlay
{
    private readonly StackPanel _rows = new() { MinWidth = 250, MinHeight = 60 };
    private string _shown = "";

    /// <summary>How many rows the player can see at most (a row is about 19 units high), plus a reserve: more are not worth building.</summary>
    public int RowCapacity => (int)Math.Ceiling(HeightLimit / 19.0) + 10;

    public PetListWindow() : base("Aion DPS Pet List", new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Right - 300, 580, LoadSize, SaveSize)
    {
        var panel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x0E, 0x18, 0x20)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x29, 0x45, 0x57)),
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
    public void Render(IReadOnlyList<(Color Color, string Name, string Level, string Distance, bool Diamond)> rows)
    {
        string key = string.Join("\n", rows.Select(r => $"{r.Color}|{r.Name}|{r.Level}|{r.Distance}|{r.Diamond}"));
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        _rows.Children.Clear();
        foreach (var (color, name, level, distance, diamond) in rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Shape dot = diamond
                ? new Polygon { Points = new PointCollection { new Point(6, 0), new Point(12, 6), new Point(6, 12), new Point(0, 6) }, Fill = new SolidColorBrush(color), Stroke = Brushes.Black, StrokeThickness = 1.2, Width = 12, Height = 12, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }
                : new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(color), Stroke = Brushes.White, StrokeThickness = 1.5, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var n = new TextBlock { Text = name, FontWeight = FontWeights.Bold, FontSize = 12.5, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            var l = new TextBlock { Text = level, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x7A)), VerticalAlignment = VerticalAlignment.Center };
            var d = new TextBlock { Text = distance, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(0x9D, 0xB3, 0xC2)), VerticalAlignment = VerticalAlignment.Center, MinWidth = 52, TextAlignment = TextAlignment.Right, Margin = new Thickness(12, 0, 0, 0) };
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
