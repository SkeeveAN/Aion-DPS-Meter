using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace AionDPS.Ui;

/// <summary>What a global hotkey does.</summary>
public enum HotkeyAction
{
    ToggleHideUi,
    TogglePause,
    CopyDamageRanking,
    ClearDamage,
    UploadBoss,
    NextMode,
    ToggleTimetable,
}

/// <summary>A key plus modifiers, written "Ctrl+Alt+H" in the settings file.</summary>
public readonly record struct HotkeyBinding(bool Ctrl, bool Alt, bool Shift, bool Win, Key Key)
{
    public bool IsEmpty => Key == Key.None;

    public override string ToString()
    {
        if (IsEmpty)
        {
            return "";
        }

        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join('+', parts);
    }

    /// <summary>Parses "Ctrl+Alt+H"; empty, unreadable or modifier-less text gives an empty binding
    /// (a global hotkey without a modifier would swallow an ordinary key in every program).</summary>
    public static HotkeyBinding Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return default;
        }

        bool ctrl = false, alt = false, shift = false, win = false;
        Key key = Key.None;
        foreach (string part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                case "win": win = true; break;
                default:
                    if (!Enum.TryParse(part, true, out key))
                    {
                        return default;
                    }

                    break;
            }
        }

        return key == Key.None || !(ctrl || alt || shift || win) ? default : new HotkeyBinding(ctrl, alt, shift, win, key);
    }
}

/// <summary>
/// System-wide hotkeys (RegisterHotKey), which replace the in-game chat commands: Aion 2's chat is
/// not readable from the network stream, so ".ui" and the others could never fire. A hotkey works
/// whatever window has the focus and sends nothing into the game. Registered on the main window's
/// handle; <see cref="Apply"/> can be called again after the settings changed.
/// </summary>
internal sealed class GlobalHotkeys : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int FirstId = 0xA110;
    private const uint ModAlt = 0x0001, ModControl = 0x0002, ModShift = 0x0004, ModWin = 0x0008, ModNoRepeat = 0x4000;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly IntPtr _handle;
    private readonly HwndSource _source;
    private readonly Dictionary<int, HotkeyAction> _registered = new();

    /// <summary>Fired on the window's thread when a registered hotkey is pressed.</summary>
    public event Action<HotkeyAction>? Pressed;

    public GlobalHotkeys(Window window)
    {
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle)
            ?? throw new InvalidOperationException("GlobalHotkeys needs the window to have a native handle (construct in OnSourceInitialized).");
        _source.AddHook(WndProc);
    }

    /// <summary>Registers the bindings, dropping the previous set first. Returns the actions whose
    /// combination could not be registered (another program already owns it).</summary>
    public IReadOnlyList<HotkeyAction> Apply(IReadOnlyDictionary<HotkeyAction, HotkeyBinding> bindings)
    {
        Clear();
        var failed = new List<HotkeyAction>();
        int id = FirstId;
        foreach (var (action, binding) in bindings)
        {
            if (binding.IsEmpty)
            {
                continue;
            }

            uint modifiers = ModNoRepeat | (binding.Ctrl ? ModControl : 0) | (binding.Alt ? ModAlt : 0)
                | (binding.Shift ? ModShift : 0) | (binding.Win ? ModWin : 0);
            if (RegisterHotKey(_handle, id, modifiers, (uint)KeyInterop.VirtualKeyFromKey(binding.Key)))
            {
                _registered[id++] = action;
            }
            else
            {
                failed.Add(action);
            }
        }

        return failed;
    }

    private void Clear()
    {
        foreach (int id in _registered.Keys)
        {
            UnregisterHotKey(_handle, id);
        }

        _registered.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _registered.TryGetValue(wParam.ToInt32(), out HotkeyAction action))
        {
            Pressed?.Invoke(action);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Clear();
        _source.RemoveHook(WndProc);
    }
}
