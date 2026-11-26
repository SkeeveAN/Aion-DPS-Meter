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
    private static readonly List<MovableOverlay> Instances = new();

    /// <summary>Every overlay window that exists (the overlay designer raises them above its dimming).</summary>
    public static IReadOnlyList<MovableOverlay> All => Instances;

    private const double MinScale = 0.5, MaxScale = 3.0;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Border _frame;
    private readonly Border _gear;
    private readonly Border _eye;
    private readonly Border _tools;
    private static readonly SolidColorBrush ToolGold = new(Color.FromRgb(0xFF, 0xBA, 0x42));
    private static readonly SolidColorBrush ToolInk = new(Color.FromRgb(0x1A, 0x12, 0x05));
    private readonly TextBlock _eyeGlyph;
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
        // the overlay designer's gear: a click opens the panel (opacity, colour, reset, hide), the same as the right-click
        _gear = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(2), Cursor = Cursors.Hand,
            Background = ToolGold, BorderBrush = ToolInk, BorderThickness = new Thickness(1.5),
            ToolTip = LocalizationManager.Instance["Designer.Settings"],
            Child = new TextBlock { Text = "\u2699", FontSize = 21, FontWeight = FontWeights.Bold, Foreground = ToolInk, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -3, 0, 0) },
        };
        _gear.MouseLeftButtonDown += (_, e) => e.Handled = true; // not a drag of the overlay
        _gear.MouseLeftButtonUp += (_, e) =>
        {
            OverlayDesigner.ShowOpacityPanel(this);
            e.Handled = true;
        };
        // the eye: whether the overlay is drawn over the game at all; closed, it keeps its place (to be shown by something else, like the map for a hidden cube)
        _eyeGlyph = new TextBlock { FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 18, Foreground = ToolInk, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _eye = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(2), Cursor = Cursors.Hand,
            Background = ToolGold, BorderBrush = ToolInk, BorderThickness = new Thickness(1.5),
            ToolTip = LocalizationManager.Instance["Designer.EyeTip"], Child = _eyeGlyph,
        };
        _eye.MouseLeftButtonDown += (_, e) => e.Handled = true;
        _eye.MouseLeftButtonUp += (_, e) =>
        {
            var s = MeterSettings.Load();
            if (!s.OverlayEyeOff.Remove(Title))
            {
                s.OverlayEyeOff.Add(Title);
            }

            s.Save();
            ShowEyeState();
            e.Handled = true;
        };
        var layer = new Grid();
        layer.Children.Add(stack);
        // the two buttons sit on a dark pill with a gold rim: they stand out from every ground (the game's bright or dark, the overlay's own colours)
        var toolRow = new StackPanel { Orientation = Orientation.Horizontal };
        toolRow.Children.Add(_eye);
        toolRow.Children.Add(_gear);
        _tools = new Border
        {
            Child = toolRow, CornerRadius = new CornerRadius(19), Padding = new Thickness(3), Margin = new Thickness(0, 3, 3, 0), Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x0E, 0x18, 0x20)), BorderBrush = ToolGold, BorderThickness = new Thickness(1.5),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.8 },
        };
        layer.Children.Add(_tools);
        _frame = new Border { Child = layer, LayoutTransform = _scale, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(2), Background = Brushes.Transparent };
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
        Instances.Add(this);
        Closed += (_, _) => Instances.Remove(this);
        // the overlay designer: a right-click on an overlay opens how clear its content and background are
        MouseRightButtonUp += (_, e) =>
        {
            if (!_locked && OverlayDesigner.Active)
            {
                OverlayDesigner.ShowOpacityPanel(this);
                e.Handled = true;
            }
        };
    }

    private readonly double _defaultLeft, _defaultTop;
    private DateTime _opacityAt = DateTime.MinValue;

    // ---- the font colour of the overlay designer (every overlay) -----------------------------------------------------------

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBlock, Brush> OriginalText = new();
    private string? _fontHex;
    private SolidColorBrush? _fontBrush;
    private bool _fontLayoutHooked;
    private bool _recolouring;

    /// <summary>The chosen font colour of this overlay (see <see cref="MeterSettings.OverlayFontColors"/>), null: its own colours.</summary>
    public Color? FontColor => _fontBrush?.Color;

    /// <summary>Reads the font colour from the settings; a change is drawn at once. The text is recoloured again after every layout, because overlays rebuild their rows all the time.</summary>
    private void ApplyFontColor(MeterSettings settings)
    {
        settings.OverlayFontColors.TryGetValue(Title, out string? hex);
        if (!string.Equals(hex, _fontHex, StringComparison.OrdinalIgnoreCase))
        {
            _fontHex = hex;
            _fontBrush = null;
            if (!string.IsNullOrEmpty(hex) && ColorConverter.ConvertFromString(hex) is Color color)
            {
                _fontBrush = new SolidColorBrush(color);
                _fontBrush.Freeze();
            }
        }

        if (!_fontLayoutHooked)
        {
            _fontLayoutHooked = true;
            _bodyHost.LayoutUpdated += (_, _) =>
            {
                if (_fontBrush is not null)
                {
                    Recolour(_bodyHost);
                }
            };
        }

        Recolour(_bodyHost);
    }

    private void Recolour(DependencyObject parent)
    {
        if (_recolouring)
        {
            return;
        }

        _recolouring = true;
        try
        {
            RecolourTree(parent);
        }
        finally
        {
            _recolouring = false;
        }
    }

    private void RecolourTree(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBlock text)
            {
                if (_fontBrush is { } brush)
                {
                    if (!ReferenceEquals(text.Foreground, brush))
                    {
                        if (!OriginalText.TryGetValue(text, out _))
                        {
                            OriginalText.Add(text, text.Foreground ?? Brushes.White);
                        }

                        text.Foreground = brush;
                    }
                }
                else if (OriginalText.TryGetValue(text, out Brush? original))
                {
                    text.Foreground = original;
                    OriginalText.Remove(text);
                }
            }

            RecolourTree(child);
        }
    }

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

    /// <summary>For the overlay designer's group frame: the scale and the size on the screen.</summary>
    public (double Scale, double Width, double Height) GroupStart() => (_scale.ScaleX, ActualWidth, ActualHeight);

    /// <summary>For the group frame: a new scale, a size pulled to (null: as it is) and a place, all at once.</summary>
    public void GroupApply(double scale, double? screenWidth, double? screenHeight, double left, double top)
    {
        SetScale(scale);
        if (screenWidth is { } w)
        {
            _frame.Width = Math.Max(10, w / _scale.ScaleX);
        }

        if (screenHeight is { } h)
        {
            _frame.Height = Math.Max(10, h / _scale.ScaleY);
        }

        Left = left;
        Top = top;
    }

    /// <summary>Writes the place, size and scale to the settings (after a group gesture).</summary>
    public void CommitPlace() => SavePlace();

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
    protected double BodyWidth => _detached is FrameworkElement taken ? taken.ActualWidth : _bodyHost.ActualWidth;

    protected double BodyHeight => _detached is FrameworkElement taken ? taken.ActualHeight : _bodyHost.ActualHeight;

    /// <summary>How far the content is pushed against the visible part of the body (a map cropped from the left or top edge): the visible part starts at minus this in the content's own coordinates.</summary>
    protected (double X, double Y) BodyShift => _detached is null ? (_shift.X, _shift.Y) : (0, 0);

    /// <summary>The content once a tool window has taken it out (see <see cref="DetachBody"/>): its size is the size it has THERE, the never shown window around it has none, and the shift and crop of that window do not apply to it.</summary>
    private UIElement? _detached;

    /// <summary>Takes the content out of a window that is never shown, to put it into a tool's own window (a live view): the window keeps rendering into it.</summary>
    public UIElement? DetachBody()
    {
        UIElement? body = _shiftHost.Child;
        _shiftHost.Child = null;
        _detached = body;
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
        else
        {
            _gear.Visibility = !locked && OverlayDesigner.Active ? Visibility.Visible : Visibility.Collapsed; // the designer may have come on with the overlay unlocked already
            _eye.Visibility = _gear.Visibility;
            _tools.Visibility = _gear.Visibility;
            ShowEyeState();
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
        _gear.Visibility = !locked && OverlayDesigner.Active ? Visibility.Visible : Visibility.Collapsed;
        _eye.Visibility = _gear.Visibility;
        _tools.Visibility = _gear.Visibility;
        ShowEyeState();
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
    /// <summary>The eye of the overlay designer is closed for this overlay: it is not drawn over the game (the designer still shows it, dimmed, so it can be placed).</summary>
    public bool EyeOff => MeterSettings.Load().OverlayEyeOff.Contains(Title);

    /// <summary>Something else shows this overlay although its eye is closed (the map for a hidden cube).</summary>
    public bool IgnoreEyeOff { get; set; }

    /// <summary>For the pictures of the overlay designer: the window's content with the designer's eye and gear showing, taken out of the (never shown) window.</summary>
    internal FrameworkElement PictureContent()
    {
        _tools.Visibility = Visibility.Visible;
        _gear.Visibility = Visibility.Visible;
        _eye.Visibility = Visibility.Visible;
        _frame.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xBA, 0x42));
        _frame.Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0));
        ShowEyeState();
        var content = (FrameworkElement)Content;
        Content = null;
        return content;
    }

    private void ShowEyeState()
    {
        bool off = EyeOff;
        _eyeGlyph.Text = off ? "\uED1A" : "\uE7B3"; // closed / open eye
        // closed: the button is dark with a gold eye (it looks switched off), open: gold with a dark eye
        _eye.Background = off ? new SolidColorBrush(Color.FromRgb(0x2A, 0x33, 0x3C)) : ToolGold;
        _eye.BorderBrush = off ? ToolGold : ToolInk;
        _eyeGlyph.Foreground = off ? ToolGold : ToolInk;
        _frame.Opacity = off && OverlayDesigner.Active ? 0.45 : 1.0;
    }

    public void ShowOverlay(bool visible)
    {
        if (visible && !IgnoreEyeOff && !OverlayDesigner.Active && EyeOff)
        {
            visible = false;
        }

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

    /// <summary>For an overlay whose background is not one border (the combat bars, the pet map): applies the designer's background opacity, text opacity and background colour itself; returns true when it did.</summary>
    protected virtual bool ApplyLook(MeterSettings settings) => false;

    private byte? _baseAlpha;

    /// <summary>A least opacity (0 to 1) the overlay keeps while set, whatever its own setting says (the pet list over the shop, where it sits on the game's own window text).</summary>
    public double? OpacityFloor { get; set; }

    /// <summary>The overlay's own opacity (0 to 1, set per overlay in its module; the key is its title) replaces the alpha of the body's dark background; <see cref="OpacityFloor"/> raises it.</summary>
    public void ApplyStoredOpacity()
    {
        ApplyFontColor(MeterSettings.Load());
        if (ApplyLook(MeterSettings.Load()))
        {
            return; // an overlay that draws its background in several places does it itself
        }

        // the content has its own clearness (the overlay designer); the background alpha is set below
        if (_shiftHost.Child is Border { Child: { } inner })
        {
            inner.Opacity = MeterSettings.Load().OverlayForegrounds.TryGetValue(Title, out double fg) ? Math.Clamp(fg, 0, 1) : 1.0;
        }

        if (_shiftHost.Child is not Border { Background: SolidColorBrush bg } body)
        {
            return;
        }

        _baseAlpha ??= bg.Color.A;
        double alpha = OverlayDesigner.TryGetBackground(MeterSettings.Load(), Title, out double v) ? Math.Clamp(v, 0, 1) : _baseAlpha.Value / 255.0;
        if (OpacityFloor is { } floor)
        {
            alpha = Math.Max(alpha, floor);
        }

        Color baseColor = bg.Color;
        if (MeterSettings.Load().OverlayBackColors.TryGetValue(Title, out string? hex) && TryColor(hex, out Color chosen))
        {
            baseColor = chosen; // the colour chosen in the overlay designer
        }

        body.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), baseColor.R, baseColor.G, baseColor.B));
    }

    private static bool TryColor(string? hex, out Color color)
    {
        color = default;
        try
        {
            if (!string.IsNullOrEmpty(hex) && ColorConverter.ConvertFromString(hex) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (FormatException)
        {
            // a damaged value: the overlay keeps its own colour
        }

        return false;
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

    private static readonly Dictionary<string, Color> GatherColors = new();
    private static readonly Dictionary<Color, SolidColorBrush> FrozenBrushes = new();

    /// <summary>The colour of a kind of collectible (shown as a diamond). Looked up for every point on every map tick, so each kind is parsed once.</summary>
    public static Color OfGather(string kind)
    {
        lock (GatherColors)
        {
            if (!GatherColors.TryGetValue(kind, out Color color))
            {
                color = (Color)ColorConverter.ConvertFromString(kind switch
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
                GatherColors[kind] = color;
            }

            return color;
        }
    }

    /// <summary>A frozen brush of a colour, shared: the map assigns one to every dot every 50 ms, and a fresh unfrozen brush each time is garbage and a re-render trigger.</summary>
    public static SolidColorBrush BrushOf(Color color)
    {
        lock (FrozenBrushes)
        {
            if (!FrozenBrushes.TryGetValue(color, out SolidColorBrush? brush))
            {
                brush = new SolidColorBrush(color);
                brush.Freeze();
                FrozenBrushes[color] = brush;
            }

            return brush;
        }
    }
}

/// <summary>
/// The pet map: the game's own world map (transparent sea), centred on the player, with a dot for every spawn point of the chosen pets' monsters,
/// each pet in its own colour. No text on it. The view glides to each new position the game reports.
/// </summary>
public sealed class PetMapWindow : MovableOverlay
{
    public const double ViewSize = 440;
    private readonly Canvas _canvas = new() { Width = ViewSize, Height = ViewSize, ClipToBounds = true, Background = Brushes.Transparent };
    private readonly Canvas _tileLayer = new() { Width = ViewSize, Height = ViewSize, ClipToBounds = true, Background = Brushes.Transparent };
    private readonly Dictionary<string, BitmapSource?> _tiles = new();
    private readonly List<string> _tileOrder = new();
    private readonly List<Image> _tileImages = new();
    private readonly List<Image> _roadImages = new();
    private readonly Dictionary<string, BitmapSource?> _roads = new();
    private readonly List<string> _roadOrder = new();

    private readonly List<TextBlock> _heightLabels = new();
    private readonly List<Grid> _cubeMarks = new();

    private static readonly System.Windows.Media.Effects.DropShadowEffect CubeGlow = Frozen(new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 9, ShadowDepth = 0, Color = Color.FromRgb(0xFF, 0xC8, 0x1E), Opacity = 1 });
    private static readonly System.Windows.Media.Effects.DropShadowEffect IconShadow = Frozen(new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.9 });

    private static System.Windows.Media.Effects.DropShadowEffect Frozen(System.Windows.Media.Effects.DropShadowEffect effect)
    {
        effect.Freeze();
        return effect;
    }

    /// <summary>The pseudo pet id the live Hidden Cubes travel under (the controller's own number).</summary>
    private const int CubeMarkPet = -1;

    /// <summary>The mark of an announced cube: 44 px, a pulsing ring, a dark disc with a thick gold ring and the cube's picture.</summary>
    private static Grid NewCubeMark()
    {
        // the gold cube itself, bigger than the places, with a glow that pulses (no frame around it)
        var cube = new Image { Width = 36, Height = 36, Source = AionDPS.Aion2.Protocol.Aion2Gather.Icon("Hidden Cube"), IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(cube, BitmapScalingMode.HighQuality);
        var glow = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 0, Color = Color.FromRgb(0xFF, 0xC8, 0x1E), Opacity = 1 };
        glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, new System.Windows.Media.Animation.DoubleAnimation(8, 24, TimeSpan.FromMilliseconds(800)) { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
        cube.Effect = glow;
        var grid = new Grid { Width = 44, Height = 44, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        Panel.SetZIndex(grid, 5);
        // a yellow field behind the real cube (the game announced it, it is there now), so it is told from the places at once
        var plate = new Ellipse
        {
            Width = 40, Height = 40, Fill = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xD2, 0x1E)), Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xF0, 0x8A)), StrokeThickness = 2, IsHitTestVisible = false,
        };
        grid.Children.Add(plate);
        grid.Children.Add(cube);
        return grid;
    }

    /// <summary>The Hidden Cubes in view with how much higher (+) or lower (-) they are than the player (world units): drawn as a blue number with an arrow beside the cube.</summary>
    public IReadOnlyList<(float X, float Y, float Dz)> CubeHeights { get; set; } = Array.Empty<(float X, float Y, float Dz)>();

    /// <summary>Lay the roads of the game over the map (the yellow lines); off shows the bare ground.</summary>
    public bool ShowRoads { get; set; } = true;
    private readonly List<Ellipse> _dots = new();
    private readonly List<Ellipse> _liveDots = new();
    private readonly List<Polygon> _gatherMarks = new();
    private readonly List<Image> _gatherIcons = new();
    private readonly List<Polygon> _edgeArrows = new();
    private readonly Polygon _me;

    /// <summary>The way the player's arrow points, in degrees clockwise from straight up (the direction he moves in).</summary>
    public double MeHeading { get; set; }
    private readonly TextBlock _meUnknown;
    private readonly Ellipse _meRing = new() { Width = 34, Height = 34, Stroke = Brushes.White, StrokeThickness = 1.6, StrokeDashArray = new DoubleCollection { 2, 2 }, IsHitTestVisible = false, Visibility = Visibility.Collapsed };

    /// <summary>The place is only a guess (no fresh exact report from the game): the arrow is drawn pale with a dashed ring round it.</summary>
    public bool MeEstimated { get; set; }

    public PetMapWindow() : base("Aion DPS Pet Map", new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Right - ViewSize - 40, 120, LoadSize, SaveSize, OverlayEdgeMode.CropAnchored)
    {
        _me = new Polygon
        {
            Points = new PointCollection { new Point(0, -11), new Point(8, 9), new Point(0, 5), new Point(-8, 9) },
            Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xBA, 0x42)), Stroke = Brushes.Black, StrokeThickness = 1,
        };
        Canvas.SetLeft(_me, ViewSize / 2);
        Canvas.SetTop(_me, ViewSize / 2);
        Panel.SetZIndex(_me, 20); // over the cube marks (5) and the guards (6)
        Panel.SetZIndex(_meRing, 19);
        _canvas.Children.Add(_meRing);
        _canvas.Children.Add(_me);
        _meUnknown = new TextBlock
        {
            Text = "?", Width = 28, TextAlignment = TextAlignment.Center, FontSize = 26, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xBA, 0x42)),
            IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Color = Colors.Black, Opacity = 1 },
        };
        Canvas.SetLeft(_meUnknown, ViewSize / 2 - 14);
        Canvas.SetTop(_meUnknown, ViewSize / 2 - 20);
        Panel.SetZIndex(_meUnknown, 20);
        _canvas.Children.Add(_meUnknown);
        Body = new Grid { Children = { _tileLayer, _canvas } };
    }

    /// <summary>The designer's look: the opacity is the map picture's (the Render call gets it), the text opacity is that of the marks (the dots, the arrow), the colour is shown behind the map.</summary>
    protected override bool ApplyLook(MeterSettings settings)
    {
        double text = Math.Clamp(settings.OverlayForegrounds.GetValueOrDefault(Title, 1.0), 0.0, 1.0);
        double back = Math.Clamp(settings.PetMapOpacity, 0.0, 1.0);
        _canvas.Opacity = text;
        Brush backdrop = Brushes.Transparent;
        try
        {
            if (settings.OverlayBackColors.TryGetValue(Title, out string? hex) && !string.IsNullOrEmpty(hex) && ColorConverter.ConvertFromString(hex) is Color chosen)
            {
                backdrop = new SolidColorBrush(Color.FromArgb((byte)Math.Round(back * 0.8 * 255), chosen.R, chosen.G, chosen.B));
            }
        }
        catch (FormatException)
        {
            // a damaged value: no colour behind the map
        }

        _tileLayer.Background = backdrop;
        return true;
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

    private BitmapSource? Road(Aion2MapInfo map, int col, int row)
    {
        string key = $"{map.Key}/{col}_{row}";
        if (_roads.TryGetValue(key, out var cached))
        {
            return cached;
        }

        BitmapSource? bitmap = MapTiles.LoadRoads(map, col, row, Math.Min(map.Tile, 1536));
        _roads[key] = bitmap;
        _roadOrder.Add(key);
        while (_roadOrder.Count > 8)
        {
            _roads.Remove(_roadOrder[0]);
            _roadOrder.RemoveAt(0);
        }

        return bitmap;
    }

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
        IReadOnlyList<(string Kind, float X, float Y)>? gather = null, bool positionKnown = true, bool petArrows = true, bool gatherArrows = true,
        IReadOnlyList<(float X, float Y)>? guards = null)
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
                    Panel.SetZIndex(image, -2); // under the danger zones of the guards and everything else
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                    _tileImages.Add(image);
                    _tileLayer.Children.Add(image);
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

        // the roads of the game over the ground (a layer of their own, so the map can be shown without them)
        int roadsUsed = 0;
        if (ShowRoads)
        {
            for (int col = c0; col <= c1; col++)
            {
                for (int row = r0; row <= r1; row++)
                {
                    if (Tile(map, col, row) is null || Road(map, col, row) is not { } road)
                    {
                        continue;
                    }

                    if (roadsUsed >= _roadImages.Count)
                    {
                        var image = new Image { Stretch = Stretch.Fill, SnapsToDevicePixels = false, IsHitTestVisible = false };
                        Panel.SetZIndex(image, -1); // over the ground, under the danger zones, the dots and everything else
                        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                        _roadImages.Add(image);
                        _tileLayer.Children.Add(image);
                    }

                    var roadImage = _roadImages[roadsUsed++];
                    roadImage.Source = road;
                    roadImage.Opacity = opacity;
                    roadImage.Width = map.Tile * k;
                    roadImage.Height = map.Tile * k;
                    Canvas.SetLeft(roadImage, cx + (col * map.Tile - nx) * k);
                    Canvas.SetTop(roadImage, cy + (row * map.Tile - ny) * k);
                    roadImage.Visibility = Visibility.Visible;
                }
            }
        }

        for (int i = roadsUsed; i < _roadImages.Count; i++)
        {
            _roadImages[i].Visibility = Visibility.Collapsed;
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
            d.Fill = PetMapPalette.BrushOf(colors.GetValueOrDefault(pet, Colors.White));
            Canvas.SetLeft(d, px - 4.5);
            Canvas.SetTop(d, py - 4.5);
            d.Visibility = Visibility.Visible;
        }

        for (int i = dot; i < _dots.Count; i++)
        {
            _dots[i].Visibility = Visibility.Collapsed;
        }

        // collectibles: the icon the game shows for the item, or a diamond in the colour of the kind where the client has none (the pets are round)
        int mark = 0, iconMark = 0;
        foreach ((string kindKey, float x, float y) in gather ?? Array.Empty<(string, float, float)>())
        {
            double px = cx + (x - worldX) * ppu, py = cy + (y - worldY) * ppu;
            if (px < 6 || py < 6 || px > vw - 6 || py > vh - 6)
            {
                continue;
            }

            int bar = kindKey.IndexOf('|');
            string kind = bar < 0 ? kindKey : kindKey[..bar];
            var icon = bar < 0 ? null : AionDPS.Aion2.Protocol.Aion2Gather.Icon(kindKey[(bar + 1)..]);
            if (icon is not null)
            {
                if (iconMark >= _gatherIcons.Count)
                {
                    var image = new Image { Width = 24, Height = 24, IsHitTestVisible = false, Stretch = Stretch.Uniform, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.9 } };
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                    _gatherIcons.Add(image);
                    _canvas.Children.Add(image);
                }

                var shown = _gatherIcons[iconMark++];
                shown.Source = icon;
                // the cube places glow gold (a soft glow round the picture, no frame); the other collectibles keep their dark shadow
                shown.Effect = kind == "Cube" ? CubeGlow : IconShadow;
                // the cubes are many and close together: a smaller picture than the other collectibles
                double side = kind == "Cube" ? 24 : 24;
                shown.Width = shown.Height = side;
                Canvas.SetLeft(shown, px - side / 2);
                Canvas.SetTop(shown, py - side / 2);
                shown.Visibility = Visibility.Visible;
                continue;
            }

            if (mark >= _gatherMarks.Count)
            {
                var g = new Polygon { Points = new PointCollection { new Point(0, -8), new Point(7, 0), new Point(0, 8), new Point(-7, 0) }, Stroke = Brushes.Black, StrokeThickness = 1.4, IsHitTestVisible = false };
                _gatherMarks.Add(g);
                _canvas.Children.Add(g);
            }

            var m = _gatherMarks[mark++];
            m.Fill = PetMapPalette.BrushOf(PetMapPalette.OfGather(kind));
            Canvas.SetLeft(m, px);
            Canvas.SetTop(m, py);
            m.Visibility = Visibility.Visible;
        }

        for (int i = mark; i < _gatherMarks.Count; i++)
        {
            _gatherMarks[i].Visibility = Visibility.Collapsed;
        }

        for (int i = iconMark; i < _gatherIcons.Count; i++)
        {
            _gatherIcons[i].Visibility = Visibility.Collapsed;
        }

        // the monsters really around the player (announced by the game): large, bright, over the general spawn points
        int liveDot = 0, cubeMark = 0;
        foreach ((int pet, float x, float y) in live)
        {
            double px = cx + (x - worldX) * ppu, py = cy + (y - worldY) * ppu;
            if (px < 8 || py < 8 || px > vw - 8 || py > vh - 8)
            {
                continue;
            }

            if (pet == CubeMarkPet)
            {
                // an announced Hidden Cube (in range right now): a large cube picture inside a bright gold ring that pulses, over everything else
                while (cubeMark >= _cubeMarks.Count)
                {
                    var created = NewCubeMark();
                    _cubeMarks.Add(created);
                    _canvas.Children.Add(created);
                }

                var cubeVisual = _cubeMarks[cubeMark++];
                Canvas.SetLeft(cubeVisual, px - 22);
                Canvas.SetTop(cubeVisual, py - 22);
                cubeVisual.Visibility = Visibility.Visible;
                continue;
            }

            if (liveDot >= _liveDots.Count)
            {
                var e = new Ellipse { Width = 17, Height = 17, Stroke = Brushes.White, StrokeThickness = 2.5, IsHitTestVisible = false, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 5, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.9 } };
                _liveDots.Add(e);
                _canvas.Children.Add(e);
            }

            var d = _liveDots[liveDot++];
            d.Fill = PetMapPalette.BrushOf(colors.GetValueOrDefault(pet, Colors.White));
            Canvas.SetLeft(d, px - 8.5);
            Canvas.SetTop(d, py - 8.5);
            d.Visibility = Visibility.Visible;
        }

        for (int i = liveDot; i < _liveDots.Count; i++)
        {
            _liveDots[i].Visibility = Visibility.Collapsed;
        }

        for (int i = cubeMark; i < _cubeMarks.Count; i++)
        {
            _cubeMarks[i].Visibility = Visibility.Collapsed;
        }

        // the height of each cube in view beside its mark: an arrow up or down and the metres (blue)
        int heightMark = 0;
        foreach ((float x, float y, float dz) in CubeHeights)
        {
            double px = cx + (x - worldX) * ppu, py = cy + (y - worldY) * ppu;
            if (px < 8 || py < 8 || px > vw - 8 || py > vh - 8)
            {
                continue;
            }

            if (heightMark >= _heightLabels.Count)
            {
                var label = new TextBlock
                {
                    FontSize = 15, FontWeight = FontWeights.ExtraBold, Foreground = new SolidColorBrush(Color.FromRgb(0x9B, 0xE0, 0xFF)), IsHitTestVisible = false,
                    // a dark plate behind the number, so that it can be read on the light and the dark parts of the map
                    Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x08, 0x10, 0x20)), Padding = new Thickness(4, 1, 4, 1),
                };
                _heightLabels.Add(label);
                _canvas.Children.Add(label);
            }

            var shownLabel = _heightLabels[heightMark++];
            int metres = (int)Math.Round(Math.Abs(dz) / 100.0);
            shownLabel.Inlines.Clear();
            if (metres > 0)
            {
                shownLabel.Inlines.Add(new System.Windows.Documents.Run(dz > 0 ? "\u25B2" : "\u25BC") { FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 10 });
            }

            shownLabel.Inlines.Add(new System.Windows.Documents.Run(" " + metres));
            Canvas.SetLeft(shownLabel, px + 24);
            Canvas.SetTop(shownLabel, py - 9);
            shownLabel.Visibility = Visibility.Visible;
        }

        for (int i = heightMark; i < _heightLabels.Count; i++)
        {
            _heightLabels[i].Visibility = Visibility.Collapsed;
        }

        // a chosen pet with nothing in view: an arrow on the edge, in its colour, towards its nearest spawn point
        int arrow = 0;
        // the edge is the edge of the visible part: it lies at minus the shift of the content, so the vectors to the points are taken from the middle of the visible part
        var (sx, sy) = BodyShift;
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
            double ex = dx + sx, ey = dy + sy; // from the middle of the visible part
            if (Math.Abs(ex) < halfX - 5 && Math.Abs(ey) < halfY - 5)
            {
                st.Visible = true;
            }
            else
            {
                double sq = dx * dx + dy * dy;
                if (sq < st.BestSq)
                {
                    st.BestSq = sq;
                    st.Bx = ex;
                    st.By = ey;
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
            ar.Fill = PetMapPalette.BrushOf(color);
            ar.RenderTransform = new RotateTransform(Math.Atan2(by, bx) * 180 / Math.PI);
            Canvas.SetLeft(ar, halfX - sx + bx * t);
            Canvas.SetTop(ar, halfY - sy + by * t);
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
            foreach ((string kindKey, float x, float y) in gather)
            {
                string kind = kindKey.Contains('|') ? kindKey[..kindKey.IndexOf('|')] : kindKey;
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
                double ex = dx + sx, ey = dy + sy;
                if (Math.Abs(ex) < halfX - 5 && Math.Abs(ey) < halfY - 5)
                {
                    st.Visible = true;
                }
                else if (dx * dx + dy * dy < st.BestSq)
                {
                    st = (false, dx * dx + dy * dy, ex, ey);
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

        DrawGuards(guards, worldX, worldY, ppu, cx, cy, vw, vh);

        Canvas.SetLeft(_me, cx);
        Canvas.SetTop(_me, cy);
        _me.RenderTransform = new RotateTransform(MeHeading);
        Canvas.SetLeft(_meUnknown, cx - 14);
        Canvas.SetTop(_meUnknown, cy - 20);
        // the player on top; a "?" instead of the arrow when the position is not known (the view then shows the last one)
        _me.Visibility = positionKnown ? Visibility.Visible : Visibility.Collapsed;
        _me.Opacity = MeEstimated ? 0.6 : 1.0;
        _meRing.Visibility = positionKnown && MeEstimated ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(_meRing, cx - 17);
        Canvas.SetTop(_meRing, cy - 17);
        _meUnknown.Visibility = positionKnown ? Visibility.Collapsed : Visibility.Visible;
        _canvas.Children.Remove(_me);
        _canvas.Children.Add(_me);
        _canvas.Children.Remove(_meUnknown);
        _canvas.Children.Add(_meUnknown);
    }

    private sealed class GuardMark
    {
        public Ellipse Zone = null!, Badge = null!;
        public TextBlock Skull = null!, Label = null!;
        public Polygon Arrow = null!;
        public Border LabelBack = null!;

        public void Hide()
        {
            Zone.Visibility = Badge.Visibility = Skull.Visibility = Arrow.Visibility = LabelBack.Visibility = Visibility.Collapsed;
        }
    }

    private readonly List<GuardMark> _guardMarks = new();
    private static readonly Brush GuardRed = new SolidColorBrush(Color.FromRgb(0xE5, 0x1F, 0x1F));
    private static readonly Brush GuardZoneFill = new SolidColorBrush(Color.FromArgb(0x48, 0xFF, 0x26, 0x26));
    private static readonly Brush GuardZoneEdge = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0x3B, 0x3B));
    private static readonly Brush GuardLabelBack = new SolidColorBrush(Color.FromArgb(0xB8, 0x10, 0x10, 0x14));
    private static readonly FontFamily SymbolFont = new("Segoe UI Symbol");

    private GuardMark NewGuardMark()
    {
        var effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.9 };
        var mark = new GuardMark
        {
            Zone = new Ellipse { Fill = GuardZoneFill, Stroke = GuardZoneEdge, StrokeThickness = 1.2, IsHitTestVisible = false },
            Badge = new Ellipse { Width = 19, Height = 19, Fill = GuardRed, Stroke = Brushes.White, StrokeThickness = 1.8, IsHitTestVisible = false, Effect = effect },
            Skull = new TextBlock { Text = "\u2620", FontFamily = SymbolFont, FontWeight = FontWeights.Bold, FontSize = 12, Foreground = Brushes.White, Width = 19, TextAlignment = TextAlignment.Center, IsHitTestVisible = false },
            Arrow = new Polygon { Points = new PointCollection { new Point(11, 0), new Point(-8, -8), new Point(-4, 0), new Point(-8, 8) }, Fill = GuardRed, Stroke = Brushes.White, StrokeThickness = 1.6, IsHitTestVisible = false, Effect = effect },
            Label = new TextBlock { FontFamily = SymbolFont, FontWeight = FontWeights.SemiBold, FontSize = 11, Foreground = GuardRed, IsHitTestVisible = false },
        };
        mark.LabelBack = new Border { Background = GuardLabelBack, CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 1, 4, 1), Child = mark.Label, IsHitTestVisible = false };
        Panel.SetZIndex(mark.Zone, -1);
        foreach (UIElement e in new UIElement[] { mark.Badge, mark.Skull, mark.Arrow, mark.LabelBack })
        {
            Panel.SetZIndex(e, 6);
        }

        foreach (UIElement e in new UIElement[] { mark.Zone, mark.Badge, mark.Skull, mark.Arrow, mark.LabelBack })
        {
            _canvas.Children.Add(e);
        }

        return mark;
    }

    /// <summary>
    /// The militia guards the game announced around the player (Stacy's idea, her fork 17 October 2026): in view a see-through red danger zone of 20 m under a red badge with a white skull,
    /// out of view a red arrow on the edge towards it with the distance beside it.
    /// </summary>
    private void DrawGuards(IReadOnlyList<(float X, float Y)>? guards, double worldX, double worldY, double ppu, double cx, double cy, double vw, double vh)
    {
        int used = 0;
        foreach ((float gx, float gy) in guards ?? Array.Empty<(float, float)>())
        {
            GuardMark mark = used < _guardMarks.Count ? _guardMarks[used] : NewGuardMark();
            if (used >= _guardMarks.Count)
            {
                _guardMarks.Add(mark);
            }

            used++;
            double dx = (gx - worldX) * ppu, dy = (gy - worldY) * ppu;
            double px = cx + dx, py = cy + dy;
            var (sx, sy) = BodyShift;
            if (px + sx >= 8 && py + sy >= 8 && px + sx <= vw - 8 && py + sy <= vh - 8)
            {
                double zone = Math.Max(14, Aion2Guards.ZoneMetres * 100 * ppu);
                mark.Zone.Width = mark.Zone.Height = zone * 2;
                Canvas.SetLeft(mark.Zone, px - zone);
                Canvas.SetTop(mark.Zone, py - zone);
                Canvas.SetLeft(mark.Badge, px - 9.5);
                Canvas.SetTop(mark.Badge, py - 9.5);
                Canvas.SetLeft(mark.Skull, px - 9.5);
                Canvas.SetTop(mark.Skull, py - 9);
                mark.Zone.Visibility = mark.Badge.Visibility = mark.Skull.Visibility = Visibility.Visible;
                mark.Arrow.Visibility = mark.LabelBack.Visibility = Visibility.Collapsed;
                continue;
            }

            // out of view: the arrow on the edge, the distance beside it (towards the middle)
            double reachX = cx - 13, reachY = cy - 13;
            double ex = dx + sx, ey = dy + sy; // from the middle of the visible part, which lies at minus the shift of the content
            double t = Math.Min(reachX / Math.Max(Math.Abs(ex), 1e-6), reachY / Math.Max(Math.Abs(ey), 1e-6));
            double angle = Math.Atan2(ey, ex);
            double tipX = cx - sx + ex * t, tipY = cy - sy + ey * t;
            mark.Arrow.RenderTransform = new RotateTransform(angle * 180 / Math.PI);
            Canvas.SetLeft(mark.Arrow, tipX);
            Canvas.SetTop(mark.Arrow, tipY);
            double metres = Math.Sqrt((gx - worldX) * (double)(gx - worldX) + (gy - worldY) * (double)(gy - worldY)) / 100.0;
            mark.Label.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, "\u2620 {0:0} m", metres);
            mark.LabelBack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size size = mark.LabelBack.DesiredSize;
            double lx = Math.Clamp(tipX - 28 * Math.Cos(angle) - size.Width / 2, 3 - sx, Math.Max(3, vw - size.Width - 3) - sx);
            double ly = Math.Clamp(tipY - 28 * Math.Sin(angle) - size.Height / 2, 3 - sy, Math.Max(3, vh - size.Height - 3) - sy);
            Canvas.SetLeft(mark.LabelBack, lx);
            Canvas.SetTop(mark.LabelBack, ly);
            mark.Zone.Visibility = mark.Badge.Visibility = mark.Skull.Visibility = Visibility.Collapsed;
            mark.Arrow.Visibility = mark.LabelBack.Visibility = Visibility.Visible;
        }

        for (int i = used; i < _guardMarks.Count; i++)
        {
            _guardMarks[i].Hide();
        }
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
