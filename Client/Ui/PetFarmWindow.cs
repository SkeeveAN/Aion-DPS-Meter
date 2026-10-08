using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// The pet farming overlay: a small panel that shows the level of the pet the targeted monster drops. Locked (the default) it is click-through
/// and completely invisible (hidden) when nothing is targeted or the monster has no pet. Unlocked (Settings: "Overlay locked" off) it stays
/// visible with an outline, takes clicks and can be dragged and scaled with the grip in its corner; place and size are remembered.
/// The target comes from the screen (<see cref="PetFarmReader"/>), the level from the pet list of the login frame.
/// </summary>
public sealed class PetFarmWindow : Window
{
    private const double MinScale = 0.6, MaxScale = 3.0;

    private readonly StackPanel _lines = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Border _panel;
    private readonly System.Windows.Controls.Primitives.Thumb _grip;
    private IReadOnlyList<(string Pet, string Level)> _current = Array.Empty<(string, string)>();
    private bool _locked = true;
    private double _centerX;
    private bool _resizing;
    private readonly OverlayEdgeResize _edges;

    public PetFarmWindow()
    {
        Title = "Aion DPS Pet Farming";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false; // the game keeps the keyboard
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;

        var settings = MeterSettings.Load();
        ApplyOpacity(settings.OverlayOpacity);
        SetScale(settings.PetFarmScale);

        _grip = new System.Windows.Controls.Primitives.Thumb
        {
            Width = 12, Height = 12, Cursor = Cursors.SizeNWSE, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Right,
            Template = GripTemplate(),
        };
        AttachGripResize(_grip, SetScale, () => _scale.ScaleX);
        _grip.DragCompleted += (_, _) =>
        {
            var s = MeterSettings.Load();
            s.PetFarmScale = _scale.ScaleX;
            s.Save();
        };

        var stack = new StackPanel();
        stack.Children.Add(_lines);
        stack.Children.Add(_grip);
        _panel = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 5, 8, 5), Child = stack, LayoutTransform = _scale, BorderThickness = new Thickness(1) };
        _panel.SetResourceReference(Border.BackgroundProperty, "Brush.OverlayBg");
        _panel.MouseLeftButtonDown += (_, e) =>
        {
            if (!_locked && e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
                var s = MeterSettings.Load();
                _centerX = Left + ActualWidth / 2;
                s.PetFarmLeft = _centerX; // the saved value is the window's middle: the text grows to both sides
                s.PetFarmTop = Top;
                s.Save();
            }
        };
        _edges = new OverlayEdgeResize(this, SetScale, () => _scale.ScaleX, () => _resizing = true, () =>
        {
            _resizing = false;
            _centerX = Left + ActualWidth / 2;
            var s = MeterSettings.Load();
            s.PetFarmScale = _scale.ScaleX;
            s.PetFarmLeft = _centerX;
            s.PetFarmTop = Top;
            s.Save();
        });
        Content = _edges.Wrap(_panel);
        _centerX = settings.PetFarmLeft ?? SystemParameters.WorkArea.Width / 2;
        Left = _centerX - 60;
        SizeChanged += (_, e) => { if (!_resizing) { Left = _centerX - e.NewSize.Width / 2; } }; // keep the middle in place when the text changes
        Top = settings.PetFarmTop ?? 140;
        ApplyLocked(settings.PetFarmLocked);
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
        var template = new ControlTemplate(typeof(System.Windows.Controls.Primitives.Thumb));
        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        var path = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        path.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M11,1 L1,11 M11,5 L5,11 M11,9 L9,11"));
        path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Brush.TextSubtle");
        path.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.0);
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

    public void ApplyOpacity(double opacity)
    {
        opacity = Math.Clamp(double.IsFinite(opacity) ? opacity : 0.6, 0.2, 1.0);
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), 0, 0, 0));
        brush.Freeze();
        Resources["Brush.OverlayBg"] = brush;
    }

    /// <summary>Locked: click-through, no outline, no grip, hidden without a pet. Unlocked: clickable, outlined, always visible so it can be placed.</summary>
    public void ApplyLocked(bool locked)
    {
        _locked = locked;
        _grip.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        _edges.Show(!locked);
        _panel.Cursor = locked ? Cursors.Arrow : Cursors.SizeAll;
        _panel.SetResourceReference(Border.BorderBrushProperty, locked ? "Brush.OverlayBg" : "Brush.Accent");
        var handle = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        if (handle != IntPtr.Zero)
        {
            using var native = new NativeOverlay(this);
            native.SetClickThrough(locked);
        }

        Render();
    }

    /// <summary>Shows the given lines (pet name, level text); with none the window is hidden - unless it is unlocked, then it shows a hint so it can be moved.</summary>
    public void Show(IReadOnlyList<(string Pet, string Level)> lines)
    {
        string key = string.Join("\n", lines.Select(l => l.Pet + "|" + l.Level));
        if (key == string.Join("\n", _current.Select(l => l.Pet + "|" + l.Level)) && IsVisible == (lines.Count > 0 || !_locked))
        {
            return;
        }

        _current = lines;
        Render();
    }

    private void Render()
    {
        bool placeholder = _current.Count == 0 && !_locked;
        if (_current.Count == 0 && !placeholder)
        {
            Hide();
            return;
        }

        _lines.Children.Clear();
        var rows = placeholder ? new[] { (LocalizationManager.Instance["PetFarm.Placeholder"], "") } : _current.ToArray();
        foreach ((string pet, string level) in rows)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1), HorizontalAlignment = HorizontalAlignment.Center };
            var name = new TextBlock { Text = pet, FontWeight = FontWeights.Bold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
            row.Children.Add(name);
            if (level.Length > 0)
            {
                var detail = new TextBlock { Text = "· " + level, FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                detail.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
                row.Children.Add(detail);
            }

            _lines.Children.Add(row);
        }

        if (!IsVisible)
        {
            Show();
            ApplyLockedStyle();
        }
    }

    // the click-through style is lost when WPF recreates the native window on a first Show
    private void ApplyLockedStyle()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            using var native = new NativeOverlay(this);
            native.SetClickThrough(_locked);
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

