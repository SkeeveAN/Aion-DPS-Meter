using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// The pet farming overlay: a small panel that shows the level of the pet the targeted monster drops, and is completely invisible
/// (hidden) when nothing is targeted or the monster has no pet. The target comes from the screen (<see cref="PetFarmReader"/> reads the
/// name on the game's target plate); the level comes from the pet list of the login frame. Drag the panel to place it, the place is remembered.
/// </summary>
public sealed class PetFarmWindow : Window
{
    private readonly StackPanel _lines = new();
    private readonly Border _panel;
    private string _shown = "";

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
        _panel = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 5, 8, 5), Child = _lines, Cursor = Cursors.SizeAll };
        _panel.SetResourceReference(Border.BackgroundProperty, "Brush.OverlayBg");
        _panel.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
                var s = MeterSettings.Load();
                s.PetFarmLeft = Left;
                s.PetFarmTop = Top;
                s.Save();
            }
        };
        Content = _panel;
        Left = settings.PetFarmLeft ?? SystemParameters.WorkArea.Width / 2 - 120;
        Top = settings.PetFarmTop ?? 140;
    }

    public void ApplyOpacity(double opacity)
    {
        opacity = Math.Clamp(double.IsFinite(opacity) ? opacity : 0.6, 0.2, 1.0);
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), 0, 0, 0));
        brush.Freeze();
        Resources["Brush.OverlayBg"] = brush;
    }

    /// <summary>Shows the given lines (pet name, level text), or hides the window when there are none.</summary>
    public void Show(IReadOnlyList<(string Pet, string Level)> lines)
    {
        string key = string.Join("\n", lines.Select(l => l.Pet + "|" + l.Level));
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        if (lines.Count == 0)
        {
            Hide();
            return;
        }

        _lines.Children.Clear();
        foreach ((string pet, string level) in lines)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            var name = new TextBlock { Text = pet, FontWeight = FontWeights.Bold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
            var detail = new TextBlock { Text = "· " + level, FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            row.Children.Add(name);
            row.Children.Add(detail);
            _lines.Children.Add(row);
        }

        if (!IsVisible)
        {
            Show();
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
        System.Drawing.Rectangle? area = GameWindow.ForegroundClientArea();
        if (area is null)
        {
            return none;
        }

        string language = LocalizationManager.Instance.Language;
        using var frame = new System.Drawing.Bitmap(area.Value.Width, area.Value.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(frame))
        {
            g.CopyFromScreen(area.Value.Location, System.Drawing.Point.Empty, area.Value.Size);
        }

        string? read = await _reader.ReadTargetNameAsync(frame, language);
        if (read is null)
        {
            return none;
        }

        var match = PetFarmReader.Match(read);
        if (match is null)
        {
            if (_unknownLogged++ < 300)
            {
                PetFarmLog.NoteUnknownName(read);
            }

            return none;
        }

        var directory = _directory();
        var states = directory?.LocalPetStates ?? Array.Empty<Aion2PetState>();
        var loc = LocalizationManager.Instance;
        var result = new List<(string, string)>();
        foreach (int petId in match.Value.Pets.OrderBy(p => p))
        {
            string name = Aion2Pets.PetName(petId, language) ?? $"#{petId}";
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
