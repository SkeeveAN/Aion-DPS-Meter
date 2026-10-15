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
        AltTabHidden.Apply(this);
        ShowActivated = false; // the game keeps the keyboard
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;

        var settings = MeterSettings.Load();
        ApplyOpacity(settings.OverlayOpacity);
        SetScale(settings.PetFarmScale);

        var stack = new DockPanel { LastChildFill = true };
        stack.Children.Add(new Border { Child = _lines, ClipToBounds = true });
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
        if (settings.PetFarmWidth is > 0) { _panel.Width = settings.PetFarmWidth.Value; }
        if (settings.PetFarmHeight is > 0) { _panel.Height = settings.PetFarmHeight.Value; }
        _edges = new OverlayEdgeResize(this, _panel, () => _scale.ScaleX, () => _resizing = true, () =>
        {
            _resizing = false;
            _centerX = Left + ActualWidth / 2;
            var s = MeterSettings.Load();
            s.PetFarmScale = _scale.ScaleX;
            s.PetFarmWidth = double.IsNaN(_panel.Width) ? null : _panel.Width;
            s.PetFarmHeight = double.IsNaN(_panel.Height) ? null : _panel.Height;
            s.PetFarmLeft = _centerX;
            s.PetFarmTop = Top;
            s.Save();
        }, SetScale, OverlayEdgeMode.Uniform);
        Content = _edges.Wrap(_panel);
        _centerX = settings.PetFarmLeft ?? SystemParameters.WorkArea.Width / 2;
        Left = _centerX - 60;
        SizeChanged += (_, e) => { if (!_resizing) { Left = _centerX - e.NewSize.Width / 2; } }; // keep the middle in place when the text changes
        Top = settings.PetFarmTop ?? 140;
        ApplyLocked(settings.PetInfoLocked);
        Action<string> onReset = name => { if (name == Title) { Dispatcher.Invoke(ReloadPlace); } };
        OverlayPlaces.WasReset += onReset;
        Closed += (_, _) => OverlayPlaces.WasReset -= onReset;
        LocationChanged += (_, _) => _centerX = Left + ActualWidth / 2; // placed from outside (docked): the middle is where it is now
        OverlayDocking.Attach(this, () =>
        {
            var s = MeterSettings.Load();
            s.PetFarmLeft = _centerX;
            s.PetFarmTop = Top;
            s.Save();
        });
    }

    /// <summary>Place, size and scale from the settings again (after "reset size and position").</summary>
    private void ReloadPlace()
    {
        var s = MeterSettings.Load();
        SetScale(s.PetFarmScale);
        _panel.Width = s.PetFarmWidth is > 0 ? s.PetFarmWidth.Value : double.NaN;
        _panel.Height = s.PetFarmHeight is > 0 ? s.PetFarmHeight.Value : double.NaN;
        _centerX = s.PetFarmLeft ?? SystemParameters.WorkArea.Width / 2;
        Left = _centerX - ActualWidth / 2;
        Top = s.PetFarmTop ?? 140;
    }

    private void SetScale(double scale)
    {
        scale = Math.Clamp(double.IsFinite(scale) ? scale : 1.0, MinScale, MaxScale);
        _scale.ScaleX = scale;
        _scale.ScaleY = scale;
    }

    public void ApplyOpacity(double opacity)
    {
        opacity = Math.Clamp(double.IsFinite(opacity) ? opacity : 0.6, 0.0, 1.0);
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), 0, 0, 0));
        brush.Freeze();
        Resources["Brush.OverlayBg"] = brush;
    }

    /// <summary>Locked: click-through, no outline, no grip, hidden without a pet. Unlocked: clickable, outlined, always visible so it can be placed.</summary>
    public void ApplyLocked(bool locked)
    {
        _locked = locked;
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

    /// <summary>One line of the overlay: the pet's name and, in the accent colour, what is known about its level.</summary>
    internal static StackPanel BuildRow(string pet, string level)
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

        return row;
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
            var row = BuildRow(pet, level);
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
        if (GameWindow.ForegroundClientArea(ignoreMenu: true) is not { } area)
        {
            return none; // the game is not the window in front
        }

        var directory = _directory();
        IReadOnlyCollection<int>? pets = null;

        // 1. The game's own message when the player marks a monster (tab or click): the entity id leads to the NPC and the pet.
        // Without a mark (aiming with the camera) the monster the player hit last stands in, for 15 seconds after the hit.
        var marked = directory?.LocalTarget is { } m && DateTime.UtcNow - m.At < TimeSpan.FromMinutes(2) ? m : (ValueTuple<int, int?, DateTime>?)null;
        var hit = directory?.LocalHitTarget is { } h && DateTime.UtcNow - h.At < TimeSpan.FromSeconds(15) ? h : (ValueTuple<int, int?, DateTime>?)null;
        var current = marked is { } a && hit is { } b ? (b.Item3 > a.Item3 ? b : a) : marked ?? hit; // the later of the two
        if (current is { Item2: int npcId })
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

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo { public int Size, Flags; public IntPtr Cursor; public Point Position; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CursorInfo info);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private const int CursorShowing = 1;

    /// <summary>Whether Windows is showing a mouse cursor. The game hides it while playing (in the control mode that does) and shows it when a menu opens.
    /// A failing call counts as "not shown": the meter then simply stays up.</summary>
    public static bool IsCursorVisible()
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        return GetCursorInfo(ref info) && (info.Flags & CursorShowing) != 0 && info.Cursor != IntPtr.Zero;
    }

    /// <summary>Whether any key or mouse button is held (not the lock keys). Asked only while the cursor shows, to tell a menu from the show-cursor key being held.</summary>
    public static bool AnyKeyDown()
    {
        for (int key = 1; key < 255; key++)
        {
            if (key is 0x14 or 0x90 or 0x91)
            {
                continue; // Caps, Num and Scroll lock stay "on" without being held
            }

            if ((GetAsyncKeyState(key) & 0x8000) != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the window in front is the game itself (not the meter's own windows).</summary>
    public static bool ForegroundIsGame()
    {
        IntPtr hwnd = GetForegroundWindow();
        return hwnd != IntPtr.Zero && IsGame(hwnd);
    }

    private static IntPtr _known;
    private static IntPtr _lastChecked;
    private static bool _lastWasGameOrSelf;

    /// <summary>Whether the window in front is the game or one of the meter's own windows (its settings, its dialogs): the DPS overlay
    /// shows only then, so it does not sit over a browser or the desktop.</summary>
    public static bool ForegroundIsGameOrSelf()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (hwnd == _lastChecked)
        {
            return _lastWasGameOrSelf && !(MenuWatcher.Hidden && IsGame(hwnd));
        }

        bool result = IsGame(hwnd);
        if (!result)
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            result = pid == (uint)Environment.ProcessId;
        }

        _lastChecked = hwnd;
        _lastWasGameOrSelf = result;
        return result && !(MenuWatcher.Hidden && IsGame(hwnd)); // a game menu is open: out of the way
    }

    /// <param name="ignoreMenu">True for the pet and collection views: they stay (and the shop reading works) while a game menu is open.</param>
    public static System.Drawing.Rectangle? ForegroundClientArea(bool ignoreMenu = false)
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !IsGame(hwnd) || (MenuWatcher.Hidden && !ignoreMenu) || !GetClientRect(hwnd, out Rect client))
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

    private static IntPtr _notGame;
    private static long _notGameAt;

    private static bool IsGame(IntPtr hwnd)
    {
        if (hwnd == _known)
        {
            return true;
        }

        // The overlays ask every 50 ms. While another window (the meter itself, a browser) is in front, looking up the process each time
        // is expensive and starves the UI thread, so a "not the game" answer is remembered for a few seconds.
        long now = Environment.TickCount64;
        if (hwnd == _notGame && now - _notGameAt < 5000)
        {
            return false;
        }

        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == (uint)Environment.ProcessId)
            {
                _notGame = hwnd;
                _notGameAt = now;
                return false;
            }

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

        _notGame = hwnd;
        _notGameAt = now;
        return false;
    }
}
