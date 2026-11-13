using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AionDPS.Ui;

/// <summary>
/// The hotkeys that are left: the overlay designer, overlays shown / hidden, pause, clear, upload and copy. A field takes the combination pressed in it (Ctrl, Alt, Shift or Win
/// plus a key), the ✕ takes the key away; every change is kept at once.
/// </summary>
public static class ClientHotkeysPage
{
    public static UIElement Build(Action<MeterSettings> applied)
    {
        var loc = LocalizationManager.Instance;
        var c = new ConfigControls(applied);
        var stack = new StackPanel { Margin = new Thickness(14, 10, 14, 14) };
        (string Label, Func<MeterSettings, string> Get, Action<MeterSettings, string> Set)[] rows =
        {
            (loc["Settings.Hotkey.Designer"], s => s.HotkeyDesigner, (s, v) => s.HotkeyDesigner = v),
            (loc["Hub.Hotkey.Overlays"], s => s.HotkeyAllOverlays, (s, v) => s.HotkeyAllOverlays = v),
            (loc["Settings.Hotkey.Pause"], s => s.HotkeyPause, (s, v) => s.HotkeyPause = v),
            (loc["Settings.Hotkey.Clear"], s => s.HotkeyClear, (s, v) => s.HotkeyClear = v),
            (loc["Settings.Hotkey.UploadBoss"], s => s.HotkeyUploadBoss, (s, v) => s.HotkeyUploadBoss = v),
            (loc["Hub.Hotkey.Copy"], s => s.HotkeyCopyDamage, (s, v) => s.HotkeyCopyDamage = v),
        };
        var list = new StackPanel();
        MeterSettings now = MeterSettings.Load();
        foreach (var (label, get, set) in rows)
        {
            var name = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            var box = new TextBox { Width = 190, Height = 28, IsReadOnly = true, IsReadOnlyCaretVisible = false, Cursor = Cursors.Hand, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            box.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
            box.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
            box.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
            string Show(string text) => HotkeyBinding.Parse(text) is { IsEmpty: false } b ? b.ToString() : loc["Settings.Hotkey.NoKey"];
            box.Text = Show(get(now));
            var clear = new Button { Content = "✕", Width = 28, Height = 28, Padding = new Thickness(0), ToolTip = loc["Settings.Hotkey.Clear.Tip"] };
            clear.Click += (_, _) =>
            {
                c.Change(s => set(s, ""));
                box.Text = Show("");
            };
            box.GotKeyboardFocus += (_, _) => box.Text = loc["Settings.Hotkey.Press"];
            box.LostKeyboardFocus += (_, _) => box.Text = Show(get(MeterSettings.Load()));
            box.PreviewKeyDown += (_, e) =>
            {
                e.Handled = true;
                Key key = e.Key switch { Key.System => e.SystemKey, Key.ImeProcessed => e.ImeProcessedKey, _ => e.Key };
                if (key == Key.Escape)
                {
                    Keyboard.ClearFocus();
                    return;
                }

                // a bare modifier is only half a combination
                if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None or Key.DeadCharProcessed)
                {
                    return;
                }

                ModifierKeys mods = Keyboard.Modifiers;
                var binding = new HotkeyBinding(mods.HasFlag(ModifierKeys.Control), mods.HasFlag(ModifierKeys.Alt), mods.HasFlag(ModifierKeys.Shift), mods.HasFlag(ModifierKeys.Windows), key);
                if (!(binding.Ctrl || binding.Alt || binding.Shift || binding.Win))
                {
                    box.Text = loc["Settings.Hotkey.NeedsModifier"]; // a global hotkey without a modifier would swallow that key in every program
                    return;
                }

                c.Change(s => set(s, binding.ToString()));
                Keyboard.ClearFocus();
            };
            var row = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
            SetDockRight(clear);
            row.Children.Add(clear);
            SetDockRight(box);
            row.Children.Add(box);
            row.Children.Add(name);
            list.Children.Add(row);
        }

        stack.Children.Add(ConfigControls.Card(loc["Settings.Hotkeys"], new UIElement[] { ConfigControls.Note(loc["Settings.Hotkeys.Hint"], 0), list }));
        stack.Children.Add(ConfigControls.Card("", ConfigControls.Note(loc["Hub.Hotkey.Note"], 0)));
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = stack };
    }

    private static void SetDockRight(UIElement element) => DockPanel.SetDock(element, Dock.Right);
}