/// <summary>
/// Drives the pet farming overlay: about once a second, while the game window is the one in front, it takes a picture of that window
/// (in memory only), has <see cref="PetFarmReader"/> read the target plate, looks the monster up in the pet table and tells the window
/// what to show. Monster names that lead to no pet go to <see cref="PetFarmLog"/>.
/// </summary>
public sealed class PetFarmController : IDisposable
{
    private readonly PetFarmWindow _window;
    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly PetFarmReader _reader = new();
    private readonly CancellationTokenSource _stop = new();
    private int _unknownLogged;

    public PetFarmController(PetFarmWindow window, Func<Aion2EntityDirectory?> directory)
    {
        _window = window;
        _directory = directory;
        _ = Task.Run(LoopAsync);
    }

    public void Dispose() => _stop.Cancel();

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(900, _stop.Token);
                var lines = await ReadOnceAsync();
                await _window.Dispatcher.InvokeAsync(() => _window.Show(lines));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException or System.IO.IOException)
            {
                PetFarmLog.Write($"ERROR {ex.GetType().Name}: {ex.Message}");
                await Task.Delay(5000, _stop.Token).ContinueWith(_ => { });
            }
        }
    }

    private async Task<IReadOnlyList<(string Pet, string Level)>> ReadOnceAsync()
    {
        var none = Array.Empty<(string, string)>();
        if (GameWindow.ForegroundClientArea() is not { } area)
        {
            return none; // the game is not the window in front
        }

        var directory = _directory();
        IReadOnlyCollection<int>? pets = null;

        // 1. The game's own message when the player marks a monster (tab or click): the entity id leads to the NPC and the pet.
        if (directory?.LocalTarget is { NpcId: int npcId, At: var at } && DateTime.UtcNow - at < TimeSpan.FromMinutes(2))
        {
            var ofNpc = Aion2Pets.PetsOfNpc(npcId);
            if (ofNpc.Count == 0)
            {
                PetFarmLog.NoteUnknownMonster(npcId);
                return none;
            }

            pets = ofNpc.ToArray();
        }
        else if (MeterSettings.Load().PetFarmScreenFallback)
        {
            // 2. Optional: read the name on the target plate from the screen (a picture of the game window, in memory only).
            string language = LocalizationManager.Instance.Language;
            using var frame = new System.Drawing.Bitmap(area.Width, area.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(frame))
            {
                g.CopyFromScreen(area.Location, System.Drawing.Point.Empty, area.Size);
            }

            string? read = await _reader.ReadTargetNameAsync(frame, language);
            if (read is null)
            {
                return none;
            }

            if (PetFarmReader.Match(read) is not { } match)
            {
                if (_unknownLogged++ < 300)
                {
                    PetFarmLog.NoteUnknownName(read);
                }

                return none;
            }

            pets = match.Pets;
        }

        if (pets is null)
        {
            return none;
        }

        var states = directory?.LocalPetStates ?? Array.Empty<Aion2PetState>();
        var loc = LocalizationManager.Instance;
        string lang = loc.Language;
        var result = new List<(string, string)>();
        foreach (int petId in pets.OrderBy(p => p))
        {
            string name = Aion2Pets.PetName(petId, lang) ?? $"#{petId}";
            string level;
            if (states.Count == 0)
            {
                level = loc["PetFarm.Unknown"];
            }
            else if (states.FirstOrDefault(s => s.PetId == petId) is { } state)
            {
                level = state.Level >= Aion2Pets.TopLevel
                    ? string.Format(loc["PetFarm.Max"], state.Level)
                    : string.Format(loc["PetFarm.Level"], state.Level) + $" ({state.Progress}/{Aion2Pets.ProgressNeeded(state.Level)})";
            }
            else
            {
                level = loc["PetFarm.NotOwned"];
            }

            result.Add((name, level));
        }

        return result;
    }
}

/// <summary>Finds the game's window (process AION2) and says where its client area is on screen, but only while it is the window in front.</summary>
internal static class GameWindow
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref Point point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private static IntPtr _known;

    public static System.Drawing.Rectangle? ForegroundClientArea()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !IsGame(hwnd) || !GetClientRect(hwnd, out Rect client))
        {
            return null;
        }

        var origin = new Point();
        if (!ClientToScreen(hwnd, ref origin) || client.Right <= 200 || client.Bottom <= 200)
        {
            return null;
        }

        return new System.Drawing.Rectangle(origin.X, origin.Y, client.Right, client.Bottom);
    }

    private static bool IsGame(IntPtr hwnd)
    {
        if (hwnd == _known)
        {
            return true;
        }

        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            if (process.ProcessName.Equals("AION2", StringComparison.OrdinalIgnoreCase))
            {
                _known = hwnd;
                return true;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        return false;
    }
}
